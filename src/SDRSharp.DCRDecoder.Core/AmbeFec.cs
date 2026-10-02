using System;

namespace SDRSharp.DCRDecoder.Core
{
    /// <summary>DCR 4x18 deinterleave, Golay correction and C1 PN removal.
    /// Golay generator/parity conventions follow blip25-vocoder (MIT); see THIRD_PARTY_NOTICES.
    /// Wire order is verified against all 724 clear and 968 privacy reference frames.</summary>
    public static class AmbeFec
    {
        private static readonly uint[] Generator = {
            0b100000000000_11000111010, 0b010000000000_01100011101,
            0b001000000000_11110110100, 0b000100000000_01111011010,
            0b000010000000_00111101101, 0b000001000000_11011001100,
            0b000000100000_01101100110, 0b000000010000_00110110011,
            0b000000001000_11011100011, 0b000000000100_10101001011,
            0b000000000010_10010011111, 0b000000000001_10001110101
        };
        private static readonly uint[] Errors = BuildSyndromes();
        private static uint Encode23(uint info)
        {
            uint word = 0;
            for (int i = 0; i < 12; i++) if (((info >> (11 - i)) & 1) != 0) word ^= Generator[i];
            return word;
        }
        private static int Syndrome(uint word) => (int)((word ^ Encode23(word >> 11)) & 2047);
        private static uint[] BuildSyndromes()
        {
            var table = new uint[2048];
            for (int a = 0; a < 23; a++)
            {
                uint one = 1u << a; table[Syndrome(one)] = one;
                for (int b = a + 1; b < 23; b++)
                {
                    uint two = one | (1u << b); table[Syndrome(two)] = two;
                    for (int c = b + 1; c < 23; c++) { uint three = two | (1u << c); table[Syndrome(three)] = three; }
                }
            }
            return table;
        }
        private static int Count(uint word)
        {
            int count = 0;
            while (word != 0) { word &= word - 1; count++; }
            return count;
        }
        public static bool TryDecode(ReadOnlySpan<byte> wire, Span<byte> natural, out int correctedBits)
        {
            if (wire.Length != 9 || natural.Length != 7) throw new ArgumentException("Expected 9 wire bytes and 7 natural bytes.");
            Span<uint> c = stackalloc uint[4]; c.Clear();
            int offset = 0;
            for (int vector = 0; vector < 4; vector++)
            {
                int width = vector == 0 ? 24 : vector == 1 ? 23 : vector == 2 ? 11 : 14;
                for (int j = 0; j < width; j++, offset++)
                {
                    int bit = (offset % 18) * 4 + offset / 18;
                    c[vector] = (c[vector] << 1) | (uint)((wire[bit / 8] >> (7 - bit % 8)) & 1);
                }
            }
            uint repaired0 = (c[0] >> 1) ^ Errors[Syndrome(c[0] >> 1)];
            uint info0 = repaired0 >> 11;
            uint reencoded0 = (repaired0 << 1) | (uint)(Count(repaired0) & 1);
            int errors0 = Count(reencoded0 ^ c[0]);
            natural.Clear(); correctedBits = errors0;
            if (errors0 > 3) return false;
            uint state = info0 * 16, mask = 0;
            for (int i = 0; i < 23; i++) { state = (173 * state + 13849) & 65535; mask = (mask << 1) | (state >> 15); }
            uint demodulated = c[1] ^ mask;
            uint correction = Errors[Syndrome(demodulated)];
            correctedBits += Count(correction);
            uint info1 = (demodulated ^ correction) >> 11;
            ulong packed = (((((ulong)info0 << 12) | info1) << 11 | c[2]) << 14 | c[3]) << 7;
            for (int i = 6; i >= 0; i--) { natural[i] = (byte)packed; packed >>= 8; }
            return true;
        }
        public static bool TryDecodeVoiceGroup(ReadOnlySpan<byte> framed, Span<byte> natural, out int correctedBits)
        {
            if (framed.Length != 40 || natural.Length != 28) throw new ArgumentException("Expected 4 framed AMBE3600 packets and 28 natural bytes.");
            correctedBits = 0; natural.Clear();
            for (int frame = 0; frame < 4; frame++)
            {
                if (framed[frame * 10] != 0x48 || !TryDecode(framed.Slice(frame * 10 + 1,9), natural.Slice(frame * 7,7),out int errors))
                { natural.Clear(); return false; }
                correctedBits += errors;
            }
            return true;
        }
    }
}
