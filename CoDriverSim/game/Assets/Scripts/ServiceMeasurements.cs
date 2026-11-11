using System;

namespace CoDriver
{
    // Schedule at most one fresh camera render per display frame. Keep up to
    // 100 ms of cadence debt across jitter, without queuing obsolete images or
    // trying to replay all the missed captures after a long stall.
    public sealed class CameraCadence
    {
        const double Interval = .05,
            RecoveryWindow = .1;
        double next = double.NaN,
            last = -1;
        public long SkippedDeadlines { get; private set; }

        public bool Due(double now)
        {
            if (!double.IsFinite(now) || now < 0 || now <= last)
                return false;
            if (double.IsNaN(next))
                next = now;
            if (now < next)
                return false;
            if (now - next > RecoveryWindow)
            {
                long skipped = (long)Math.Ceiling((now - next - RecoveryWindow) / Interval);
                SkippedDeadlines += skipped;
                next += skipped * Interval;
            }
            next += Interval;
            last = now;
            return true;
        }
    }

    // GPU readback completion order is not the capture order. Obsolete pixels
    // must never enter the strictly ordered observation/recording connection.
    public sealed class FrameDeliveryOrder
    {
        long lastId = -1;
        double lastTime = -1;
        public int Dropped { get; private set; }

        public bool TryAdvance(long id, double timestamp)
        {
            if (
                id <= lastId
                || !double.IsFinite(timestamp)
                || timestamp < 0
                || timestamp <= lastTime
            )
            {
                Dropped++;
                return false;
            }
            lastId = id;
            lastTime = timestamp;
            return true;
        }
    }

    // Evaluation only. A cached successful response is not evidence of a live service.
    public sealed class ServiceMeasurements
    {
        int generation = -1,
            previousReceived,
            previousProcessed;
        public long Received { get; private set; }
        public long Processed { get; private set; }
        public double StartedAt { get; private set; }

        public void Start(double now) => StartedAt = now;

        public void Observe(ObserverResult result, int serviceGeneration)
        {
            if (result == null)
                return;
            if (generation != serviceGeneration)
            {
                generation = serviceGeneration;
                previousReceived = previousProcessed = 0;
            }
            Received += Math.Max(0, result.received_frames - previousReceived);
            Processed += Math.Max(0, result.processed_frames - previousProcessed);
            previousReceived = Math.Max(previousReceived, result.received_frames);
            previousProcessed = Math.Max(previousProcessed, result.processed_frames);
        }

        public float CameraHz(double now) => (float)(Received / Math.Max(.001, now - StartedAt));

        public float DetectionHz(double now) =>
            (float)(Processed / Math.Max(.001, now - StartedAt));

        public static bool CommandFresh(CommaCommand command, string session, double age) =>
            command != null
            && command.session_id == session
            && double.IsFinite(age)
            && age >= 0
            && age <= .25;

        public static bool ObserverFresh(ObserverResult result, string session, double now) =>
            result != null
            && result.session_id == session
            && double.IsFinite(result.timestamp)
            && now >= result.timestamp
            && now - result.timestamp < 1;
    }
}
