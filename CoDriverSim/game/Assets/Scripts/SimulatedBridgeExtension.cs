using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CoDriver
{
    // Authored simulation addition, never represented as an OpenStreetMap fact.
    // The source Davis database and its verified turn exclusions stay unchanged.
    public static class SimulatedBridgeExtension
    {
        public const string LaneId = "simulation/bridge-loop/v1";
        public const string Outbound = "25001678#27_0";
        public const string Inbound = "-25001678#27_0";

        public static void Apply(MapDefinition map)
        {
            var departure = map.LaneById[Outbound];
            var arrival = map.LaneById[Inbound];
            var origin = departure.points.Last();
            var forward = departure.Path.Direction(departure.Path.Length);
            var right = Vector3.Cross(Vector3.up, forward);
            var points = new List<Vector3>();
            void Curve(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int samples = Mathf.CeilToInt(
                    (Vector3.Distance(a, b) + Vector3.Distance(b, c) + Vector3.Distance(c, d))
                        / 1.5f
                );
                for (int i = points.Count == 0 ? 0 : 1; i <= samples; i++)
                {
                    float t = (float)i / samples,
                        s = 1 - t;
                    var p =
                        a * (s * s * s)
                        + b * (3 * s * s * t)
                        + c * (3 * s * t * t)
                        + d * (t * t * t);
                    p.y = Height(Vector3.Dot(p - origin, forward));
                    points.Add(p);
                }
            }
            Vector3 P(float along, float across) => origin + forward * along + right * across;
            Curve(P(0, 0), P(60, 0), P(120, 0), P(180, 0));
            // Spacious terminal loop, with no reverse manoeuvre or invisible teleport.
            Curve(P(180, 0), P(213.14f, 0), P(240, -13.43f), P(240, -30));
            Curve(P(240, -30), P(240, -46.57f), P(213.14f, -60), P(180, -60));
            Curve(P(180, -60), P(140, -60), P(160, -3.2f), P(120, -3.2f));
            Curve(P(120, -3.2f), P(80, -3.2f), arrival.points[0] + forward * 40, arrival.points[0]);
            var extension = new MapLane
            {
                id = LaneId,
                sourceId = LaneId,
                name = "Bridge Loop (simulation addition)",
                width = 3.2f,
                speedMps = 8.94f,
                access = "yes",
                points = points.ToArray(),
                next = new[] { Inbound },
            };
            departure.boundary = arrival.boundary = false;
            departure.next = new[] { LaneId };
            map.lanes = map.lanes.Append(extension).ToArray();
            map.roads = map
                .roads.Append(
                    new MapRoad
                    {
                        id = LaneId,
                        sourceId = LaneId,
                        name = extension.name,
                        access = "yes",
                        oneway = "yes",
                        bridge = true,
                        sourceLayer = "simulation",
                        estimatedWidth = true,
                        laneIds = new[] { LaneId },
                    }
                )
                .ToArray();
            float previousMax = map.boundsMax.x;
            map.boundsMax.x = Mathf.Max(map.boundsMax.x, points.Max(p => p.x) + 35);
            map.name = "Downtown Davis + fictional bridge loop";
            map.assumptions = (map.assumptions ?? new string[0])
                .Append(
                    "Bridge Loop v1 is a fictional simulation addition east of the mapped district."
                )
                .ToArray();
            map.landmarks = map
                .landmarks.Append(
                    new MapLandmark
                    {
                        sourceId = LaneId,
                        kind = "simulation_addition",
                        name = "Fictional bridge loop",
                        position = P(75, -1.6f),
                    }
                )
                .ToArray();
            map.roadSurface = Join(map.roadSurface, Ribbon(points, 3.2f));
            // The source road mesh is clipped at the old district edge. Fill only
            // the previously absent portions of the two connecting lane surfaces.
            foreach (var connector in new[] { departure, arrival })
                map.roadSurface = Join(
                    map.roadSurface,
                    ClipX(Ribbon(connector.points, connector.width), previousMax)
                );
            map.ground = Join(map.ground, Terrain(map, previousMax, origin, forward));
            map.sidewalkSurface = Join(
                map.sidewalkSurface,
                Ribbon(
                    points
                        .Select(
                            (p, i) =>
                                p
                                + Vector3.Cross(Vector3.up, Direction(points, i)) * 2.35f
                                + Vector3.up * .12f
                        )
                        .ToArray(),
                    1.4f
                )
            );
        }

        public static float Height(float distance)
        {
            float t =
                distance < 60 ? Mathf.InverseLerp(10, 60, distance)
                : distance > 90 ? 1 - Mathf.InverseLerp(90, 140, distance)
                : 1;
            return 4 * t * t * (3 - 2 * t);
        }

        static Vector3 Direction(IList<Vector3> points, int index) =>
            (
                points[Mathf.Min(index + 1, points.Count - 1)] - points[Mathf.Max(0, index - 1)]
            ).normalized;

        public static MapMesh Ribbon(IList<Vector3> points, float width)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i < points.Count; i++)
            {
                var right =
                    Vector3.Cross(Vector3.up, Direction(points, i)).normalized * width * .5f;
                vertices.Add(points[i] - right);
                vertices.Add(points[i] + right);
                if (i == 0)
                    continue;
                int k = (i - 1) * 2;
                triangles.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });
            }
            return new MapMesh { vertices = vertices.ToArray(), triangles = triangles.ToArray() };
        }

        static MapMesh ClipX(MapMesh mesh, float minimum)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int t = 0; t < mesh.triangles.Length; t += 3)
            {
                var polygon = new List<Vector3>();
                for (int i = 0; i < 3; i++)
                {
                    var a = mesh.vertices[mesh.triangles[t + i]];
                    var b = mesh.vertices[mesh.triangles[t + (i + 1) % 3]];
                    if (a.x >= minimum)
                        polygon.Add(a);
                    if ((a.x >= minimum) != (b.x >= minimum))
                        polygon.Add(Vector3.Lerp(a, b, (minimum - a.x) / (b.x - a.x)));
                }
                int start = vertices.Count;
                vertices.AddRange(polygon);
                for (int i = 1; i < polygon.Count - 1; i++)
                    triangles.AddRange(new[] { start, start + i, start + i + 1 });
            }
            return new MapMesh { vertices = vertices.ToArray(), triangles = triangles.ToArray() };
        }

        static MapMesh Terrain(MapDefinition map, float minimum, Vector3 origin, Vector3 forward)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            int columns = Mathf.CeilToInt((map.boundsMax.x - minimum) / 5);
            int rows = Mathf.CeilToInt((map.boundsMax.z - map.boundsMin.z) / 5);
            for (int z = 0; z <= rows; z++)
            for (int x = 0; x <= columns; x++)
            {
                var p = new Vector3(
                    Mathf.Lerp(minimum, map.boundsMax.x, (float)x / columns),
                    0,
                    Mathf.Lerp(map.boundsMin.z, map.boundsMax.z, (float)z / rows)
                );
                float channel = Mathf.Abs(Vector3.Dot(p - origin, forward) - 75);
                p.y = Mathf.Lerp(
                    -2.8f,
                    -.16f,
                    Mathf.SmoothStep(0, 1, Mathf.InverseLerp(13, 22, channel))
                );
                vertices.Add(p);
                if (x == 0 || z == 0)
                    continue;
                int k = z * (columns + 1) + x;
                triangles.AddRange(
                    new[] { k, k - columns - 2, k - 1, k, k - columns - 1, k - columns - 2 }
                );
            }
            return new MapMesh { vertices = vertices.ToArray(), triangles = triangles.ToArray() };
        }

        static MapMesh Join(MapMesh a, MapMesh b) =>
            new MapMesh
            {
                vertices = a.vertices.Concat(b.vertices).ToArray(),
                triangles = a
                    .triangles.Concat(b.triangles.Select(i => i + a.vertices.Length))
                    .ToArray(),
            };

        public static void BuildScenery(Town town)
        {
            var departure = town.Map.LaneById[Outbound];
            var origin = departure.points.Last();
            var forward = departure.Path.Direction(departure.Path.Length);
            var right = Vector3.Cross(Vector3.up, forward);
            var centre = origin + forward * 75 - right * 1.6f;
            var bridge = Realism.Spawn(
                "Bridge",
                town.transform,
                centre + Vector3.up * 3.99f,
                Quaternion.LookRotation(forward).eulerAngles.y
            );
            // The source's outer bounds include abutment feet. Its actual truss
            // faces start at |X|=3.7 m, so size the clear span from that geometry.
            if (bridge)
                bridge.transform.localScale = new Vector3(1.3f, 1, 1);
            var waterMaterial = new Material(Shader.Find("CoDriver/WaterSurface"));
            RuntimeResources.Register(town.gameObject, waterMaterial);
            waterMaterial.SetColor("_BaseColor", new Color(.055f, .15f, .16f));
            var water = Art.Marking(
                "Simulated waterway",
                town.transform,
                new Vector3(centre.x, -.35f, centre.z),
                new Vector2(450, 25),
                waterMaterial
            );
            water.transform.rotation = Quaternion.LookRotation(forward);
            for (int side = -1; side <= 1; side += 2)
            {
                var rail = Art.Box(
                    "Bridge guardrail",
                    town.transform,
                    centre + right * side * 4.7f + Vector3.up * 4.65f,
                    new Vector3(.18f, .9f, 30),
                    Art.Metal
                );
                rail.transform.rotation = Quaternion.LookRotation(forward);
                for (int end = -1; end <= 1; end += 2)
                {
                    var pier = Art.Box(
                        "Bridge abutment",
                        town.transform,
                        centre + forward * end * 14 + right * side * 4 + Vector3.up * .7f,
                        new Vector3(1.2f, 6.5f, 2),
                        Art.Concrete
                    );
                    pier.transform.rotation = Quaternion.LookRotation(forward);
                }
            }
            Art.Text(
                "BRIDGE LOOP\nSIMULATION ADDITION",
                town.transform,
                origin + right * 4 + Vector3.up * 2,
                .16f,
                Color.white,
                Quaternion.LookRotation(forward)
            );
        }
    }
}
