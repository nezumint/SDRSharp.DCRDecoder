using System;
using System.Collections.Generic;
using System.Numerics;

namespace SDRSharp.DCRDecoder.Core
{
    /// <summary>Experimental continuous 60 kSPS acquisition. Sync is fitted at every sample;
    /// adjacent-frame spacing confirms lock and each frame re-estimates sample phase/CFO.</summary>
    public sealed class StreamingDcrDecoder
    {
        private readonly double[] taps = ReferenceDsp.MakeReceiveTaps();
        private readonly double[] fir = new double[500];
        private readonly double[] samples = new double[32768];
        private readonly List<long> candidates = new List<long>();
        private readonly Queue<long> pending = new Queue<long>();
        private Complex previous;
        private bool havePrevious;
        private int firPosition;
        private long index = -1, cluster = -1, lastHit = -1, lastQueued = -10000, lastDecoded = -10000;
        private double bestMse;
        public event Action<DecodedFrame>? FrameDecoded;
        public event Action? LockLost;
        public bool Locked { get; private set; }
        public double ResidualCfo { get; private set; }
        public long ValidFrames { get; private set; }
        public long RejectedFrames { get; private set; }

        public void Reset()
        {
            Array.Clear(fir, 0, fir.Length); Array.Clear(samples, 0, samples.Length);
            previous = default; havePrevious = false; firPosition = 0;
            index = cluster = lastHit = -1; lastQueued = lastDecoded = -10000;
            candidates.Clear(); pending.Clear(); Locked = false; ResidualCfo = 0;
            ValidFrames = RejectedFrames = 0;
        }
        private double At(long position) => samples[(int)(position & (samples.Length - 1))];
        public void Push(Complex iq)
        {
            if (!havePrevious) { previous = iq; havePrevious = true; return; }
            Complex product = iq * Complex.Conjugate(previous); previous = iq;
            fir[firPosition] = Math.Atan2(product.Imaginary, product.Real) * 60000 / (2 * Math.PI);
            double filtered = 0;
            int p = firPosition;
            for (int i = 0; i < taps.Length; i++)
            {
                filtered += taps[i] * fir[p];
                if (--p < 0) p = fir.Length - 1;
            }
            if (++firPosition == fir.Length) firPosition = 0;
            samples[(int)(++index & (samples.Length - 1))] = filtered;
            if (index >= 225)
            {
                long start = index - 225;
                Span<double> sync = stackalloc double[10];
                for (int i = 0; i < 10; i++) sync[i] = At(start + i * 25);
                var fit = ReferenceDsp.SyncMetrics(sync);
                if (Math.Abs(fit.Scale) > 100 && Math.Abs(fit.Scale) < 500 && fit.Mse < 5000)
                {
                    if (cluster < 0 || fit.Mse < bestMse) { cluster = start; bestMse = fit.Mse; }
                    lastHit = start;
                }
                if (cluster >= 0 && start - lastHit > 100)
                {
                    Candidate(cluster); cluster = -1;
                }
            }
            while (pending.Count > 0 && index >= pending.Peek() + 191 * 25) DecodePending();
            if (Locked && index - lastDecoded > 3 * 4800) { Locked = false; LockLost?.Invoke(); }
        }
        private void Candidate(long start)
        {
            candidates.RemoveAll(old => start - old > 4825);
            foreach (long old in candidates)
            {
                if (Math.Abs(start - old - 4800) <= 25)
                {
                    QueueFrame(old); QueueFrame(start);
                    break;
                }
            }
            candidates.Add(start);
        }
        private void QueueFrame(long start)
        {
            if (start <= lastQueued + 100) return;
            pending.Enqueue(start); lastQueued = start;
        }
        private void DecodePending()
        {
            long start = pending.Dequeue();
            Span<double> values = stackalloc double[192];
            for (int i = 0; i < values.Length; i++) values[i] = At(start + i * 25);
            var fit = ReferenceDsp.SyncMetrics(values);
            if (fit.Scale < 0) for (int i = 0; i < values.Length; i++) values[i] = -values[i];
            var frame = DcrProtocol.Decode(ReferenceDsp.Quantize(values));
            if (!frame.Rich.ParityOk || (frame.Sacch != null && !frame.Sacch.CrcOk) || (frame.Pich != null && !frame.Pich.CrcOk))
            { RejectedFrames++; return; }
            ResidualCfo = fit.Cfo; lastDecoded = start; Locked = true; ValidFrames++;
            FrameDecoded?.Invoke(frame);
        }
    }
}
