using System;
using System.Collections.Generic;
using System.Threading;

namespace CoDriver
{
    // GPU completions can arrive together even when exposure times are 50 ms apart.
    // Keep at most two waiting frames, and never send one queued for over 100 ms.
    // This absorbs short delivery bursts without creating an inference backlog.
    public sealed class CameraDeliveryQueue : IDisposable
    {
        public sealed class Packet : IDisposable
        {
            public readonly CameraFrameBuffer Image;
            public readonly string Header;
            public readonly double QueuedAt;

            public Packet(CameraFrameBuffer image, string header, double queuedAt)
            {
                Image = image;
                Header = header;
                QueuedAt = queuedAt;
            }

            public void Dispose() => Image.Dispose();
        }

        readonly object sync = new object();
        readonly Queue<Packet> waiting = new Queue<Packet>(2);
        bool disposed;
        int dropped;
        public int Dropped => Volatile.Read(ref dropped);

        // Transfers ownership even when the queue is closed or the frame is rejected.
        public void Submit(CameraFrameBuffer image, string header, double now)
        {
            lock (sync)
            {
                if (disposed || !double.IsFinite(now))
                {
                    image.Dispose();
                    return;
                }
                while (waiting.Count >= 2)
                    Drop();
                waiting.Enqueue(new Packet(image, header, now));
                Monitor.Pulse(sync);
            }
        }

        public Packet Take(double now)
        {
            lock (sync)
            {
                while (waiting.Count > 0)
                {
                    var packet = waiting.Peek();
                    double age = now - packet.QueuedAt;
                    if (double.IsFinite(age) && age >= 0 && age <= .1)
                        return waiting.Dequeue();
                    Drop();
                }
                return null;
            }
        }

        public void WaitForFrame()
        {
            lock (sync)
                if (!disposed && waiting.Count == 0)
                    Monitor.Wait(sync, 100);
        }

        void Drop()
        {
            waiting.Dequeue().Dispose();
            Interlocked.Increment(ref dropped);
        }

        public void Dispose()
        {
            lock (sync)
            {
                disposed = true;
                while (waiting.Count > 0)
                    waiting.Dequeue().Dispose();
                Monitor.PulseAll(sync);
            }
        }
    }
}
