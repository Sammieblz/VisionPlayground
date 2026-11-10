using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CoDriver.Editor
{
    public static class TrafficStopAudit
    {
        [System.Serializable]
        sealed class Pole
        {
            public string approach;
            public Vector3 position;
            public float limitWithoutPole;
            public bool clearWithoutPole;
        }

        [System.Serializable]
        sealed class Report
        {
            public string scope = "Reproduction of car 18's 5th Street stop in the sustained run";
            public Vector3 vehiclePosition,
                vehicleSize;
            public float forwardLimit;
            public bool forwardClear;
            public string blocker;
            public Pole[] nearbyPoles;
            public Probe bus,
                truck;
        }

        [System.Serializable]
        sealed class Probe
        {
            public string lane,
                forwardBlocker,
                pathBlocker,
                sweepBlocker;
            public float forwardLimit,
                pathLimit;
            public bool clearTurn;
        }

        static Probe Inspect(Town town, TrafficActor actor)
        {
            var shape = actor.GetComponent<BoxCollider>();
            var lane = town.Map.Nearest(actor.transform.position, actor.transform.forward, false);
            float distance = actor.Path.NearestDistance(actor.transform.position);
            float remaining =
                lane.Path.Length - lane.Path.NearestDistance(actor.transform.position);
            float exit = distance + remaining + 25;
            for (float d = remaining + 3; d < remaining + 75; d += 3)
            {
                var following = town.Map.Nearest(
                    actor.Path.Position(distance + d),
                    actor.Path.Direction(distance + d),
                    false
                );
                if (following != null && following.id != lane.id && !following.internalLane)
                {
                    exit = distance + d + shape.size.z * .5f + 2;
                    break;
                }
            }
            float forward = TrafficClearance.ForwardSpeedLimit(shape, 0, immediate: true);
            string forwardBlocker = TrafficClearance.LastBlocker;
            float path = TrafficClearance.SafeSpeed(actor.Path, distance, 0, 0, shape);
            string pathBlocker = TrafficClearance.LastBlocker;
            bool clear = TrafficClearance.ClearJunctionSweep(
                actor.Path,
                distance + remaining,
                exit,
                shape
            );
            return new Probe
            {
                lane = lane.id,
                forwardLimit = float.IsInfinity(forward) ? -1 : forward,
                pathLimit = float.IsInfinity(path) ? -1 : path,
                forwardBlocker = forwardBlocker,
                pathBlocker = pathBlocker,
                clearTurn = clear,
                sweepBlocker = TrafficClearance.LastBlocker,
            };
        }

        static void Place(TrafficActor actor, Vector3 position, Vector3 forward)
        {
            foreach (var collider in actor.GetComponentsInChildren<Collider>())
                collider.enabled = true;
            var body = actor.GetComponent<Rigidbody>();
            body.position = position;
            body.rotation = Quaternion.LookRotation(forward);
            actor.transform.SetPositionAndRotation(body.position, body.rotation);
        }

        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var town = new GameObject("Davis clearance reproduction").AddComponent<Town>();
            town.Map = MapDefinition.Load("davis-downtown");
            town.Seed = 21;
            var player = new GameObject("Audit player");
            player.transform.position = new Vector3(10000, 0, 10000);
            town.Build(player.transform);
            var actor = town.Traffic[18];
            // The live snapshot had no other actors within 30 m. Isolate static
            // scenery here instead of letting the initial population obscure it.
            foreach (var other in town.Traffic.Where(other => other != actor))
            foreach (var collider in other.GetComponentsInChildren<Collider>())
                collider.enabled = false;
            foreach (var walker in town.Walkers)
            foreach (var collider in walker.GetComponentsInChildren<Collider>())
                collider.enabled = false;
            var body = actor.GetComponent<Rigidbody>();
            var shape = actor.GetComponent<BoxCollider>();
            body.position = new Vector3(158.132675f, -.022403f, 251.659851f);
            body.rotation = Quaternion.LookRotation(new Vector3(-.971082f, -.0151495f, -.238264f));
            actor.transform.SetPositionAndRotation(body.position, body.rotation);
            Physics.SyncTransforms();
            float limit = TrafficClearance.ForwardSpeedLimit(shape, 0, immediate: true);
            var report = new Report
            {
                vehiclePosition = body.position,
                vehicleSize = shape.size,
                forwardLimit = float.IsInfinity(limit) ? -1 : limit,
                forwardClear = float.IsPositiveInfinity(limit),
                blocker = TrafficClearance.LastBlocker,
            };
            report.nearbyPoles = town.GetComponentsInChildren<BoxCollider>()
                .Where(c =>
                    c.name == "Signal pole"
                    && Vector3.Distance(c.ClosestPoint(body.position), body.position) < 12
                )
                .Select(c =>
                {
                    c.enabled = false;
                    Physics.SyncTransforms();
                    float without = TrafficClearance.ForwardSpeedLimit(shape, 0, immediate: true);
                    c.enabled = true;
                    Physics.SyncTransforms();
                    return new Pole
                    {
                        approach = c.transform.parent.name,
                        position = c.transform.position,
                        limitWithoutPole = float.IsInfinity(without) ? -1 : without,
                        clearWithoutPole = float.IsPositiveInfinity(without),
                    };
                })
                .ToArray();
            foreach (var collider in actor.GetComponentsInChildren<Collider>())
                collider.enabled = false;
            Place(
                town.Bus,
                new Vector3(75.76129f, -.053773f, -354.50024f),
                new Vector3(.220793f, 0, -.975321f)
            );
            Place(
                town.Traffic[0],
                new Vector3(80.53678f, .119454f, -357.92062f),
                new Vector3(-.777291f, -.007504f, .629097f)
            );
            Physics.SyncTransforms();
            report.bus = Inspect(town, town.Bus);
            report.truck = Inspect(town, town.Traffic[0]);
            string output = Path.GetFullPath(
                "../outputs/qualification/realism/traffic-stop-audit.json"
            );
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            Debug.Log("Traffic stop audit: " + output);
        }
    }
}
