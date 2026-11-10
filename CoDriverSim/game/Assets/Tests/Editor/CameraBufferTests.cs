using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace CoDriver.Tests
{
    public class CameraBufferTests
    {
        sealed class TrackingPool : ArrayPool<byte>
        {
            public int Returned;

            public override byte[] Rent(int minimumLength) => new byte[minimumLength + 64];

            public override void Return(byte[] array, bool clearArray = false)
            {
                Interlocked.Increment(ref Returned);
            }
        }

        [Test]
        public void DroppingQueuedFramesAndClosingTheLinkReturnsEveryBuffer()
        {
            var pool = new TrackingPool();
            using (var link = new SensorLink())
            {
                link.Submit(new CameraFrameBuffer(3, pool), "{}");
                link.Submit(new CameraFrameBuffer(3, pool), "{}");
                Assert.That(pool.Returned, Is.Zero, "A two-frame completion burst was discarded");
                link.Submit(new CameraFrameBuffer(3, pool), "{}");
                Assert.That(pool.Returned, Is.EqualTo(1), "Superseded frame retained");
                link.Dispose();
                Assert.That(pool.Returned, Is.EqualTo(3), "Queued frame retained on shutdown");
                link.Submit(new CameraFrameBuffer(3, pool), "{}");
                Assert.That(pool.Returned, Is.EqualTo(4), "Late readback retained after shutdown");
            }
            Assert.That(pool.Returned, Is.EqualTo(4));
        }

        [Test]
        public void ShortCameraBurstPreservesExposureOrderWithoutKeepingObsoleteFrames()
        {
            var pool = new TrackingPool();
            using (var queue = new CameraDeliveryQueue())
            {
                queue.Submit(new CameraFrameBuffer(3, pool), "frame 11", 1);
                queue.Submit(new CameraFrameBuffer(3, pool), "frame 12", 1.002);
                using (var first = queue.Take(1.01))
                    Assert.That(first.Header, Is.EqualTo("frame 11"));
                using (var second = queue.Take(1.03))
                    Assert.That(second.Header, Is.EqualTo("frame 12"));
                Assert.That(queue.Dropped, Is.Zero);
                queue.Submit(new CameraFrameBuffer(3, pool), "frame 13", 2);
                queue.Submit(new CameraFrameBuffer(3, pool), "frame 14", 2.05);
                using (var fresh = queue.Take(2.12))
                    Assert.That(fresh.Header, Is.EqualTo("frame 14"));
                Assert.That(queue.Dropped, Is.EqualTo(1));
                Assert.That(queue.Take(2.13), Is.Null);
            }
            Assert.That(pool.Returned, Is.EqualTo(4));
        }

        [Test]
        public void CameraOverloadKeepsOnlyTheTwoNewestFramesAndReleasesClosedQueue()
        {
            var pool = new TrackingPool();
            var queue = new CameraDeliveryQueue();
            for (int i = 0; i < 20; i++)
                queue.Submit(new CameraFrameBuffer(3, pool), i.ToString(), 1);
            Assert.That(pool.Returned, Is.EqualTo(18));
            using (var newestButOne = queue.Take(1.01))
                Assert.That(newestButOne.Header, Is.EqualTo("18"));
            queue.Dispose();
            queue.Submit(new CameraFrameBuffer(3, pool), "late", 1.02);
            Assert.That(queue.Take(1.03), Is.Null);
            Assert.That(pool.Returned, Is.EqualTo(21));
        }

        [Test]
        public void CameraSocketDeliversBothFramesArrivingDuringAResponse()
        {
            var pool = new TrackingPool();
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using (var receivedFirst = new ManualResetEventSlim())
            using (var releaseResponse = new ManualResetEventSlim())
            using (var link = new SensorLink())
            {
                var received = Task.Run(() =>
                {
                    using (var socket = listener.AcceptTcpClient())
                    using (var stream = socket.GetStream())
                    {
                        socket.NoDelay = true;
                        stream.ReadTimeout = 3000;
                        var pixels = new byte[3];
                        for (int i = 0; i < 3; i++)
                        {
                            int headerLength = BitConverter.ToInt32(SensorLink.Read(stream, 4), 0);
                            int length = BitConverter.ToInt32(SensorLink.Read(stream, 4), 0);
                            SensorLink.Read(stream, headerLength);
                            pixels[i] = SensorLink.Read(stream, length)[0];
                            if (i == 0)
                            {
                                receivedFirst.Set();
                                if (!releaseResponse.Wait(3000))
                                    throw new TimeoutException("Test did not release the response");
                            }
                            var reply = Encoding.UTF8.GetBytes(
                                "{\"status\":\"ready\",\"image_bytes\":1}"
                            );
                            stream.Write(BitConverter.GetBytes(reply.Length), 0, 4);
                            stream.Write(reply, 0, reply.Length);
                            stream.WriteByte(0);
                        }
                        return pixels;
                    }
                });
                try
                {
                    link.Start(((IPEndPoint)listener.LocalEndpoint).Port);
                    var first = new CameraFrameBuffer(1, pool);
                    first.Buffer[0] = 11;
                    link.Submit(first, "{}");
                    Assert.That(receivedFirst.Wait(3000), Is.True);
                    foreach (byte value in new byte[] { 22, 33 })
                    {
                        var frame = new CameraFrameBuffer(1, pool);
                        frame.Buffer[0] = value;
                        link.Submit(frame, "{}");
                    }
                    releaseResponse.Set();
                    Assert.That(received.Wait(3000), Is.True);
                    Assert.That(received.Result, Is.EqualTo(new byte[] { 11, 22, 33 }));
                    Assert.That(pool.Returned, Is.EqualTo(3));
                    Assert.That(link.Dropped, Is.Zero);
                }
                finally
                {
                    releaseResponse.Set();
                    link.Dispose();
                    listener.Stop();
                }
            }
        }

        [Test]
        public void DisposingAFrameTwiceCannotReturnItTwiceOrExposeReleasedPixels()
        {
            var pool = new TrackingPool();
            var frame = new CameraFrameBuffer(3, pool);
            frame.Dispose();
            frame.Dispose();
            Assert.That(pool.Returned, Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => _ = frame.Buffer);
        }

        [Test]
        public void SocketSendsOnlyImageBytesAndReturnsBuffersAfterMalformedResponse()
        {
            var pool = new TrackingPool();
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using (var link = new SensorLink())
            {
                var received = Task.Run(() =>
                {
                    using (var socket = listener.AcceptTcpClient())
                    using (var stream = socket.GetStream())
                    {
                        stream.ReadTimeout = 3000;
                        int headerLength = BitConverter.ToInt32(SensorLink.Read(stream, 4), 0);
                        int imageLength = BitConverter.ToInt32(SensorLink.Read(stream, 4), 0);
                        SensorLink.Read(stream, headerLength);
                        byte[] image = SensorLink.Read(stream, imageLength);
                        stream.Write(new byte[4], 0, 4); // Invalid response size exercises failure cleanup.
                        return image;
                    }
                });
                try
                {
                    var frame = new CameraFrameBuffer(3, pool);
                    frame.Buffer[0] = 11;
                    frame.Buffer[1] = 22;
                    frame.Buffer[2] = 33;
                    link.Submit(frame, "{}");
                    link.Start(((IPEndPoint)listener.LocalEndpoint).Port);
                    Assert.That(received.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    Assert.That(received.Result, Is.EqualTo(new byte[] { 11, 22, 33 }));
                    Assert.That(
                        SpinWait.SpinUntil(
                            () => pool.Returned == 1 && link.Status.StartsWith("Disconnected:"),
                            3000
                        ),
                        Is.True
                    );
                    link.Submit(new CameraFrameBuffer(3, pool), "{}");
                    Assert.That(SpinWait.SpinUntil(() => pool.Returned == 2, 3000), Is.True);
                }
                finally
                {
                    link.Dispose();
                    listener.Stop();
                }
            }
        }
    }
}
