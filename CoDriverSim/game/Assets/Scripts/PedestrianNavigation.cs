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

        public static bool TryCrossing(
            Town town,
            Vector3 position,
            out Vector3 start,
            out Vector3 finish
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
                if (
                    !NavMesh.SamplePosition(
                        candidate.centre + direction * candidate.halfSpan,
                        out var near,
                        2,
                        WalkingMask
                    )
                    || !NavMesh.SamplePosition(
                        candidate.centre - direction * candidate.halfSpan,
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
