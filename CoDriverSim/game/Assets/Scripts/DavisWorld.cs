using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace CoDriver
{
    public static class DavisWorld
    {
        static readonly Dictionary<int, Material> facades = new Dictionary<int, Material>();

        static Material Facade(int variation, bool residential)
        {
            int key = variation % 5 + (residential ? 5 : 0);
            if (facades.TryGetValue(key, out var material))
                return material;
            material = new Material(residential ? Art.Concrete : Art.Brick);
            material.name = "Davis textured facade " + key;
            material.color = Color.Lerp(
                new Color(.65f, .56f, .46f),
                new Color(.95f, .93f, .88f),
                variation % 5 / 4f
            );
            material.enableInstancing = true;
            facades[key] = material;
            return material;
        }

        public static Mesh Mesh(MapMesh data)
        {
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.vertices = data.vertices;
            mesh.triangles = data.triangles;
            mesh.uv = data.vertices.Select(p => new Vector2(p.x, p.z) * .2f).ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static GameObject Surface(
            string name,
            Transform parent,
            MapMesh data,
            Material material,
            bool collision = true
        )
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = TrafficClearance.RoadLayer;
            var mesh = RuntimeResources.Register(go, Mesh(data));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (collision)
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        public static void Build(Town town)
        {
            var map = town.Map;
            if (map.LaneById.ContainsKey(SimulatedBridgeExtension.LaneId))
                SimulatedBridgeExtension.BuildScenery(town);
            Surface("District terrain", town.transform, map.ground, Art.Grass);
            Surface("Source road surfaces", town.transform, map.roadSurface, Art.Asphalt);
            var access = Surface(
                "Estimated parking driveways",
                town.transform,
                map.parkingAccessSurface,
                Art.Asphalt
            );
            town.WalkSurfaces.Add(
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Mesh,
                    sourceObject = access.GetComponent<MeshFilter>().sharedMesh,
                    transform = Matrix4x4.identity,
                    area = 0,
                }
            );
            Surface(
                "Richards Boulevard underpass",
                town.transform,
                map.underpassSurface,
                Art.Asphalt
            );
            var parking = Surface(
                "Mapped parking lots",
                town.transform,
                map.parkingSurface,
                Art.Asphalt
            );
            town.WalkSurfaces.Add(
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Mesh,
                    sourceObject = parking.GetComponent<MeshFilter>().sharedMesh,
                    transform = Matrix4x4.identity,
                    area = 0,
                }
            );
            var sidewalk = Surface(
                "Mapped sidewalks and paths",
                town.transform,
                map.sidewalkSurface,
                Art.Concrete
            );
            town.WalkSurfaces.Add(
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Mesh,
                    sourceObject = sidewalk.GetComponent<MeshFilter>().sharedMesh,
                    transform = Matrix4x4.identity,
                    area = 0,
                }
            );
            // Low terrain beneath the underpass provides support outside the pavement.
            Art.Box(
                "Below-grade foundation",
                town.transform,
                new Vector3(0, -8.5f, 0),
                new Vector3(1100, 1, 1100),
                Art.Concrete
            );
            foreach (var lane in map.lanes)
            {
                if (lane.internalLane)
                    continue;
                for (float d = 2; d < lane.Path.Length - 4; d += 6)
                {
                    var p = lane.Path.Position(d);
                    if (!map.Contains(p, 2))
                        continue;
                    var direction = lane.Path.Direction(d);
                    var edge = p - Vector3.Cross(Vector3.up, direction) * lane.width * .5f;
                    var opposing = map.Nearest(
                        edge - Vector3.Cross(Vector3.up, direction) * 1.6f,
                        -direction,
                        false
                    );
                    bool centreLine =
                        opposing != null
                        && opposing != lane
                        && Vector3.Dot(
                            opposing.Path.Direction(opposing.Path.NearestDistance(edge)),
                            direction
                        ) < -.7f
                        && (
                            opposing.Path.Position(opposing.Path.NearestDistance(edge)) - edge
                        ).sqrMagnitude < 9;
                    if (lane.id == SimulatedBridgeExtension.LaneId)
                    {
                        var departure = map.LaneById[SimulatedBridgeExtension.Outbound];
                        var axis = departure.Path.Direction(departure.Path.Length);
                        float along = Vector3.Dot(p - departure.points.Last(), axis);
                        centreLine = along >= 0 && along <= 120;
                        if (centreLine)
                            edge += Vector3.Cross(Vector3.up, direction) * .09f;
                    }
                    var stripe = Art.Marking(
                        "Lane boundary " + lane.sourceId,
                        town.transform,
                        edge + Vector3.up * .004f,
                        new Vector2(centreLine ? .14f : .1f, centreLine ? 6 : 2.8f),
                        centreLine ? Art.Yellow : Art.White
                    );
                    stripe.transform.rotation = Quaternion.LookRotation(direction);
                }
            }
            int index = 0;
            foreach (var building in map.buildings)
                Building(town, building, index++);
            foreach (var landmark in map.landmarks)
            {
                if (!map.Contains(landmark.position, 3))
                    continue;
                var lane = map.Nearest(landmark.position, routable: false);
                if (lane == null)
                    continue;
                float d = lane.Path.NearestDistance(landmark.position);
                Vector3 forward = lane.Path.Direction(d),
                    right = Vector3.Cross(Vector3.up, forward);
                if (landmark.kind == "crossing")
                {
                    Vector3 centre = map.CrossingCentre(landmark);
                    for (int n = -4; n <= 4; n++)
                    {
                        var mark = Art.Marking(
                            "Mapped crossing " + landmark.sourceId,
                            town.transform,
                            centre + right * n * .8f + Vector3.up * .004f,
                            new Vector2(.4f, 2.8f),
                            Art.White
                        );
                        mark.transform.rotation = Quaternion.LookRotation(forward);
                    }
                    var crossing = Art.Box(
                        "Crossing navigation",
                        town.transform,
                        centre,
                        new Vector3(14, .08f, 3),
                        Art.Asphalt,
                        false
                    );
                    crossing.transform.rotation = Quaternion.LookRotation(forward);
                    crossing.GetComponent<Renderer>().enabled = false;
                    town.WalkSurfaces.Add(
                        new NavMeshBuildSource
                        {
                            shape = NavMeshBuildSourceShape.Box,
                            transform = crossing.transform.localToWorldMatrix,
                            size = Vector3.one,
                            area = PedestrianNavigation.CrossingArea,
                        }
                    );
                }
                if (landmark.kind == "transit_stop")
                {
                    Art.Box(
                        "Mapped bus stop " + landmark.sourceId,
                        town.transform,
                        landmark.position + Vector3.up * 1.4f,
                        new Vector3(.06f, 2.8f, .06f),
                        Art.Metal
                    );
                    Art.Text(
                        "BUS\nSIM 15 MIN",
                        town.transform,
                        landmark.position + Vector3.up * 2.8f,
                        .08f,
                        Color.white,
                        Quaternion.LookRotation(-right)
                    );
                }
                if (
                    landmark.kind == "stop_sign"
                    && map.LaneById.TryGetValue(landmark.laneId ?? "", out var approach)
                )
                {
                    float along = approach.Path.NearestDistance(landmark.position);
                    var direction = approach.Path.Direction(along);
                    var sign = new GameObject("Mapped stop sign " + landmark.sourceId).transform;
                    sign.SetParent(town.transform, false);
                    sign.SetPositionAndRotation(
                        approach.Path.Position(along)
                            + Vector3.Cross(Vector3.up, direction) * (approach.width * .5f + .6f),
                        Quaternion.LookRotation(direction)
                    );
                    if (WorldSigns.Place("Stop.Sign", sign))
                        continue;
                    Art.Box(
                        "Sign pole",
                        sign,
                        Vector3.up * 1.3f,
                        new Vector3(.07f, 2.6f, .07f),
                        Art.Metal
                    );
                    Art.Octagon(sign, Vector3.up * 2.1f, .43f, Art.White);
                    Art.Octagon(
                        sign,
                        new Vector3(0, 2.1f, -.015f),
                        .39f,
                        Art.Material("Stop red", new Color(.65f, .025f, .02f))
                    );
                    Art.Text(
                        "STOP",
                        sign,
                        new Vector3(0, 2.1f, -.04f),
                        .065f,
                        Color.white,
                        Quaternion.identity
                    );
                }
                if (landmark.subtype == "level_crossing" && lane.Path.Position(d).y > -1)
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var gate = new GameObject(
                            "Mapped railway gate " + landmark.sourceId
                        ).AddComponent<RailGate>();
                        gate.transform.SetParent(town.transform, false);
                        gate.transform.SetPositionAndRotation(
                            landmark.position + right * side * 5 - forward * side * 8,
                            Quaternion.LookRotation(forward * side)
                        );
                        gate.ExplicitCrossing = true;
                        gate.CrossingPoint = landmark.position;
                        gate.Initialize(town);
                    }
                }
            }
            foreach (var signal in map.signals)
            {
                var group = new GameObject("Mapped signal " + signal.id);
                group.transform.SetParent(town.transform, false);
                group.transform.position = signal.position;
                var controller = group.AddComponent<Signal>();
                controller.Mapped = signal;
                controller.Initialize(town, 0);
                town.Signals.Add(controller);
                town.MappedJunctions[signal.id] = controller;
                MappedSignalAssembly.Build(town, signal);
            }
            foreach (var junction in map.junctions)
            {
                if (town.MappedJunctions.ContainsKey(junction.id))
                    continue;
                var go = new GameObject("Mapped priority junction " + junction.id);
                go.transform.SetParent(town.transform, false);
                go.transform.position = junction.position;
                var priority = go.AddComponent<Signal>();
                priority.Uncontrolled = true;
                priority.Initialize(town, 0);
                town.MappedJunctions[junction.id] = priority;
                town.Signals.Add(priority);
            }
            foreach (var track in map.rail)
                RailArchitecture.Build(town, track);
            // Tunnel roof follows the source-tagged section only. Road stays below rail.
            foreach (var road in map.roads.Where(r => r.tunnel))
            foreach (string identity in road.laneIds)
            {
                var lane = map.LaneById[identity];
                for (float d = 0; d < lane.Path.Length; d += 4)
                {
                    var p = lane.Path.Position(d);
                    var f = lane.Path.Direction(d);
                    var roof = Art.Box(
                        "Richards railway deck",
                        town.transform,
                        new Vector3(p.x, -.35f, p.z),
                        new Vector3(lane.width + .3f, .6f, 4.1f),
                        Art.Concrete
                    );
                    roof.transform.rotation = Quaternion.LookRotation(
                        Vector3.ProjectOnPlane(f, Vector3.up)
                    );
                }
            }
            Physics.SyncTransforms();
            var rng = new System.Random(town.Seed);
            foreach (var lane in map.lanes.Where(l => !l.internalLane && !l.boundary).Take(100))
            {
                float d = (float)rng.NextDouble() * lane.Path.Length;
                Vector3 p =
                    lane.Path.Position(d) + Vector3.Cross(Vector3.up, lane.Path.Direction(d)) * 6;
                if (
                    Physics.Raycast(
                        p + Vector3.up * 2,
                        Vector3.down,
                        out var support,
                        4,
                        1 << TrafficClearance.RoadLayer
                    )
                    && support.collider.gameObject == access
                )
                    continue;
                if (
                    p.y < -.05f
                    || !map.Contains(p, 10)
                    || Physics.CheckSphere(
                        p + Vector3.up * 2,
                        2,
                        ~(1 << TrafficClearance.RoadLayer)
                    )
                )
                    continue;
                Realism.Spawn(rng.Next(2) == 0 ? "Oak" : "Pine", town.transform, p);
            }
            Boundaries(town);
            Physics.SyncTransforms();
            var bays = new List<Vector3>();
            foreach (
                var lot in map.landmarks.Where(l =>
                    l.kind == "parking"
                    && map.parkingAccessBayIds.Contains(l.sourceId)
                    && map.Contains(l.position, 30)
                )
            )
            {
                var nearest = map.Nearest(lot.position);
                if (
                    nearest == null
                    || (
                        nearest.Path.Position(nearest.Path.NearestDistance(lot.position))
                        - lot.position
                    ).magnitude > 35
                )
                    continue;
                if (
                    Physics.CheckBox(
                        lot.position + Vector3.up * 1.6f,
                        new Vector3(3, 1.3f, 5),
                        Quaternion.identity,
                        ~(1 << TrafficClearance.RoadLayer)
                    )
                )
                    continue;
                bays.Add(lot.position + Vector3.up * .1f);
            }
            if (bays.Count < 3)
                throw new System.InvalidOperationException(
                    "Davis needs three verified clear activity bays"
                );
            town.EmergencyBay = bays[0];
            town.IncidentBay = bays[1];
            town.ParkingBay = bays[2];
            Art.Text(
                "SIMULATED EMS STAGING",
                town.transform,
                town.EmergencyBay + Vector3.up * 3,
                .13f,
                Color.white,
                Quaternion.identity
            );
            town.Construction = new GameObject(
                "Simulated scenario construction"
            ).AddComponent<ConstructionSite>();
            town.Construction.transform.SetParent(town.transform, false);
            town.Construction.transform.position = bays[bays.Count - 1];
            town.Construction.Build();
        }

        static void AddWall(
            Vector3 topA,
            Vector3 topB,
            float bottom,
            Vector3 origin,
            List<Vector3> vertices,
            List<int> triangles,
            List<Vector2> uv
        )
        {
            if ((topB - topA).sqrMagnitude < .00001f)
                return;
            int k = vertices.Count;
            vertices.AddRange(
                new[]
                {
                    new Vector3(topA.x, bottom, topA.z),
                    new Vector3(topB.x, bottom, topB.z),
                    topB,
                    topA,
                }
            );
            triangles.AddRange(new[] { k, k + 2, k + 1, k, k + 3, k + 2 });
            float uA = Vector3.ProjectOnPlane(topA - origin, Vector3.up).magnitude / 3;
            float uB = Vector3.ProjectOnPlane(topB - origin, Vector3.up).magnitude / 3;
            uv.AddRange(
                new[]
                {
                    new Vector2(uA, bottom / 3),
                    new Vector2(uB, bottom / 3),
                    new Vector2(uB, topB.y / 3),
                    new Vector2(uA, topA.y / 3),
                }
            );
        }

        static void Building(Town town, MapBuilding b, int variation)
        {
            Transform parent = town.transform;
            var entrance = MappedStorefront.Find(town.Map, b);
            bool residential =
                b.usage == "house"
                || b.usage == "houses"
                || b.usage == "residential"
                || b.usage == "detached";
            var vertices = new List<Vector3>(b.roof.vertices);
            var triangles = new List<int>(b.roof.triangles);
            var uv = b.roof.vertices.Select(p => new Vector2(p.x, p.z) * .2f).ToList();
            foreach (var ring in b.rings)
                for (int i = 1; i < ring.points.Length; i++)
                {
                    Vector3 a = ring.points[i - 1],
                        c = ring.points[i];
                    // The roof includes ridge intersections and eaves along this
                    // footprint edge. Follow their heights to close every gable.
                    var edge = c - a;
                    var roofEdge = b
                        .roof.vertices.Where(p =>
                        {
                            Vector3 flat = new Vector3(p.x, 0, p.z) - a;
                            float t = Vector3.Dot(flat, edge) / edge.sqrMagnitude;
                            return t >= -.0001f
                                && t <= 1.0001f
                                && (flat - edge * t).sqrMagnitude < .00001f;
                        })
                        .Distinct()
                        .OrderBy(p => Vector3.Dot(p - a, edge))
                        .ToArray();
                    for (int j = 1; j < roofEdge.Length; j++)
                    {
                        var topA = roofEdge[j - 1];
                        var topB = roofEdge[j];
                        if ((topA - topB).sqrMagnitude < .00001f)
                            continue;
                        bool doorEdge =
                            entrance != null && ring == b.rings[0] && i == entrance.Edge;
                        float start = Vector3.Dot(topA - a, edge.normalized);
                        float end = Vector3.Dot(topB - a, edge.normalized);
                        float left = doorEdge
                            ? Mathf.Max(
                                start,
                                entrance.CentreDistance - MappedStorefront.Opening * .5f
                            )
                            : end;
                        float right = doorEdge
                            ? Mathf.Min(
                                end,
                                entrance.CentreDistance + MappedStorefront.Opening * .5f
                            )
                            : start;
                        if (left < right)
                        {
                            var l = Vector3.Lerp(topA, topB, (left - start) / (end - start));
                            var r = Vector3.Lerp(topA, topB, (right - start) / (end - start));
                            AddWall(topA, l, 0, a, vertices, triangles, uv);
                            AddWall(l, r, MappedStorefront.Height, a, vertices, triangles, uv);
                            AddWall(r, topB, 0, a, vertices, triangles, uv);
                        }
                        else
                            AddWall(topA, topB, 0, a, vertices, triangles, uv);
                    }
                }
            var root = Surface(
                "Building " + b.sourceId + " " + b.name,
                parent,
                new MapMesh { vertices = vertices.ToArray(), triangles = triangles.ToArray() },
                Facade(variation, residential)
            );
            root.layer = 0;
            root.GetComponent<MeshFilter>().sharedMesh.uv = uv.ToArray();
            root.GetComponent<MeshFilter>().sharedMesh.RecalculateTangents();
            var shell = root.GetComponent<MeshFilter>().sharedMesh;
            // Rasterize the same walls used by PhysX into navigation. Sidewalk
            // surfaces alone can otherwise propose a path through a facade edge.
            // Door openings remain in this mesh, so shop interiors stay reachable.
            town.WalkSurfaces.Add(
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Mesh,
                    sourceObject = shell,
                    transform = root.transform.localToWorldMatrix,
                    area = 1, // Unity's Not Walkable area.
                }
            );
            shell.subMeshCount = 2;
            shell.SetTriangles(b.roof.triangles, 0);
            shell.SetTriangles(triangles.Skip(b.roof.triangles.Length).ToArray(), 1);
            root.GetComponent<MeshRenderer>().sharedMaterials = new[]
            {
                Art.Roof,
                Facade(variation, residential),
            };
            entrance?.Build(town);
            if (entrance == null && MappedBuildingArt.TryAdd(root.transform, b))
            {
                root.GetComponent<MeshRenderer>().enabled = false;
                return;
            }
            if (FacadeArchitecture.Add(root.transform, b, entrance))
                return;
            // Recessed window groups follow each actual facade, preserving footprint.
            vertices.Clear();
            triangles.Clear();
            foreach (var ring in b.rings.Take(1))
                for (int i = 1; i < ring.points.Length; i++)
                {
                    Vector3 a = ring.points[i - 1],
                        c = ring.points[i],
                        f = (c - a).normalized;
                    float length = Vector3.Distance(a, c);
                    if (length < 4 || length > 90)
                        continue;
                    for (float d = 2; d < length - 1; d += 3.2f)
                    for (float h = 2; h < b.height - .7f; h += 3.2f)
                    {
                        Vector3 centre =
                            a + f * d + Vector3.up * h + Vector3.Cross(Vector3.up, f) * .035f;
                        if (h < 2.1f && d < 2.1f)
                        {
                            var door = Art.Box(
                                "Entrance trim",
                                root.transform,
                                centre - Vector3.up * .7f,
                                new Vector3(1.2f, 2.5f, .12f),
                                Art.White,
                                false
                            );
                            door.transform.rotation = Quaternion.LookRotation(
                                Vector3.Cross(Vector3.up, f)
                            );
                            var leaf = Art.Box(
                                "Entrance door",
                                door.transform,
                                new Vector3(0, 0, .6f),
                                new Vector3(.86f, .9f, .1f),
                                Art.Wood,
                                false
                            );
                            continue;
                        }
                        int k = vertices.Count;
                        vertices.AddRange(
                            new[]
                            {
                                centre - f * .7f - Vector3.up * .75f,
                                centre + f * .7f - Vector3.up * .75f,
                                centre + f * .7f + Vector3.up * .75f,
                                centre - f * .7f + Vector3.up * .75f,
                            }
                        );
                        triangles.AddRange(
                            new[]
                            {
                                k,
                                k + 1,
                                k + 2,
                                k,
                                k + 2,
                                k + 3,
                                k + 2,
                                k + 1,
                                k,
                                k + 3,
                                k + 2,
                                k,
                            }
                        );
                    }
                }
            Surface(
                "Facade glazing",
                root.transform,
                new MapMesh { vertices = vertices.ToArray(), triangles = triangles.ToArray() },
                Art.Glass,
                false
            );
        }

        static void Boundaries(Town town)
        {
            foreach (var closure in town.Map.Closures)
            {
                var barrier = Art.Box(
                    "District road closure",
                    town.transform,
                    closure.position + Vector3.up * .65f,
                    new Vector3(closure.width, 1.2f, .35f),
                    Art.Material("Boundary orange", new Color(.95f, .36f, .05f))
                );
                barrier.transform.rotation = Quaternion.LookRotation(closure.forward);
                Art.Text(
                    "DISTRICT CLOSED\nTURN AT INTERSECTION",
                    barrier.transform,
                    new Vector3(0, .4f, -.6f),
                    .04f,
                    Color.white,
                    Quaternion.identity
                );
            }
        }
    }

    public sealed class MappedSignalHead : MonoBehaviour
    {
        MapSignal signal;
        int index;
        Town town;
        Renderer[] lights = new Renderer[3];
        char previous;

        public void Initialize(MapSignal definition, int link, Town owner)
        {
            signal = definition;
            index = link;
            town = owner;
            Realism.Spawn("Signal", transform, new Vector3(0, 3.5f, 0), 180);
            for (int i = 0; i < 3; i++)
                lights[i] = Art.Shape(
                        "Signal lens",
                        transform,
                        new Vector3(0, 4.3f - i * .4f, -.18f),
                        new Vector3(.3f, .3f, .05f),
                        Art.Rubber,
                        PrimitiveType.Sphere
                    )
                    .GetComponent<Renderer>();
        }

        void Update()
        {
            char state = signal.State(index, Time.time - town.Epoch);
            if (state == previous)
                return;
            previous = state;
            int active =
                state == 'G' || state == 'g' ? 2
                : state == 'y' || state == 'Y' ? 1
                : 0;
            for (int i = 0; i < 3; i++)
                lights[i].sharedMaterial =
                    i == active
                        ? Art.Material(
                            "Mapped light " + i,
                            i == 0 ? Color.red
                                : i == 1 ? Color.yellow
                                : Color.green,
                            true
                        )
                        : Art.Rubber;
        }
    }
}
