using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoDriver.Tests
{
    // An isolated physical course, not a formula-level check of actuator mapping.
    // These broad game-behavior bounds do not establish a calibrated real vehicle model.
    public sealed class VehiclePhysicsTests
    {
        Scene previous,
            course;
        PhysicsScene physics;
        Vehicle car;
        float previousStep;

        [SetUp]
        public void CreateCourse()
        {
            previous = SceneManager.GetActiveScene();
            course = EditorSceneManager.NewPreviewScene();
            physics = course.GetPhysicsScene();
            Assert.IsTrue(physics.IsValid());
            Assert.AreNotEqual(
                Physics.defaultPhysicsScene,
                physics,
                "Course physics must be isolated"
            );
            previousStep = Time.fixedDeltaTime;
            Time.fixedDeltaTime = .01f;
            var ground = new GameObject("Course surface");
            SceneManager.MoveGameObjectToScene(ground, course);
            ground.transform.position = new Vector3(0, -.5f, 0);
            ground.AddComponent<BoxCollider>().size = new Vector3(300, 1, 500);
            Art.Initialize();
            car = new GameObject("Course vehicle").AddComponent<Vehicle>();
            SceneManager.MoveGameObjectToScene(car.gameObject, course);
            car.transform.position = Vector3.up * .2f;
            car.Initialize();
            car.Body.interpolation = RigidbodyInterpolation.None;
            car.Brake = 1;
            Step(2);
            Assert.AreEqual(
                4,
                car.GroundedWheels,
                "All suspension wheels must settle on the course"
            );
            car.Brake = 0;
        }

        void Step(float seconds)
        {
            var step = typeof(Vehicle).GetMethod(
                "FixedUpdate",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
            );
            for (int i = 0; i < Mathf.RoundToInt(seconds / .01f); i++)
            {
                step.Invoke(car, null);
                physics.Simulate(.01f);
            }
        }

        [TearDown]
        public void CloseCourse()
        {
            Time.fixedDeltaTime = previousStep;
            SceneManager.SetActiveScene(previous);
            if (course.IsValid())
                EditorSceneManager.ClosePreviewScene(course);
        }

        [Test]
        [TestCase("Person", 78f)]
        [TestCase("Dog", 22f)]
        [TestCase("Cat", 4.5f)]
        public void WalkerCannotContinuouslyPushHandbrakedCar(string species, float mass)
        {
            car.Handbrake = true;
            car.Brake = 1;
            var actor = new GameObject(species);
            SceneManager.MoveGameObjectToScene(actor, course);
            actor.layer = TrafficClearance.WalkerLayer;
            actor.transform.position = new Vector3(1.18f, .05f, 0);
            var capsule = actor.AddComponent<CapsuleCollider>();
            capsule.height = species == "Person" ? 1.8f : .6f;
            capsule.radius = .24f;
            capsule.center = Vector3.up * capsule.height / 2;
            var body = actor.AddComponent<Rigidbody>();
            var locomotion = actor.AddComponent<ActorLocomotion>();
            locomotion.Initialize(species);
            Assert.AreEqual(mass, body.mass);
            Assert.IsFalse(body.isKinematic);
            var start = car.Body.position;
            for (int i = 0; i < 1500; i++)
            {
                locomotion.StepVelocity(Vector3.left * 1.4f, .01f);
                Step(.01f);
            }
            float displacement = Vector3
                .ProjectOnPlane(car.Body.position - start, Vector3.up)
                .magnitude;
            TestContext.WriteLine(
                species + " 15 s contact displacement: " + displacement.ToString("F4") + " m"
            );
            Assert.Less(displacement, .05f);
        }

        [TestCase("Person")]
        [TestCase("Dog")]
        [TestCase("Cat")]
        public void WalkingActorStepsOntoCurbWithFiniteForces(string species)
        {
            var curb = new GameObject("Fourteen centimetre sidewalk");
            SceneManager.MoveGameObjectToScene(curb, course);
            curb.transform.position = new Vector3(20, .07f, 6);
            curb.AddComponent<BoxCollider>().size = new Vector3(5, .14f, 8);
            var actor = new GameObject(species);
            SceneManager.MoveGameObjectToScene(actor, course);
            actor.layer = TrafficClearance.WalkerLayer;
            actor.transform.position = new Vector3(20, .02f, 0);
            var shape = actor.AddComponent<CapsuleCollider>();
            shape.height = species == "Person" ? 1.8f : .6f;
            shape.radius = .24f;
            shape.center = Vector3.up * shape.height / 2;
            var body = actor.AddComponent<Rigidbody>();
            var locomotion = actor.AddComponent<ActorLocomotion>();
            locomotion.Initialize(species);
            body.interpolation = RigidbodyInterpolation.None;
            for (int i = 0; i < 650; i++)
            {
                locomotion.StepVelocity(Vector3.forward * 1.25f, .01f);
                physics.Simulate(.01f);
            }
            Assert.Greater(body.position.z, 5, species + " must complete the curb crossing");
            Assert.That(body.position.y, Is.InRange(.10f, .20f));
            Assert.IsFalse(body.isKinematic);
        }

        [Test]
        public void AmbulanceSuspensionSupportsItsMassWithoutDraggingTheShell()
        {
            var ambulance = new GameObject("Utility suspension course");
            SceneManager.MoveGameObjectToScene(ambulance, course);
            ambulance.transform.position = new Vector3(10, .3f, 0);
            ambulance.layer = TrafficClearance.TrafficLayer;
            var collider = ambulance.AddComponent<BoxCollider>();
            collider.center = new Vector3(0, .85f, 0);
            collider.size = new Vector3(1.74f, 1.7f, 4.4f);
            var body = ambulance.AddComponent<Rigidbody>();
            var dynamics = new RvpVehicleDynamics(body, VehicleProfile.ForTraffic("Emergency"));
            for (int i = 0; i < 400; i++)
            {
                dynamics.Step(new VehicleControls { brake = 1 }, .01f);
                physics.Simulate(.01f);
            }
            Assert.AreEqual(4, dynamics.GroundedWheels);
            Assert.Greater(body.position.y, .03f, "Collision shell must be above the surface");
            for (int i = 0; i < 400; i++)
            {
                dynamics.Step(new VehicleControls { throttle = .7f }, .01f);
                physics.Simulate(.01f);
            }
            Assert.Greater(body.linearVelocity.z, 3);
        }

        [TestCase("Truck", 0f)]
        [TestCase("Truck", 5f)]
        [TestCase("Car", 0f)]
        [TestCase("Emergency", 0f)]
        [TestCase("Bus", 0f)]
        public void BrakedVehicleHoldsPositionThroughLongQueue(string kind, float crossSlope)
        {
            var surface = new GameObject("Stationary queue surface");
            SceneManager.MoveGameObjectToScene(surface, course);
            surface.transform.SetPositionAndRotation(
                new Vector3(20, 1, 0),
                Quaternion.Euler(0, 0, crossSlope)
            );
            surface.AddComponent<BoxCollider>().size = new Vector3(30, 1, 100);
            var go = new GameObject("Stationary " + kind);
            SceneManager.MoveGameObjectToScene(go, course);
            go.layer = TrafficClearance.TrafficLayer;
            go.transform.SetPositionAndRotation(
                new Vector3(20, 1.8f, 0),
                surface.transform.rotation * Quaternion.Euler(0, -13, 0)
            );
            var profile = VehicleProfile.ForTraffic(kind);
            var shape = go.AddComponent<BoxCollider>();
            shape.center = profile.collisionCentre;
            shape.size = profile.collisionSize;
            var body = go.AddComponent<Rigidbody>();
            var dynamics = new RvpVehicleDynamics(body, profile);
            body.interpolation = RigidbodyInterpolation.None;
            var controls = new VehicleControls
            {
                brake = 1,
                handbrake = true,
                roadWheelAngle = 23,
            };
            for (int i = 0; i < 500; i++)
            {
                dynamics.Step(controls, .01f);
                physics.Simulate(.01f);
            }
            Assert.AreEqual(4, dynamics.GroundedWheels);
            Vector3 start = body.position;
            Quaternion rotation = body.rotation;
            float maximumDrift = 0;
            float maximumTurn = 0;
            for (int i = 0; i < 60000; i++)
            {
                dynamics.Step(controls, .01f);
                physics.Simulate(.01f);
                maximumDrift = Mathf.Max(maximumDrift, Vector3.Distance(start, body.position));
                maximumTurn = Mathf.Max(maximumTurn, Quaternion.Angle(rotation, body.rotation));
            }
            TestContext.WriteLine(
                $"{kind} slope={crossSlope}: 600 s drift={maximumDrift:F4} m, rotation={maximumTurn:F3} degrees; start={rotation.eulerAngles:F3}; end={body.rotation.eulerAngles:F3}"
            );
            Assert.Less(maximumDrift, .10f, "A braked queue cannot creep into another lane");
            Assert.Less(maximumTurn, 1f, "A braked queue cannot rotate into oncoming traffic");
            Assert.IsFalse(body.isKinematic);
            // Tire grip must remain finite: an external impact can still move it.
            body.AddForce(Vector3.right * profile.mass * 2, ForceMode.Impulse);
            float impactTravel = 0;
            for (int i = 0; i < 200; i++)
            {
                dynamics.Step(controls, .01f);
                physics.Simulate(.01f);
                impactTravel = Mathf.Max(impactTravel, Vector3.Distance(start, body.position));
            }
            Assert.Greater(
                impactTravel,
                .08f,
                "Stationary holding must not freeze the physical body"
            );
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CurvedBayManoeuvreArrivesPhysically(bool reverse)
        {
            var go = new GameObject("Access course vehicle");
            SceneManager.MoveGameObjectToScene(go, course);
            go.layer = TrafficClearance.TrafficLayer;
            go.transform.position = new Vector3(20, .2f, 0);
            var profile = VehicleProfile.ForTraffic("Emergency");
            var shell = go.AddComponent<BoxCollider>();
            shell.center = profile.collisionCentre;
            shell.size = profile.collisionSize;
            var body = go.AddComponent<Rigidbody>();
            var driver = new PhysicalDriver(body, profile);
            body.interpolation = RigidbodyInterpolation.None;
            Vector3 goal = new Vector3(25, .1f, reverse ? -18 : 18);
            var path = PhysicalDriver.AccessPath(
                body.position,
                Vector3.forward,
                goal,
                Vector3.forward,
                reverse
            );
            for (int i = 0; i < 6000; i++)
            {
                float remaining = path.Length - path.NearestDistance(body.position);
                driver.Follow(
                    path,
                    Mathf.Min(2.5f, Mathf.Sqrt(Mathf.Max(0, remaining - .35f) * 2.5f)),
                    0,
                    reverse
                );
                physics.Simulate(.01f);
            }
            float error = Vector3.ProjectOnPlane(goal - body.position, Vector3.up).magnitude;
            TestContext.WriteLine(
                $"Reverse={reverse}, final={body.position}, error={error:F2}, heading={body.rotation.eulerAngles.y:F1}"
            );
            Assert.Less(error, .8f);
            Assert.Less(Quaternion.Angle(body.rotation, Quaternion.identity), 8);
        }

        [Test]
        public void DrySurfaceAccelerationAndBrakingCalibration()
        {
            car.Throttle = 1;
            float elapsed = 0;
            while (elapsed < 20 && car.Speed < 26.8224f)
            {
                Step(.01f);
                elapsed += .01f;
            }
            TestContext.WriteLine("0–60 mph: " + elapsed.ToString("F2") + " s");
            Assert.That(elapsed, Is.InRange(8f, 11f));
            var atBrake = car.Body.position;
            car.Throttle = 0;
            car.Brake = 1;
            Step(8);
            float distance = car.Body.position.z - atBrake.z;
            TestContext.WriteLine("60 mph braking: " + distance.ToString("F2") + " m");
            Assert.That(distance, Is.InRange(40f, 55f));
            Assert.Less(car.Speed, .2f);
        }

        [Test]
        public void GlsDrySurfaceCalibration()
        {
            Object.DestroyImmediate(car.gameObject);
            car = new GameObject("GLS course vehicle").AddComponent<Vehicle>();
            SceneManager.MoveGameObjectToScene(car.gameObject, course);
            car.Profile = VehicleProfile.GlsSimulation();
            car.transform.position = Vector3.up * .2f;
            car.Initialize();
            car.Body.interpolation = RigidbodyInterpolation.None;
            car.Brake = 1;
            Step(2);
            Assert.AreEqual(4, car.GroundedWheels);
            car.Brake = 0;
            car.Throttle = 1;
            float elapsed = 0;
            while (elapsed < 20 && car.Speed < 26.8224f)
            {
                Step(.01f);
                elapsed += .01f;
            }
            var atBrake = car.Body.position;
            car.Throttle = 0;
            car.Brake = 1;
            Step(8);
            float stoppingDistance = car.Body.position.z - atBrake.z;
            TestContext.WriteLine(
                $"GLS 0-60 mph: {elapsed:F2} s; braking: {stoppingDistance:F2} m"
            );
            Assert.That(elapsed, Is.InRange(5.2f, 6.2f));
            Assert.That(stoppingDistance, Is.InRange(40f, 55f));
            Assert.Less(car.Speed, .2f);
        }

        [TestCase(8f)]
        [TestCase(26.8224f)]
        public void SimulatedBridgeApproachDoesNotStrikeGlsChassis(float speed)
        {
            Object.DestroyImmediate(car.gameObject);
            var points = new System.Collections.Generic.List<Vector3>();
            for (int z = -20; z <= 190; z += 2)
                points.Add(new Vector3(0, SimulatedBridgeExtension.Height(z), z));
            var road = DavisWorld.Surface(
                "Bridge course",
                null,
                SimulatedBridgeExtension.Ribbon(points, 8),
                Art.Asphalt
            );
            SceneManager.MoveGameObjectToScene(road, course);
            car = new GameObject("GLS bridge course").AddComponent<Vehicle>();
            SceneManager.MoveGameObjectToScene(car.gameObject, course);
            car.Profile = VehicleProfile.GlsSimulation();
            car.transform.position = new Vector3(0, .2f, -10);
            car.Initialize();
            car.Body.interpolation = RigidbodyInterpolation.None;
            car.Brake = 1;
            Step(2);
            car.Brake = 0;
            car.Body.linearVelocity = Vector3.forward * speed;
            for (int i = 0; i < 4000 && car.Body.position.z < 155; i++)
            {
                car.ApplyDesiredAcceleration(Mathf.Clamp((speed - car.Speed) * 2, -2, 2));
                Step(.01f);
            }
            TestContext.WriteLine(
                $"Bridge entry {speed:F2} m/s: final={car.Body.position}, contact={car.LastContact}"
            );
            Assert.Greater(car.Body.position.z, 154);
            Assert.AreEqual(
                "None",
                car.LastContact,
                "A wheel-supported crossing must not strike the body collider"
            );
        }

        [Test]
        public void AccelerateThenBrakeFromThirtyMphWithoutExcessiveDrift()
        {
            car.Throttle = 1;
            for (int i = 0; i < 600 && car.Speed < 13.4f; i++)
                Step(.02f);
            Assert.GreaterOrEqual(
                car.Speed,
                13.4f,
                "Car should reach 30 mph within twelve seconds"
            );
            var atBrake = car.Body.position;
            car.Throttle = 0;
            car.Brake = 1;
            Step(5);
            float stoppingDistance = car.Body.position.z - atBrake.z;
            Assert.Less(car.Speed, .2f);
            Assert.That(stoppingDistance, Is.InRange(10f, 16f));
            Assert.Less(Mathf.Abs(car.Body.position.x), .5f);
            TestContext.WriteLine(
                "30 mph braking distance: " + stoppingDistance.ToString("F2") + " m"
            );
        }

        [Test]
        public void ReversePropelsBackwardsAndHandbrakeHoldsAtRest()
        {
            Assert.IsTrue(car.TryToggleDirection(), "A stopped car must accept reverse");
            Assert.IsTrue(car.Reverse);
            car.Throttle = .35f;
            Step(3);
            Assert.Less(car.Body.position.z, -1);
            Assert.IsFalse(car.TryToggleDirection(), "Gear direction must not change in motion");
            Assert.IsTrue(car.Reverse);
            car.Throttle = 0;
            car.Brake = 1;
            Step(3);
            Assert.IsTrue(
                car.TryToggleDirection(),
                "Braking to rest must permit drive selection again"
            );
            Assert.IsFalse(car.Reverse);
            car.Brake = 0;
            car.Handbrake = true;
            var stopped = car.Body.position;
            Step(3);
            Assert.Less(Vector3.Distance(stopped, car.Body.position), .2f);
        }

        [Test]
        public void TireFootprintCrossesSmallDrivewayLipWithoutAChassisDeadlock()
        {
            var lip = new GameObject("Raised driveway lip");
            SceneManager.MoveGameObjectToScene(lip, course);
            lip.transform.position = new Vector3(0, .02f, 10);
            lip.AddComponent<BoxCollider>().size = new Vector3(8, .24f, 12);
            car.Throttle = .25f;
            Step(7);
            TestContext.WriteLine(
                $"Driveway lip: rear_z={car.Body.position.z - car.Profile.collisionSize.z * .5f:F3} "
                    + $"lip_z={lip.GetComponent<Collider>().bounds.min.z:F3} speed={car.Speed:F3} m/s"
            );
            Assert.Greater(
                car.Body.position.z - car.Profile.collisionSize.z * .5f,
                lip.GetComponent<Collider>().bounds.min.z + .25f,
                "The entire car must clear the 14 cm driveway lip, including its rear overhang"
            );
            Assert.Greater(Vector3.Dot(car.transform.up, Vector3.up), .9f);
            Assert.Greater(
                car.Speed,
                1,
                "The car must continue moving after its complete body clears the lip"
            );
            Assert.IsTrue(float.IsFinite(car.Body.position.y));
        }

        [Test]
        public void SteeringTurnsVehicleRightWithoutRollingOver()
        {
            car.Throttle = .25f;
            Step(3);
            car.Steer = .55f;
            Step(3);
            Assert.Greater(car.Body.position.x, 1);
            Assert.Greater(Vector3.Dot(car.transform.forward, Vector3.right), .2f);
            Assert.Greater(Vector3.Dot(car.transform.up, Vector3.up), .9f);
            Assert.GreaterOrEqual(car.GroundedWheels, 3);
        }

        [Test]
        public void VehicleClimbsTownBridgeGradeAndBrakesOnTheSlope()
        {
            var ground = System.Array.Find(
                course.GetRootGameObjects(),
                g => g.name == "Course surface"
            );
            var slope = Quaternion.Euler(-6.5f, 0, 0);
            ground.transform.rotation = slope;
            car.Body.position = ground.transform.TransformPoint(new Vector3(0, .7f, 0));
            car.Body.rotation = slope;
            car.Body.linearVelocity = car.Body.angularVelocity = Vector3.zero;
            car.Brake = 1;
            Step(2);
            float initialHeight = car.Body.position.y;
            car.Brake = 0;
            car.Throttle = .8f;
            Step(5);
            Assert.Greater(car.Body.position.y - initialHeight, .8f);
            Assert.Greater(Vector3.Dot(car.transform.up, slope * Vector3.up), .95f);
            car.Throttle = 0;
            car.Brake = 1;
            Step(5);
            Assert.Less(car.Speed, .2f);
        }

        [Test]
        public void SolidWallStopsVehicleWithoutTunnelling()
        {
            var wall = new GameObject("Course barrier");
            SceneManager.MoveGameObjectToScene(wall, course);
            wall.transform.position = new Vector3(0, 2, 12);
            wall.AddComponent<BoxCollider>().size = new Vector3(12, 4, .5f);
            car.Throttle = .5f;
            Step(8);
            Assert.Less(car.Body.position.z, 10.1f);
            Assert.Greater(car.Body.position.z, 7);
            Assert.Less(car.Speed, .5f);
        }
    }
}
