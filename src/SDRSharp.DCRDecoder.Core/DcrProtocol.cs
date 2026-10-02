using System;
using System.Linq;

namespace SDRSharp.DCRDecoder.Core
{
    public sealed class RichInfo
    {
        public int F { get; internal set; }
        public int M { get; internal set; }
        public int D { get; internal set; }
        public bool ParityOk { get; internal set; }
    }
    public sealed class SacchInfo
    {
        public int MsgType { get; internal set; }
        public int CallStat { get; internal set; }
        public int UserCode { get; internal set; }
        public int MakerCode { get; internal set; }
        public bool CrcOk { get; internal set; }
        public int BitErrors { get; internal set; }
    }
    public sealed class PichInfo
    {
        public string Csm { get; internal set; } = "";
        public bool CrcOk { get; internal set; }
        public int BitErrors { get; internal set; }
    }
    public sealed class DecodedFrame
    {
        public RichInfo Rich { get; internal set; } = new RichInfo();
        public SacchInfo? Sacch { get; internal set; }
        public PichInfo? Pich { get; internal set; }
        public byte[] Ambe3600 { get; internal set; } = Array.Empty<byte>();
        public bool IsVoice => Rich.F == 1 && Rich.M == 3;
    }

    /// <summary>Frame-level reference-compatible decoder, independent of the IQ source and timing method.</summary>
    public static class DcrProtocol
    {
        public static sbyte[] Dewhiten(ReadOnlySpan<sbyte> symbols)
        {
            if (symbols.Length != 192) throw new ArgumentException("192 symbols required.");
            var result = symbols.ToArray();
            int[] state = {0,1,1,1,0,0,1,0,0};
            for (int i = 10; i < 192; i++)
            {
                int bit = state[8], next = state[4] ^ bit;
                for (int j = 8; j > 0; j--) state[j] = state[j - 1];
                state[0] = next;
                if (bit != 0) result[i] = (sbyte)-result[i];
            }
            return result;
        }
        public static byte[] SymbolsToBits(ReadOnlySpan<sbyte> symbols)
        {
            var result = new byte[symbols.Length * 2];
            for (int i = 0; i < symbols.Length; i++)
            {
                int dibit;
                switch (symbols[i])
                {
                    case -3: dibit = 3; break;
                    case -1: dibit = 2; break;
                    case 1: dibit = 0; break;
                    case 3: dibit = 1; break;
                    default: throw new ArgumentException("Invalid 4FSK symbol.");
                }
                result[2 * i] = (byte)(dibit >> 1);
                result[2 * i + 1] = (byte)(dibit & 1);
            }
            return result;
        }
        private static void ValidateBits(ReadOnlySpan<byte> bits, int length)
        {
            if (bits.Length != length) throw new ArgumentException($"Expected {length} bits.");
            foreach (byte b in bits) if (b > 1) throw new ArgumentException("Bits must be zero or one.");
        }
        private static int Number(ReadOnlySpan<byte> bits)
        {
            int value = 0;
            foreach (byte bit in bits) value = (value << 1) | bit;
            return value;
        }
        public static RichInfo DecodeRich(ReadOnlySpan<byte> bits)
        {
            ValidateBits(bits, 16);
            int parity = 0;
            for (int i = 0; i < 7; i++) parity ^= bits[2 * i];
            return new RichInfo { F = bits[0], M = (bits[6] << 2) | (bits[8] << 1) | bits[10], D = bits[12], ParityOk = parity == bits[14] };
        }

        private static byte[] Deinterleave(ReadOnlySpan<byte> input, int rows, int columns)
        {
            var output = new byte[input.Length];
            for (int c = 0; c < columns; c++)
                for (int r = 0; r < rows; r++) output[r * columns + c] = input[c * rows + r];
            return output;
        }
        private static (byte[] Bits, int Errors) Viterbi(int[] received, int steps, int dataSteps)
        {
            const int infinity = 1000000;
            var metric = Enumerable.Repeat(infinity, 16).ToArray();
            var parent = new byte[steps, 16];
            var decision = new byte[steps, 16];
            metric[0] = 0;
            for (int step = 0; step < steps; step++)
            {
                var next = Enumerable.Repeat(infinity, 16).ToArray();
                for (int state = 0; state < 16; state++)
                {
                    if (metric[state] == infinity) continue;
                    for (int bit = 0; bit <= (step < dataSteps ? 1 : 0); bit++)
                    {
                        int x1 = (state >> 3) & 1, x2 = (state >> 2) & 1, x3 = (state >> 1) & 1, x4 = state & 1;
                        int g1 = bit ^ x3 ^ x4, g2 = bit ^ x1 ^ x2 ^ x4;
                        int r1 = received[2 * step], r2 = received[2 * step + 1];
                        int cost = metric[state] + (r1 >= 0 && r1 != g1 ? 1 : 0) + (r2 >= 0 && r2 != g2 ? 1 : 0);
                        int destination = (bit << 3) | (state >> 1);
                        if (cost < next[destination])
                        {
                            next[destination] = cost;
                            parent[step, destination] = (byte)state;
                            decision[step, destination] = (byte)bit;
                        }
                    }
                }
                metric = next;
            }
            var decoded = new byte[steps];
            int current = 0;
            for (int step = steps - 1; step >= 0; step--)
            {
                decoded[step] = decision[step, current];
                current = parent[step, current];
            }
            return (decoded, metric[0]);
        }
        private static bool CheckCrc(ReadOnlySpan<byte> data, ReadOnlySpan<byte> crc, int width, int feedbackMask)
        {
            int state = (1 << width) - 1;
            foreach (byte bit in data)
            {
                int feedback = bit ^ ((state >> (width - 1)) & 1);
                state = (state << 1) & ((1 << width) - 1);
                if (feedback != 0) state ^= feedbackMask;
            }
            int forward = 0;
            for (int i = 0; i < width; i++) forward = (forward << 1) | ((state >> i) & 1);
            // Preserve the supplied reference's acceptance of both CRC bit orders.
            return Number(crc) == state || Number(crc) == forward;
        }
        public static SacchInfo DecodeSacch(ReadOnlySpan<byte> bits)
        {
            ValidateBits(bits, 60);
            byte[] interleaved = Deinterleave(bits, 5, 12);
            var punctured = new int[72];
            int source = 0, dest = 0;
            for (int repeat = 0; repeat < 6; repeat++)
                for (int p = 0; p < 6; p++)
                {
                    punctured[dest++] = interleaved[source++];
                    punctured[dest++] = p == 2 || p == 5 ? -1 : interleaved[source++];
                }
            var decoded = Viterbi(punctured, 36, 32);
            var b = decoded.Bits.AsSpan();
            return new SacchInfo {
                MsgType = Number(b.Slice(3, 5)), CallStat = Number(b.Slice(8, 2)),
                UserCode = Number(b.Slice(10, 9)), MakerCode = Number(b.Slice(19, 7)),
                CrcOk = CheckCrc(b.Slice(0, 26), b.Slice(26, 6), 6, 0x27), BitErrors = decoded.Errors
            };
        }
        public static PichInfo DecodePich(ReadOnlySpan<byte> bits)
        {
            ValidateBits(bits, 144);
            byte[] interleaved = Deinterleave(bits, 9, 16);
            var punctured = new int[192];
            for (int i = 0; i < 48; i++)
            {
                punctured[4 * i] = interleaved[3 * i]; punctured[4 * i + 1] = -1;
                punctured[4 * i + 2] = interleaved[3 * i + 1]; punctured[4 * i + 3] = interleaved[3 * i + 2];
            }
            var decoded = Viterbi(punctured, 96, 92);
            var b = decoded.Bits.AsSpan();
            string csm = "";
            for (int i = 0; i < 9; i++) csm += Number(b.Slice(4 * i, 4)).ToString("X");
            return new PichInfo { Csm = csm, CrcOk = CheckCrc(b.Slice(0, 80), b.Slice(80, 12), 12, 0x80f), BitErrors = decoded.Errors };
        }
        public static byte[] PackTraffic(ReadOnlySpan<byte> bits)
        {
            ValidateBits(bits, 288);
            var output = new byte[40];
            for (int frame = 0; frame < 4; frame++)
            {
                output[frame * 10] = 0x48;
                for (int i = 0; i < 9; i++) output[frame * 10 + 1 + i] = (byte)Number(bits.Slice(frame * 72 + i * 8, 8));
            }
            return output;
        }
        public static DecodedFrame Decode(ReadOnlySpan<sbyte> whitenedSymbols)
        {
            byte[] bits = SymbolsToBits(Dewhiten(whitenedSymbols));
            var frame = new DecodedFrame { Rich = DecodeRich(bits.AsSpan(20, 16)) };
            if (frame.Rich.F == 0) frame.Pich = DecodePich(bits.AsSpan(96, 144));
            else frame.Sacch = DecodeSacch(bits.AsSpan(36, 60));
            // Deliberately identical to reference extraction; live playback must separately gate validity/privacy.
            if (frame.IsVoice) frame.Ambe3600 = PackTraffic(bits.AsSpan(96, 288));
            return frame;
        }
    }
}
