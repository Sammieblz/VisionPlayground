using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CoDriver.Tests
{
    public class RealismTests
    {
        [TestCase("Sedan", 4.4f, 1.46f)]
        [TestCase("Bus", 9.5f, 2.81f)]
        [TestCase("Metro", 13.5f, 3.3f)]
        public void ImportedVehiclesFitTheSimulationEnvelope(
            string name,
            float length,
            float height
        )
        {
            var prefab = Resources.Load<GameObject>("Realism/Prepared/" + name);
            Assert.IsNotNull(prefab, "Run the licensed asset fetch and Unity build preparation");
            var instance = Object.Instantiate(prefab);
            try
            {
                var renderers = instance.GetComponentsInChildren<Renderer>();
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers)
                    bounds.Encapsulate(renderer.bounds);
                Assert.That(bounds.size.z, Is.EqualTo(length).Within(.02));
                Assert.That(bounds.size.y, Is.EqualTo(height).Within(.02));
                Assert.That(
                    bounds.min.y,
                    Is.EqualTo(0).Within(.02),
                    "Tires and rail wheels must sit on the ground"
                );
                Assert.Less(renderers.Length, 40, "Rail static detail must be merged by material");
                Assert.IsTrue(
                    renderers.SelectMany(r => r.sharedMaterials).All(m => m && m.shader.isSupported)
                );
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [TestCase("Bus", 2f, 1.4f, 3.7f, -1f)]
        [TestCase("ParkingSedan", -2f, .95f, 0f, 1f)]
        public void MovingDoorHasARealOpeningInTheVehicleShell(
            string name,
            float x,
            float y,
            float z,
            float direction
        )
        {
            var instance = Object.Instantiate(
                Resources.Load<GameObject>("Realism/Prepared/" + name)
            );
            try
            {
                var panels = instance
                    .GetComponentsInChildren<MeshFilter>()
                    .Where(m => m.name == "DoorPanel")
                    .ToArray();
                Assert.IsNotEmpty(panels);
                var ray = new Ray(new Vector3(x, y, z), Vector3.right * direction);
                bool closedHitsPanel = false;
                foreach (var filter in instance.GetComponentsInChildren<MeshFilter>())
                {
                    var collider = filter.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh;
                    Physics.SyncTransforms();
                    bool hit = collider.Raycast(ray, out _, 1.9f);
                    if (filter.name == "DoorPanel")
                        closedHitsPanel |= hit;
                    else
                        Assert.IsFalse(hit, "Opening is obstructed by " + filter.name);
                }
                Assert.IsTrue(closedHitsPanel, "The closed door must fill the opening");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void CockpitWheelAndColumnRemainAlignedAtTheLeftDrivingPosition()
        {
            var instance = Object.Instantiate(Resources.Load<GameObject>("Cockpit/Interior"));
            try
            {
                var renderers = instance.GetComponentsInChildren<Renderer>();
                var wheel = renderers.Single(r => r.name == "InteriorSteeringWheel01").bounds;
                var column = renderers.Single(r => r.name == "InteriorSteeringCylinder").bounds;
                Assert.That(wheel.center.x, Is.EqualTo(-.35f).Within(.04f));
                Assert.Less(Mathf.Abs(wheel.center.x - column.center.x), .04f);
                Assert.Less(Vector3.Distance(wheel.center, column.center), .3f);
                Assert.IsTrue(renderers.Any(r => r.name == "InteriorSteeringDash"));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void SurfaceMapsAndSkyAreBoundToPreparedMaterials()
        {
            foreach (string name in new[] { "Asphalt", "Concrete", "Grass", "Brick", "Roof" })
            {
                var material = Resources.Load<Material>("Realism/Prepared/" + name);
                Assert.IsNotNull(material.mainTexture);
                Assert.IsNotNull(material.GetTexture("_BumpMap"), name);
                Assert.IsNotNull(material.GetTexture("_MetallicGlossMap"), name);
            }
            Assert.IsInstanceOf<Cubemap>(
                Resources.Load<Material>("Realism/Prepared/Sky").GetTexture("_Tex")
            );
        }

        [Test]
        public void PlayerBodyDoesNotObstructTheIndependentForwardCamera()
        {
            var instance = Object.Instantiate(
                Resources.Load<GameObject>("Realism/Prepared/PlayerSedan")
            );
            try
            {
                var ray = new Ray(new Vector3(0, 1.22f, 1.7f), Vector3.forward);
                foreach (var filter in instance.GetComponentsInChildren<MeshFilter>())
                {
                    if (filter.sharedMesh.vertexCount == 0)
                        continue;
                    var collider = filter.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh;
                    Physics.SyncTransforms();
                    Assert.IsFalse(
                        collider.Raycast(ray, out _, 20),
                        "Camera sees its own " + filter.name
                    );
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void PlayerWheelsMatchSuspensionRadiusAndPivot()
        {
            foreach (string side in new[] { "Left", "Right" })
            {
                var mesh = Resources
                    .Load<GameObject>("Realism/Prepared/Wheel" + side)
                    .GetComponent<MeshFilter>()
                    .sharedMesh;
                Assert.That(mesh.bounds.center.magnitude, Is.LessThan(.001));
                Assert.That(mesh.bounds.size.x / 2, Is.EqualTo(.34f).Within(.001));
                Assert.That(mesh.bounds.size.z / 2, Is.EqualTo(.34f).Within(.01));
            }
        }
    }
}
