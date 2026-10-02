namespace SDRSharp.DCRDecoder.Core
{
    /// <summary>Implementations must only perform bounded nonblocking publication.
    /// OnSourceReset can run on the SDR DSP callback; no decode, lock or IO is allowed there.</summary>
    public interface IRealtimeFrameSink
    {
        void OnFrame(DecodedFrame frame, int generation);
        void OnSourceReset(int generation);
    }
}
