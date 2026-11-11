using NUnit.Framework;

namespace CoDriver.Tests
{
    public class ServiceMeasurementsTests
    {
        [Test]
        public void CameraCadenceRecoversFromRenderJitterWithoutDuplicateFrames()
        {
            var cadence = new CameraCadence();
            double now = 20;
            int captured = 0;
            for (int frame = 0; frame < 1800; frame++)
            {
                // Roughly 30 FPS, with a 150 ms render stall every two seconds.
                now += frame % 60 == 0 ? .15 : 1.0 / 30;
                if (cadence.Due(now))
                    captured++;
                Assert.IsFalse(
                    cadence.Due(now),
                    "The same render time cannot produce a second frame"
                );
            }
            Assert.Greater(captured / (now - 20), 19.5);
            Assert.LessOrEqual(captured / (now - 20), 20.1);
        }

        [Test]
        public void LongCameraStallDoesNotCreateUnboundedCatchUpWork()
        {
            var cadence = new CameraCadence();
            Assert.IsTrue(cadence.Due(0));
            int captured = 0;
            for (int frame = 0; frame < 100; frame++)
                if (cadence.Due(10 + frame * .01))
                    captured++;
            Assert.That(
                captured,
                Is.InRange(20, 22),
                "Only the bounded recovery window can be made up"
            );
            Assert.Greater(cadence.SkippedDeadlines, 190);
            Assert.IsFalse(cadence.Due(double.NaN));
            Assert.IsFalse(cadence.Due(double.PositiveInfinity));
            Assert.IsFalse(cadence.Due(-1));
            Assert.IsFalse(cadence.Due(5));
        }

        [Test]
        public void AServiceThatStopsCannotRetainItsPreviousThroughput()
        {
            var measurements = new ServiceMeasurements();
            measurements.Start(100);
            var result = new ObserverResult { received_frames = 200, processed_frames = 50 };
            measurements.Observe(result, 1);
            Assert.That(measurements.CameraHz(110), Is.EqualTo(20));
            Assert.That(measurements.DetectionHz(110), Is.EqualTo(5));
            measurements.Observe(result, 1); // Same cached result while disconnected.
            Assert.That(measurements.CameraHz(120), Is.EqualTo(10));
            Assert.That(measurements.DetectionHz(120), Is.EqualTo(2.5));
            measurements.Observe(
                new ObserverResult { received_frames = 100, processed_frames = 25 },
                2
            );
            Assert.That(measurements.Received, Is.EqualTo(300));
            Assert.That(measurements.CameraHz(130), Is.EqualTo(10));
        }

        [Test]
        public void ReadyFlagsExpireWithTheirActualCommandAndObservation()
        {
            var command = new CommaCommand
            {
                session_id = "current",
                healthy = true,
                engageable = true,
            };
            Assert.That(ServiceMeasurements.CommandFresh(command, "current", .2), Is.True);
            Assert.That(ServiceMeasurements.CommandFresh(command, "current", 10), Is.False);
            Assert.That(ServiceMeasurements.CommandFresh(command, "another", .1), Is.False);
            Assert.That(ServiceMeasurements.CommandFresh(command, "current", double.NaN), Is.False);
            var result = new ObserverResult { session_id = "current", timestamp = 100 };
            Assert.That(ServiceMeasurements.ObserverFresh(result, "current", 100.5), Is.True);
            Assert.That(ServiceMeasurements.ObserverFresh(result, "current", 110), Is.False);
            Assert.That(ServiceMeasurements.ObserverFresh(result, "current", 99), Is.False);
        }

        [Test]
        public void LateGpuCompletionCannotRegressAnObservationStream()
        {
            var order = new FrameDeliveryOrder();
            Assert.That(order.TryAdvance(10, 1), Is.True);
            Assert.That(order.TryAdvance(12, 1.1), Is.True);
            Assert.That(order.TryAdvance(11, 1.05), Is.False);
            Assert.That(order.TryAdvance(12, 1.1), Is.False);
            Assert.That(order.TryAdvance(13, 1.05), Is.False);
            Assert.That(order.TryAdvance(13, double.NaN), Is.False);
            Assert.That(order.TryAdvance(14, 1.2), Is.True);
            Assert.That(order.Dropped, Is.EqualTo(4));
        }
    }
}
