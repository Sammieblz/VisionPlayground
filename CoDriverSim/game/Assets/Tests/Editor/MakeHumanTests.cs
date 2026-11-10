using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CoDriver.Tests
{
    public class MakeHumanTests
    {
        [TestCase("Citizen_A", 1.78f)]
        [TestCase("Citizen_B", 1.65f)]
        public void ClothedPublicCharactersHavePhysicalScaleAndTwoRiggedDetailLevels(
            string name,
            float height
        )
        {
            var prefab = Resources.Load<GameObject>("WorldArt/Civilians/" + name);
            Assert.IsNotNull(prefab, "Run setup-civilians and MakeHumanImporter.Prepare");
            var instance = Object.Instantiate(prefab);
            try
            {
                var metadata = instance.GetComponent<AssetDefinition>();
                Assert.IsTrue(metadata.publicRedistribution);
                Assert.AreEqual("CC0-1.0", metadata.license);
                Assert.AreEqual(64, metadata.preparedSha256.Length);
                Assert.IsEmpty(
                    instance.GetComponentsInChildren<Collider>(),
                    "Presentation must not alter actor contact physics"
                );
                var animator = instance.GetComponent<Animator>();
                Assert.IsTrue(animator.avatar.isValid && animator.avatar.isHuman);
                Assert.IsFalse(animator.applyRootMotion);
                Assert.IsTrue(
                    animator.runtimeAnimatorController.animationClips.Any(c =>
                        c.name.EndsWith("Walk_Loop")
                    )
                );
                var lods = instance.GetComponent<LODGroup>().GetLODs();
                Assert.AreEqual(2, lods.Length);
                int[] triangles = new int[2];
                for (int i = 0; i < 2; i++)
                {
                    Assert.GreaterOrEqual(lods[i].renderers.Length, 6);
                    var bounds = new Bounds();
                    bool firstVertex = true;
                    foreach (var renderer in lods[i].renderers)
                    {
                        var skin = (SkinnedMeshRenderer)renderer;
                        triangles[i] += skin.sharedMesh.triangles.Length / 3;
                        Assert.IsNotEmpty(skin.bones);
                        var baked = new Mesh();
                        try
                        {
                            // Imported FBX renderers carry the centimetre conversion.
                            skin.BakeMesh(baked, true);
                            foreach (var vertex in baked.vertices)
                            {
                                var point = skin.transform.TransformPoint(vertex);
                                if (firstVertex)
                                {
                                    bounds = new Bounds(point, Vector3.zero);
                                    firstVertex = false;
                                }
                                else
                                    bounds.Encapsulate(point);
                            }
                        }
                        finally
                        {
                            Object.DestroyImmediate(baked);
                        }
                        foreach (var material in skin.sharedMaterials)
                        {
                            Assert.AreEqual("Universal Render Pipeline/Lit", material.shader.name);
                            Assert.IsNotNull(material.mainTexture, material.name);
                        }
                    }
                    Assert.That(bounds.size.y, Is.InRange(height - .08f, height + .12f));
                    Assert.Less(Mathf.Abs(bounds.min.y), .08f);
                }
                Assert.Less(triangles[1], triangles[0] * .65f);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
