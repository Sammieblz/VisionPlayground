using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace CoDriver.Editor
{
    public static class CrossingStopAudit
    {
        [System.Serializable]
        sealed class Report
        {
            public Vector3 position,
                start,
                finish,
                navPoint,
                carPosition,
                carForward;
            public Vector3[] corners;
            public string pathStatus,
                forwardBlocker;
            public bool crossingFound,
                onNavigation,
                directBlocked;
            public float navDistance,
                hitDistance;
        }

        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            var town = new GameObject("Crossing stop reproduction").AddComponent<Town>();
            town.Map = MapDefinition.Load("davis-downtown");
            town.Build(null);
            foreach (var actor in town.Traffic)
            foreach (var collider in actor.GetComponentsInChildren<Collider>())
                collider.enabled = actor == town.Traffic[9];
            foreach (var actor in town.Walkers)
            foreach (var collider in actor.GetComponentsInChildren<Collider>())
                collider.enabled = false;
            var car = town.Traffic[9];
            var carPosition = new Vector3(-78.35664f, -.022403f, -97.78930f);
            var carForward = new Vector3(-.6362376f, -.01515008f, .7713445f);
            car.transform.SetPositionAndRotation(carPosition, Quaternion.LookRotation(carForward));
            car.GetComponent<Rigidbody>().position = carPosition;
            car.GetComponent<Rigidbody>().rotation = car.transform.rotation;
            Physics.SyncTransforms();
            var position = new Vector3(-79.7172546f, 0, -95.3961563f);
            bool found = PedestrianNavigation.TryCrossing(
                town,
                new Vector3(-88.97843f, .1f, -96.29824f),
                out var start,
                out var finish
            );
            var path = new NavMeshPath();
            NavMesh.CalculatePath(position, finish, NavMesh.AllAreas, path);
            bool nav = NavMesh.SamplePosition(position, out var nearest, 4, NavMesh.AllAreas);
            var direction = Vector3.ProjectOnPlane(finish - position, Vector3.up).normalized;
            bool hit = Physics.SphereCast(
                position + Vector3.up * .35f,
                .22f,
                direction,
                out var contact,
                1,
                1 << TrafficClearance.TrafficLayer,
                QueryTriggerInteraction.Ignore
            );
            var report = new Report
            {
                position = position,
                start = start,
                finish = finish,
                crossingFound = found,
                onNavigation = nav,
                navPoint = nearest.position,
                navDistance = nearest.distance,
                carPosition = carPosition,
                carForward = carForward,
                corners = path.corners,
                pathStatus = path.status.ToString(),
                directBlocked = hit,
                forwardBlocker = hit ? contact.collider.name : "None",
                hitDistance = contact.distance,
            };
            var output = Path.GetFullPath(
                "../outputs/qualification/realism/crossing-stop-audit.json"
            );
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            Debug.Log("Crossing stop audit: " + output);
        }
    }
}
