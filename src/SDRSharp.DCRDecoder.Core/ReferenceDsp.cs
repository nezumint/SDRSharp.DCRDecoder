using System;

namespace SDRSharp.DCRDecoder.Core
{
    /// <summary>Receive filter, sync estimation and quantization used by the streaming decoder.</summary>
    public static class ReferenceDsp
    {
        public const int SampleRate = 60000;
        public const int SamplesPerSymbol = 25;
        public const int FrameSymbols = 192;
        private static readonly double[] Sync = { -3, 1, -3, 3, -3, -3, 3, 3, -1, 3 };

        private static double BesselI0(double x)
        {
            double term = 1, sum = 1;
            for (int k = 1; k < 100; k++)
            {
                term *= x * x / (4.0 * k * k);
                sum += term;
                if (term < sum * 1e-16) break;
            }
            return sum;
        }

        public static double[] MakeReceiveTaps()
        {
            const int fftLength = 4096, count = 500;
            const double period = 1.0 / 2400, alpha = 0.2;
            var taps = new double[count];
            double sum = 0, denominator = BesselI0(8);
            // Direct inverse DFT of the nonzero spectrum, equivalent to numpy IFFT + fftshift.
            for (int i = 0; i < count; i++)
            {
                double value = 0;
                for (int k = -fftLength / 2; k < fftLength / 2; k++)
                {
                    double f = k * (double)SampleRate / fftLength;
                    double af = Math.Abs(f);
                    double h = af < 960 ? 1 : af <= 1440
                        ? Math.Cos(period / (4 * alpha) * (2 * Math.PI * af - Math.PI * (1 - alpha) / period)) : 0;
                    if (h == 0) continue;
                    double x = Math.PI * f * period;
                    double pulse = x == 0 ? 1 : Math.Sin(x) / x;
                    value += h / Math.Max(Math.Abs(pulse), 1e-8) * Math.Cos(2 * Math.PI * k * (i - count / 2) / fftLength);
                }
                double position = 2.0 * i / (count - 1) - 1;
                taps[i] = value / fftLength * BesselI0(8 * Math.Sqrt(Math.Max(0, 1 - position * position))) / denominator;
                sum += taps[i];
            }
            for (int i = 0; i < count; i++) taps[i] /= sum;
            return taps;
        }

        public static (double Mse, double Scale, double Cfo) SyncMetrics(ReadOnlySpan<double> values)
        {
            if (values.Length < 10) throw new ArgumentException("Ten sync symbols required.");
            double sx = 0, sxx = 0, sy = 0, sy2 = 0, sxy = 0;
            for (int i = 0; i < 10; i++)
            {
                sx += Sync[i]; sxx += Sync[i] * Sync[i];
                sy += values[i]; sy2 += values[i] * values[i]; sxy += Sync[i] * values[i];
            }
            double variance = sxx - sx * sx / 10;
            double covariance = sxy - sx * sy / 10;
            double scale = covariance / variance;
            return (Math.Max((sy2 - sy * sy / 10 - covariance * covariance / variance) / 10, 0), scale, sy / 10 - scale * sx / 10);
        }

        public static sbyte[] Quantize(ReadOnlySpan<double> values)
        {
            if (values.Length != FrameSymbols) throw new ArgumentException("192 symbols required.");
            var fit = SyncMetrics(values);
            if (fit.Scale <= 0) throw new InvalidOperationException("Unexpected inverted scale.");
            var result = new sbyte[FrameSymbols];
            for (int i = 0; i < result.Length; i++)
            {
                double v = (values[i] - fit.Cfo) / fit.Scale;
                result[i] = (sbyte)(v <= -2 ? -3 : v <= 0 ? -1 : v <= 2 ? 1 : 3);
            }
            return result;
        }
    }
}
