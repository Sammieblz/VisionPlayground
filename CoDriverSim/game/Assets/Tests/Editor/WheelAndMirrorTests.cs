using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoDriver.Tests
{
    public class WheelAndMirrorTests
    {
        [Test]
        public void FirstWheelPoseBeforePhysicsUsesThePlacedChassis()
        {
            var car = new GameObject("Paused startup wheels");
            try
            {
                car.transform.SetPositionAndRotation(
                    new Vector3(50, 3, -20),
                    Quaternion.Euler(9, 67, -4)
                );
                var profile = VehicleProfile.GlsSimulation();
                var dynamics = new RvpVehicleDynamics(car.AddComponent<Rigidbody>(), profile);
                for (int i = 0; i < 4; i++)
                {
                    dynamics.WheelPose(i, out var position, out var rotation);
                    Vector3 expected =
                        car.transform.TransformPoint(dynamics.Mount(i))
                        - car.transform.up * profile.suspensionLength;
                    Assert.Less(Vector3.Distance(expected, position), .0001f);
                    Assert.Less(Quaternion.Angle(car.transform.rotation, rotation), .001f);
                    Assert.AreEqual(0, dynamics.Rpm[i]);
                }
                Assert.AreEqual(0, dynamics.GroundedWheels);
            }
            finally
            {
                Object.DestroyImmediate(car);
            }
        }

        [Test]
        public void TeleportRebasesWheelPosesWithoutChangingSuspensionOrAdvancingPhysics()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var ground = new GameObject("Wheel pose ground");
                SceneManager.MoveGameObjectToScene(ground, scene);
                ground.transform.position = Vector3.down * .5f;
                ground.AddComponent<BoxCollider>().size = new Vector3(20, 1, 20);
                var car = new GameObject("Paused teleport wheels");
                SceneManager.MoveGameObjectToScene(car, scene);
                var profile = VehicleProfile.GlsSimulation();
                car.transform.position =
                    Vector3.up * (profile.radius + .12f - profile.wheelMountHeight);
                var body = car.AddComponent<Rigidbody>();
                var dynamics = new RvpVehicleDynamics(body, profile);
                body.linearVelocity = Vector3.forward * 5;
                Physics.SyncTransforms();
                dynamics.Step(new VehicleControls { roadWheelAngle = 20, brake = .1f }, .01f);
                Assert.AreEqual(4, dynamics.GroundedWheels);
                var localPositions = new Vector3[4];
                var localRotations = new Quaternion[4];
                for (int i = 0; i < 4; i++)
                {
                    dynamics.WheelPose(i, out var position, out var rotation);
                    localPositions[i] = car.transform.InverseTransformPoint(position);
                    localRotations[i] = Quaternion.Inverse(car.transform.rotation) * rotation;
                    Assert.Greater(
                        localPositions[i].y,
                        dynamics.Mount(i).y - profile.suspensionLength + .05f,
                        "The fixture must preserve compressed suspension, not just default droop"
                    );
                }
                var rpm = dynamics.Rpm.ToArray();
                float engine = dynamics.EngineRpm;
                var velocity = body.linearVelocity;
                body.position = new Vector3(120, 4, -65);
                body.rotation = Quaternion.Euler(12, 113, -7);
                car.transform.SetPositionAndRotation(body.position, body.rotation);
                for (int i = 0; i < 4; i++)
                {
                    dynamics.WheelPose(i, out var position, out var rotation);
                    Assert.Less(
                        Vector3.Distance(car.transform.TransformPoint(localPositions[i]), position),
                        .0001f
                    );
                    Assert.Less(
                        Quaternion.Angle(car.transform.rotation * localRotations[i], rotation),
                        .01f
                    );
                }
                CollectionAssert.AreEqual(rpm, dynamics.Rpm);
                Assert.AreEqual(engine, dynamics.EngineRpm);
                Assert.AreEqual(velocity, body.linearVelocity);
                Assert.AreEqual(4, dynamics.GroundedWheels);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void PickupCabFacesThePhysicalFrontAndEachTireHasItsOwnHub()
        {
            var source = Resources.Load<GameObject>("Realism/Prepared/Pickup");
            Assert.IsNotNull(source);
            var truck = Object.Instantiate(source);
            try
            {
                var body = truck
                    .GetComponentsInChildren<MeshFilter>()
                    .Where(f => !f.name.Contains("Wheel"));
                var upperCab = body.SelectMany(f =>
                        f.sharedMesh.vertices.Select(v =>
                            truck.transform.InverseTransformPoint(f.transform.TransformPoint(v))
                        )
                    )
                    .Where(v => v.y > 1.1f)
                    .ToArray();
                Assert.Greater(upperCab.Length, 20);
                Assert.Greater(
                    upperCab.Average(v => v.z),
                    .2f,
                    "The imported cab/hood must face the route's +Z direction"
                );
                var tires = truck
                    .GetComponentsInChildren<MeshRenderer>()
                    .Where(r => r.name.Contains("Wheel"))
                    .ToArray();
                Assert.AreEqual(4, tires.Length);
                foreach (var tire in tires)
                {
                    Assert.Greater(Mathf.Abs(tire.bounds.center.x), .5f);
                    Assert.Less(
                        tire.bounds.size.x,
                        .6f,
                        "Paired tires must not share an axle rotation pivot"
                    );
                }
            }
            finally
            {
                Object.DestroyImmediate(truck);
            }
        }

        sealed class Poses : IVehicleDynamics
        {
            public Rigidbody Body => null;
            public VehicleProfile Profile { get; } = VehicleProfile.GlsSimulation();
            public float EngineRpm => 800;
            public int Gear => 1;
            public int GroundedWheels => 4;
            public Quaternion Chassis,
                Rotation;
            public Vector3 Position;

            public void Step(VehicleControls controls, float dt) { }

            public void WheelPose(int index, out Vector3 position, out Quaternion rotation)
            {
                position = Position;
                rotation = Chassis * Rotation;
            }
        }

        [TestCase(0, false)]
        [TestCase(90, false)]
        [TestCase(-90, false)]
        [TestCase(180, false)]
        [TestCase(90, true)]
        [TestCase(-90, true)]
        public void RollingKeepsEveryTireOnItsAxleAndInsideItsWheelArch(float roll, bool cylinder)
        {
            var car = new GameObject("Wheel orientation course");
            try
            {
                car.transform.rotation = Quaternion.Euler(0, 43, 0);
                var pose = new Poses { Chassis = car.transform.rotation };
                for (int i = 0; i < 4; i++)
                {
                    var wheel = new GameObject("Wheel " + i).transform;
                    wheel.SetParent(car.transform, false);
                    var local = new Vector3(
                        (i % 2 == 0 ? -1 : 1) * .86f,
                        .39f,
                        (i < 2 ? 1 : -1) * 1.56f
                    );
                    wheel.localPosition = local;
                    wheel.localRotation = cylinder
                        ? Quaternion.Euler(0, 0, 90)
                        : Quaternion.identity;
                    var binding = WheelVisualBinding.Bind(car.transform, wheel, i, pose.Profile);
                    float steer = i < 2 ? 23 : 0;
                    pose.Rotation =
                        Quaternion.Euler(0, steer, 0) * Quaternion.AngleAxis(roll, Vector3.right);
                    pose.Position = car.transform.TransformPoint(
                        new Vector3(
                            (i % 2 == 0 ? -1 : 1) * pose.Profile.TrackAt(i) * .5f,
                            .31f,
                            (i < 2 ? 1 : -1) * pose.Profile.wheelbase * .5f
                        )
                    );
                    binding.Apply(car.transform, pose);
                    var expectedAxle =
                        car.transform.rotation * Quaternion.Euler(0, steer, 0) * Vector3.right;
                    Assert.Greater(
                        Vector3.Dot(wheel.rotation * binding.axleLocal, expectedAxle),
                        .9999f,
                        "Rolling cannot tip a wheel onto its side"
                    );
                    var actual = car.transform.InverseTransformPoint(wheel.position);
                    Assert.That(actual.x, Is.EqualTo(local.x).Within(.0001f));
                    Assert.That(actual.z, Is.EqualTo(local.z).Within(.0001f));
                    Assert.That(
                        actual.y,
                        Is.EqualTo(.31f).Within(.0001f),
                        "Visual hub follows physical suspension travel"
                    );
                }
            }
            finally
            {
                Object.DestroyImmediate(car);
            }
        }

        [Test]
        public void PhysicsWheelPoseHasNoImportedMeshCorrection()
        {
            var car = new GameObject("Neutral axle pose");
            try
            {
                car.transform.rotation = Quaternion.Euler(0, 37, 0);
                var dynamics = new RvpVehicleDynamics(
                    car.AddComponent<Rigidbody>(),
                    VehicleProfile.GlsSimulation()
                );
                dynamics.Step(new VehicleControls { brake = 1, roadWheelAngle = 20 }, .01f);
                for (int i = 0; i < 4; i++)
                {
                    dynamics.WheelPose(i, out _, out var rotation);
                    var axle =
                        Quaternion.Inverse(car.transform.rotation) * rotation * Vector3.right;
                    Assert.That(
                        axle.y,
                        Is.EqualTo(0).Within(.0001f),
                        "A legacy cylinder basis tipped the imported wheel"
                    );
                    Assert.Greater(axle.x, .8f);
                }
            }
            finally
            {
                Object.DestroyImmediate(car);
            }
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1)]
        public void AdjustedMirrorsLookRearwardOutwardAndRespondToSeatMovement(int side)
        {
            var profile = VehicleProfile.GlsSimulation();
            Vector3 eye = profile.driverEye;
            var rearWindow = new Vector3(0, eye.y - .1f, -profile.wheelbase * .6f);
            var mirror = new MirrorSurface
            {
                side = side,
                centre =
                    side == 0
                        ? new Vector3(0, 1.497f, .55f)
                        : new Vector3(side * .96f, 1.26f, .62f),
                normal =
                    side == 0
                        ? new Vector3(-.15f, 0, -.9887f)
                        : new Vector3(-side * .32f, 0, -.9474f),
            };
            Vector3 originalCentre = mirror.centre,
                originalNormal = mirror.normal;
            Vector3 direction = mirror.DrivingViewDirection(eye, eye, rearWindow);
            Assert.Less(direction.z, -.85f);
            if (side == 0)
                Assert.Greater(
                    Vector3.Dot(direction, (rearWindow - mirror.centre).normalized),
                    .9999f
                );
            else
                Assert.That(
                    side * Mathf.Atan2(direction.x, -direction.z) * Mathf.Rad2Deg,
                    Is.InRange(26, 28)
                );
            Vector3 moved = mirror.DrivingViewDirection(
                eye + new Vector3(0, .06f, .1f),
                eye,
                rearWindow
            );
            Assert.Greater(
                Vector3.Angle(direction, moved),
                1,
                "An adjusted mirror must still reflect the current driver's eye"
            );
            Assert.AreEqual(originalCentre, mirror.centre);
            Assert.AreEqual(originalNormal, mirror.normal);
        }

        [Test]
        public void PreparedCabinHasThreeFittedRearFacingMirrorSurfaces()
        {
            var prefab = Resources.Load<GameObject>("WorldArt/Local/MercedesVehicle");
            if (!prefab)
                Assert.Ignore("Local cabin is intentionally absent from public builds");
            var definition = prefab.GetComponent<AssetDefinition>();
            Assert.That(definition.mirrors, Has.Length.EqualTo(3));
            foreach (var mirror in definition.mirrors)
            {
                Assert.IsNotNull(mirror.mesh);
                Assert.Greater(mirror.mesh.triangles.Length, 3);
                Assert.Greater(mirror.size.x, .12f);
                Assert.Greater(mirror.size.y, .035f);
                Assert.Less(mirror.size.x, .30f);
                Assert.Less(mirror.normal.z, -.9f);
                var direction = MirrorSurface.ViewDirection(
                    definition.driverEye,
                    mirror.centre,
                    mirror.normal
                );
                Assert.Less(direction.z, -.6f, "The mirror must show the road behind");
                foreach (var uv in mirror.mesh.uv)
                {
                    Assert.That(uv.x, Is.InRange(-.01f, 1.01f));
                    Assert.That(uv.y, Is.InRange(-.01f, 1.01f));
                }
            }
        }
    }
}
