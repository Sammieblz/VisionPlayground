using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CoDriver.Tests
{
    public sealed class TrafficPopulationTests
    {
        Town town;
        Scene boundaryCourse;
        float oldScale,
            oldStep;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            oldScale = Time.timeScale;
            oldStep = Time.fixedDeltaTime;
            Time.timeScale = 4;
            Time.fixedDeltaTime = .01f;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (town)
                Object.Destroy(town.gameObject);
            if (boundaryCourse.IsValid())
                yield return SceneManager.UnloadSceneAsync(boundaryCourse);
            Time.timeScale = oldScale;
            Time.fixedDeltaTime = oldStep;
            yield return null;
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator CedarRushHourKeepsItsPhysicalPopulation()
        {
            return CheckPopulation(MapDefinition.Cedar());
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator DavisRushHourKeepsItsPhysicalPopulation()
        {
            return CheckPopulation(MapDefinition.Load("davis-downtown"));
        }

        [TestCase(false, 0f)]
        [TestCase(true, -4f)]
        public void PhysicalPerimeterStopsRoadSpeedEscapeAtGroundAndUnderpassHeight(
            bool davis,
            float groundHeight
        )
        {
            Art.Initialize();
            boundaryCourse = SceneManager.CreateScene(
                "Isolated perimeter impact course",
                new CreateSceneParameters(LocalPhysicsMode.Physics3D)
            );
            var physics = boundaryCourse.GetPhysicsScene();
            Assert.IsTrue(physics.IsValid());
            Assert.AreNotEqual(Physics.defaultPhysicsScene, physics);
            town = new GameObject("Physical perimeter course").AddComponent<Town>();
            SceneManager.MoveGameObjectToScene(town.gameObject, boundaryCourse);
            town.Map = davis ? MapDefinition.Load("davis-downtown") : MapDefinition.Cedar();
            WorldBoundary.Build(town);
            // Off-road escape, away from existing street-end barricades. The
            // second case reaches the perimeter below normal terrain height.
            var wall = WorldBoundary.Envelopes(town.Map)[0];
            var start = new Vector3(wall.max.x + 20, groundHeight + .2f, 137);
            var surface = new GameObject("Perimeter approach surface");
            surface.transform.SetParent(town.transform, false);
            surface.transform.position = new Vector3(wall.center.x, groundHeight - .5f, start.z);
            surface.layer = TrafficClearance.RoadLayer;
            surface.AddComponent<BoxCollider>().size = new Vector3(80, 1, 50);
            var vehicle = new GameObject("Finite mass perimeter vehicle");
            vehicle.transform.SetParent(town.transform, false);
            vehicle.transform.SetPositionAndRotation(start, Quaternion.Euler(0, -90, 0));
            var profile = new VehicleProfile();
            var envelope = vehicle.AddComponent<BoxCollider>();
            envelope.center = profile.collisionCentre;
            envelope.size = profile.collisionSize;
            var body = vehicle.AddComponent<Rigidbody>();
            var dynamics = new RvpVehicleDynamics(body, profile);
            body.interpolation = RigidbodyInterpolation.None;
            Assert.IsFalse(body.isKinematic);
            Assert.Greater(body.mass, 1000);
            Physics.SyncTransforms();
            for (int i = 0; i < 150; i++)
            {
                dynamics.Step(new VehicleControls { brake = 1, handbrake = true }, .01f);
                physics.Simulate(.01f);
            }
            Assert.AreEqual(4, dynamics.GroundedWheels);
            Vector3 settled = body.position;
            body.linearVelocity = Vector3.left * (30 * .44704f);
            var previous = body.position;
            for (int i = 0; i < 400; i++)
            {
                dynamics.Step(new VehicleControls { throttle = .2f }, .01f);
                physics.Simulate(.01f);
                Assert.IsTrue(
                    float.IsFinite(body.position.x)
                        && float.IsFinite(body.position.y)
                        && float.IsFinite(body.position.z)
                        && float.IsFinite(body.linearVelocity.sqrMagnitude)
                );
                Assert.Less(
                    Vector3.Distance(previous, body.position),
                    .5f,
                    "Containment must use collision response, not repositioning."
                );
                Assert.IsTrue(
                    town.Map.Contains(body.position, .5f),
                    "The finite physical body escaped beyond the perimeter."
                );
                Assert.GreaterOrEqual(envelope.bounds.min.x, town.Map.boundsMin.x - .05f);
                previous = body.position;
            }
            Assert.Greater(
                settled.x - body.position.x,
                15,
                "The car must actually reach the perimeter at road speed."
            );
            Assert.Less(body.position.x - wall.max.x, 4);
            Assert.Less(
                body.linearVelocity.magnitude,
                .5f,
                "The wall must stop the vehicle while forward throttle remains requested."
            );
        }

        IEnumerator CheckPopulation(MapDefinition map)
        {
            town = new GameObject("Rush hour physical population").AddComponent<Town>();
            town.Map = map;
            town.Density = TrafficDensity.RushHour;
            town.Build(null);
            var actors = town.Traffic.ToArray();
            var ordinary = actors.Where(a => a.Kind == "Car" || a.Kind == "Truck").ToArray();
            Assert.AreEqual(32, ordinary.Length);
            Assert.AreEqual(35, actors.Length);
            Assert.AreSame(town.Bus, actors[12]);
            Assert.AreSame(town.Emergency, actors[13]);
            Assert.AreSame(town.ParkingCar, actors[14]);
            Assert.IsNotNull(town.Train);
            Assert.IsNotNull(town.Tram);
            Physics.SyncTransforms();
            foreach (var actor in ordinary)
            foreach (var other in actors.Where(a => a != actor))
                Assert.IsFalse(
                    Physics.ComputePenetration(
                        actor.GetComponent<BoxCollider>(),
                        actor.transform.position,
                        actor.transform.rotation,
                        other.GetComponent<BoxCollider>(),
                        other.transform.position,
                        other.transform.rotation,
                        out _,
                        out _
                    ),
                    actor.name + " initially overlaps " + other.name
                );
            var previous = ordinary.Select(a => a.transform.position).ToArray();
            float started = Time.time;
            var anchors = ordinary.Select(a => a.transform.position).ToArray();
            var lastMotion = ordinary.Select(_ => started).ToArray();
            var longestWait = new float[ordinary.Length];
            var progress = new float[ordinary.Length];
            float travelled = 0;
            while (Time.time - started < 180)
            {
                yield return new WaitForFixedUpdate();
                CollectionAssert.AreEqual(
                    actors,
                    town.Traffic,
                    "Traffic stays persistent during driving."
                );
                for (int i = 0; i < ordinary.Length; i++)
                {
                    var actor = ordinary[i];
                    float displacement = Vector3.Distance(previous[i], actor.transform.position);
                    Assert.Less(displacement, 1, actor.name + " changed position discontinuously.");
                    Assert.IsFalse(
                        actor.ReverseRequested,
                        actor.name + " requested reverse on an ordinary route."
                    );
                    Assert.IsFalse(
                        float.IsNaN(actor.SignedSpeed) || float.IsInfinity(actor.SignedSpeed)
                    );
                    // Match the ordinary-traffic queue regression: sub-half-
                    // metre settling or vibration does not reset a waiting car.
                    float advance = Vector3.Distance(anchors[i], actor.transform.position);
                    if (advance > .5f)
                    {
                        progress[i] += advance;
                        anchors[i] = actor.transform.position;
                        lastMotion[i] = Time.time;
                    }
                    longestWait[i] = Mathf.Max(longestWait[i], Time.time - lastMotion[i]);
                    if (longestWait[i] >= 120)
                        Assert.Fail(
                            $"{map.id} RushHour {actor.name} wait={longestWait[i]:F1}s "
                                + $"progress={progress[i]:F1}m state={actor.State} "
                                + $"blocker={actor.Clearance} at={actor.transform.position:F2}"
                        );
                    travelled += displacement;
                    previous[i] = actor.transform.position;
                }
            }
            for (int i = 0; i < ordinary.Length; i++)
            {
                string detail =
                    $"{map.id} RushHour {ordinary[i].name} wait={longestWait[i]:F1}s "
                    + $"progress={progress[i]:F1}m state={ordinary[i].State} "
                    + $"blocker={ordinary[i].Clearance} at={ordinary[i].transform.position:F2}";
                Assert.Greater(progress[i], .5f, detail);
                Assert.Less(longestWait[i], 120, detail);
            }
            Assert.Greater(travelled, 50, "The denser population must make physical progress.");
        }
    }
}
