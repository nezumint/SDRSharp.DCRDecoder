using System.Diagnostics;
using System.Threading.Channels;
using SDRSharp.DCRDecoder.Core;

namespace SDRSharp.DCRDecoder.Vocoder;

public sealed record AudioSettings(bool Enabled = false, bool PrivacyEnabled = false, int? PrivacyCode = null,
    bool Muted = false, int Volume = 70, int Device = -1, string PythonPath = "", string HelperPath = "");
public sealed record AudioSnapshot(string Status = "Disabled", int BufferMilliseconds = 0, long PcmFrames = 0,
    long CorrectedBits = 0, long FecRejected = 0, long DroppedGroups = 0, long Underruns = 0, string? Error = null);

/// <summary>Bounded, nonblocking decoder-to-audio handoff. Backend and output have one worker owner.</summary>
public sealed class AudioEngine : IRealtimeFrameSink, IDisposable
{
    private sealed record Work(DecodedFrame? Frame, long Epoch, int SourceGeneration, long Created);
    private readonly Channel<Work> queue = Channel.CreateBounded<Work>(new BoundedChannelOptions(8) {
        SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
    private readonly Func<AudioSettings,CancellationToken,IVocoder> makeVocoder;
    private readonly Func<AudioSettings,IAudioOutput> makeOutput;
    private readonly Action? invalidateOutput;
    private readonly CancellationTokenSource stop = new();
    private readonly Thread worker;
    private AudioSettings settings = new();
    private AudioSnapshot snapshot = new();
    private string inputState = "Disabled";
    private long epoch, dropped;
    private int sourceGeneration, disposed;
    public AudioSnapshot Snapshot => Volatile.Read(ref snapshot);
    public AudioSettings Settings => Volatile.Read(ref settings);
    public AudioEngine(Func<AudioSettings,CancellationToken,IVocoder> makeVocoder, Func<AudioSettings,IAudioOutput> makeOutput, Action? invalidateOutput = null)
    {
        this.makeVocoder = makeVocoder; this.makeOutput = makeOutput;
        this.invalidateOutput = invalidateOutput;
        worker = new Thread(Run) { IsBackground = true, Name = "DCR audio" }; worker.Start();
    }
    public void Apply(AudioSettings value)
    {
        if (value.Volume < 0 || value.Volume > 100 || value.PrivacyCode is < 1 or > 32767)
            throw new ArgumentOutOfRangeException(nameof(value),"Volume: 0..100; privacy code: 1..32767.");
        var old = Settings;
        Volatile.Write(ref settings,value);
        if (value == old with { Volume = value.Volume } && value.Volume != old.Volume) return;
        Invalidate(value.Enabled ? value.Muted ? "Muted" : "Waiting for voice" : "Disabled");
    }
    private void Invalidate(string state) { Volatile.Write(ref inputState,state); Interlocked.Increment(ref epoch); invalidateOutput?.Invoke(); }
    public void OnSourceReset(int generation)
    {
        int previous;
        do { previous = Volatile.Read(ref sourceGeneration); if (generation < previous) return; }
        while (Interlocked.CompareExchange(ref sourceGeneration,generation,previous) != previous);
        Invalidate(Settings.Enabled ? "Waiting for voice" : "Disabled");
    }
    public void OnFrame(DecodedFrame frame, int generation)
    {
        if (Volatile.Read(ref disposed) != 0 || generation != Volatile.Read(ref sourceGeneration)) return;
        var options = Settings;
        if (!options.Enabled || options.Muted) return;
        if (!frame.IsVoice)
        {
            // End marker is ordered after preceding audio, allowing the final syllable to drain.
            Enqueue(new Work(null,Interlocked.Read(ref epoch),generation,Stopwatch.GetTimestamp())); return;
        }
        if (!frame.Rich.ParityOk || frame.Sacch?.CrcOk != true) { Invalidate("Invalid control data"); return; }
        int privacy = frame.Sacch.CallStat;
        if (privacy != 0 && privacy != 1) { Invalidate("Unsupported privacy"); return; }
        if (privacy == 1 && (!options.PrivacyEnabled || options.PrivacyCode == null)) { Invalidate("Code required"); return; }
        Volatile.Write(ref inputState,"Receiving voice");
        Enqueue(new Work(frame,Interlocked.Read(ref epoch),generation,Stopwatch.GetTimestamp()));
    }
    private void Enqueue(Work item)
    {
        if (!queue.Writer.TryWrite(item)) { Interlocked.Increment(ref dropped); Invalidate("Audio queue overflow"); }
    }
    private void Run()
    {
        IVocoder? vocoder = null; IAudioOutput? output = null;
        AudioSettings? backendSettings = null;
        long activeEpoch = -1, frames = 0, corrections = 0, rejected = 0;
        bool ended = true; string? error = null;
        var natural = new byte[28]; var pcm = new short[640];
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var options = Settings;
                long nowEpoch = Interlocked.Read(ref epoch);
                try
                {
                    if (activeEpoch != nowEpoch)
                    {
                        output?.Clear(); ended = true; activeEpoch = nowEpoch; error = null;
                        if (backendSettings == null || backendSettings.Enabled != options.Enabled || backendSettings.PythonPath != options.PythonPath || backendSettings.HelperPath != options.HelperPath || backendSettings.Device != options.Device)
                        {
                            output?.Dispose(); output = null; vocoder?.Dispose(); vocoder = null;
                            backendSettings = options;
                        }
                    }
                    if (queue.Reader.TryRead(out var item))
                    {
                        if (item.Epoch != activeEpoch || item.SourceGeneration != Volatile.Read(ref sourceGeneration) || !options.Enabled || options.Muted) continue;
                        if (item.Frame == null) { output?.Complete(); ended = true; Volatile.Write(ref inputState,"Waiting for voice"); }
                        else if (error == null)
                        {
                            if (Stopwatch.GetElapsedTime(item.Created).TotalMilliseconds > 640)
                            { Interlocked.Increment(ref dropped); Invalidate("Audio backlog discarded"); continue; }
                            if (!AmbeFec.TryDecodeVoiceGroup(item.Frame.Ambe3600,natural,out int bits))
                            { rejected++; Invalidate("Uncorrectable AMBE"); continue; }
                            corrections += bits;
                            if (item.Frame.Sacch!.CallStat == 1)
                            {
                                // Re-check at the consumption boundary. Unknown/missing code never reaches a vocoder.
                                if (!options.PrivacyEnabled || options.PrivacyCode == null) { Invalidate("Code required"); continue; }
                                StandardPrivacy.TransformVoiceGroup(natural,natural,options.PrivacyCode.Value);
                            }
                            vocoder ??= makeVocoder(options,stop.Token); output ??= makeOutput(options);
                            if (ended) { vocoder.Reset(); ended = false; }
                            for (int i = 0; i < 4; i++) vocoder.Decode(natural.AsSpan(i*7,7),pcm.AsSpan(i*160,160));
                            if (stop.IsCancellationRequested || item.Epoch != Interlocked.Read(ref epoch) || item.SourceGeneration != Volatile.Read(ref sourceGeneration)) continue;
                            for (int i=0;i<pcm.Length;i++) pcm[i] = (short)(pcm[i] * options.Volume / 100);
                            output.Write(pcm); frames += 4;
                        }
                    }
                    output?.Pump();
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    try { output?.Dispose(); } catch { } output = null;
                    try { vocoder?.Dispose(); } catch { } vocoder = null;
                    ended = true;
                }
                int buffered = output?.BufferedMilliseconds ?? 0;
                string status = error != null ? "Backend error" : !options.Enabled ? "Disabled" : options.Muted ? "Muted" : buffered > 0 ? "Playing" : Volatile.Read(ref inputState);
                Volatile.Write(ref snapshot,new AudioSnapshot(status,buffered,frames,corrections,rejected,
                    Interlocked.Read(ref dropped),output?.Underruns ?? 0,error));
                stop.Token.WaitHandle.WaitOne(2);
            }
        }
        catch (Exception ex) { Volatile.Write(ref snapshot,new AudioSnapshot("Audio worker error",Error:ex.Message)); }
        finally
        {
            try { output?.Dispose(); } catch { }
            try { vocoder?.Dispose(); } catch { }
        }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed,1) != 0) return;
        stop.Cancel(); queue.Writer.TryComplete(); worker.Join(); stop.Dispose();
    }
}
