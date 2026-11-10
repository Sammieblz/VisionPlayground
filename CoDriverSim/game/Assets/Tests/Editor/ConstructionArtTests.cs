using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoDriver.Tests
{
    public sealed class ConstructionArtTests
    {
        [Test]
        public void ConstructionBodySettlesAndActivityCannotOverrideAnImpact()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var ground = new GameObject("Construction physical pad");
                SceneManager.MoveGameObjectToScene(ground, scene);
                ground.transform.position = Vector3.down * .5f;
                ground.AddComponent<BoxCollider>().size = new Vector3(100, 1, 100);
                Art.Initialize();
                var site = new GameObject(
                    "Construction contact course"
                ).AddComponent<ConstructionSite>();
                SceneManager.MoveGameObjectToScene(site.gameObject, scene);
                site.Build();
                var machine = site.GetComponentInChildren<ExcavatorVisual>();
                Assert.IsNotNull(machine);
                var body = machine.GetComponent<Rigidbody>();
                body.interpolation = RigidbodyInterpolation.None;
                Assert.IsFalse(body.isKinematic);
                Assert.AreEqual(3600, body.mass);
                var physics = scene.GetPhysicsScene();
                for (int step = 0; step < 3200; step++)
                {
                    machine.Pose(step * .01f);
                    physics.Simulate(.01f);
                }
                Assert.Less(body.linearVelocity.magnitude, .05f);
                Assert.Less(
                    Vector2.Distance(
                        new Vector2(body.position.x, body.position.z),
                        new Vector2(6, 0)
                    ),
                    .05f
                );
                Assert.Less(Quaternion.Angle(body.rotation, Quaternion.identity), 1);
                var before = body.position;
                body.AddForce(Vector3.right * 3600, ForceMode.Impulse);
                for (int step = 0; step < 100; step++)
                {
                    machine.Pose(step * .01f);
                    physics.Simulate(.01f);
                }
                Assert.Greater(
                    body.position.x - before.x,
                    .03f,
                    "Finite impact response was overwritten by presentation."
                );
                Assert.Less(body.position.x - before.x, 1.1f);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void PreparedMachineHasPublicProvenanceAndCoherentDetailLevels()
        {
            var prefab = Resources.Load<GameObject>("WorldArt/Construction/Excavator");
            Assert.IsNotNull(prefab, "Run setup-world-art and the construction importer first.");
            var definition = prefab.GetComponent<AssetDefinition>();
            Assert.IsTrue(definition.publicRedistribution);
            Assert.AreEqual("CC0-1.0", definition.license);
            var levels = prefab.GetComponent<LODGroup>().GetLODs();
            Assert.AreEqual(2, levels.Length);
            foreach (var level in levels)
            {
                Assert.AreEqual(5, level.renderers.Length);
                Assert.IsTrue(level.renderers.All(r => r.sharedMaterials.All(m => m && m.shader)));
            }
            Assert.IsEmpty(
                prefab.GetComponentsInChildren<Collider>(),
                "Runtime owns the finite-mass compound body."
            );
            var parts = prefab.GetComponentsInChildren<Transform>();
            var boom = parts.Single(t => t.name == "Boom");
            var bucket = parts.Single(t => t.name == "Bucket");
            Assert.Greater(
                bucket.position.z,
                boom.position.z,
                "Bucket must be in front of the cab on +Z."
            );
            Assert.That(bucket.position.y, Is.InRange(.65f, .73f));
        }

        [Test]
        public void FullCycleKeepsHydraulicsConnectedAndReturnsWithoutAPoseJump()
        {
            Art.Initialize();
            var root = Object.Instantiate(
                Resources.Load<GameObject>("WorldArt/Construction/Excavator")
            );
            try
            {
                root.transform.SetPositionAndRotation(
                    new Vector3(12, 0, -20),
                    Quaternion.Euler(0, 47, 0)
                );
                var visual = root.GetComponent<ExcavatorVisual>();
                visual.Pose(0);
                var initial = visual.Bucket.position;
                var previous = initial;
                float maximumTravel = 0;
                for (int step = 1; step <= 1600; step++)
                {
                    visual.Pose(step * .01f);
                    Assert.Less(
                        visual.HydraulicEndpointError,
                        .002f,
                        "A hydraulic rod detached from its pin."
                    );
                    var current = visual.Bucket.position;
                    Assert.Less(
                        Vector3.Distance(previous, current),
                        .04f,
                        "An animation transition jumped."
                    );
                    maximumTravel = Mathf.Max(maximumTravel, Vector3.Distance(initial, current));
                    var bucketMesh = visual
                        .Bucket.GetComponentsInChildren<Renderer>()
                        .First(r => r.name.Contains("LOD0"));
                    Assert.Greater(
                        bucketMesh.bounds.min.y,
                        -.08f,
                        "The bucket penetrates the level construction pad."
                    );
                    previous = current;
                }
                Assert.Greater(maximumTravel, .7f);
                Assert.Less(Vector3.Distance(initial, previous), .001f);
                Assert.AreEqual(
                    new Vector3(12, 0, -20),
                    root.transform.position,
                    "Visual activity must never reposition the physical root."
                );
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
