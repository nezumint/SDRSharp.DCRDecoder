using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace SDRSharp.DCRDecoder.Vocoder;

/// <summary>Offline import of one known upstream wheel. Never downloads or executes installer scripts.</summary>
public sealed class EmbeddedVocoderRuntime
{
    public const string WheelName = "blip25_vocoder-1.0.0-cp39-abi3-win_amd64.whl";
    public const string WheelSha256 = "292f4ea9ec1821211e07761bd01f0db5ec1382a32b9ff72ac969253462eb2ef4";
    public const string DownloadPage = "https://pypi.org/project/blip25-vocoder/1.0.0/#files";
    private readonly string root;
    public EmbeddedVocoderRuntime(string pluginDirectory) => root = Path.Combine(Path.GetFullPath(pluginDirectory), "DcrRuntime");
    public string PythonPath => Path.Combine(root, "python", "python.exe");
    public string EngineDirectory => Path.Combine(root, "engines", "blip25-vocoder-1.0.0");
    public bool RuntimeAvailable => File.Exists(PythonPath) && Directory.Exists(Path.Combine(root, "packages", "numpy"));
    public bool EngineAvailable => File.Exists(Path.Combine(EngineDirectory, "blip25_vocoder", "_blip25_vocoder.pyd"))
        && File.Exists(Path.Combine(EngineDirectory, "installed.sha256"));

    public async Task InstallWheelAsync(string wheelPath, CancellationToken cancellationToken = default)
    {
        if (!RuntimeAvailable) throw new FileNotFoundException("Bundled Python/NumPy is missing. Copy the complete DcrRuntime folder from the release ZIP.");
        // Hash and extract the same open file, preventing replacement between validation and extraction.
        using var file = new FileStream(wheelPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > 8 * 1024 * 1024) throw new InvalidDataException("Unsupported wheel. Select " + WheelName);
        string hash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
        if (!hash.Equals(WheelSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Wheel checksum does not match the supported Windows x64 release: " + WheelName);
        if (Directory.Exists(EngineDirectory))
        {
            // Do not overwrite a loaded extension or silently replace an existing installation.
            await ProbeAsync(EngineDirectory, cancellationToken);
            if (!EngineAvailable) throw new InvalidDataException("Incomplete engine folder. Close SDR# and move the engine folder aside before importing again.");
            return;
        }
        string parent = Path.GetDirectoryName(EngineDirectory)!;
        Directory.CreateDirectory(parent);
        string staging = Path.Combine(parent, ".import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            file.Position = 0;
            using (var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true))
            {
                long total = 0;
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string name = entry.FullName;
                    if ((!name.StartsWith("blip25_vocoder/", StringComparison.Ordinal) && !name.StartsWith("blip25_vocoder-1.0.0.dist-info/", StringComparison.Ordinal))
                        || name.Contains('\\') || name.Contains(':') || name.Split('/').Any(p => p is ".." or "."))
                        throw new InvalidDataException("Unexpected wheel path.");
                    total += entry.Length;
                    if (total > 32 * 1024 * 1024) throw new InvalidDataException("Wheel expands beyond the supported size.");
                    string target = Path.GetFullPath(Path.Combine(staging, name));
                    if (!target.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid wheel path.");
                    if (name.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: false);
                }
            }
            await ProbeAsync(staging, cancellationToken);
            File.WriteAllText(Path.Combine(staging, "installed.sha256"), WheelSha256);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(staging, EngineDirectory);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
    }

    private async Task ProbeAsync(string engineDirectory, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(PythonPath) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-I", "-B", "-c",
            "import sys; sys.path.insert(0,sys.argv[1]); import numpy; from blip25_vocoder import Vocoder,Rate; v=Vocoder(Rate.HALF_RATE_2450X2450); p=v.decode_bits(bytes(7)); assert len(p)==160 and p.dtype==numpy.int16; print('DCR_ENGINE_OK')", engineDirectory }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Could not start bundled Python.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            string output = await stdout, error = await stderr;
            if (process.ExitCode != 0 || output.Trim() != "DCR_ENGINE_OK")
                throw new InvalidDataException("Engine load test failed: " + error[..Math.Min(error.Length, 800)]);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Engine load test timed out after 15 seconds.");
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            try { await Task.WhenAll(stdout, stderr); } catch (OperationCanceledException) { }
        }
    }
}
