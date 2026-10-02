using System;

namespace SDRSharp.DCRDecoder.Core
{
    /// <summary>Known-code transform ported from the supplied descramble_known_key.py.</summary>
    public static class StandardPrivacy
    {
        public const int PackedAmbeBytes = 7;
        public const int VoiceGroupBytes = 28;

        public static void GenerateSequence(int code, Span<byte> output)
        {
            if (code < 1 || code > 32767) throw new ArgumentOutOfRangeException(nameof(code));
            if (output.Length != 196) throw new ArgumentException("Expected 196 bits.", nameof(output));
            int state = code;
            for (int i = 0; i < output.Length; i++)
            {
                output[i] = (byte)(state & 1);
                int feedback = (state & 1) ^ ((state >> 1) & 1);
                state = (state >> 1) | (feedback << 14);
            }
        }

        /// <summary>Four 49-bit MSB-first frames, each padded to seven bytes. Resets PN per DCR frame.</summary>
        public static void TransformVoiceGroup(ReadOnlySpan<byte> input, Span<byte> output, int code)
        {
            if (input.Length != VoiceGroupBytes || output.Length != VoiceGroupBytes)
                throw new ArgumentException("Expected one 28-byte DCR voice group.");
            Span<byte> pn = stackalloc byte[196];
            GenerateSequence(code, pn);
            // A temporary result allows in-place and overlapping buffers safely.
            Span<byte> result = stackalloc byte[VoiceGroupBytes];
            result.Clear();
            for (int frame = 0; frame < 4; frame++)
                for (int bit = 0; bit < 49; bit++)
                {
                    int index = frame * 7 + bit / 8;
                    int shift = 7 - bit % 8;
                    int value = ((input[index] >> shift) & 1) ^ pn[frame * 49 + bit];
                    result[index] |= (byte)(value << shift);
                }
            result.CopyTo(output);
        }
    }
}
