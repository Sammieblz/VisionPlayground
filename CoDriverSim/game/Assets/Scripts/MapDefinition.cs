using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CoDriver
{
    [Serializable]
    public sealed class MapTurnLimits
    {
        public int schemaVersion;
        public string mapSha256,
            basis;
        public string[] carExcluded,
            busExcluded;
    }

    [Serializable]
    public sealed class MapMesh
    {
        public Vector3[] vertices;
        public int[] triangles;
    }

    [Serializable]
    public sealed class MapRing
    {
        public Vector3[] points;
    }

    [Serializable]
    public sealed class MapBuilding
    {
        public string sourceId,
            name;
        public string usage;
        public float height;
        public bool estimatedHeight;
        public MapMesh roof;
        public MapRing[] rings;
    }

    [Serializable]
    public sealed class MapRoad
    {
        public string id,
            sourceId,
            name,
            access,
            oneway;
        public string[] laneIds;
        public bool tunnel,
            estimatedWidth;
        public bool bridge;
        public string sourceLayer;
    }

    [Serializable]
    public sealed class MapLane
    {
        public string id,
            sourceId,
            name,
            signal;
        public string junction,
            access;
        public int signalIndex;
        public Vector3[] points;
        public string[] next;
        public float width,
            speedMps;
        public bool boundary,
            internalLane;

        [NonSerialized]
        public TrackPath Path;
    }

    [Serializable]
    public sealed class MapPhase
    {
        public float duration;
        public string state;
    }

    [Serializable]
    public sealed class MapSignal
    {
        public string id;
        public Vector3 position;
        public MapPhase[] phases;
        public bool estimatedTiming;

        [NonSerialized]
        public float ForcedRedUntil;

        public char State(int index, float time)
        {
            if (time < ForcedRedUntil)
                return 'r';
            float cycle = phases.Sum(p => p.duration);
            float at = Mathf.Repeat(time, cycle + 8);
            if (at >= cycle)
                return 'r';
            foreach (var p in phases)
            {
                if (at < p.duration)
                    return index >= 0 && index < p.state.Length ? p.state[index] : 'r';
                at -= p.duration;
            }
            return 'r';
        }

        public bool Walk(float time)
        {
            if (time < ForcedRedUntil)
                return false;
            float cycle = phases.Sum(p => p.duration);
            float at = Mathf.Repeat(time, cycle + 8);
            return at >= cycle + 1 && at < cycle + 7;
        }
    }

    [Serializable]
    public sealed class MapRail
    {
        public string id,
            name;
        public Vector3[] points;
    }

    [Serializable]
    public sealed class MapLandmark
    {
        public string sourceId,
            kind,
            name;
        public string subtype,
            laneId;
        public string[] controlledLaneIds;

        public bool ControlsLane(string id) =>
            laneId == id
            || (controlledLaneIds != null && System.Array.IndexOf(controlledLaneIds, id) >= 0);

        public Vector3 position;
    }

    [Serializable]
    public sealed class MapJunction
    {
        public string id,
            type;
        public Vector3 position;
    }

    // Shared navigation/world definition. There is no reference to the observer or actuators.
    public sealed class MapClosure
    {
        public string laneId;
        public Vector3 position,
            forward;
        public float width;
    }

    [Serializable]
    public sealed class MapDefinition
    {
        static readonly Unity.Profiling.ProfilerMarker nearestMarker =
            new Unity.Profiling.ProfilerMarker("CoDriver.MapNearest");
        public int schemaVersion;
        public string id,
            name,
            attribution,
            sourceSha256,
            spawnLane;
        public double latitude,
            longitude;
        public Vector3 boundsMin,
            boundsMax;
        public MapRoad[] roads = Array.Empty<MapRoad>();
        public MapLane[] lanes = Array.Empty<MapLane>();
        public MapBuilding[] buildings = Array.Empty<MapBuilding>();
        public MapSignal[] signals = Array.Empty<MapSignal>();
        public MapJunction[] junctions = Array.Empty<MapJunction>();
        public MapRail[] rail = Array.Empty<MapRail>();
        public MapLandmark[] landmarks = Array.Empty<MapLandmark>();
        public string[] parkingAccessBayIds = Array.Empty<string>();
        public MapMesh ground,
            roadSurface,
            underpassSurface,
            sidewalkSurface,
            parkingAccessSurface,
            parkingSurface;
        public string[] assumptions;

        [NonSerialized]
        public readonly List<MapClosure> Closures = new List<MapClosure>();

        [NonSerialized]
        public Dictionary<string, MapLane> LaneById;

        [NonSerialized]
        public Dictionary<string, MapSignal> SignalById;
        readonly Dictionary<Vector2Int, List<MapLane>> cells =
            new Dictionary<Vector2Int, List<MapLane>>();
        public bool IsDavis => id == "davis-downtown";
        readonly HashSet<string> carExcluded = new HashSet<string>(),
            busExcluded = new HashSet<string>();

        public bool FitsVehicle(string lane, bool bus = false) =>
            !(bus ? busExcluded : carExcluded).Contains(lane);

        public static MapDefinition Load(string id)
        {
            if (id != "davis-downtown")
                return Cedar();
            var resource = Resources.Load<TextAsset>("Maps/" + id);
            if (!resource)
                throw new InvalidOperationException(
                    "Missing compiled Davis map; run compile-district.py"
                );
            var map = JsonUtility.FromJson<MapDefinition>(resource.text);
            map.Index();
            var limitsResource = Resources.Load<TextAsset>("Maps/davis-turn-limits");
            if (!limitsResource)
                throw new InvalidOperationException(
                    "Run validate-district.py --write-routing before building Davis"
                );
            var limits = JsonUtility.FromJson<MapTurnLimits>(limitsResource.text);
            using (var sha = System.Security.Cryptography.SHA256.Create())
                if (
                    limits.schemaVersion != 1
                    || BitConverter
                        .ToString(sha.ComputeHash(resource.bytes))
                        .Replace("-", "")
                        .ToLowerInvariant() != limits.mapSha256
                )
                    throw new InvalidOperationException(
                        "Davis routing envelope limits do not match this map"
                    );
            map.carExcluded.UnionWith(limits.carExcluded);
            map.busExcluded.UnionWith(limits.busExcluded);
            SimulatedBridgeExtension.Apply(map);
            map.Index();
            return map;
        }

        void Index()
        {
            cells.Clear();
            LaneById = lanes.ToDictionary(l => l.id);
            SignalById = signals.ToDictionary(s => s.id);
            foreach (var lane in lanes)
            {
                lane.Path = new TrackPath(lane.points, false);
                for (float d = 0; d <= lane.Path.Length; d += 8)
                {
                    var point = lane.Path.Position(d);
                    var cell = new Vector2Int(
                        Mathf.FloorToInt(point.x / 24),
                        Mathf.FloorToInt(point.z / 24)
                    );
                    if (!cells.TryGetValue(cell, out var list))
                    {
                        list = new List<MapLane>();
                        cells[cell] = list;
                    }
                    if (!list.Contains(lane))
                        list.Add(lane);
                }
            }
            // Close outgoing district roads shortly beyond their final usable
            // junction. That junction remains available for a three-point turn;
            // drivers are not sent down a long dead end at the extraction edge.
            Closures.Clear();
            foreach (var lane in lanes.Where(l => l.boundary && !l.internalLane))
            {
                bool outgoing = Contains(lane.points[0], 4);
                float distance = outgoing
                    ? Mathf.Min(18, lane.Path.Length * .4f)
                    : Mathf.Max(0, lane.Path.Length - 18);
                var point = lane.Path.Position(distance);
                if (!Contains(point, 2))
                    continue;
                Closures.Add(
                    new MapClosure
                    {
                        laneId = lane.id,
                        position = point,
                        forward = lane.Path.Direction(distance),
                        width = lane.width,
                    }
                );
            }
        }

        public static MapDefinition Cedar()
        {
            var map = new MapDefinition
            {
                schemaVersion = 1,
                id = "cedar-grove",
                name = "Cedar Grove",
                latitude = 38.5453,
                longitude = -121.74165,
                attribution = "Synthetic regression town",
                boundsMin = new Vector3(-500, 0, -500),
                boundsMax = new Vector3(500, 0, 500),
            };
            var list = new List<MapLane>();
            foreach (float v in Town.Grid)
                for (int axis = 0; axis < 2; axis++)
                for (int direction = -1; direction <= 1; direction += 2)
                {
                    Vector3 a =
                        axis == 0
                            ? new Vector3(v + 2 * direction, 0, -480 * direction)
                            : new Vector3(-480 * direction, 0, v - 2 * direction);
                    Vector3 b = axis == 0 ? new Vector3(a.x, 0, -a.z) : new Vector3(-a.x, 0, a.z);
                    list.Add(
                        new MapLane
                        {
                            id = "cedar-" + list.Count,
                            name = axis == 0 ? "North Avenue" : "Cedar Street",
                            points = new[] { a, b },
                            width = axis == 1 && v == 0 ? 8 : 4.5f,
                            next = Array.Empty<string>(),
                            speedMps = 11.2f,
                        }
                    );
                }
            map.lanes = list.ToArray();
            map.Index();
            return map;
        }

        public void Gps(Vector3 point, out double lat, out double lon)
        {
            lat = latitude + point.z / 6378137.0 * 180 / Math.PI;
            lon =
                longitude
                + point.x / (6378137.0 * Math.Cos(latitude * Math.PI / 180)) * 180 / Math.PI;
        }

        public bool Contains(Vector3 p, float margin = 0) =>
            p.x > boundsMin.x + margin
            && p.z > boundsMin.z + margin
            && p.x < boundsMax.x - margin
            && p.z < boundsMax.z - margin;

        public Vector3 CrossingCentre(MapLandmark crossing)
        {
            var lane = Nearest(crossing.position, routable: false);
            float at = lane.Path.NearestDistance(crossing.position);
            var right = Vector3.Cross(Vector3.up, lane.Path.Direction(at));
            return lane.Path.Position(at) - right * lane.width * .5f;
        }

        public MapLane Nearest(Vector3 point, Vector3 forward = default, bool routable = true)
        {
            using var measurement = nearestMarker.Auto();
            MapLane best = null;
            float error = float.PositiveInfinity;
            var cell = new Vector2Int(
                Mathf.FloorToInt(point.x / 24),
                Mathf.FloorToInt(point.z / 24)
            );
            var candidates = new HashSet<MapLane>();
            for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
                if (cells.TryGetValue(cell + new Vector2Int(x, z), out var nearby))
                    foreach (var lane in nearby)
                        candidates.Add(lane);
            foreach (var lane in candidates.Count > 0 ? (IEnumerable<MapLane>)candidates : lanes)
            {
                if (routable && (lane.boundary || lane.next.Length == 0))
                    continue;
                float d = lane.Path.NearestDistance(point);
                float e = (lane.Path.Position(d) - point).sqrMagnitude;
                if (forward.sqrMagnitude > .1f && Vector3.Dot(forward, lane.Path.Direction(d)) < 0)
                    e += 64;
                if (e < error)
                {
                    best = lane;
                    error = e;
                }
            }
            return best;
        }

        public List<MapLane> Route(
            MapLane start,
            MapLane finish,
            bool bus = false,
            ISet<string> blocked = null
        )
        {
            if (start == null || finish == null)
                return new List<MapLane>();
            var parent = new Dictionary<string, string> { [start.id] = null };
            var queue = new Queue<string>();
            queue.Enqueue(start.id);
            while (queue.Count > 0)
            {
                string at = queue.Dequeue();
                if (at == finish.id)
                    break;
                foreach (string next in LaneById[at].next)
                    if (
                        LaneById.TryGetValue(next, out var lane)
                        && !lane.boundary
                        && FitsVehicle(next, bus)
                        && (blocked == null || !blocked.Contains(next))
                        && (
                            lane.access != "destination"
                                && lane.access != "customers"
                                && lane.access != "delivery"
                            || lane.sourceId == finish.sourceId
                            || lane.sourceId == start.sourceId
                        )
                        && !parent.ContainsKey(next)
                    )
                    {
                        parent[next] = at;
                        queue.Enqueue(next);
                    }
            }
            var route = new List<MapLane>();
            if (!parent.ContainsKey(finish.id))
                return route;
            for (string at = finish.id; at != null; at = parent[at])
                route.Add(LaneById[at]);
            route.Reverse();
            return route;
        }

        public TrackPath Circuit(int seed, int index, bool bus = false)
        {
            var choices = lanes
                .Where(l =>
                    !l.internalLane
                    && !l.boundary
                    && l.next.Length > 0
                    && l.Path.Length > 25
                    && l.access != "destination"
                    && l.access != "customers"
                    && l.access != "delivery"
                )
                .ToArray();
            var random = new System.Random(seed + index * 103);
            for (int attempt = 0; attempt < 300; attempt++)
            {
                var a = choices[(index * 17 + attempt) % choices.Length];
                var b = choices[random.Next(choices.Length)];
                var outbound = Route(a, b, bus);
                var inbound = Route(b, a, bus);
                if (outbound.Count < 5 || inbound.Count < 5)
                    continue;
                outbound.AddRange(inbound.Skip(1).Take(inbound.Count - 2));
                return new TrackPath(outbound.SelectMany(l => l.points), true);
            }
            throw new InvalidOperationException("No connected legal traffic circuit for " + id);
        }

        public TrackPath RoundTrip(Vector3 first, Vector3 second, bool bus = false)
        {
            var a = Nearest(first);
            var b = Nearest(second);
            var outbound = Route(a, b, bus);
            var inbound = Route(b, a, bus);
            if (outbound.Count < 2 || inbound.Count < 2)
            {
                if (bus)
                    throw new InvalidOperationException(
                        "No bus-safe round trip between selected stops"
                    );
                return Circuit(21, 0, bus);
            }
            outbound.AddRange(inbound.Skip(1).Take(inbound.Count - 2));
            return new TrackPath(outbound.SelectMany(l => l.points), true);
        }

        public Vector3 Spawn =>
            IsDavis
                ? LaneById[spawnLane].Path.Position(15) + Vector3.up * .2f
                : new Vector3(2.3f, .2f, -85);
        public Quaternion SpawnRotation =>
            IsDavis
                ? Quaternion.LookRotation(LaneById[spawnLane].Path.Direction(15))
                : Quaternion.identity;
    }
}
