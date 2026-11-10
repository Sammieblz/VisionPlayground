using System;
using System.Buffers;
using System.Threading;

namespace CoDriver
{
    // One owner at a time: readback -> bounded delivery queue -> socket writer.
    // Pool capacity is bounded; the wire always uses Length, not rented capacity.
    public sealed class CameraFrameBuffer : IDisposable
    {
        static readonly ArrayPool<byte> shared = ArrayPool<byte>.Create(8 * 1024 * 1024, 4);
        readonly ArrayPool<byte> pool;
        byte[] buffer;
        public int Length { get; }
        public byte[] Buffer =>
            buffer ?? throw new ObjectDisposedException(nameof(CameraFrameBuffer));

        public CameraFrameBuffer(int length, ArrayPool<byte> pool = null)
        {
            if (length < 1 || length > 8000000)
                throw new ArgumentOutOfRangeException(nameof(length));
            Length = length;
            this.pool = pool ?? shared;
            buffer = this.pool.Rent(length);
        }

        public void Dispose()
        {
            var released = Interlocked.Exchange(ref buffer, null);
            if (released != null)
                pool.Return(released);
        }
    }
}
