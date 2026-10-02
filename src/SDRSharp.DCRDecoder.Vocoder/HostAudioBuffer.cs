namespace SDRSharp.DCRDecoder.Vocoder;

/// <summary>8 kHz mono PCM bridge. The host callback never waits for the producer.</summary>
public sealed class HostAudioBuffer : IAudioOutput
{
    private readonly object gate = new();
    private readonly float[] samples = new float[4000];
    private int head, count;
    private double phase;
    private bool playing, complete;
    private long underruns, overflows, callbacks;
    private int resetRequested, resetApplied;
    private double outputRate;
    public double OutputSampleRate => Volatile.Read(ref outputRate);
    public long Callbacks => Interlocked.Read(ref callbacks);
    public long Overflows => Interlocked.Read(ref overflows);
    public long Underruns => Interlocked.Read(ref underruns);
    public int BufferedMilliseconds { get { lock (gate) return count / 8; } }
    public void Write(ReadOnlySpan<short> pcm)
    {
        lock (gate)
        {
            ApplyReset();
            if (pcm.Length > samples.Length) throw new ArgumentException("PCM block too large.");
            if (count + pcm.Length > samples.Length) { Reset(); Interlocked.Increment(ref overflows); }
            foreach (short value in pcm) { samples[(head + count) % samples.Length] = value / 32768f; count++; }
            complete = false;
        }
    }
    /// <summary>Write interleaved L/R floats. sampleRate is frames per second, not float values per second.</summary>
    public void RenderStereo(Span<float> destination, double sampleRate, bool replace)
    {
        Interlocked.Increment(ref callbacks);
        Volatile.Write(ref outputRate, sampleRate);
        if (!replace) return;
        destination.Clear();
        if ((destination.Length & 1) != 0 || !double.IsFinite(sampleRate) || sampleRate < 8000 || sampleRate > 384000) return;
        if (!Monitor.TryEnter(gate)) return;
        try
        {
            ApplyReset();
            if (!playing && (count >= 1280 || (complete && count > 0))) playing = true;
            if (!playing) return;
            double step = 8000 / sampleRate;
            for (int i = 0; i < destination.Length; i += 2)
            {
                if (count == 0 || (count == 1 && !complete))
                {
                    if (!complete) Interlocked.Increment(ref underruns);
                    playing = false;
                    break;
                }
                float a = samples[head], b = count > 1 ? samples[(head + 1) % samples.Length] : a;
                float value = a + (b - a) * (float)phase;
                destination[i] = destination[i + 1] = value;
                // Advance once per stereo frame; advancing for both L and R doubles pitch and consumption.
                phase += step;
                if (phase >= 1 - 1e-10)
                {
                    phase = Math.Max(0, phase - 1);
                    head = (head + 1) % samples.Length;
                    count--;
                }
            }
        }
        finally { Monitor.Exit(gate); }
    }
    private void Reset() { head = count = 0; phase = 0; playing = complete = false; }
    public void Invalidate() => Interlocked.Increment(ref resetRequested);
    private void ApplyReset()
    {
        int requested = Volatile.Read(ref resetRequested);
        if (requested != resetApplied) { Reset(); resetApplied = requested; }
    }
    public void Clear() { lock (gate) Reset(); }
    public void Complete() { lock (gate) complete = true; }
    public void Pump() { }
    // AudioEngine owns output sessions; the bridge remains available to the registered hook.
    public void Dispose() => Clear();
}
