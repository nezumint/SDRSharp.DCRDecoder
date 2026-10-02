using System;
using System.Numerics;
using System.Threading;

namespace SDRSharp.DCRDecoder.Core
{
    public sealed class ReceiverSnapshot
    {
        public string Status { get; internal set; } = "Stopped";
        public string Mode { get; internal set; } = "Unknown";
        public string Csm { get; internal set; } = "—";
        public string Privacy { get; internal set; } = "Unknown";
        public int? UserCode { get; internal set; }
        public int? MakerCode { get; internal set; }
        public double SampleRate { get; internal set; }
        public double Cfo { get; internal set; }
        public long Frames { get; internal set; }
        public long RejectedFrames { get; internal set; }
        public long AmbeFrames { get; internal set; }
        public long DroppedBlocks { get; internal set; }
        public string? Error { get; internal set; }
        public int Generation { get; internal set; }
    }
    /// <summary>Host-independent receiver lifetime. DSP producer only reserves/copies/publishes;
    /// this worker performs all DSP. Radio Stop makes it idle; Dispose cancels and joins it.</summary>
    public sealed class RealtimeReceiver : IDisposable
    {
        public IqBlockQueue Queue { get; } = new IqBlockQueue();
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly Thread worker;
        private readonly IRealtimeFrameSink? frameSink;
        private ReceiverSnapshot snapshot = new ReceiverSnapshot();
        private int generation, running, disposed;
        private double sampleRate;
        public ReceiverSnapshot Snapshot => Volatile.Read(ref snapshot);
        public int Generation => Volatile.Read(ref generation);
        public bool Running => Volatile.Read(ref running) != 0;
        public double SampleRate
        {
            get => Volatile.Read(ref sampleRate);
            set { if (Interlocked.Exchange(ref sampleRate, value) != value) Reset(); }
        }
        public RealtimeReceiver(IRealtimeFrameSink? frameSink = null)
        {
            this.frameSink = frameSink;
            worker = new Thread(() => {
                try { Work(); }
                catch (Exception ex) { Volatile.Write(ref snapshot, new ReceiverSnapshot { Status = "Error", Error = ex.Message }); }
            }) { IsBackground = true, Name = "DCR decoder" };
            worker.Start();
        }
        public void SetRunning(bool value)
        {
            if (Interlocked.Exchange(ref running, value ? 1 : 0) != (value ? 1 : 0)) Reset();
        }
        public void Reset()
        {
            int current = Interlocked.Increment(ref generation);
            frameSink?.OnSourceReset(current);
        }
        private void Work()
        {
            var decoder = new StreamingDcrDecoder();
            Action<Complex> sink = decoder.Push;
            ChannelResampler? resampler = null;
            int activeGeneration = -1;
            string csm = "—", mode = "Unknown", privacy = "Unknown";
            int? user = null, maker = null;
            long ambe = 0;
            string? error = null;
            decoder.FrameDecoded += frame => {
                if (frame.Pich != null) csm = frame.Pich.Csm;
                mode = frame.Rich.F == 0 ? "Sync" : frame.Rich.M == 3 ? "Voice" : frame.Rich.M == 5 ? "Idle" : "Unknown(" + frame.Rich.M + ")";
                if (frame.Sacch != null)
                {
                    user = frame.Sacch.UserCode; maker = frame.Sacch.MakerCode;
                    privacy = frame.Sacch.CallStat == 0 ? "OFF" : frame.Sacch.CallStat == 1 ? "Standard ON" : "Unknown(" + frame.Sacch.CallStat + ")";
                }
                ambe += frame.Ambe3600.Length / 10;
                if (activeGeneration == Generation) frameSink?.OnFrame(frame,activeGeneration);
            };
            decoder.LockLost += () => { csm = "—"; mode = privacy = "Unknown"; user = maker = null; frameSink?.OnSourceReset(Generation); };
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    int current = Generation;
                    if (activeGeneration != current)
                    {
                        decoder.Reset(); resampler = null; activeGeneration = current;
                        csm = "—"; mode = privacy = "Unknown"; user = maker = null; ambe = 0; error = null;
                    }
                    bool didWork = false;
                    if (Queue.TryPeek(out var block, out double rate, out int epoch))
                    {
                        try
                        {
                            if (Running && epoch == activeGeneration && epoch == Generation && error == null)
                            {
                                resampler ??= new ChannelResampler(rate);
                                for (int i = 0; i < block.Length; i++)
                                {
                                    if ((i & 255) == 0 && (cancellation.IsCancellationRequested || epoch != Generation || !Running)) break;
                                    resampler.Push(new Complex(block[i].Real, block[i].Imag), sink);
                                }
                                didWork = true;
                            }
                        }
                        catch (Exception ex) { error = ex.Message; decoder.Reset(); csm = "—"; mode = privacy = "Unknown"; user = maker = null; ambe = 0; }
                        finally { Queue.Release(); }
                    }
                    bool locked = decoder.Locked && Running && activeGeneration == Generation;
                    Volatile.Write(ref snapshot, new ReceiverSnapshot {
                        Status = error != null ? "Error" : !Running ? "Stopped" : locked ? "Locked" : "Searching",
                        Mode = locked ? mode : "Unknown", Csm = locked ? csm : "—", Privacy = locked ? privacy : "Unknown",
                        UserCode = locked ? user : null, MakerCode = locked ? maker : null,
                        SampleRate = SampleRate, Cfo = locked ? decoder.ResidualCfo : 0, Frames = decoder.ValidFrames,
                        RejectedFrames = decoder.RejectedFrames, AmbeFrames = ambe, DroppedBlocks = Queue.DroppedBlocks,
                        Error = error, Generation = activeGeneration
                    });
                    if (!didWork) cancellation.Token.WaitHandle.WaitOne(5);
                }
            }
            catch (Exception ex) { Volatile.Write(ref snapshot, new ReceiverSnapshot { Status = "Error", Error = ex.Message }); }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            SetRunning(false); cancellation.Cancel(); worker.Join(); cancellation.Dispose();
        }
    }
}
