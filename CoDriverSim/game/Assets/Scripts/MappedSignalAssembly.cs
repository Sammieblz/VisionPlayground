using System.Linq;
using UnityEngine;

namespace CoDriver
{
    public static class MappedSignalAssembly
    {
        // SUMO lane IDs end in _<index>. A mast belongs to the whole approach,
        // not each lane: an inner lane's curb is another lane's carriageway.
        public static string ApproachId(MapLane lane)
        {
            int separator = lane.id.LastIndexOf('_');
            return separator < 0 ? lane.id : lane.id.Substring(0, separator);
        }

        public static void Build(Town town, MapSignal signal)
        {
            foreach (
                var approach in town
                    .Map.lanes.Where(l => l.signal == signal.id && !l.internalLane)
                    .GroupBy(ApproachId)
            )
            {
                var lanes = approach.ToArray();
                var first = lanes[0];
                Vector3 forward = first.Path.Direction(first.Path.Length);
                forward.y = 0;
                forward.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                Vector3 origin = first.points[first.points.Length - 1];
                float curb =
                    lanes.Max(l =>
                        Vector3.Dot(l.points[l.points.Length - 1] - origin, right) + l.width * .5f
                    ) + .65f;
                float left = lanes.Min(l =>
                    Vector3.Dot(l.points[l.points.Length - 1] - origin, right)
                );
                var mast = new GameObject("Signal approach " + approach.Key);
                mast.transform.SetParent(town.transform, false);
                mast.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(forward));
                Art.Box(
                    "Signal pole",
                    mast.transform,
                    new Vector3(curb, 3.3f, 0),
                    new Vector3(.18f, 6.6f, .18f),
                    Art.Metal
                );
                Art.Box(
                    "Signal mast arm",
                    mast.transform,
                    new Vector3((curb + left) * .5f, 6.6f, 0),
                    new Vector3(curb - left + .18f, .18f, .18f),
                    Art.Metal
                );
                foreach (var lane in lanes)
                {
                    var head = new GameObject("Signal for " + lane.id);
                    head.transform.SetParent(mast.transform, false);
                    head.transform.SetPositionAndRotation(
                        lane.points[lane.points.Length - 1] + Vector3.up * 1.8f,
                        Quaternion.LookRotation(lane.Path.Direction(lane.Path.Length))
                    );
                    head.AddComponent<MappedSignalHead>()
                        .Initialize(signal, lane.signalIndex, town);
                }
            }
        }
    }
}
