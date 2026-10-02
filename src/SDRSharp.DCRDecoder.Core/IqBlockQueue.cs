using System;
using System.Threading;

namespace SDRSharp.DCRDecoder.Core
{
    public struct IqSample { public float Real; public float Imag; }
    /// <summary>One producer, one consumer. Producer reserves, fills, then commits a preallocated slot.
    /// Consumer releases only after processing. Never reset indices while either thread is running.</summary>
    public sealed class IqBlockQueue
    {
        private readonly IqSample[][] buffers;
        private readonly int[] lengths, epochs;
        private readonly double[] rates;
        private long write, read, dropped;
        private readonly int capacity;
        public long DroppedBlocks => Interlocked.Read(ref dropped);
        public IqBlockQueue(int slots = 16, int maxBlockSamples = 65536)
        {
            if (slots < 2 || maxBlockSamples < 1) throw new ArgumentOutOfRangeException();
            capacity = maxBlockSamples;
            buffers = new IqSample[slots][]; lengths = new int[slots]; epochs = new int[slots]; rates = new double[slots];
            for (int i = 0; i < slots; i++) buffers[i] = new IqSample[maxBlockSamples];
        }
        public bool TryReserve(int length, out Span<IqSample> buffer)
        {
            buffer = default;
            if (length <= 0 || length > capacity || write - Volatile.Read(ref read) >= buffers.Length)
            { Interlocked.Increment(ref dropped); return false; }
            buffer = buffers[(int)(write % buffers.Length)].AsSpan(0, length); return true;
        }
        public void Commit(int length, double rate, int epoch)
        {
            int slot = (int)(write % buffers.Length);
            lengths[slot] = length; rates[slot] = rate; epochs[slot] = epoch;
            Volatile.Write(ref write, write + 1);
        }
        public bool TryPeek(out ReadOnlySpan<IqSample> buffer, out double rate, out int epoch)
        {
            buffer = default; rate = 0; epoch = 0;
            if (read == Volatile.Read(ref write)) return false;
            int slot = (int)(read % buffers.Length);
            buffer = buffers[slot].AsSpan(0, lengths[slot]); rate = rates[slot]; epoch = epochs[slot];
            return true;
        }
        public void Release() => Volatile.Write(ref read, read + 1);
    }
}
