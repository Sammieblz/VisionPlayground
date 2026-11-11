using UnityEngine;

namespace CoDriver
{
    public static class TrafficClearance
    {
        static readonly Unity.Profiling.ProfilerMarker speedMarker =
            new Unity.Profiling.ProfilerMarker("CoDriver.PathClearance");
        public const int TrafficLayer = 10,
            PlayerLayer = 11,
            WalkerLayer = 12,
            ParkedLayer = 13,
            RoadLayer = 14,
            RailLayer = 15;
        static readonly Collider[] nearby = new Collider[32];
        public static string LastBlocker { get; private set; }
        const int ActorMask =
            (1 << TrafficLayer)
            | (1 << PlayerLayer)
            | (1 << WalkerLayer)
            | (1 << ParkedLayer)
            | (1 << RailLayer);

        public static bool CrossingPathClear(Vector3 start, Vector3 end)
        {
            var delta = end - start;
            delta.y = 0;
            if (delta.sqrMagnitude < .01f)
                return false;
            // A stopped vehicle can still occupy the crosswalk. Waiting for its
            // full body to clear prevents walkers entering its flank and then
            // mutually blocking a driver that is trying to finish crossing.
            return !Physics.CheckBox(
                (start + end) * .5f + Vector3.up * .9f,
                new Vector3(.4f, .75f, delta.magnitude * .5f),
                Quaternion.LookRotation(delta),
                ActorMask & ~(1 << WalkerLayer),
                QueryTriggerInteraction.Ignore
            );
        }

        public static float CrossingSpeedLimit(BoxCollider body, Vector3 pedestrian)
        {
            // A person beside a bus already occupying the crossing must be able
            // to wait for it to clear. A radial stop rule deadlocks both actors.
            // Physical forward/curved-path clearance still protects the person.
            Vector3 local = body.transform.InverseTransformPoint(pedestrian) - body.center;
            if (Mathf.Abs(local.y + body.center.y) > 2.5f || Mathf.Abs(local.x) > 6)
                return float.PositiveInfinity;
            float front = body.size.z * .5f;
            if (local.z < -front - .5f)
                return float.PositiveInfinity;
            if (local.z < front)
                return 2;
            float clearance = Mathf.Max(0, local.z - front - 1.5f);
            return Mathf.Min(clearance / 1.4f, Mathf.Sqrt(8 * clearance));
        }

        public static float ForwardSpeedLimit(
            BoxCollider body,
            float speed,
            float deceleration = 4,
            bool immediate = false,
            bool staticObstacles = true
        )
        {
            var car = body.transform;
            LastBlocker = "";
            var origin = car.position + Vector3.up + car.forward * (body.size.z / 2 + .3f);
            if (
                Physics.BoxCast(
                    origin,
                    new Vector3(body.size.x / 2 + .15f, .65f, .1f),
                    car.forward,
                    out var hit,
                    car.rotation,
                    immediate
                        ? 3
                        : Mathf.Max(speed * 1.7f + 8, speed * speed / (2 * deceleration) + 12),
                    staticObstacles ? ~(1 << RoadLayer) : ActorMask,
                    QueryTriggerInteraction.Ignore
                ) && !hit.collider.transform.IsChildOf(car)
            )
            {
                LastBlocker = hit.collider.name;
                float clearance = Mathf.Max(0, hit.distance - (immediate ? .3f : 2));
                return Mathf.Min(clearance / 1.7f, Mathf.Sqrt(2 * deceleration * clearance));
            }
            return float.PositiveInfinity;
        }

        // When a turning vehicle is almost stopped, a straight nose cast can
        // point into the opposing queue even though the steered body clears it.
        // Check the actual pose along a short bicycle arc before permitting a
        // walking-speed continuation. Never move or rotate the physical body here.
        public static float TurningCreepLimit(BoxCollider body, float wheelbase, float roadAngle)
        {
            LastBlocker = "";
            Quaternion rotation = body.transform.rotation;
            Vector3 axle = body.transform.position - body.transform.forward * wheelbase * .5f;
            float curvature = Mathf.Tan(roadAngle * Mathf.Deg2Rad) / wheelbase;
            const float step = .1f;
            for (float ahead = 0; ahead <= 3; ahead += step)
            {
                if (ahead > 0)
                {
                    Quaternion half = Quaternion.AngleAxis(
                        curvature * step * Mathf.Rad2Deg * .5f,
                        Vector3.up
                    );
                    axle += half * rotation * Vector3.forward * step;
                    rotation = half * half * rotation;
                }
                Vector3 centre =
                    axle + rotation * (body.center + Vector3.forward * wheelbase * .5f);
                int count = Physics.OverlapBoxNonAlloc(
                    centre,
                    body.size * .5f + new Vector3(.08f, 0, .1f),
                    nearby,
                    rotation,
                    ~(1 << RoadLayer),
                    QueryTriggerInteraction.Ignore
                );
                for (int i = 0; i < count; i++)
                    if (nearby[i] != body && !nearby[i].transform.IsChildOf(body.transform))
                    {
                        LastBlocker = nearby[i].name;
                        return Mathf.Min(1, Mathf.Max(0, ahead - .4f) / 1.7f);
                    }
            }
            return 1;
        }

        public static bool ClearExit(
            TrackPath route,
            float distance,
            BoxCollider body,
            float lateral = 0
        )
        {
            LastBlocker = "None";
            var rotation = Quaternion.LookRotation(route.Direction(distance));
            int count = Physics.OverlapBoxNonAlloc(
                route.Position(distance) + rotation * (body.center + Vector3.right * lateral),
                body.size * .5f + new Vector3(.15f, 0, .6f),
                nearby,
                rotation,
                ActorMask,
                QueryTriggerInteraction.Ignore
            );
            for (int i = 0; i < count; i++)
                if (nearby[i] != body && !nearby[i].transform.IsChildOf(body.transform))
                {
                    LastBlocker =
                        nearby[i].name + " at=" + nearby[i].transform.position.ToString("F2");
                    return false;
                }
            return true;
        }

        public static float YieldOffset(MapLane lane, float distance, float width, float length)
        {
            // Stay entirely inside mapped drivable space and away from junctions.
            // Wider shoulders are used only when explicitly represented as lanes.
            if (
                lane == null
                || lane.internalLane
                || distance < length + 4
                || distance > lane.Path.Length - length - 8
            )
                return 0;
            return Mathf.Clamp((lane.width - width) * .5f - .25f, 0, 1.5f);
        }

        public static bool ClearJunctionSweep(
            TrackPath route,
            float begin,
            float end,
            BoxCollider body
        )
        {
            LastBlocker = "None";
            // The long bus needs a conservative turning allowance for body swing
            // while its rear axle follows the curve. Check the complete crossing
            // before entering, not merely the space immediately beyond its nose.
            for (float at = begin; at <= end; at += 2)
            {
                // Body swing belongs to the bend. Carrying the full bus allowance
                // along a straight exit incorrectly occupies the opposing lane and
                // can deadlock a bus against an otherwise clear oncoming vehicle.
                float curvature =
                    Vector3.Angle(route.Direction(at - 2), route.Direction(at + 2))
                    * Mathf.Deg2Rad
                    / 4;
                float margin =
                    body.size.z > 7
                        ? Mathf.Clamp(body.size.z * body.size.z * curvature / 8, .25f, 1.05f)
                        : .25f;
                var rotation = Quaternion.LookRotation(route.Direction(at));
                int count = Physics.OverlapBoxNonAlloc(
                    route.Position(at) + rotation * body.center,
                    body.size * .5f + new Vector3(margin, 0, .4f),
                    nearby,
                    rotation,
                    ActorMask,
                    QueryTriggerInteraction.Ignore
                );
                for (int i = 0; i < count; i++)
                    if (nearby[i] != body && !nearby[i].transform.IsChildOf(body.transform))
                    {
                        LastBlocker =
                            nearby[i].name
                            + " at="
                            + nearby[i].transform.position.ToString("F2")
                            + " sweep="
                            + route.Position(at).ToString("F2");
                        return false;
                    }
            }
            return true;
        }

        // Follow the curved lane ahead, not just a ray along the present heading.
        // This is traffic simulation logic; none of these contacts enter perception.
        public static float SafeSpeed(
            TrackPath route,
            float distance,
            float lateral,
            float speed,
            BoxCollider body,
            float deceleration = 4,
            bool staticObstacles = false
        )
        {
            using var measurement = speedMarker.Auto();
            LastBlocker = "";
            Vector3 size = body.size / 2 + new Vector3(.12f, 0, .15f);
            float safe = float.PositiveInfinity;
            float horizon = Mathf.Max(
                speed * 1.7f + 10,
                speed * speed / (2 * deceleration) + speed * .5f + 12
            );
            for (float ahead = 1; ahead <= horizon; ahead += 2)
            {
                Vector3 forward = route.Direction(distance + ahead);
                Quaternion rotation = Quaternion.LookRotation(forward);
                Vector3 position =
                    route.Position(distance + ahead)
                    + Vector3.Cross(Vector3.up, forward) * lateral
                    + Vector3.up * .1f;
                int count = Physics.OverlapBoxNonAlloc(
                    position + rotation * body.center,
                    size,
                    nearby,
                    rotation,
                    staticObstacles ? ~(1 << RoadLayer) : ActorMask,
                    QueryTriggerInteraction.Ignore
                );
                for (int i = 0; i < count; i++)
                {
                    if (nearby[i] == body || nearby[i].transform.IsChildOf(body.transform))
                        continue;
                    LastBlocker = nearby[i].name;
                    float clearance = Mathf.Max(0, ahead - 3);
                    // Respect this actor's braking limit as well as headway.
                    safe = Mathf.Min(
                        safe,
                        clearance / 1.7f,
                        Mathf.Sqrt(2 * deceleration * clearance)
                    );
                }
                if (!float.IsPositiveInfinity(safe))
                    break;
            }
            return safe;
        }
    }
}
