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
        public IEnumerator InitialRoadVehiclesDoNotOverlapRailBodies()
        {
            Art.Initialize();
            foreach (string map in new[] { "cedar-grove", "davis-downtown" })
            {
                var town = Own("Initial placement " + map).AddComponent<Town>();
                town.Map = MapDefinition.Load(map);
                town.Build(null);
                var overlaps = new List<string>();
                foreach (var road in town.Traffic)
                foreach (var rail in new[] { town.Train, town.Tram })
                {
                    if (!rail.gameObject.activeInHierarchy)
                        continue;
                    var car = road.GetComponent<BoxCollider>();
                    foreach (var coach in rail.CollisionBodies)
                        if (
                            Physics.ComputePenetration(
                                car,
                                car.transform.position,
                                car.transform.rotation,
                                coach,
                                coach.transform.position,
                                coach.transform.rotation,
                                out _,
                                out float depth
                            )
                        )
                            overlaps.Add(
                                $"{road.name} at {road.transform.position} / {rail.name} depth={depth:F3}"
                            );
                }
                Assert.IsEmpty(
                    overlaps,
                    map + " starts with overlapping bodies: " + string.Join("; ", overlaps)
                );
                UnityEngine.Object.Destroy(town.gameObject);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator CockpitPresentationIsExcludedFromIndependentCamera()
        {
            Art.Initialize();
            var vehicle = Own("Cockpit isolation vehicle").AddComponent<Vehicle>();
            vehicle.Initialize();
            vehicle.Body.isKinematic = true;
            var cockpit = vehicle.GetComponentInChildren<Cockpit>();
            cockpit.AttachDisplay(null);
            var sensors = Own("Isolated camera rig").AddComponent<SensorRig>();
            sensors.Initialize(vehicle, "cockpit-isolation", "test-token");
            yield return null;
            var display = vehicle.GetComponentInChildren<OpenpilotDisplay>();
            Assert.IsNotNull(display);
            Assert.IsNotNull(vehicle.GetComponentInChildren<CockpitInstruments>());
            foreach (var canvas in vehicle.GetComponentsInChildren<Canvas>())
            foreach (var part in canvas.GetComponentsInChildren<Transform>(true))
                Assert.AreEqual(
                    0,
                    sensors.Sensor.cullingMask & (1 << part.gameObject.layer),
                    "Cockpit presentation must never appear in the observer camera: " + part.name
                );
        }

        [UnityTest]
        public IEnumerator ProjectedModelGeometryStaysInsideCockpitScreen()
        {
            Art.Initialize();
            var vehicle = Own("Viewport clipping vehicle").AddComponent<Vehicle>();
            vehicle.Initialize();
            vehicle.Body.isKinematic = true;
            vehicle.GetComponentInChildren<Cockpit>().AttachDisplay(null);
            var display = vehicle.GetComponentInChildren<OpenpilotDisplay>();
            var camera = Own("Driver viewport camera").AddComponent<Camera>();
            camera.transform.position = vehicle.Profile.driverEye;
            camera.transform.rotation = Quaternion.Euler(6, 0, 0);
            camera.fieldOfView = 70;
            camera.nearClipPlane = .04f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 1 << 9;
            camera.enabled = false;
            var target = new RenderTexture(640, 360, 24);
            target.Create();
            camera.targetTexture = target;
            var snapshot = new OpenpilotUiSnapshot
            {
                schema_version = 1,
                session_id = "viewport",
                timestamp = 100,
                healthy = true,
                calibration = new Calibration(),
                alert_size = "none",
            };
            yield return null;
            try
            {
                display.Present(
                    snapshot,
                    "viewport",
                    100,
                    0,
                    false,
                    false,
                    false,
                    Texture2D.blackTexture,
                    42,
                    _ => Texture2D.blackTexture,
                    "",
                    "quiet"
                );
                var before = ReadViewport(camera, target);
                snapshot.road = new UiModelGeometry
                {
                    schema_version = 2,
                    session_id = "viewport",
                    timestamp = 100,
                    frame_id = 42,
                    model = "deliberately out-of-bounds layout fixture",
                    path = new ModelLine
                    {
                        x = new[] { 2f, 4, 8, 16 },
                        y = new[] { -20f, 20, -20, 20 },
                        z = new[] { 0f, 0, 0, 0 },
                    },
                };
                display.Present(
                    snapshot,
                    "viewport",
                    100,
                    0,
                    false,
                    false,
                    false,
                    Texture2D.blackTexture,
                    42,
                    _ => Texture2D.blackTexture,
                    "",
                    "quiet"
                );
                var after = ReadViewport(camera, target);
                var viewport = display.transform.Find("Camera viewport") as RectTransform;
                var corners = new Vector3[4];
                viewport.GetWorldCorners(corners);
                var projected = corners.Select(camera.WorldToScreenPoint).ToArray();
                float left = projected.Min(p => p.x) - 2,
                    right = projected.Max(p => p.x) + 2;
                float bottom = projected.Min(p => p.y) - 2,
                    top = projected.Max(p => p.y) + 2;
                int inside = 0,
                    outside = 0;
                for (int i = 0; i < after.Length; i++)
                {
                    int delta =
                        Mathf.Abs(after[i].r - before[i].r)
                        + Mathf.Abs(after[i].g - before[i].g)
                        + Mathf.Abs(after[i].b - before[i].b);
                    if (delta < 20)
                        continue;
                    int x = i % target.width,
                        y = i / target.width;
                    if (x >= left && x <= right && y >= bottom && y <= top)
                        inside++;
                    else
                        outside++;
                }
                Assert.Greater(inside, 10, "The fixture must actually draw model geometry");
                Assert.AreEqual(0, outside, "Model geometry cannot spill across the dashboard");
            }
            finally
            {
                camera.targetTexture = null;
                target.Release();
                UnityEngine.Object.Destroy(target);
            }
        }

        static Color32[] ReadViewport(Camera camera, RenderTexture target)
        {
            Canvas.ForceUpdateCanvases();
            camera.Render();
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            var pixels = image.GetPixels32();
            RenderTexture.active = previous;
            UnityEngine.Object.Destroy(image);
            return pixels;
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
        public IEnumerator RecordedDavisCatCompletesCrossing()
        {
            Art.Initialize();
            var town = Own("Recorded Davis cat crossing").AddComponent<Town>();
            town.Map = MapDefinition.Load("davis-downtown");
            town.Build(null);
            var cat = town.Walkers[17];
            foreach (var traffic in town.Traffic)
                traffic.gameObject.SetActive(false);
            foreach (var other in town.Walkers)
                if (other != cat)
                    other.gameObject.SetActive(false);
            town.Train.gameObject.SetActive(false);
            town.Tram.gameObject.SetActive(false);
            var goal = new Vector3(61.64f, .11f, 216.93f);
            cat.PrepareCrossing(new Vector3(59.34f, .10f, 228.96f), goal);
            cat.BeginCrossing();
            float start = Time.time;
            while (cat.Crossing && Time.time - start < 30)
                yield return new WaitForFixedUpdate();
            string details =
                cat.NavigationDetail + " " + cat.GetComponent<ActorLocomotion>().Diagnostic;
            Debug.Log("CODRIVER_RECORDED_CAT " + details);
            Assert.Less(
                Vector3.ProjectOnPlane(cat.transform.position - goal, Vector3.up).magnitude,
                .65f,
                details
            );
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator RecordedCedarWalkersRecoverFromBackOfSidewalk()
        {
            Art.Initialize();
            var town = Own("Recorded Cedar sidewalk recovery").AddComponent<Town>();
            town.Build(null);
            foreach (var car in town.Traffic)
                car.gameObject.SetActive(false);
            town.Train.gameObject.SetActive(false);
            town.Tram.gameObject.SetActive(false);
            foreach (var walker in town.Walkers)
                walker.gameObject.SetActive(false);
            foreach (int index in new[] { 31, 17 })
            {
                var walker = town.Walkers[index];
                walker.gameObject.SetActive(true);
                var goal = new Vector3(-10, .2f, -192.3f);
                walker.PrepareCrossing(new Vector3(-11.72f, -.15f, -208.05f), goal);
                walker.BeginCrossing();
                var body = walker.GetComponent<Rigidbody>();
                var previous = body.position;
                float start = Time.time;
                while (walker.Crossing && Time.time - start < 35)
                {
                    yield return new WaitForFixedUpdate();
                    Assert.Less(
                        Vector3.Distance(body.position, previous),
                        .09f,
                        "Recovering from the raised sidewalk must use finite motion"
                    );
                    previous = body.position;
                }
                Assert.IsFalse(
                    walker.Crossing,
                    walker.NavigationDetail
                        + " "
                        + walker.GetComponent<ActorLocomotion>().Diagnostic
                );
                Assert.Less(
                    Vector3.ProjectOnPlane(body.position - goal, Vector3.up).magnitude,
                    .65f
                );
                Assert.Greater(body.position.y, .1f);
                walker.gameObject.SetActive(false);
            }
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator RecordedCedarWalkerClearsSignalPole()
        {
            Art.Initialize();
            var town = Own("Recorded Cedar signal pole").AddComponent<Town>();
            town.Build(null);
            foreach (var car in town.Traffic)
                car.gameObject.SetActive(false);
            town.Train.gameObject.SetActive(false);
            town.Tram.gameObject.SetActive(false);
            var walker = town.Walkers[44];
            foreach (var other in town.Walkers)
                if (other != walker)
                    other.gameObject.SetActive(false);
            var goal = new Vector3(-189.64f, .2f, -11.1f);
            walker.PrepareCrossing(new Vector3(-189.96f, 0, -4.67f), goal);
            walker.BeginCrossing();
            var body = walker.GetComponent<Rigidbody>();
            var previous = body.position;
            float start = Time.time;
            while (walker.Crossing && Time.time - start < 25)
            {
                yield return new WaitForFixedUpdate();
                Assert.Less(Vector3.Distance(body.position, previous), .09f);
                previous = body.position;
            }
            Assert.IsFalse(
                walker.Crossing,
                walker.NavigationDetail + " " + walker.GetComponent<ActorLocomotion>().Diagnostic
            );
            Assert.Less(Vector3.ProjectOnPlane(body.position - goal, Vector3.up).magnitude, .65f);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator RecordedCedarCrossingClearsTruckContact()
        {
            Art.Initialize();
            var town = Own("Recorded Cedar truck crossing").AddComponent<Town>();
            town.Build(null);
            var truck = town.Traffic[8];
            var pedestrian = town.Walkers[50];
            foreach (var other in town.Traffic)
                if (other != truck)
                    other.gameObject.SetActive(false);
            foreach (var other in town.Walkers)
                if (other != pedestrian)
                    other.gameObject.SetActive(false);
            town.Train.gameObject.SetActive(false);
            town.Tram.gameObject.SetActive(false);
            truck.enabled = false;
            var holding = new PhysicalDriver(truck.Body, VehicleProfile.ForTraffic("Truck"));
            PlaceFixture(
                truck,
                new Vector3(-13.51f, .12f, -.30f),
                Quaternion.Euler(0, 101.9f, 0) * Vector3.forward
            );
            for (int step = 0; step < 250; step++)
            {
                holding.Hold();
                yield return new WaitForFixedUpdate();
            }
            var parked = truck.Body.position;
            var goal = new Vector3(-11.5f, .19f, 11);
            pedestrian.PrepareCrossing(new Vector3(-11.22f, .01f, -1.37f), goal);
            pedestrian.BeginCrossing();
            var body = pedestrian.GetComponent<Rigidbody>();
            var locomotion = pedestrian.GetComponent<ActorLocomotion>();
            var previous = body.position;
            float start = Time.time;
            while (pedestrian.Crossing && Time.time - start < 40)
            {
                holding.Hold();
                yield return new WaitForFixedUpdate();
                Assert.Less(
                    Vector3.Distance(previous, body.position),
                    .09f,
                    "Contact recovery must use finite locomotion without repositioning"
                );
                previous = body.position;
            }
            Debug.Log(
                "CODRIVER_CEDAR_TRUCK_CROSSING "
                    + pedestrian.NavigationDetail
                    + " "
                    + locomotion.Diagnostic
            );
            Assert.Less(
                Vector3.ProjectOnPlane(body.position - goal, Vector3.up).magnitude,
                .65f,
                pedestrian.NavigationDetail + " " + locomotion.Diagnostic
            );
            Assert.Less(
                Vector3.Distance(parked, truck.Body.position),
                .15f,
                "Walking cannot move the stopped truck aside"
            );
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator RecordedDavisCrossingDetoursToSameSideOfStoppedCar()
        {
            Art.Initialize();
            var town = Own("Recorded Davis pedestrian crossing").AddComponent<Town>();
            town.Map = MapDefinition.Load("davis-downtown");
            town.Build(null);
            var car = town.Traffic[9];
            var pedestrian = town.Walkers[49];
            foreach (var other in town.Traffic)
                if (other != car)
                    other.gameObject.SetActive(false);
            foreach (var other in town.Walkers)
                if (other != pedestrian)
                    other.gameObject.SetActive(false);
            town.Train.gameObject.SetActive(false);
            town.Tram.gameObject.SetActive(false);
            car.ScenarioSpeed = 0;
            PlaceFixture(
                car,
                new Vector3(-78.35664f, -.022403f, -97.78930f),
                new Vector3(-.6362376f, -.01515008f, .7713445f)
            );
            yield return new WaitForSeconds(1.5f);
            var parked = car.transform.position;
            var agent = pedestrian.GetComponent<NavMeshAgent>();
            var body = pedestrian.GetComponent<Rigidbody>();
            var shape = pedestrian.GetComponent<Collider>();
            var shell = car.GetComponent<BoxCollider>();
            var at = new Vector3(-79.7172546f, .01f, -95.3961563f);
            var destination = new Vector3(-73.10962f, .11f, -94.17220f);
            pedestrian.PrepareCrossing(at, destination);
            pedestrian.BeginCrossing();
            // Exercise a lost navigation intent after the crossing has started.
            // Recovery must be an actual physical walk, not clearing the Crossing flag.
            agent.ResetPath();
            agent.isStopped = true;
            var previous = body.position;
            float start = Time.time;
            while (
                Time.time - start < 20
                && Vector3.ProjectOnPlane(body.position - destination, Vector3.up).magnitude > .4f
            )
            {
                yield return new WaitForFixedUpdate();
                Assert.Less(
                    Vector3.Distance(previous, body.position),
                    .09f,
                    "The actor must walk around the obstacle without navigation snapping"
                );
                previous = body.position;
                Assert.IsFalse(
                    Physics.ComputePenetration(
                        shape,
                        body.position,
                        body.rotation,
                        shell,
                        car.transform.position,
                        car.transform.rotation,
                        out _,
                        out _
                    ),
                    "The detour cannot pass through the car"
                );
            }
            Debug.Log(
                "CODRIVER_RECORDED_CROSSING seconds="
                    + (Time.time - start)
                    + " "
                    + pedestrian.NavigationDetail
                    + " "
                    + pedestrian.GetComponent<ActorLocomotion>().Diagnostic
            );
            Assert.Less(
                Vector3.ProjectOnPlane(body.position - destination, Vector3.up).magnitude,
                .4f,
                "The pedestrian must clear the exact recorded car nose"
            );
            Assert.Less(
                Vector3.Distance(parked, car.transform.position),
                .15f,
                "The pedestrian must not push the held vehicle aside"
            );
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator DavisPreviouslyIdleCitizensCompleteSidewalkJourneys()
        {
            Art.Initialize();
            var town = Own("Davis sidewalk journey regression").AddComponent<Town>();
            town.Map = MapDefinition.Load("davis-downtown");
            town.Build(null);
            yield return new WaitForSeconds(2);
            int[] indices = { 21, 22, 24, 27, 29, 30, 32, 35 };
            var walkers = indices.Select(i => town.Walkers[i]).ToArray();
            var starts = walkers.Select(w => w.transform.position).ToArray();
            var goals = new Vector3[walkers.Length];
            for (int i = 0; i < walkers.Length; i++)
            {
                // Hold only the high-level activity timer. Navigation and dynamic
                // locomotion remain enabled in the complete populated world.
                walkers[i].enabled = false;
                bool routeReady = walkers[i].WalkAbout();
                Debug.Log(
                    "CODRIVER_SIDEWALK_START " + routeReady + " " + walkers[i].NavigationDetail
                );
                if (!routeReady)
                    File.WriteAllText(
                        Path.GetFullPath("../outputs/qualification/realism/sidewalk-navmesh.json"),
                        JsonUtility.ToJson(NavMesh.CalculateTriangulation())
                    );
                Assert.IsTrue(routeReady, walkers[i].NavigationDetail);
                var agent = walkers[i].GetComponent<NavMeshAgent>();
                Assert.AreEqual(PedestrianNavigation.WalkingMask, agent.areaMask);
                goals[i] = agent.destination;
            }
            float deadline = Time.time + 80;
            var arrived = new bool[walkers.Length];
            while (Time.time < deadline && arrived.Any(a => !a))
            {
                yield return new WaitForSeconds(.5f);
                for (int i = 0; i < walkers.Length; i++)
                    arrived[i] |=
                        Vector3
                            .ProjectOnPlane(walkers[i].transform.position - goals[i], Vector3.up)
                            .magnitude < .8f;
            }
            for (int i = 0; i < walkers.Length; i++)
            {
                Debug.Log(
                    $"CODRIVER_SIDEWALK_JOURNEY id={indices[i]} arrived={arrived[i]} travel={Vector3.Distance(starts[i], walkers[i].transform.position):F2} {walkers[i].NavigationDetail}"
                );
                if (!arrived[i])
                    Debug.Log(
                        "CODRIVER_SIDEWALK_BLOCKED "
                            + indices[i]
                            + " "
                            + walkers[i].GetComponent<ActorLocomotion>().Diagnostic
                            + " nearby="
                            + string.Join(
                                ";",
                                Physics
                                    .OverlapSphere(
                                        walkers[i].transform.position + Vector3.up * .8f,
                                        .85f
                                    )
                                    .Where(c => c.gameObject != walkers[i].gameObject)
                                    .Select(c => c.name + " " + c.bounds)
                            )
                    );
            }
            Assert.IsTrue(
                arrived.All(a => a),
                string.Join(
                    ";",
                    walkers.Where((w, i) => !arrived[i]).Select(w => w.NavigationDetail)
                )
            );
            for (int i = 0; i < walkers.Length; i++)
                Assert.Greater(Vector3.Distance(starts[i], walkers[i].transform.position), 3);
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
        public IEnumerator DavisTruckClearsRecordedBusDeadlockUsingPhysicalSteering()
        {
            Art.Initialize();
            var town = Own("Recorded Davis turn").AddComponent<Town>();
            town.Map = MapDefinition.Load("davis-downtown");
            town.Build(null);
            var truck = town.Traffic[0];
            var bus = town.Bus;
            foreach (var actor in town.Traffic)
                if (actor != truck && actor != bus)
                    actor.gameObject.SetActive(false);
            foreach (var walker in town.Walkers)
                walker.gameObject.SetActive(false);
            bus.ScenarioSpeed = 0;
            PlaceFixture(
                truck,
                new Vector3(80.53678f, .119454f, -357.92062f),
                new Vector3(-.777291f, -.007504f, .629097f)
            );
            PlaceFixture(
                bus,
                new Vector3(75.76129f, -.053773f, -354.50024f),
                new Vector3(.220793f, 0, -.975321f)
            );
            Vector3 start = truck.transform.position;
            Vector3 busStart = bus.transform.position;
            float begin = Time.time;
            var a = truck.GetComponent<BoxCollider>();
            var b = bus.GetComponent<BoxCollider>();
            while (Time.time - begin < 60 && Vector3.Distance(start, truck.transform.position) < 15)
            {
                yield return new WaitForFixedUpdate();
                Assert.IsFalse(
                    Physics.ComputePenetration(
                        a,
                        truck.transform.position,
                        truck.transform.rotation,
                        b,
                        bus.transform.position,
                        bus.transform.rotation,
                        out _,
                        out _
                    ),
                    "The turning truck must clear the stopped bus physically"
                );
                Assert.Less(
                    Vector3.ProjectOnPlane(bus.transform.position - busStart, Vector3.up).magnitude,
                    .15f,
                    "The turning truck must not push the waiting bus aside"
                );
            }
            Debug.Log(
                $"CODRIVER_RECORDED_BUS_CONFLICT moved={Vector3.Distance(start, truck.transform.position):F2} state={truck.State} clearance={truck.Clearance} contact={truck.Contact}"
            );
            Assert.Greater(
                Vector3.Distance(start, truck.transform.position),
                15,
                "Truck must finish its turn without pushing or repositioning the bus: "
                    + truck.Clearance
            );
        }

        static void PlaceFixture(TrafficActor actor, Vector3 position, Vector3 forward)
        {
            actor.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
            actor.Body.position = position;
            actor.Body.rotation = actor.transform.rotation;
            actor.Body.linearVelocity = actor.Body.angularVelocity = Vector3.zero;
            actor.Distance = actor.Path.NearestDistance(position);
            Physics.SyncTransforms();
        }

        [UnityTest]
        public IEnumerator RoadVehicleStopsBeforeClosedRailArmAndWaitsForReopening()
        {
            return RailApproach(new VehicleProfile());
        }

        [UnityTest]
        public IEnumerator GlsStopsBeforeClosedRailArmAndWaitsForReopening()
        {
            return RailApproach(VehicleProfile.GlsSimulation());
        }

        IEnumerator RailApproach(VehicleProfile profile)
        {
            Art.Initialize();
            var town = Own("Rail warning fixture").AddComponent<Town>();
            town.Map = MapDefinition.Cedar();
            var surface = Own("Rail test pavement").AddComponent<BoxCollider>();
            surface.gameObject.layer = TrafficClearance.RoadLayer;
            surface.transform.position = new Vector3(0, -.5f, -20);
            surface.size = new Vector3(30, 1, 150);
            var gate = Own("Test physical railway gate").AddComponent<RailGate>();
            gate.transform.position = new Vector3(5, 0, 0);
            gate.Initialize(town);
            var arm = gate.transform.Find("Gate hinge");
            var vehicle = Own("Rail approach car").AddComponent<Vehicle>();
            vehicle.Profile = profile;
            vehicle.transform.position = new Vector3(0, .1f, -55);
            vehicle.Initialize();
            vehicle.Handbrake = true;
            yield return new WaitForSeconds(1);
            vehicle.Handbrake = false;
            vehicle.Body.linearVelocity = Vector3.forward * 13.4f;
            var envelope = vehicle.GetComponent<BoxCollider>();
            float begin = Time.time;
            do
            {
                // Keep the fixture closed, then exercise the production opening
                // animation below. No train or hidden truth enters perception.
                arm.localRotation = Quaternion.identity;
                float target = Mathf.Min(13.4f, town.RailSpeedLimit(envelope));
                vehicle.ApplyDesiredAcceleration(
                    Mathf.Clamp((target - vehicle.Speed) * 1.2f, -4, 2)
                );
                yield return new WaitForFixedUpdate();
            } while (Time.time - begin < 25 && vehicle.Speed > .15f);
            Assert.Less(vehicle.Speed, .15f, "Car must finish braking before the closed arm");
            float front = vehicle
                .transform.TransformPoint(envelope.center + Vector3.forward * envelope.size.z * .5f)
                .z;
            Debug.Log(
                $"CODRIVER_RAIL_STOP profile={profile.id} clearance={-front:F2} speed={vehicle.Speed:F3} contacts={vehicle.Collisions}"
            );
            Assert.Less(front, -1.5f);
            Assert.AreEqual(0, vehicle.Collisions);
            begin = Time.time;
            while (Time.time - begin < 20 && vehicle.transform.position.z < 12)
            {
                float target = Mathf.Min(8, town.RailSpeedLimit(envelope));
                if (gate.MustWait)
                    Assert.AreEqual(0, target, "Do not drive into a rising arm");
                vehicle.ApplyDesiredAcceleration(
                    Mathf.Clamp((target - vehicle.Speed) * 1.2f, -4, 2)
                );
                yield return new WaitForFixedUpdate();
            }
            Assert.Greater(vehicle.transform.position.z, 12, "Resume once the actual arm clears");
            Assert.AreEqual(0, vehicle.Collisions);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator CedarOrdinaryTrafficDoesNotFormPermanentRoadblocks()
        {
            var railContacts = new List<string>();
            void ObserveContact(string message, string trace, LogType type)
            {
                if (
                    message.StartsWith("CODRIVER_ACTOR_CONTACT ")
                    && (message.Contains(" with Streetcar ") || message.Contains(" with Carriage "))
                )
                    railContacts.Add(message);
            }
            Application.logMessageReceived += ObserveContact;
            try
            {
                yield return OrdinaryTrafficRemainsLive(MapDefinition.Cedar(), "cedar-clearance");
            }
            finally
            {
                Application.logMessageReceived -= ObserveContact;
            }
            Assert.IsEmpty(
                railContacts,
                "Unscripted pedestrian/rail contact: " + string.Join("; ", railContacts)
            );
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator DavisOrdinaryTrafficDoesNotFormPermanentRoadblocks()
        {
            return OrdinaryTrafficRemainsLive(
                MapDefinition.Load("davis-downtown"),
                "davis-clearance"
            );
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator PedestrianWaitsForApproachingStreetcarBeforeCrossing()
        {
            return RailPedestrianCourse(-2.5f, -20, true);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator PedestrianAlreadyOnTrackContinuesToSafety()
        {
            return RailPedestrianCourse(0, -24, false);
        }

        IEnumerator RailPedestrianCourse(float startZ, float trainX, bool shouldWait)
        {
            Art.Initialize();
            var town = Own("Rail pedestrian conflict course").AddComponent<Town>();
            town.Map = MapDefinition.Cedar();
            var ground = Own("Rail crossing physical surface");
            ground.transform.position = Vector3.down * .5f;
            ground.AddComponent<BoxCollider>().size = new Vector3(160, 1, 40);
            ground.layer = TrafficClearance.RoadLayer;
            var tram = Own("Streetcar").AddComponent<RailVehicle>();
            tram.transform.SetParent(town.transform);
            tram.Initialize(town, true);
            town.Tram = tram;
            tram.enabled = false;
            tram.Path = new TrackPath(
                new[] { new Vector3(-70, 0, 0), new Vector3(70, 0, 0) },
                false
            );
            var drive = tram.GetComponent<RailDynamics>();
            drive.Path = tram.Path;
            tram.PlaceForScenario(new Vector3(trainX, 0, 0));
            drive.Body.linearVelocity = Vector3.right * 7;
            drive.TargetSpeed = 7;

            var actor = Own("Rail crossing pedestrian");
            actor.transform.SetParent(town.transform);
            actor.transform.position = new Vector3(0, .05f, startZ);
            actor.layer = TrafficClearance.WalkerLayer;
            var shape = actor.AddComponent<CapsuleCollider>();
            shape.height = 1.8f;
            shape.radius = .24f;
            shape.center = Vector3.up * .9f;
            var body = actor.AddComponent<Rigidbody>();
            var locomotion = actor.AddComponent<ActorLocomotion>();
            locomotion.Initialize("Person");
            locomotion.enabled = false;
            float began = Time.time;
            bool waitedOutside = false;
            while (Time.time - began < 16 && body.position.z < 3.5f)
            {
                locomotion.StepVelocity(Vector3.forward * 1.25f, .01f);
                yield return new WaitForFixedUpdate();
                if (Time.time - began > .5f && body.position.z < -1.8f && locomotion.Speed < .1f)
                    waitedOutside = true;
            }
            Assert.AreEqual(shouldWait, waitedOutside);
            Assert.AreEqual(
                0,
                locomotion.ImpactSpeed,
                "The person must wait before entering the moving rail envelope."
            );
            Assert.Greater(
                body.position.z,
                3.5f,
                "The person must continue after the streetcar passes."
            );
            Assert.Less(
                Mathf.Abs(body.position.x),
                .4f,
                "Crossing must not depend on being pushed along the track."
            );
            Assert.Greater(
                drive.Body.linearVelocity.x,
                6.5f,
                "The walker cannot stop or displace the streetcar to pass this check."
            );
            Debug.Log(
                $"CODRIVER_RAIL_WALK start_z={startZ:F2} waited={waitedOutside} seconds={Time.time - began:F2} impacts={locomotion.ImpactSpeed:F2}"
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
            bool trace = Array.IndexOf(Environment.GetCommandLineArgs(), "-traffic-trace") >= 0;
            float nextTrace = start + 30;
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
                if (trace && Time.time >= nextTrace)
                {
                    CapturePopulation(
                        town,
                        evidence + "-trace-" + Mathf.RoundToInt(Time.time - start),
                        Time.time - start
                    );
                    Debug.Log(
                        "CODRIVER_TRAFFIC_TRACE "
                            + evidence
                            + " t="
                            + (Time.time - start).ToString("F1")
                            + " "
                            + string.Join(
                                "; ",
                                cars.Select(
                                    (car, i) =>
                                        car.name
                                        + " wait="
                                        + (Time.time - lastMotion[i]).ToString("F1")
                                        + " "
                                        + car.State
                                        + " "
                                        + car.Clearance
                                )
                            )
                    );
                    nextTrace += 30;
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

        [UnityTest, Timeout(180000)]
        public IEnumerator DavisParkingFinishesAlignedReverseWithTrafficQueuedBehind()
        {
            Art.Initialize();
            var town = Own("Recorded Davis parking queue").AddComponent<Town>();
            town.Map = MapDefinition.Load("davis-downtown");
            town.Build(null);
            var parking = town.ParkingCar;
            var queued = town.Traffic[5];
            foreach (var actor in town.Traffic)
                if (actor != parking && actor != queued)
                    actor.gameObject.SetActive(false);
            foreach (var actor in town.Walkers)
                actor.gameObject.SetActive(false);
            parking.PrepareParkingConflict();
            parking.HoldActivity = false;
            parking.DriverReady = true;
            queued.ScenarioSpeed = 0;
            PlaceFixture(
                parking,
                new Vector3(11.48556f, -.01240f, -233.42502f),
                new Vector3(-.99403f, -.01515f, -.10803f)
            );
            PlaceFixture(
                queued,
                new Vector3(18.59096f, -.01240f, -232.05316f),
                new Vector3(-.97445f, -.01515f, -.22411f)
            );
            Vector3 start = parking.Body.position,
                queueStart = queued.Body.position;
            Vector3 previous = start;
            float began = Time.time;
            while (Time.time - began < 22)
            {
                yield return new WaitForFixedUpdate();
                Assert.Less(
                    Vector3.Distance(previous, parking.Body.position),
                    .25f,
                    "Parking recovery must use continuous physical movement"
                );
                previous = parking.Body.position;
                Assert.Less(
                    Vector3.Distance(queueStart, queued.Body.position),
                    .15f,
                    "The parking car must not push the waiting car out of its way"
                );
                Assert.IsEmpty(parking.Contact, "No contact is allowed to clear the manoeuvre");
            }
            Debug.Log("CODRIVER_RECORDED_PARKING_QUEUE " + parking.NavigationDetail);
            Assert.Greater(
                Vector3.Dot(parking.Body.position - start, new Vector3(-.97445f, 0, -.22411f)),
                5,
                "An aligned vehicle must finish reversing and drive forward without waiting forever: "
                    + parking.NavigationDetail
            );
            Assert.IsFalse(parking.ReverseRequested);
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
                walkers = town
                    .Walkers.Select(w =>
                        w.name
                        + ":"
                        + w.NavigationDetail
                        + " "
                        + w.GetComponent<ActorLocomotion>().Diagnostic
                    )
                    .ToArray(),
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
