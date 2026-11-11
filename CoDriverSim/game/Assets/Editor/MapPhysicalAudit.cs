using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoDriver.Editor
{
    // Offline qualification only. It never edits map rules or controls a running game.
    public static class MapPhysicalAudit
    {
        [Serializable]
        sealed class Turn
        {
            public string vehicle,
                incoming,
                outgoing;
            public string[] internalLanes;
            public bool reachedExit;
            public float seconds,
                exitError,
                maximumTrackingError,
                maximumRoll,
                maximumSpeed;
            public int samples,
                footprintOutsideSamples,
                finalGroundedWheels;
            public Vector3 startPosition,
                finalPosition;
            public List<Vector3> outsidePositions = new List<Vector3>();
        }

        [Serializable]
        sealed class Report
        {
            public string dynamicsSha256,
                driverSha256,
                mapSha256;
            public string scope =
                "Isolated 100 Hz physical traversal of legal, routing-enabled Davis turns";
            public string conditions =
                "Dry configured surface; full car/bus body footprint; 0.25 m pavement-edge tolerance; no other actors or signals. A failure is reported, never silently removed from routing.";
            public BuildIdentity build;
            public int enumeratedPaths,
                skippedByEnvelopeLimits;
            public List<Turn> turns = new List<Turn>();
        }

        static IEnumerable<List<MapLane>> Connections(
            MapDefinition map,
            MapLane entry,
            List<MapLane> chain
        )
        {
            if (chain.Count > 8)
                throw new InvalidOperationException("Unexpected internal lane cycle");
            foreach (string id in entry.next)
            {
                var next = map.LaneById[id];
                if (next.boundary || chain.Contains(next))
                    continue;
                var extended = new List<MapLane>(chain) { next };
                if (next.internalLane)
                {
                    foreach (var path in Connections(map, next, extended))
                        yield return path;
                }
                else if (chain.Count > 1)
                    yield return extended;
            }
        }

        static TrackPath Course(List<MapLane> lanes, VehicleProfile profile)
        {
            var points = new List<Vector3>();
            void Add(Vector3 p)
            {
                if (points.Count == 0 || (points[points.Count - 1] - p).sqrMagnitude > .0001f)
                    points.Add(p);
            }
            var incoming = lanes[0].Path;
            // Begin with the complete vehicle on the incoming segment and stop
            // before a terminal outgoing road's cap, rather than auditing a
            // deliberate half-body excursion beyond a cropped course endpoint.
            float start = Mathf.Min(
                incoming.Length - 2,
                Mathf.Max(profile.collisionSize.z * .5f + 1, incoming.Length - 35)
            );
            for (float d = Mathf.Max(0, start); d < incoming.Length; d += 1)
                Add(incoming.Position(d));
            Add(incoming.Position(incoming.Length));
            foreach (var lane in lanes.Skip(1).Take(lanes.Count - 2))
            foreach (var p in lane.points)
                Add(p);
            var outgoing = lanes[lanes.Count - 1].Path;
            float end = Mathf.Min(
                35,
                Mathf.Max(2, outgoing.Length - profile.collisionSize.z * .5f - 1)
            );
            for (float d = 0; d < end; d += 1)
                Add(outgoing.Position(d));
            Add(outgoing.Position(end));
            return new TrackPath(points, false);
        }

        static void Surface(Scene scene, MapMesh data, int layer, List<Mesh> meshes)
        {
            if (data == null || data.triangles.Length == 0)
                return;
            var go = new GameObject("Audited source collision surface");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.layer = layer;
            var mesh = DavisWorld.Mesh(data);
            meshes.Add(mesh);
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        static Turn Drive(Scene scene, PhysicsScene physics, List<MapLane> lanes, bool bus)
        {
            var profile = VehicleProfile.ForTraffic(bus ? "Bus" : "Car");
            var path = Course(lanes, profile);
            var go = new GameObject("Physical turn audit vehicle");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.layer = TrafficClearance.TrafficLayer;
            go.transform.SetPositionAndRotation(
                path.Position(0) + Vector3.up * .2f,
                Quaternion.LookRotation(path.Direction(0))
            );
            var shell = go.AddComponent<BoxCollider>();
            shell.center = profile.collisionCentre;
            shell.size = profile.collisionSize;
            var body = go.AddComponent<Rigidbody>();
            var driver = new PhysicalDriver(body, profile);
            body.interpolation = RigidbodyInterpolation.None;
            var result = new Turn
            {
                vehicle = bus ? "bus" : "car",
                incoming = lanes[0].id,
                outgoing = lanes[lanes.Count - 1].id,
                internalLanes = lanes.Skip(1).Take(lanes.Count - 2).Select(l => l.id).ToArray(),
                startPosition = body.position,
            };
            try
            {
                for (int i = 0; i < 200; i++)
                {
                    driver.Hold();
                    physics.Simulate(.01f);
                }
                int budget = Mathf.CeilToInt((path.Length / 2 + 20) * 100);
                float at = 0;
                for (int tick = 0; tick < budget; tick++)
                {
                    at = path.NearestDistance(body.position, at);
                    float remaining = path.Length - at;
                    if (tick % 10 == 0)
                        driver.Follow(
                            path,
                            Mathf.Min(bus ? 3 : 4, Mathf.Sqrt(Mathf.Max(0, remaining - .6f) * 3))
                        );
                    else
                        driver.Continue();
                    physics.Simulate(.01f);
                    result.seconds = (tick + 1) * .01f;
                    result.maximumSpeed = Mathf.Max(
                        result.maximumSpeed,
                        body.linearVelocity.magnitude
                    );
                    result.finalPosition = body.position;
                    result.finalGroundedWheels = driver.Dynamics.GroundedWheels;
                    result.exitError = Vector3.Distance(body.position, path.Position(path.Length));
                    if (tick % 10 == 0)
                    {
                        result.samples++;
                        result.maximumTrackingError = Mathf.Max(
                            result.maximumTrackingError,
                            Vector3
                                .ProjectOnPlane(body.position - path.Position(at), Vector3.up)
                                .magnitude
                        );
                        result.maximumRoll = Mathf.Max(
                            result.maximumRoll,
                            Mathf.Abs(Mathf.DeltaAngle(0, body.rotation.eulerAngles.z))
                        );
                        bool outside = false;
                        foreach (float x in new[] { -1f, 1f })
                        foreach (float z in new[] { -1f, 1f })
                        {
                            Vector3 corner =
                                body.position
                                + body.rotation
                                    * new Vector3(
                                        x * profile.collisionSize.x * .5f,
                                        0,
                                        z * profile.collisionSize.z * .5f
                                    );
                            if (
                                !physics.SphereCast(
                                    corner + Vector3.up * 2,
                                    .25f,
                                    Vector3.down,
                                    out _,
                                    4,
                                    1 << TrafficClearance.RoadLayer,
                                    QueryTriggerInteraction.Ignore
                                )
                            )
                                outside = true;
                        }
                        if (outside)
                        {
                            result.footprintOutsideSamples++;
                            if (
                                result.outsidePositions.Count == 0
                                || Vector3.Distance(result.outsidePositions.Last(), body.position)
                                    > 2
                            )
                                result.outsidePositions.Add(body.position);
                        }
                    }
                    if (
                        remaining < 1.5f
                        && result.exitError < 1.6f
                        && body.linearVelocity.magnitude < .5f
                    )
                    {
                        result.reachedExit = true;
                        break;
                    }
                    if (body.position.y < -12 || result.maximumRoll > 40)
                        break;
                }
                return result;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        static bool reviewOnly;

        static string Key(Turn t) =>
            t.vehicle
            + ":"
            + t.incoming
            + ":"
            + string.Join(",", t.internalLanes)
            + ":"
            + t.outgoing;

        public static void RunReview()
        {
            reviewOnly = true;
            Run();
        }

        public static void Run()
        {
            var map = MapDefinition.Load("davis-downtown");
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewPreviewScene();
            var meshes = new List<Mesh>();
            float oldStep = Time.fixedDeltaTime;
            string HashFile(string path)
            {
                using (var sha = System.Security.Cryptography.SHA256.Create())
                    return BitConverter
                        .ToString(sha.ComputeHash(File.ReadAllBytes(path)))
                        .Replace("-", "")
                        .ToLowerInvariant();
            }
            var report = new Report
            {
                build = BuildIdentity.Current,
                dynamicsSha256 = HashFile("Assets/Scripts/VehicleDynamics.cs"),
                driverSha256 = HashFile("Assets/Scripts/PhysicalDriver.cs"),
                mapSha256 = HashFile("Assets/Resources/Maps/davis-downtown.json"),
            };
            string destination = Path.GetFullPath(
                "../outputs/qualification/overhaul/map-physical-turns.json"
            );
            var args = Environment.GetCommandLineArgs();
            int outputFlag = Array.IndexOf(args, "-audit-output");
            if (outputFlag >= 0 && outputFlag + 1 < args.Length)
                destination = Path.GetFullPath(args[outputFlag + 1]);
            HashSet<string> requested = null;
            if (reviewOnly)
            {
                var previousReport = JsonUtility.FromJson<Report>(File.ReadAllText(destination));
                requested = previousReport
                    .turns.Where(t => !t.reachedExit || t.footprintOutsideSamples > 0)
                    .Select(Key)
                    .ToHashSet();
                destination = Path.Combine(
                    Path.GetDirectoryName(destination),
                    "map-physical-review.json"
                );
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            try
            {
                Time.fixedDeltaTime = .01f;
                var physics = scene.GetPhysicsScene();
                Surface(scene, map.ground, 0, meshes);
                Surface(scene, map.sidewalkSurface, 0, meshes);
                Surface(scene, map.roadSurface, TrafficClearance.RoadLayer, meshes);
                Surface(scene, map.underpassSurface, TrafficClearance.RoadLayer, meshes);
                foreach (var incoming in map.lanes.Where(l => !l.internalLane && !l.boundary))
                foreach (var path in Connections(map, incoming, new List<MapLane> { incoming }))
                {
                    report.enumeratedPaths++;
                    foreach (bool bus in new[] { false, true })
                    {
                        if (path.Any(l => !map.FitsVehicle(l.id, bus)))
                        {
                            report.skippedByEnvelopeLimits++;
                            continue;
                        }
                        if (
                            requested != null
                            && !requested.Contains(
                                Key(
                                    new Turn
                                    {
                                        vehicle = bus ? "bus" : "car",
                                        incoming = path[0].id,
                                        outgoing = path.Last().id,
                                        internalLanes = path.Skip(1)
                                            .Take(path.Count - 2)
                                            .Select(l => l.id)
                                            .ToArray(),
                                    }
                                )
                            )
                        )
                            continue;
                        report.turns.Add(Drive(scene, physics, path, bus));
                        if (report.turns.Count % 50 == 0)
                        {
                            File.WriteAllText(destination, JsonUtility.ToJson(report, true));
                            Debug.Log("CODRIVER_PHYSICAL_TURNS " + report.turns.Count);
                        }
                    }
                }
            }
            finally
            {
                File.WriteAllText(destination, JsonUtility.ToJson(report, true));
                Time.fixedDeltaTime = oldStep;
                SceneManager.SetActiveScene(previous);
                EditorSceneManager.ClosePreviewScene(scene);
                foreach (var mesh in meshes)
                    UnityEngine.Object.DestroyImmediate(mesh);
            }
            int failures = report.turns.Count(t => !t.reachedExit || t.footprintOutsideSamples > 0);
            Debug.Log(
                $"CODRIVER_PHYSICAL_TURNS_COMPLETE {report.turns.Count} cases, {failures} require review"
            );
        }
    }
}
