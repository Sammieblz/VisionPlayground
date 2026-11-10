using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace CoDriver.Tests
{
    public sealed class PhysicalWorldTests
    {
        readonly List<GameObject> owned = new List<GameObject>();
        float oldScale,
            oldStep;

        GameObject Own(string name)
        {
            var go = new GameObject(name);
            owned.Add(go);
            return go;
        }

        [UnitySetUp]
        public IEnumerator Setup()
        {
            oldScale = Time.timeScale;
            oldStep = Time.fixedDeltaTime;
            Time.timeScale = 8;
            Time.fixedDeltaTime = .01f;
            Own("Test audio listener").AddComponent<AudioListener>();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            foreach (var go in owned)
                if (go)
                    UnityEngine.Object.Destroy(go);
            owned.Clear();
            Time.timeScale = oldScale;
            Time.fixedDeltaTime = oldStep;
            yield return null;
        }

        [UnityTest]
        public IEnumerator CedarBridgeHasContinuousPhysicalApproaches()
        {
            Art.Initialize();
            var town = Own("Production bridge course").AddComponent<Town>();
            typeof(Town)
                .GetMethod(
                    "Road",
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic
                )
                .Invoke(
                    town,
                    new object[] { new Vector3(0, 0, 180), new Vector3(0, 0, 420), true }
                );
            foreach (float targetSpeed in new[] { 8f, 26.82f })
            foreach (int direction in new[] { 1, -1 })
            {
                var car = Own("Bridge vehicle " + direction).AddComponent<Vehicle>();
                car.transform.position = new Vector3(
                    direction * 2.3f,
                    .2f,
                    direction > 0 ? 215 : 390
                );
                car.transform.rotation = Quaternion.Euler(0, direction > 0 ? 0 : 180, 0);
                car.Initialize();
                car.Handbrake = true;
                yield return new WaitForSeconds(1);
                car.Handbrake = false;
                car.Body.linearVelocity = car.transform.forward * targetSpeed;
                float start = Time.time;
                while (
                    Time.time - start < 45
                    && (direction > 0 ? car.Body.position.z < 385 : car.Body.position.z > 220)
                )
                {
                    car.ApplyDesiredAcceleration(
                        Mathf.Clamp((targetSpeed - car.Speed) * 1.8f, -3, 2)
                    );
                    Assert.Greater(
                        Vector3.Dot(car.transform.up, Vector3.up),
                        .9f,
                        "Car must remain upright over the approach"
                    );
                    yield return new WaitForFixedUpdate();
                }
                Assert.Less(Time.time - start, 45, "Car must traverse the complete bridge");
                Assert.AreEqual(
                    0,
                    car.Collisions,
                    "The road must not strike the chassis: " + car.LastContact
                );
                Debug.Log(
                    $"CODRIVER_BRIDGE target_mps={targetSpeed} direction={direction} seconds={Time.time - start:F2} contacts={car.Collisions}"
                );
                UnityEngine.Object.Destroy(car.gameObject);
                yield return null;
            }
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator RebuildingDavisReleasesGeneratedGeometry()
        {
            Art.Initialize();
            var map = MapDefinition.Load("davis-downtown");
            for (int repetition = 0; repetition < 2; repetition++)
            {
                var town = Own("Davis resource lifetime " + repetition).AddComponent<Town>();
                town.Map = map;
                town.Build(null);
                var resources = town.GetComponent<RuntimeResources>().Owned.ToArray();
                Assert.Greater(
                    resources.OfType<Mesh>().Count(),
                    300,
                    "The real generated district must participate in lifetime cleanup."
                );
                UnityEngine.Object.Destroy(town.gameObject);
                yield return null;
                yield return null;
                Assert.IsTrue(
                    resources.All(r => !r),
                    "Leaving a district must release its generated meshes and materials."
                );
            }
        }

        [UnityTest]
        public IEnumerator VehicleImpactInterruptsWalkingWithoutPoseSnapping()
        {
            var ground = Own("Contact course");
            ground.transform.position = Vector3.down * .5f;
            ground.AddComponent<BoxCollider>().size = new Vector3(30, 1, 50);
            var actor = Own("Impact pedestrian");
            actor.layer = TrafficClearance.WalkerLayer;
            actor.transform.position = new Vector3(0, .05f, 3);
            var shape = actor.AddComponent<CapsuleCollider>();
            shape.center = Vector3.up * .9f;
            shape.height = 1.8f;
            shape.radius = .25f;
            var body = actor.AddComponent<Rigidbody>();
            var mover = actor.AddComponent<ActorLocomotion>();
            mover.Initialize("Person");
            var vehicle = Own("Finite mass impact vehicle");
            vehicle.layer = TrafficClearance.PlayerLayer;
            vehicle.transform.position = new Vector3(0, .7f, -4);
            vehicle.AddComponent<BoxCollider>().size = new Vector3(1.8f, 1.1f, 4.4f);
            var car = vehicle.AddComponent<Rigidbody>();
            car.mass = 1462;
            car.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            car.linearVelocity = Vector3.forward * 12;
            float end = Time.time + 1.3f;
            bool reacted = false;
            float furthest = 3;
            while (Time.time < end)
            {
                reacted |= mover.Reacting;
                furthest = Mathf.Max(furthest, body.position.z);
                Assert.IsTrue(float.IsFinite(body.position.x));
                yield return new WaitForFixedUpdate();
            }
            Assert.IsTrue(reacted, "Physical contact must interrupt locomotion");
            Assert.Greater(furthest, 3.4f, "Finite mass actor must respond to impact");
            Assert.Greater(
                body.position.z,
                3.4f,
                "Navigation must not reset the impacted actor to its spawn"
            );
        }

        [UnityTest]
        public IEnumerator DisplacedWalkerReturnsToSidewalkWithFiniteMotion()
        {
            var ground = Own("Navigation recovery course");
            ground.transform.position = Vector3.down * .5f;
            ground.AddComponent<BoxCollider>().size = new Vector3(30, 1, 30);
            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = .28f;
            var data = NavMeshBuilder.BuildNavMeshData(
                settings,
                new List<NavMeshBuildSource>
                {
                    new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Box,
                        transform = Matrix4x4.TRS(
                            Vector3.down * .05f,
                            Quaternion.identity,
                            Vector3.one
                        ),
                        size = new Vector3(8, .1f, 20),
                        area = 0,
                    },
                },
                new Bounds(Vector3.zero, new Vector3(12, 5, 24)),
                Vector3.zero,
                Quaternion.identity
            );
            var navigation = NavMesh.AddNavMeshData(data);
            try
            {
                var actor = Own("Displaced walker");
                actor.transform.position = new Vector3(2.8f, .05f, 0);
                var shape = actor.AddComponent<CapsuleCollider>();
                shape.center = Vector3.up * .9f;
                shape.height = 1.8f;
                shape.radius = .25f;
                var body = actor.AddComponent<Rigidbody>();
                var agent = actor.AddComponent<NavMeshAgent>();
                agent.areaMask = PedestrianNavigation.WalkingMask;
                agent.speed = 1.25f;
                actor.AddComponent<ActorLocomotion>().Initialize("Person");
                agent.SetDestination(new Vector3(2.8f, 0, 4));
                yield return new WaitForSeconds(.3f);
                body.AddForce(Vector3.right * 5, ForceMode.VelocityChange);
                float until = Time.time + 10,
                    furthest = body.position.x;
                var previous = body.position;
                while (Time.time < until)
                {
                    yield return new WaitForFixedUpdate();
                    furthest = Mathf.Max(furthest, body.position.x);
                    Assert.Less(
                        Vector3.Distance(previous, body.position),
                        .09f,
                        "Navigation recovery must not snap the physical body"
                    );
                    previous = body.position;
                }
                Assert.Greater(furthest, 4.3f, "Fixture must displace the body off the sidewalk");
                Assert.Less(
                    body.position.x,
                    3.85f,
                    "Walker must physically return to the sidewalk"
                );
                Assert.IsFalse(body.isKinematic);
            }
            finally
            {
                navigation.Remove();
                UnityEngine.Object.Destroy(data);
            }
        }

        [UnityTest]
        public IEnumerator OccupiedCrossingClearsWithoutBusPedestrianDeadlock()
        {
            var ground = Own("Crossing course");
            ground.transform.position = Vector3.down * .5f;
            ground.AddComponent<BoxCollider>().size = new Vector3(40, 1, 160);
            var route = new TrackPath(
                new[] { new Vector3(0, 0, -30), new Vector3(0, 0, 70) },
                false
            );
            var drivers = new List<PhysicalDriver>();
            var shapes = new List<BoxCollider>();
            foreach (string kind in new[] { "Bus", "Truck" })
            {
                var go = Own("Crossing " + kind);
                go.layer = TrafficClearance.TrafficLayer;
                go.transform.position = new Vector3(0, .2f, kind == "Bus" ? 0 : -15);
                var profile = VehicleProfile.ForTraffic(kind);
                var shell = go.AddComponent<BoxCollider>();
                shell.size = profile.collisionSize;
                shell.center = profile.collisionCentre;
                shapes.Add(shell);
                drivers.Add(new PhysicalDriver(go.AddComponent<Rigidbody>(), profile));
            }
            var person = Own("Waiting beside occupied bus crossing");
            person.layer = TrafficClearance.WalkerLayer;
            person.transform.position = new Vector3(1.9f, .05f, 3);
            var capsule = person.AddComponent<CapsuleCollider>();
            capsule.height = 1.8f;
            capsule.radius = .25f;
            capsule.center = Vector3.up * .9f;
            var actorBody = person.AddComponent<Rigidbody>();
            var mover = person.AddComponent<ActorLocomotion>();
            mover.Initialize("Person");
            mover.enabled = false; // This fixture supplies the navigation velocity.
            float start = Time.time;
            bool crossed = false;
            while (Time.time - start < 40 && (!crossed || drivers[1].Dynamics.Body.position.z < 10))
            {
                foreach (var driver in drivers)
                {
                    var body = driver.Dynamics.Body;
                    var shape = body.GetComponent<BoxCollider>();
                    float speed = body.linearVelocity.magnitude;
                    float target = Mathf.Min(
                        4,
                        crossed
                            ? float.PositiveInfinity
                            : TrafficClearance.CrossingSpeedLimit(shape, actorBody.position),
                        TrafficClearance.ForwardSpeedLimit(shape, speed),
                        TrafficClearance.SafeSpeed(
                            route,
                            route.NearestDistance(body.position),
                            0,
                            speed,
                            shape
                        )
                    );
                    driver.Follow(route, target);
                    Assert.IsFalse(
                        Physics.ComputePenetration(
                            shape,
                            body.position,
                            body.rotation,
                            capsule,
                            actorBody.position,
                            actorBody.rotation,
                            out _,
                            out _
                        ),
                        "Clearing the crossing must not drive through the pedestrian"
                    );
                }
                crossed |= actorBody.position.x < -6;
                mover.StepVelocity(
                    crossed ? Vector3.zero : Vector3.left * 1.4f,
                    Time.fixedDeltaTime
                );
                yield return new WaitForFixedUpdate();
            }
            Assert.IsTrue(crossed, "Waiting pedestrian must finish after the occupied bus clears");
            Assert.Greater(drivers[0].Dynamics.Body.position.z, 12);
            Assert.Greater(
                drivers[1].Dynamics.Body.position.z,
                10,
                "Following vehicle must yield and then proceed without a permanent queue"
            );
            Debug.Log($"CODRIVER_CROSSING_CLEAR seconds={Time.time - start:F2}");
        }

        [UnityTest]
        public IEnumerator PedestrianWalksAroundStoppedCarWithoutPushingIt()
        {
            var ground = Own("Stopped-car crossing course");
            ground.transform.position = Vector3.down * .5f;
            ground.AddComponent<BoxCollider>().size = new Vector3(24, 1, 24);
            var data = NavMeshBuilder.BuildNavMeshData(
                NavMesh.GetSettingsByID(0),
                new List<NavMeshBuildSource>
                {
                    new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Box,
                        transform = Matrix4x4.TRS(
                            Vector3.down * .05f,
                            Quaternion.identity,
                            Vector3.one
                        ),
                        size = new Vector3(20, .1f, 20),
                        area = PedestrianNavigation.CrossingArea,
                    },
                },
                new Bounds(Vector3.zero, new Vector3(24, 5, 24)),
                Vector3.zero,
                Quaternion.identity
            );
            var navigation = NavMesh.AddNavMeshData(data);
            try
            {
                var car = Own("Stopped finite-mass car");
                car.layer = TrafficClearance.TrafficLayer;
                car.transform.position = Vector3.up * .2f;
                var profile = VehicleProfile.ForTraffic("Car");
                var shell = car.AddComponent<BoxCollider>();
                shell.size = profile.collisionSize;
                shell.center = profile.collisionCentre;
                var driver = new PhysicalDriver(car.AddComponent<Rigidbody>(), profile);
                for (int i = 0; i < 150; i++)
                {
                    driver.Hold();
                    yield return new WaitForFixedUpdate();
                }
                var parked = driver.Dynamics.Body.position;
                var actor = Own("Blocked crossing pedestrian");
                actor.layer = TrafficClearance.WalkerLayer;
                actor.transform.position = new Vector3(4, .05f, profile.collisionSize.z * .5f);
                var shape = actor.AddComponent<CapsuleCollider>();
                shape.center = Vector3.up * .9f;
                shape.height = 1.8f;
                shape.radius = .25f;
                var body = actor.AddComponent<Rigidbody>();
                var agent = actor.AddComponent<NavMeshAgent>();
                agent.areaMask = NavMesh.AllAreas;
                agent.speed = 1.25f;
                actor.AddComponent<ActorLocomotion>().Initialize("Person");
                Vector3 destination = new Vector3(-4, 0, profile.collisionSize.z * .5f);
                agent.SetDestination(destination);
                float start = Time.time;
                while (
                    Time.time - start < 20
                    && Vector3.ProjectOnPlane(body.position - destination, Vector3.up).magnitude
                        > .4f
                )
                {
                    driver.Hold();
                    yield return new WaitForFixedUpdate();
                }
                Assert.Less(
                    Vector3.ProjectOnPlane(body.position - destination, Vector3.up).magnitude,
                    .4f,
                    "A physically blocked route must find a walkable detour around the stopped car"
                );
                Assert.Less(
                    Vector3
                        .ProjectOnPlane(driver.Dynamics.Body.position - parked, Vector3.up)
                        .magnitude,
                    .15f,
                    "The pedestrian must not push the handbraked car aside"
                );
                Assert.IsFalse(body.isKinematic);
            }
            finally
            {
                navigation.Remove();
                UnityEngine.Object.Destroy(data);
            }
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator CedarBusCompletesCornersWithoutFurnitureDeadlock()
        {
            Art.Initialize();
            var town = Own("Cedar transit qualification").AddComponent<Town>();
            town.Map = MapDefinition.Cedar();
            town.Build(null);
            float start = Time.time;
            Vector3 previous = town.Bus.transform.position;
            float travelled = 0;
            float stationarySince = Time.time;
            Vector3 anchor = previous;
            while (Time.time - start < 650 && town.Bus.StopVisits < 2)
            {
                yield return new WaitForSeconds(.5f);
                Vector3 position = town.Bus.transform.position;
                travelled += Vector3.Distance(previous, position);
                previous = position;
                if (Vector3.Distance(position, anchor) > .5f)
                {
                    stationarySince = Time.time;
                    anchor = position;
                }
                if (Time.time - stationarySince > 120)
                    break;
            }
            Debug.Log(
                $"CODRIVER_CEDAR_TRANSIT stops={town.Bus.StopVisits} travelled={travelled:F1} position={town.Bus.transform.position} state={town.Bus.State} contact={town.Bus.Contact}"
            );
            Assert.GreaterOrEqual(
                town.Bus.StopVisits,
                2,
                "Bus must continue through the populated town: " + town.Bus.State
            );
            Assert.Greater(travelled, 200);
            Assert.IsEmpty(town.Bus.Contact, "Furniture must be avoided without collisions");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator CedarOrdinaryTrafficDoesNotFormPermanentRoadblocks()
        {
            return OrdinaryTrafficRemainsLive(MapDefinition.Cedar(), "cedar-clearance");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator DavisOrdinaryTrafficDoesNotFormPermanentRoadblocks()
        {
            return OrdinaryTrafficRemainsLive(
                MapDefinition.Load("davis-downtown"),
                "davis-clearance"
            );
        }

        [UnityTest]
        public IEnumerator CedarAnimalsFinishBothDirectionsOfAvenueCrossing()
        {
            Art.Initialize();
            var town = Own("Animal crossing qualification").AddComponent<Town>();
            town.Build(null);
            foreach (var traffic in town.Traffic)
                traffic.gameObject.SetActive(false);
            town.Train.gameObject.SetActive(false);
            town.Tram.gameObject.SetActive(false);
            foreach (var walker in town.Walkers)
                walker.gameObject.SetActive(false);
            foreach (int index in new[] { 16, 17 })
            {
                var actor = town.Walkers[index];
                actor.gameObject.SetActive(true);
                foreach (int direction in new[] { 1, -1 })
                {
                    var start = new Vector3(-11.5f, .2f, -10.9f * direction);
                    var finish = new Vector3(-11.5f, .2f, 10.9f * direction);
                    actor.PrepareCrossing(start, finish);
                    actor.BeginCrossing();
                    float began = Time.time;
                    while (actor.Crossing && Time.time - began < 40)
                        yield return new WaitForSeconds(.25f);
                    Assert.IsFalse(
                        actor.Crossing,
                        actor.NavigationDetail
                            + " "
                            + actor.GetComponent<ActorLocomotion>().Diagnostic
                    );
                    Assert.Less(
                        Vector3.Distance(actor.transform.position, finish),
                        1,
                        "An animal must physically reach the opposite sidewalk."
                    );
                    Assert.Greater(
                        actor.transform.position.y,
                        .1f,
                        "The visual/navigation origin must remain above the sidewalk."
                    );
                }
                actor.gameObject.SetActive(false);
            }
        }

        IEnumerator OrdinaryTrafficRemainsLive(MapDefinition map, string evidence)
        {
            Art.Initialize();
            var town = Own("Population clearance qualification").AddComponent<Town>();
            town.Map = map;
            town.Build(null);
            var cars = town.Traffic.Where(t => t.Kind == "Car" || t.Kind == "Truck").ToArray();
            var anchors = cars.Select(t => t.transform.position).ToArray();
            var lastMotion = cars.Select(t => Time.time).ToArray();
            var longestWait = new float[cars.Length];
            Vector3 trainPrevious = town.Train.transform.position;
            Vector3 tramPrevious = town.Tram ? town.Tram.transform.position : Vector3.zero;
            float trainTravel = 0,
                tramTravel = 0;
            float start = Time.time;
            while (Time.time - start < 360 && longestWait.Max() < 121)
            {
                yield return new WaitForSeconds(.5f);
                trainTravel += Vector3.Distance(trainPrevious, town.Train.transform.position);
                trainPrevious = town.Train.transform.position;
                if (town.Tram)
                {
                    tramTravel += Vector3.Distance(tramPrevious, town.Tram.transform.position);
                    tramPrevious = town.Tram.transform.position;
                }
                for (int i = 0; i < cars.Length; i++)
                {
                    if (Vector3.Distance(anchors[i], cars[i].transform.position) > .5f)
                    {
                        anchors[i] = cars[i].transform.position;
                        lastMotion[i] = Time.time;
                    }
                    longestWait[i] = Mathf.Max(longestWait[i], Time.time - lastMotion[i]);
                }
            }
            CapturePopulation(town, evidence, Time.time - start);
            string details = string.Join(
                "; ",
                cars.Select(
                    (t, i) =>
                        t.name
                        + " wait="
                        + longestWait[i].ToString("F1")
                        + " "
                        + t.State
                        + " blocker="
                        + t.Clearance
                        + " at="
                        + t.transform.position
                )
            );
            Assert.Less(longestWait.Max(), 120, details);
            Assert.IsFalse(town.Train.Derailed, town.Train.NavigationDetail);
            Assert.Greater(
                trainTravel,
                500,
                "Train must cross the populated district: " + town.Train.NavigationDetail
            );
            if (town.Tram && town.Tram.gameObject.activeInHierarchy)
            {
                Assert.IsFalse(town.Tram.Derailed, town.Tram.NavigationDetail);
                Assert.Greater(
                    tramTravel,
                    200,
                    "Streetcar must leave its initial block: " + town.Tram.NavigationDetail
                );
            }
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator DavisBusTravelsBetweenStopsAndCompletesPassengerTrip()
        {
            Art.Initialize();
            var town = Own("Davis transit qualification").AddComponent<Town>();
            town.Map = MapDefinition.Load("davis-downtown");
            town.Build(null);
            float start = Time.time;
            Vector3 previous = town.Bus.transform.position;
            float travelled = 0;
            bool boarded = false;
            bool alighted = false;
            var leaving = new HashSet<Walker>();
            float nextSnapshot = Time.time;
            while (Time.time - start < 900 && (town.Bus.StopVisits < 2 || !boarded || !alighted))
            {
                yield return new WaitForSeconds(.25f);
                if (Time.time >= nextSnapshot)
                {
                    CapturePopulation(town, "transit", Time.time - start);
                    nextSnapshot = Time.time + 15;
                }
                travelled += Vector3.Distance(previous, town.Bus.transform.position);
                previous = town.Bus.transform.position;
                boarded |= town.Walkers.Where((w, i) => i != 12).Any(w => w.State == "Passenger");
                foreach (var walker in town.Walkers.Where((w, i) => i != 12))
                {
                    if (walker.State == "Leaving transit")
                        leaving.Add(walker);
                    if (leaving.Contains(walker) && walker.State == "Walking")
                        alighted = true;
                }
            }
            Debug.Log(
                $"CODRIVER_TRANSIT stops={town.Bus.StopVisits} travelled={travelled:F1} boarded={boarded} alighted={alighted} {town.Bus.State} {town.Bus.NavigationDetail}"
            );
            Assert.GreaterOrEqual(town.Bus.StopVisits, 2);
            Assert.Greater(
                travelled,
                200,
                "Repeated door cycles at one stop are not a route cycle"
            );
            Assert.IsTrue(
                boarded,
                string.Join("; ", town.Walkers.Select(w => w.State + " " + w.transform.position))
            );
            Assert.IsTrue(alighted, "A passenger must physically finish leaving at a later stop");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator DavisStorefrontsHaveCompleteNavigationFromTheSidewalk()
        {
            Art.Initialize();
            var town = Own("Davis shop access qualification").AddComponent<Town>();
            town.Map = MapDefinition.Load("davis-downtown");
            town.Build(null);
            yield return null;
            int checkedDoors = 0;
            foreach (var building in town.Map.buildings)
            {
                var entry = MappedStorefront.Find(town.Map, building);
                if (entry == null)
                    continue;
                var door = town.Doors.OrderBy(d => (d.Inside - entry.Centre).sqrMagnitude).First();
                Assert.IsTrue(
                    NavMesh.SamplePosition(
                        entry.SidewalkPoint,
                        out var start,
                        .5f,
                        NavMesh.AllAreas
                    ),
                    building.sourceId + " sidewalk is not navigable"
                );
                Assert.IsTrue(
                    NavMesh.SamplePosition(door.Inside, out var end, .5f, NavMesh.AllAreas),
                    building.sourceId + " interior is not navigable"
                );
                var route = new NavMeshPath();
                Assert.IsTrue(
                    NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, route)
                );
                Assert.AreEqual(
                    NavMeshPathStatus.PathComplete,
                    route.status,
                    building.sourceId + " has a disconnected shop threshold"
                );
                checkedDoors++;
            }
            Assert.GreaterOrEqual(checkedDoors, 7);
            int crossings = 0;
            foreach (var walker in town.Walkers.Where(w => !w.IsAnimal))
            {
                Assert.AreEqual(
                    0,
                    walker.GetComponent<NavMeshAgent>().areaMask
                        & (1 << PedestrianNavigation.CrossingArea),
                    "Ordinary walking must not bypass crossing decisions"
                );
                if (
                    !PedestrianNavigation.TryCrossing(
                        town,
                        walker.transform.position,
                        out var from,
                        out var to
                    )
                )
                    continue;
                Assert.IsTrue(
                    NavMesh.Raycast(from, to, out _, PedestrianNavigation.WalkingMask),
                    "Crosswalk must interrupt an ordinary sidewalk journey"
                );
                var crossing = new NavMeshPath();
                Assert.IsTrue(NavMesh.CalculatePath(from, to, NavMesh.AllAreas, crossing));
                Assert.AreEqual(NavMeshPathStatus.PathComplete, crossing.status);
                crossings++;
            }
            Assert.GreaterOrEqual(
                crossings,
                12,
                "Nearby citizens need reachable controlled crossing approaches"
            );
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator DavisParkingOccupantAndVehicleCompleteOnePhysicalCycle()
        {
            Art.Initialize();
            var town = Own("Davis parking qualification").AddComponent<Town>();
            town.Map = MapDefinition.Load("davis-downtown");
            town.Build(null);
            float start = Time.time;
            var states = new HashSet<string>();
            float nextSnapshot = Time.time;
            while (Time.time - start < 650 && town.ParkingCar.ParkingVisits == 0)
            {
                states.Add(town.ParkingCar.State);
                yield return new WaitForSeconds(.5f);
                if (Time.time >= nextSnapshot)
                {
                    CapturePopulation(town, "parking", Time.time - start);
                    nextSnapshot = Time.time + 15;
                }
            }
            Debug.Log(
                "CODRIVER_PARKING_CYCLE "
                    + town.ParkingCar.ParkingVisits
                    + " "
                    + string.Join(",", states)
                    + " "
                    + town.ParkingCar.NavigationDetail
                    + " contact="
                    + town.ParkingCar.Contact
            );
            if (town.ParkingCar.ParkingVisits == 0)
                Debug.Log(
                    "CODRIVER_PARKING_SURROUNDINGS "
                        + string.Join(
                            ";",
                            town.Walkers.Where(w =>
                                    (
                                        w.transform.position - town.ParkingCar.transform.position
                                    ).sqrMagnitude < 3600
                                )
                                .Select(w => w.NavigationDetail)
                        )
                );
            Assert.Greater(
                town.ParkingCar.ParkingVisits,
                0,
                town.ParkingCar.State
                    + " "
                    + town.ParkingCar.NavigationDetail
                    + " "
                    + town.ParkingCar.Contact
            );
            Assert.IsTrue(states.Contains("Reversing out"));
            Assert.IsTrue(states.Contains("Parking"));
            var occupant = town.Walkers[11];
            var occupantBody = occupant.GetComponent<Rigidbody>();
            var occupantShape = occupant.GetComponent<Collider>();
            float returnedAt = Time.time;
            while (
                Time.time - returnedAt < 25 && (!occupantShape.enabled || occupantBody.isKinematic)
            )
                yield return new WaitForFixedUpdate();
            Assert.IsTrue(occupantShape.enabled, "Driver must finish leaving the parked car");
            Assert.IsFalse(occupantBody.isKinematic, "Walking driver must resume finite dynamics");
            var car = town.ParkingCar.GetComponent<BoxCollider>();
            Assert.IsFalse(
                Physics.ComputePenetration(
                    occupantShape,
                    occupantBody.position,
                    occupantBody.rotation,
                    car,
                    town.ParkingCar.transform.position,
                    town.ParkingCar.transform.rotation,
                    out _,
                    out _
                ),
                "Alighting must not reactivate the driver collider inside the car"
            );
            Assert.Less(
                Vector3.Distance(occupantBody.position, town.ParkingCar.DriverDoorPoint),
                1
            );
        }

        [UnityTest, Timeout(1200000)]
        public IEnumerator TenSeededDavisAmbulanceDispatchAndReturnCycles()
        {
            Art.Initialize();
            var town = Own("Davis lifecycle qualification").AddComponent<Town>();
            town.Map = MapDefinition.Load("davis-downtown");
            town.Build(null);
            var reports = new List<Cycle>();
            int firstSeed = 21,
                lastSeed = 31;
            string[] args = Environment.GetCommandLineArgs();
            int seedFlag = Array.IndexOf(args, "-ambulance-seed");
            if (
                seedFlag >= 0
                && seedFlag + 1 < args.Length
                && int.TryParse(args[seedFlag + 1], out int selected)
            )
            {
                firstSeed = selected;
                lastSeed = selected + 1;
                town.Seed = selected;
                town.ResetActivities();
                yield return null;
            }
            for (int seed = firstSeed; seed < lastSeed; seed++)
            {
                if (seed != firstSeed)
                {
                    town.Seed = seed;
                    town.ResetActivities();
                    yield return null;
                }
                var ambulance = town.Emergency;
                var blocker = Own("Temporary occupied emergency access");
                blocker.layer = TrafficClearance.ParkedLayer;
                blocker.transform.SetPositionAndRotation(
                    ambulance.transform.position + ambulance.transform.forward * 7,
                    ambulance.transform.rotation
                );
                var shape = blocker.AddComponent<BoxCollider>();
                shape.size = new Vector3(2, 1.15f, 4.4f);
                shape.center = Vector3.up * .85f;
                blocker.AddComponent<ParkedDynamics>();
                ambulance.Dispatch();
                float start = Time.time;
                float sampledAt = Time.time;
                Vector3 previous = ambulance.transform.position;
                float moved = 0,
                    maxJump = 0;
                float stationarySince = Time.time;
                bool cleared = false;
                while (Time.time - start < 900)
                {
                    if (!cleared && Time.time - start > 12)
                    {
                        UnityEngine.Object.Destroy(blocker);
                        cleared = true;
                    }
                    yield return new WaitForSeconds(.25f);
                    float travel = Vector3.Distance(previous, ambulance.transform.position);
                    moved += travel;
                    if (travel > .05f)
                        stationarySince = Time.time;
                    maxJump = Mathf.Max(maxJump, travel / Mathf.Max(.001f, Time.time - sampledAt));
                    sampledAt = Time.time;
                    previous = ambulance.transform.position;
                    if (ambulance.Activity.StartsWith("phase 0:") && Time.time - start > 20)
                        break;
                    if (Time.time - stationarySince > 100)
                        break;
                }
                reports.Add(
                    new Cycle
                    {
                        seed = seed,
                        seconds = Time.time - start,
                        completed = ambulance.Activity.StartsWith("phase 0:"),
                        distance = moved,
                        maxQuarterSecondMovement = maxJump,
                        finalState = ambulance.Activity,
                        finalContact = ambulance.Contact,
                        junctions = town
                            .Signals.Where(s => s.Reservation != "None")
                            .Select(s =>
                                s.name
                                + " at="
                                + s.transform.position.ToString("F2")
                                + " reservation="
                                + s.Reservation
                            )
                            .ToArray(),
                        navigation = ambulance.NavigationDetail,
                        walkers = town
                            .Walkers.Select(w =>
                                w.Species + ":" + w.State + " at=" + w.transform.position
                            )
                            .ToArray(),
                        traffic = town
                            .Traffic.Select(t =>
                                t.Kind
                                + ":"
                                + t.State
                                + " v="
                                + t.Speed.ToString("F2")
                                + " at="
                                + t.transform.position
                                + " contact="
                                + t.Contact
                                + " "
                                + t.NavigationDetail
                                + " clearance="
                                + t.Clearance
                            )
                            .ToArray(),
                    }
                );
                string folder = Path.GetFullPath("../outputs/qualification/overhaul");
                Directory.CreateDirectory(folder);
                File.WriteAllText(
                    Path.Combine(folder, "ambulance-playmode.json"),
                    JsonUtility.ToJson(new Report { cycles = reports.ToArray() }, true)
                );
                if (!reports[reports.Count - 1].completed)
                    break;
            }
            Assert.IsTrue(
                reports.All(r => r.completed),
                string.Join(
                    "\n",
                    reports
                        .Where(r => !r.completed)
                        .Select(r => r.seed + ": " + r.finalState + " " + r.finalContact)
                )
            );
            Assert.IsTrue(
                reports.All(r => r.maxQuarterSecondMovement < 25),
                "Unexplained physical repositioning detected"
            );
        }

        [Serializable]
        class Cycle
        {
            public int seed;
            public float seconds,
                distance,
                maxQuarterSecondMovement;
            public bool completed;
            public string finalState,
                finalContact,
                navigation;
            public string[] traffic,
                walkers,
                junctions,
                rail;
        }

        static void CapturePopulation(Town town, string name, float seconds)
        {
            var cycle = new Cycle
            {
                seconds = seconds,
                rail = new[] { town.Train, town.Tram }
                    .Where(r => r)
                    .Select(r => r.name + ":" + r.State + " " + r.NavigationDetail)
                    .ToArray(),
                traffic = town
                    .Traffic.Select(t =>
                        t.name
                        + ":"
                        + t.State
                        + " v="
                        + t.Speed.ToString("F2")
                        + " "
                        + t.NavigationDetail
                        + " clearance="
                        + t.Clearance
                        + " contact="
                        + t.Contact
                    )
                    .ToArray(),
                walkers = town.Walkers.Select(w => w.name + ":" + w.NavigationDetail).ToArray(),
                junctions = town
                    .Signals.Where(s => s.Reservation != "None")
                    .Select(s =>
                        s.name
                        + " at="
                        + s.transform.position.ToString("F2")
                        + " reservation="
                        + s.Reservation
                    )
                    .ToArray(),
            };
            string folder = Path.GetFullPath("../outputs/qualification/realism");
            Directory.CreateDirectory(folder);
            File.WriteAllText(
                Path.Combine(folder, name + "-population-latest.json"),
                JsonUtility.ToJson(cycle, true)
            );
        }

        [Serializable]
        class Report
        {
            public Cycle[] cycles;
        }
    }
}
