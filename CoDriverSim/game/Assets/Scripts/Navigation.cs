using System.Collections.Generic;
using UnityEngine;

namespace CoDriver
{
    // Explicit public navigation map. No actor positions or signal states enter it.
    public sealed class LaneGraph
    {
        public readonly Dictionary<Vector2Int, List<Vector2Int>> Neighbours =
            new Dictionary<Vector2Int, List<Vector2Int>>();

        public LaneGraph()
        {
            for (int x = 0; x < 5; x++)
            for (int z = 0; z < 5; z++)
            {
                var at = new Vector2Int(x, z);
                var links = new List<Vector2Int>();
                foreach (
                    var delta in new[]
                    {
                        Vector2Int.up,
                        Vector2Int.down,
                        Vector2Int.left,
                        Vector2Int.right,
                    }
                )
                {
                    var n = at + delta;
                    if (n.x >= 0 && n.x < 5 && n.y >= 0 && n.y < 5)
                        links.Add(n);
                }
                Neighbours[at] = links;
            }
        }

        public static Vector2Int Node(Vector3 p) =>
            new Vector2Int(
                Mathf.Clamp(Mathf.RoundToInt((p.x + 400) / 200), 0, 4),
                Mathf.Clamp(Mathf.RoundToInt((p.z + 400) / 200), 0, 4)
            );

        public static Vector3 Position(Vector2Int n) =>
            new Vector3(n.x * 200 - 400, 0, n.y * 200 - 400);

        public List<Vector3> Circuit(int seed, int index)
        {
            var start = new Vector2Int(index % 5, index / 5 % 5);
            var route = new List<Vector2Int> { start };
            var random = new System.Random(seed);
            int budget = 4096;
            if (!FindCircuit(route, random, ref budget))
            {
                int x = Mathf.Min(start.x, 3),
                    z = Mathf.Min(start.y, 3);
                route = new List<Vector2Int>
                {
                    new Vector2Int(x, z),
                    new Vector2Int(x + 1, z),
                    new Vector2Int(x + 1, z + 1),
                    new Vector2Int(x, z + 1),
                };
            }
            var points = new List<Vector3>();
            foreach (var node in route)
                points.Add(Position(node));
            return points;
        }

        bool FindCircuit(List<Vector2Int> route, System.Random random, ref int budget)
        {
            if (--budget < 0)
                return false;
            var candidates = new List<Vector2Int>(Neighbours[route[route.Count - 1]]);
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                var swap = candidates[i];
                candidates[i] = candidates[j];
                candidates[j] = swap;
            }
            foreach (var next in candidates)
            {
                if (next == route[0] && route.Count >= 4)
                    return true;
                if (route.Count >= 16 || route.Contains(next))
                    continue;
                route.Add(next);
                if (FindCircuit(route, random, ref budget))
                    return true;
                route.RemoveAt(route.Count - 1);
            }
            return false;
        }

        public List<Vector3> Route(Vector3 from, Vector3 to)
        {
            var start = Node(from);
            var finish = Node(to);
            var visited = new Dictionary<Vector2Int, Vector2Int>();
            var pending = new Queue<Vector2Int>();
            pending.Enqueue(start);
            visited[start] = start;
            while (pending.Count > 0)
            {
                var at = pending.Dequeue();
                if (at == finish)
                    break;
                foreach (var next in Neighbours[at])
                    if (!visited.ContainsKey(next))
                    {
                        visited[next] = at;
                        pending.Enqueue(next);
                    }
            }
            var result = new List<Vector3>();
            var current = finish;
            while (current != start)
            {
                result.Add(Position(current));
                current = visited[current];
            }
            result.Add(Position(start));
            result.Reverse();
            return result;
        }
    }

    public sealed class Navigation
    {
        public MapDefinition Map;
        public Vector3 Target;
        public readonly LaneGraph Graph = new LaneGraph();
        public List<Vector3> Route = new List<Vector3>();
        public string Instruction = "Explore Cedar Grove",
            Destination = "Free drive";
        int index;
        TrackPath planned;
        float nextRecalculation;

        public void Select(Vector3 from, string destination)
        {
            Destination = destination;
            Vector3 target =
                destination == "Station" ? new Vector3(-200, 0, 200)
                : destination == "Construction" ? new Vector3(200, 0, -200)
                : new Vector3(400, 0, 400);
            if (Map != null && Map.IsDavis)
            {
                var places = System.Array.FindAll(
                    Map.landmarks,
                    p => destination == "Station" ? p.subtype == "station" : p.kind == "parking"
                );
                if (places.Length > 0)
                    target = places[0].position;
            }
            SelectPoint(from, target, destination);
        }

        public void SelectPoint(Vector3 from, Vector3 target, string name)
        {
            Destination = name;
            Target = target;
            if (Map != null && Map.IsDavis)
            {
                var lanes = Map.Route(Map.Nearest(from), Map.Nearest(target));
                Route = new List<Vector3>();
                foreach (var lane in lanes)
                    Route.AddRange(lane.points);
                Instruction =
                    Route.Count == 0 ? "No legal route inside this district" : "Route to " + name;
                index = 0;
                planned = Route.Count > 1 ? new TrackPath(Route, false) : null;
                return;
            }
            Route = Graph.Route(from, target);
            planned = Route.Count > 1 ? new TrackPath(Route, false) : null;
            index = 0;
        }

        public void Update(Vector3 position, Vector3 forward)
        {
            if (Route.Count == 0)
                return;
            if (planned != null)
            {
                float d = planned.NearestDistance(position);
                if (
                    Vector3.Distance(planned.Position(d), position) > 12
                    && Time.unscaledTime >= nextRecalculation
                )
                {
                    nextRecalculation = Time.unscaledTime + 2;
                    SelectPoint(position, Target, Destination);
                    return;
                }
                if (
                    planned.Length - d < 18
                    && Vector3.Distance(position, planned.Position(planned.Length)) < 18
                )
                {
                    Instruction = "Arrived near " + Destination;
                    return;
                }
                var ahead = planned.Position(d + 35) - position;
                float angle = Vector3.SignedAngle(forward, ahead, Vector3.up);
                Instruction =
                    (
                        Mathf.Abs(angle) < 35 ? "Continue"
                        : angle > 0 ? "Turn right"
                        : "Turn left"
                    )
                    + " · "
                    + Destination;
                return;
            }
            while (index < Route.Count - 1 && Vector3.Distance(position, Route[index]) < 22)
                index++;
            Vector3 delta = Route[index] - position;
            if (delta.magnitude < 18 && index == Route.Count - 1)
            {
                Instruction = "Arrived near " + Destination;
                return;
            }
            float turn = Vector3.SignedAngle(forward, delta, Vector3.up);
            Instruction =
                (
                    Mathf.Abs(turn) < 35 ? "Continue"
                    : turn > 0 ? "Turn right"
                    : "Turn left"
                )
                + " in "
                + (delta.magnitude * 3.28084f).ToString("0")
                + " ft · "
                + Destination;
        }
    }
}
