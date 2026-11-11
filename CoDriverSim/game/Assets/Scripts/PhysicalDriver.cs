using System.Collections.Generic;
using UnityEngine;

namespace CoDriver
{
    // NPC planner: only steering and pedals cross into the shared physical vehicle.
    // It is never used for player/Comma control or by the camera observer.
    public sealed class PhysicalDriver
    {
        static readonly Unity.Profiling.ProfilerMarker followMarker =
            new Unity.Profiling.ProfilerMarker("CoDriver.DriverPlan");
        static readonly Unity.Profiling.ProfilerMarker stepMarker =
            new Unity.Profiling.ProfilerMarker("CoDriver.DriverStep");
        public readonly RvpVehicleDynamics Dynamics;
        float steer,
            accelerator;
        float requestedSteer,
            requestedSpeed;
        bool reversing,
            holding = true;
        public bool ReverseRequested => reversing && !holding;
        TrackPath previousPath;
        float progress;

        public PhysicalDriver(Rigidbody body, VehicleProfile profile)
        {
            Dynamics = new RvpVehicleDynamics(body, profile);
        }

        public void Hold()
        {
            holding = true;
            Continue();
        }

        public void Follow(
            TrackPath path,
            float targetSpeed,
            float offset = 0,
            bool reverse = false
        )
        {
            using var measurement = followMarker.Auto();
            var body = Dynamics.Body;
            var tr = body.transform;
            float speed = Vector3.Dot(body.linearVelocity, tr.forward) * (reverse ? -1 : 1);
            float lookahead = Mathf.Max(3, Mathf.Abs(speed) * .75f + Dynamics.Profile.wheelbase);
            // Bicycle geometry is referenced to the unsteered rear axle in both
            // gears. Switching to the front axle in reverse creates a systematic
            // lateral parking error and leaves the body diagonally across its bay.
            Vector3 axle = body.position - tr.forward * Dynamics.Profile.wheelbase * .5f;
            float along = path.NearestDistance(axle, previousPath == path ? progress : float.NaN);
            progress = along;
            previousPath = path;
            Vector3 goal = path.Position(along + lookahead);
            if (!path.Closed && along + lookahead > path.Length)
                goal += path.Direction(path.Length) * (along + lookahead - path.Length);
            goal += Vector3.Cross(Vector3.up, path.Direction(along + lookahead)) * offset;
            Vector3 local = tr.InverseTransformDirection(goal - axle);
            float curvature = 2 * local.x / Mathf.Max(1, local.x * local.x + local.z * local.z);
            float requested = Mathf.Atan(curvature * Dynamics.Profile.wheelbase) * Mathf.Rad2Deg;
            requestedSteer = Mathf.Clamp(
                requested,
                -Dynamics.Profile.maxSteering,
                Dynamics.Profile.maxSteering
            );
            requestedSpeed = Mathf.Min(
                targetSpeed,
                Mathf.Sqrt(2.2f / Mathf.Max(.001f, Mathf.Abs(curvature)))
            );
            // Brake before the corner, rather than discovering its curvature
            // only after the physical car is already entering it.
            for (float ahead = 2; ahead < Mathf.Max(18, speed * speed / 6 + 8); ahead += 3)
            {
                float at = along + ahead;
                float bend =
                    Vector3.Angle(path.Direction(at - 2), path.Direction(at + 2))
                    * Mathf.Deg2Rad
                    / 4;
                float cornerSpeed = Mathf.Sqrt(2.2f / Mathf.Max(.001f, bend));
                requestedSpeed = Mathf.Min(
                    requestedSpeed,
                    Mathf.Sqrt(cornerSpeed * cornerSpeed + 6 * Mathf.Max(0, ahead - 3))
                );
            }
            reversing = reverse;
            holding = false;
            Continue();
        }

        // Planning is 10 Hz; forces, input smoothing and wheel contacts remain 100 Hz.
        public void Continue()
        {
            using var measurement = stepMarker.Auto();
            if (holding)
            {
                Dynamics.Step(
                    new VehicleControls { brake = 1, handbrake = true },
                    Time.fixedDeltaTime
                );
                return;
            }
            float speed =
                Vector3.Dot(Dynamics.Body.linearVelocity, Dynamics.Body.transform.forward)
                * (reversing ? -1 : 1);
            steer = Mathf.MoveTowards(steer, requestedSteer, Time.fixedDeltaTime * 90);
            float error = requestedSpeed - speed;
            accelerator = Mathf.MoveTowards(
                accelerator,
                Mathf.Clamp01(error * .45f),
                Time.fixedDeltaTime * .8f
            );
            float brake = requestedSpeed < .05f ? 1 : Mathf.Clamp01(-error * .5f);
            Dynamics.Step(
                new VehicleControls
                {
                    throttle = brake > 0 ? 0 : accelerator,
                    brake = brake,
                    roadWheelAngle = steer,
                    reverse = reversing,
                    handbrake = requestedSpeed < .05f && Mathf.Abs(speed) < .1f,
                },
                Time.fixedDeltaTime
            );
        }

        public static TrackPath AccessPath(
            Vector3 start,
            Vector3 startForward,
            Vector3 end,
            Vector3 endForward,
            bool reverse
        )
        {
            float handle = Mathf.Max(5, Vector3.Distance(start, end) * .55f);
            float sign = reverse ? -1 : 1;
            Vector3 a = start + startForward * handle * sign;
            Vector3 b = end - endForward * handle * sign;
            var points = new List<Vector3>();
            for (int i = 0; i <= 64; i++)
            {
                float t = i / 64f,
                    s = 1 - t;
                points.Add(
                    s * s * s * start + 3 * s * s * t * a + 3 * s * t * t * b + t * t * t * end
                );
            }
            return new TrackPath(points, false);
        }
    }
}
