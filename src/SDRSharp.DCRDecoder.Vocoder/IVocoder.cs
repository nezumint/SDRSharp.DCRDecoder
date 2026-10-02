namespace SDRSharp.DCRDecoder.Vocoder;

public interface IVocoder : IDisposable
{
    int SampleRate { get; }
    int SamplesPerFrame { get; }
    void Reset();
    void Decode(ReadOnlySpan<byte> naturalFrame, Span<short> pcm);
}

public interface IAudioOutput : IDisposable
{
    int BufferedMilliseconds { get; }
    long Underruns { get; }
    void Write(ReadOnlySpan<short> samples);
    void Clear();
    void Complete();
    void Pump();
}
