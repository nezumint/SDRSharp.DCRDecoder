using System.Buffers.Binary;
using System.Diagnostics;

namespace SDRSharp.DCRDecoder.Vocoder;

/// <summary>Single audio-worker owner. Pipe failures have deadlines and kill the child.</summary>
public sealed class PythonVocoder : IVocoder
{
    private readonly Process process;
    private readonly byte[] request = new byte[8], response = new byte[320];
    private string error = "";
    private bool disposed;
    private readonly CancellationToken shutdown;
    public int SampleRate => 8000;
    public int SamplesPerFrame => 160;
    public PythonVocoder(string pythonPath, string helperPath, CancellationToken shutdown = default)
    {
        this.shutdown = shutdown;
        if (!Path.IsPathFullyQualified(pythonPath) || !File.Exists(pythonPath)) throw new FileNotFoundException("Select an existing Python executable.",pythonPath);
        if (!File.Exists(helperPath)) throw new FileNotFoundException("Vocoder helper missing.",helperPath);
        var start = new ProcessStartInfo(pythonPath) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-I"); start.ArgumentList.Add("-B");
        start.ArgumentList.Add("-u"); start.ArgumentList.Add(Path.GetFullPath(helperPath));
        process = new Process { StartInfo = start };
        process.ErrorDataReceived += (_,e) => { if (e.Data != null) Volatile.Write(ref error, e.Data.Length > 500 ? e.Data[..500] : e.Data); };
        try
        {
            process.Start(); process.BeginErrorReadLine();
            Exchange(0,4,10000);
            if (!response.AsSpan(0,4).SequenceEqual("DCR1"u8)) throw new InvalidDataException("Invalid vocoder handshake.");
        }
        catch { Dispose(); throw; }
    }
    private void Exchange(int requestBytes, int responseBytes, int timeoutMs = 2000)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        try { ExchangeAsync(requestBytes,responseBytes,timeoutMs).GetAwaiter().GetResult(); }
        catch (Exception ex)
        {
            string detail = Volatile.Read(ref error);
            Dispose();
            throw new IOException("Vocoder pipe failed or timed out. " + detail,ex);
        }
    }
    private async Task ExchangeAsync(int count, int reply, int timeout)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        deadline.CancelAfter(timeout);
        if (count != 0)
        {
            await process.StandardInput.BaseStream.WriteAsync(request.AsMemory(0,count),deadline.Token).ConfigureAwait(false);
            await process.StandardInput.BaseStream.FlushAsync(deadline.Token).ConfigureAwait(false);
        }
        await process.StandardOutput.BaseStream.ReadExactlyAsync(response.AsMemory(0,reply),deadline.Token).ConfigureAwait(false);
    }
    public void Reset()
    {
        request[0] = (byte)'R'; Exchange(1,1);
        if (response[0] != (byte)'R') throw new InvalidDataException("Vocoder reset failed.");
    }
    public void Decode(ReadOnlySpan<byte> naturalFrame, Span<short> pcm)
    {
        if (naturalFrame.Length != 7 || pcm.Length != 160) throw new ArgumentException("Expected 7 bytes and 160 samples.");
        request[0] = (byte)'D'; naturalFrame.CopyTo(request.AsSpan(1)); Exchange(8,320);
        for (int i = 0; i < 160; i++) pcm[i] = BinaryPrimitives.ReadInt16LittleEndian(response.AsSpan(i * 2,2));
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        try { if (!process.HasExited) { process.Kill(entireProcessTree:true); process.WaitForExit(1000); } } catch (InvalidOperationException) { }
        finally { process.Dispose(); }
    }
}
