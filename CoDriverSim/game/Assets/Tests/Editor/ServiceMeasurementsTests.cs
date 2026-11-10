using NUnit.Framework;

namespace CoDriver.Tests
{
    public class ServiceMeasurementsTests
    {
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
