using UnityEngine;

namespace CoDriver
{
    // Explicit test harness, selected only by -qualification-driver. Never a
    // fallback for Comma and never an input to the camera observer or scorecards.
    public sealed class QualificationDriver
    {
        TrackPath route = new TrackPath(-400, -400, 400, 400, 24);
        float progress;
        Signal stoppedAt;
        float stopTime;
        BoxCollider body;
        string exitLane;
        float exitDistance;
        public string Status = "Scripted physics/performance test driver";

        public void Initialize(Vehicle vehicle, Town town)
        {
            body = vehicle.GetComponent<BoxCollider>();
            if (town.Map.IsDavis)
                route = town.Map.Circuit(town.Seed, 3);
            progress = route.NearestDistance(new Vector3(402, 0, 210));
            vehicle.Body.position = route.Position(progress) + Vector3.up * .2f;
            vehicle.Body.rotation = Quaternion.LookRotation(route.Direction(progress));
            vehicle.transform.SetPositionAndRotation(vehicle.Body.position, vehicle.Body.rotation);
            vehicle.Body.linearVelocity = vehicle.Body.angularVelocity = Vector3.zero;
        }

        public void Apply(Vehicle vehicle, Town town)
        {
            float best = progress,
                error = float.PositiveInfinity;
            for (float d = progress - 8; d < progress + 18; d += .5f)
            {
                float e = (route.Position(d) - vehicle.transform.position).sqrMagnitude;
                if (e < error)
                {
                    best = d;
                    error = e;
                }
            }
            progress = best;
            float lookahead = Mathf.Max(3, vehicle.Speed * .75f + vehicle.Profile.wheelbase);
            Vector3 axle =
                vehicle.transform.position
                - vehicle.transform.forward * vehicle.Profile.wheelbase * .5f;
            float axleProgress = route.NearestDistance(axle, progress);
            Vector3 local = vehicle.transform.InverseTransformDirection(
                route.Position(axleProgress + lookahead) - axle
            );
            float angle =
                Mathf.Atan2(2 * 2.7f * local.x, local.x * local.x + local.z * local.z)
                * Mathf.Rad2Deg;
            vehicle.CommandedRoadAngle = Mathf.Clamp(
                angle,
                -vehicle.Profile.maxSteering,
                vehicle.Profile.maxSteering
            );
            vehicle.Steer = vehicle.CommandedRoadAngle / vehicle.Profile.maxSteering;
            float bend = Vector3.Angle(route.Direction(progress), route.Direction(progress + 20));
            float target = bend > 15 ? 4 : 8;
            foreach (var walker in town.Walkers)
                if (walker.Crossing)
                    target = Mathf.Min(
                        target,
                        TrafficClearance.CrossingSpeedLimit(body, walker.transform.position)
                    );
            for (
                float preview = 2;
                preview < Mathf.Max(18, vehicle.Speed * vehicle.Speed / 6 + 8);
                preview += 3
            )
            {
                float at = axleProgress + preview;
                float curvature =
                    Vector3.Angle(route.Direction(at - 2), route.Direction(at + 2))
                    * Mathf.Deg2Rad
                    / 4;
                float limit = Mathf.Sqrt(2.2f / Mathf.Max(.001f, curvature));
                target = Mathf.Min(
                    target,
                    Mathf.Sqrt(limit * limit + 6 * Mathf.Max(0, preview - 3))
                );
            }
            if (town.Map.IsDavis)
            {
                var lane = town.Map.Nearest(
                    vehicle.transform.position,
                    vehicle.transform.forward,
                    false
                );
                if (lane != null && !lane.internalLane)
                {
                    float remaining =
                        lane.Path.Length - lane.Path.NearestDistance(vehicle.transform.position);
                    if (
                        !string.IsNullOrEmpty(lane.signal)
                        && town.Map.SignalById.TryGetValue(lane.signal, out var light)
                    )
                    {
                        char state = light.State(lane.signalIndex, Time.time - town.Epoch);
                        if (state != 'g' && state != 'G')
                            target = Mathf.Min(target, Mathf.Sqrt(Mathf.Max(0, remaining - 3) * 4));
                    }
                    if (
                        !string.IsNullOrEmpty(lane.junction)
                        && town.MappedJunctions.TryGetValue(lane.junction, out var junction)
                    )
                    {
                        if (remaining > 0 && remaining < 40)
                        {
                            if (exitLane != lane.id)
                            {
                                exitLane = lane.id;
                                exitDistance = progress + remaining + 25;
                                for (float d = remaining + 3; d < remaining + 75; d += 3)
                                {
                                    var following = town.Map.Nearest(
                                        route.Position(progress + d),
                                        route.Direction(progress + d),
                                        false
                                    );
                                    if (
                                        following != null
                                        && following.id != lane.id
                                        && !following.internalLane
                                    )
                                    {
                                        exitDistance = progress + d + body.size.z * .5f + 2;
                                        break;
                                    }
                                }
                            }
                            bool clear =
                                TrafficClearance.ClearExit(route, exitDistance, body)
                                && TrafficClearance.ClearJunctionSweep(
                                    route,
                                    progress + remaining,
                                    exitDistance,
                                    body
                                );
                            if (!clear)
                                junction.ReleaseBlockedApproach(vehicle);
                            if (!clear || !junction.Reserve(vehicle))
                                target = Mathf.Min(
                                    target,
                                    Mathf.Sqrt(
                                        Mathf.Max(0, remaining - junction.WaitingSetback) * 4
                                    )
                                );
                        }
                    }
                }
                target = Mathf.Min(
                    target,
                    TrafficClearance.SafeSpeed(route, progress, 0, vehicle.Speed, body),
                    TrafficClearance.ForwardSpeedLimit(body, vehicle.Speed, immediate: true)
                );
                vehicle.ApplyDesiredAcceleration(
                    Mathf.Clamp((target - vehicle.Speed) * 1.2f, -4, 1.5f)
                );
                vehicle.Handbrake = false;
                return;
            }
            var signal = town.ClosestSignal(vehicle.transform.position);
            Vector3 delta = signal.transform.position - vehicle.transform.position;
            float ahead = Vector3.Dot(delta, vehicle.transform.forward);
            bool north = Mathf.Abs(vehicle.transform.forward.z) > .7f;
            if (
                ahead > 12
                && ahead < 40
                && (!signal.Green(north) || (signal.StopControlled && stoppedAt != signal))
            )
            {
                target = Mathf.Min(target, Mathf.Sqrt(Mathf.Max(0, ahead - 16) * 3));
                if (signal.StopControlled && vehicle.Speed < .25f)
                {
                    stopTime += Time.deltaTime;
                    if (stopTime > 2)
                    {
                        stoppedAt = signal;
                        stopTime = 0;
                    }
                }
            }
            if (
                stoppedAt
                && (stoppedAt.transform.position - vehicle.transform.position).sqrMagnitude > 3600
            )
                stoppedAt = null;
            if (ahead > 8 && ahead < 40 && signal.Green(north) && !signal.Reserve(vehicle))
                target = Mathf.Min(target, Mathf.Sqrt(Mathf.Max(0, ahead - 16) * 3));
            target = Mathf.Min(
                target,
                TrafficClearance.SafeSpeed(route, progress, 0, vehicle.Speed, body)
            );
            float crossingAhead =
                (110 - vehicle.transform.position.z) * Mathf.Sign(vehicle.transform.forward.z);
            if (
                north
                && crossingAhead > 10
                && crossingAhead < 40
                && town.RailClosedAt(new Vector3(vehicle.transform.position.x, 0, 110))
            )
                target = Mathf.Min(target, Mathf.Sqrt(Mathf.Max(0, crossingAhead - 14) * 3));
            var origin = vehicle.transform.position + Vector3.up + vehicle.transform.forward * 2.7f;
            foreach (
                var hit in Physics.SphereCastAll(
                    origin,
                    .7f,
                    vehicle.transform.forward,
                    vehicle.Speed * 2 + 6
                )
            )
            {
                if (hit.collider.GetComponentInParent<Vehicle>() == vehicle)
                    continue;
                // Road ramps are traversable surfaces, not traffic obstacles.
                if (
                    !hit.collider.GetComponentInParent<TrafficActor>()
                    && !hit.collider.GetComponentInParent<Walker>()
                    && !hit.collider.GetComponentInParent<RailVehicle>()
                )
                    continue;
                target = Mathf.Min(target, Mathf.Max(0, (hit.distance - 3) / 2));
            }
            vehicle.ApplyDesiredAcceleration(
                Mathf.Clamp((target - vehicle.Speed) * 1.2f, -4, 1.5f)
            );
            vehicle.Handbrake = false;
        }
    }
}
