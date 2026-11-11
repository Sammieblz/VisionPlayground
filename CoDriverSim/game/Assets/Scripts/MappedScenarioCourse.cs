using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CoDriver
{
    // Scenario/evaluation coordinates follow the public lane graph. These transforms
    // never enter camera perception or the human/Comma control boundary.
    public sealed class MappedScenarioCourse
    {
        public readonly TrackPath Path;
        public readonly float Anchor;
        public readonly string SourceId;
        public readonly Signal Signal;
        public readonly Vector3 StopLine;
        readonly float centreX;

        public MappedScenarioCourse(Town town, string scenario, float x)
        {
            centreX = x;
            var map = town.Map;
            Vector3 at = map.Spawn;
            float localZ = 0;
            string kind =
                scenario == "Stop compliance" ? "stop_sign"
                : scenario.Contains("crossing") && scenario != "Railway crossing" ? "crossing"
                : scenario == "Bus departure" ? "transit_stop"
                : "";
            var feature = map
                .landmarks.Where(l => l.kind == kind && map.Contains(l.position, 60))
                .OrderBy(l => (l.position - map.Spawn).sqrMagnitude)
                .FirstOrDefault();
            if (feature != null)
            {
                at = feature.position;
                SourceId = feature.sourceId;
            }
            if (scenario == "Animal crossing" || scenario == "Occluded pedestrian crossing")
                localZ = -10;
            if (scenario == "Stop compliance")
                localZ = -13;
            if (scenario == "Parking conflict")
            {
                at = town.ParkingBay;
                localZ = -43;
                SourceId = "simulation/parking-conflict";
            }
            if (scenario == "Construction closure")
            {
                at = town.Construction.transform.position;
                localZ = -90;
                SourceId = "simulation/construction";
            }
            if (scenario == "Emergency yielding")
            {
                at = town.EmergencyBay;
                localZ = -130;
                SourceId = "simulation/emergency-staging";
            }
            if (scenario == "Railway crossing")
            {
                feature = map
                    .landmarks.Where(l =>
                        l.subtype == "level_crossing"
                        && map.Contains(l.position, 60)
                        && Vector3.Distance(
                            town.Train.Path.Position(town.Train.Path.NearestDistance(l.position)),
                            l.position
                        ) < 8
                    )
                    .OrderBy(l => (l.position - map.Spawn).sqrMagnitude)
                    .FirstOrDefault();
                if (feature == null)
                    throw new System.InvalidOperationException(
                        "No qualified road/rail crossing on the active train alignment"
                    );
                at = feature.position;
                SourceId = feature.sourceId;
                localZ = 110;
            }
            if (scenario == "Bus departure")
            {
                feature = map
                    .landmarks.Where(l =>
                        l.kind == "transit_stop"
                        && Vector3.Distance(
                            town.Bus.Path.Position(town.Bus.Path.NearestDistance(l.position)),
                            l.position
                        ) < 12
                    )
                    .OrderBy(l => (l.position - map.Spawn).sqrMagnitude)
                    .First();
                at = feature.position;
                SourceId = feature.sourceId;
                localZ = -100;
            }
            MapLane anchor =
                feature != null
                && !string.IsNullOrEmpty(feature.laneId)
                && map.LaneById.ContainsKey(feature.laneId)
                    ? map.LaneById[feature.laneId]
                    : map.Nearest(at);
            if (scenario == "Signal compliance")
            {
                anchor = map
                    .lanes.Where(l =>
                        !l.internalLane && !l.boundary && !string.IsNullOrEmpty(l.signal)
                    )
                    .OrderBy(l => (l.points.Last() - map.Spawn).sqrMagnitude)
                    .First();
                at = map.SignalById[anchor.signal].position;
                SourceId = anchor.signal;
            }
            var route = new List<MapLane> { anchor };
            var used = new HashSet<string> { anchor.id };
            float length = anchor.Path.Length;
            for (int i = 0; i < 40 && length < 280; i++)
            {
                var first = route[0];
                var previous = map
                    .lanes.Where(l =>
                        !l.boundary && !used.Contains(l.id) && l.next.Contains(first.id)
                    )
                    .OrderBy(l =>
                        Vector3.Angle(l.Path.Direction(l.Path.Length), first.Path.Direction(0))
                    )
                    .FirstOrDefault();
                if (previous == null)
                    break;
                route.Insert(0, previous);
                used.Add(previous.id);
                length += previous.Path.Length;
            }
            length = 0;
            for (int i = 0; i < 40 && length < 300; i++)
            {
                var last = route.Last();
                var next = last
                    .next.Where(map.LaneById.ContainsKey)
                    .Select(id => map.LaneById[id])
                    .Where(l => !l.boundary && !used.Contains(l.id))
                    .OrderBy(l =>
                        Vector3.Angle(last.Path.Direction(last.Path.Length), l.Path.Direction(0))
                    )
                    .FirstOrDefault();
                if (next == null)
                    break;
                route.Add(next);
                used.Add(next.id);
                length += next.Path.Length;
            }
            Path = new TrackPath(route.SelectMany(l => l.points), false);
            StopLine = scenario == "Stop compliance" ? at : anchor.points.Last();
            Anchor = Path.NearestDistance(at) - localZ;
            Signal = town.ClosestSignal(at);
        }

        public Vector3 World(Vector3 local)
        {
            float d = Anchor + local.z;
            return Path.Position(d)
                + Vector3.Cross(Vector3.up, Path.Direction(d)) * (local.x - centreX)
                + Vector3.up * local.y;
        }

        public Vector3 Local(Vector3 world)
        {
            float d = Path.NearestDistance(world);
            var p = Path.Position(d);
            var right = Vector3.Cross(Vector3.up, Path.Direction(d));
            return new Vector3(centreX + Vector3.Dot(world - p, right), world.y - p.y, d - Anchor);
        }

        public Quaternion Rotation(float z) => Quaternion.LookRotation(Path.Direction(Anchor + z));
    }
}
