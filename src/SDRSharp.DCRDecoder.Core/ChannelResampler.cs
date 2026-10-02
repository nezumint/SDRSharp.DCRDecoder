using System;
using System.Numerics;

namespace SDRSharp.DCRDecoder.Core
{
    /// <summary>Worker-only bandlimited resampler for already centered, decimated IQ.
    /// Raw RF channel extraction is intentionally not accepted by this class.</summary>
    public sealed class ChannelResampler
    {
        private const int Half = 64, Phases = 512, Width = 129;
        private readonly Complex[] history = new Complex[512];
        private readonly double[][] coefficients;
        private readonly double ratio;
        private readonly bool bypass;
        private long index = -1;
        private double next;
        public ChannelResampler(double sampleRate)
        {
            if (double.IsNaN(sampleRate) || sampleRate < 12000 || sampleRate > 192000)
                throw new ArgumentOutOfRangeException(nameof(sampleRate), "Supported centered IQ rates: 12–192 kSPS.");
            bypass = sampleRate == 60000;
            ratio = sampleRate / 60000;
            coefficients = new double[Phases][];
            if (bypass) return;
            double cutoff = Math.Min(5000, sampleRate * 0.4) / sampleRate;
            for (int phase = 0; phase < Phases; phase++)
            {
                var taps = coefficients[phase] = new double[Width];
                double sum = 0, fraction = (double)phase / Phases;
                for (int tap = 0; tap < Width; tap++)
                {
                    double delta = tap - Half - fraction;
                    double x = 2 * Math.PI * cutoff * delta;
                    double sinc = x == 0 ? 1 : Math.Sin(x) / x;
                    double window = 0.42 + 0.5 * Math.Cos(Math.PI * delta / (Half + 1)) + 0.08 * Math.Cos(2 * Math.PI * delta / (Half + 1));
                    taps[tap] = 2 * cutoff * sinc * window; sum += taps[tap];
                }
                for (int tap = 0; tap < Width; tap++) taps[tap] /= sum;
            }
        }
        public void Push(Complex sample, Action<Complex> output)
        {
            if (bypass) { output(sample); return; }
            history[(int)(++index & 511)] = sample;
            while (Math.Floor(next) + Half <= index)
            {
                long center = (long)Math.Floor(next);
                int phase = Math.Min(Phases - 1, (int)((next - center) * Phases));
                var taps = coefficients[phase];
                Complex value = default;
                for (int tap = 0; tap < Width; tap++)
                {
                    long position = center - Half + tap;
                    if (position >= 0) value += history[(int)(position & 511)] * taps[tap];
                }
                output(value); next += ratio;
            }
        }
    }
}
