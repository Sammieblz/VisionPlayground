using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
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
                Assert.That(pool.Returned, Is.EqualTo(1), "Superseded frame retained");
                link.Dispose();
                Assert.That(pool.Returned, Is.EqualTo(2), "Queued frame retained on shutdown");
                link.Submit(new CameraFrameBuffer(3, pool), "{}");
                Assert.That(pool.Returned, Is.EqualTo(3), "Late readback retained after shutdown");
            }
            Assert.That(pool.Returned, Is.EqualTo(3));
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
