using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace CoDriver
{
    // A normal sidewalk journey cannot silently route across a road. Crossing
    // surfaces become available only after the pedestrian's look/wait decision.
    public static class PedestrianNavigation
    {
        public const int CrossingArea = 3;
        public const int WalkingMask = NavMesh.AllAreas & ~(1 << CrossingArea);

        public static void AddStreetFurniture(Town town)
        {
            Physics.SyncTransforms();
            foreach (var collider in town.GetComponentsInChildren<Collider>())
            {
                if (
                    !collider.enabled
                    || collider.isTrigger
                    || collider.attachedRigidbody
                    || collider.gameObject.layer != 0
                    || collider.GetComponentInParent<ActivityDoor>()
                )
                    continue;
                var bounds = collider.bounds;
                if (bounds.size.y < .35f || bounds.size.x > 3 || bounds.size.z > 3)
                    continue;
                // Small static furniture (signal posts, trees, bollards, benches)
                // must be present in the navigation bake as well as PhysX.
                // Conservative bounds keep every species clear of their shape.
                // Large building walls have their exact mesh sources separately.
                town.WalkSurfaces.Add(
                    new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Box,
                        transform = Matrix4x4.TRS(bounds.center, Quaternion.identity, Vector3.one),
                        size = bounds.size,
                        area = 1,
                    }
                );
            }
        }

        public static bool TryWalkRoute(
            Vector3 position,
            System.Random random,
            out NavMeshPath route,
            float minimumTravel = 4
        )
        {
            route = new NavMeshPath();
            float angle = (float)random.NextDouble() * Mathf.PI * 2;
            for (int attempt = 0; attempt < 32; attempt++)
            {
                float radius = 8 + (float)random.NextDouble() * 20;
                float heading = angle + attempt * 2.399963f;
                var proposal =
                    position + new Vector3(Mathf.Cos(heading), 0, Mathf.Sin(heading)) * radius;
                if (
                    !NavMesh.SamplePosition(proposal, out var target, 3, WalkingMask)
                    || Vector3.Distance(position, target.position) < minimumTravel
                    || !NavMesh.CalculatePath(position, target.position, WalkingMask, route)
                    || route.status != NavMeshPathStatus.PathComplete
                )
                    continue;
                float length = 0;
                var corners = route.corners;
                for (int i = 1; i < corners.Length; i++)
                    length += Vector3.Distance(corners[i - 1], corners[i]);
                if (length >= minimumTravel && length <= 40)
                    return true;
            }
            return false;
        }

        public static bool TryCrossing(
            Town town,
            Vector3 position,
            out Vector3 start,
            out Vector3 finish,
            float lateralOffset = 0
        )
        {
            var candidates = new List<(Vector3 centre, Vector3 right, float halfSpan)>();
            if (town.Map.IsDavis)
            {
                foreach (
                    var landmark in town
                        .Map.landmarks.Where(l => l.kind == "crossing")
                        .OrderBy(l => (l.position - position).sqrMagnitude)
                        .Take(24)
                )
                {
                    var lane = town.Map.Nearest(landmark.position, routable: false);
                    var right = Vector3.Cross(
                        Vector3.up,
                        lane.Path.Direction(lane.Path.NearestDistance(landmark.position))
                    );
                    candidates.Add((town.Map.CrossingCentre(landmark), right, 8));
                }
            }
            else
            {
                foreach (
                    var signal in town
                        .Signals.OrderBy(s => (s.transform.position - position).sqrMagnitude)
                        .Take(4)
                )
                foreach (float side in new[] { -1f, 1f })
                {
                    var p = signal.transform.position;
                    candidates.Add((p + Vector3.forward * side * 10, Vector3.right, 8.5f));
                    candidates.Add(
                        (
                            p + Vector3.right * side * 10,
                            Vector3.forward,
                            Mathf.Abs(p.z) < .1f ? 12 : 8.5f
                        )
                    );
                }
            }
            foreach (var candidate in candidates.OrderBy(c => (c.centre - position).sqrMagnitude))
            {
                var direction = candidate.right;
                if (Vector3.Dot(position - candidate.centre, direction) < 0)
                    direction = -direction;
                // Use the crosswalk's width, not one shared point for every
                // arrival and departure. The seeded caller varies each journey.
                var centre =
                    candidate.centre
                    + Vector3.Cross(Vector3.up, direction) * Mathf.Clamp(lateralOffset, -.8f, .8f);
                if (
                    !NavMesh.SamplePosition(
                        centre + direction * candidate.halfSpan,
                        out var near,
                        2,
                        WalkingMask
                    )
                    || !NavMesh.SamplePosition(
                        centre - direction * candidate.halfSpan,
                        out var far,
                        2,
                        WalkingMask
                    )
                )
                    continue;
                var approach = new NavMeshPath();
                var crossing = new NavMeshPath();
                if (
                    !NavMesh.CalculatePath(position, near.position, WalkingMask, approach)
                    || approach.status != NavMeshPathStatus.PathComplete
                    || !NavMesh.CalculatePath(
                        near.position,
                        far.position,
                        NavMesh.AllAreas,
                        crossing
                    )
                    || crossing.status != NavMeshPathStatus.PathComplete
                )
                    continue;
                start = near.position;
                finish = far.position;
                return true;
            }
            start = finish = default;
            return false;
        }
    }
}
