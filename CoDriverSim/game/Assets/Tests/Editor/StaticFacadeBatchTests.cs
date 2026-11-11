using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoDriver.Tests
{
    public sealed class StaticFacadeBatchTests
    {
        [Test]
        public void NearbyModulesKeepWorldGeometryUvAndMaterialsWithFewerRenderers()
        {
            var root = new GameObject("Architecture batching fixture");
            var mesh = Quad();
            var a = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var b = new Material(a);
            try
            {
                root.transform.SetPositionAndRotation(
                    new Vector3(30, 2, -10),
                    Quaternion.Euler(0, 23, 0)
                );
                var source = new GameObject("Original facade transform");
                source.transform.SetParent(root.transform, false);
                source.transform.localRotation = Quaternion.Euler(0, 12, 0);
                source.transform.localScale = new Vector3(1.3f, .8f, 1.1f);
                var collider = source.AddComponent<BoxCollider>();
                var batcher = root.AddComponent<StaticFacadeBatches>();
                var modules = new[]
                {
                    Part(mesh, new Vector3(5, 1, 5), 15),
                    Part(mesh, new Vector3(10, 2, 5), -20),
                };
                var secondMaterial = new[] { Part(mesh, new Vector3(15, 2, 5), 0) };
                var expected = new Dictionary<Material, Vector3[]>
                {
                    [a] = WorldVertices(source.transform, modules),
                    [b] = WorldVertices(source.transform, secondMaterial),
                };
                batcher.Add(source.transform, a, modules);
                batcher.Add(source.transform, b, secondMaterial);
                batcher.Bake();
                var renderers = root.GetComponentsInChildren<MeshRenderer>();
                Assert.AreEqual(
                    2,
                    renderers.Length,
                    "Three modules should need two material submissions."
                );
                foreach (var renderer in renderers)
                {
                    var combined = renderer.GetComponent<MeshFilter>().sharedMesh;
                    var actual = combined
                        .vertices.Select(renderer.transform.TransformPoint)
                        .ToArray();
                    var wanted = expected[renderer.sharedMaterial];
                    Assert.AreEqual(wanted.Length, actual.Length);
                    foreach (var vertex in wanted)
                        Assert.IsTrue(
                            actual.Any(v => (v - vertex).sqrMagnitude < .000001f),
                            "A transformed architectural vertex moved."
                        );
                    Assert.AreEqual(wanted.Length / 4 * 6, combined.triangles.Length);
                    Assert.IsTrue(
                        combined.uv.All(uv => mesh.uv.Contains(uv)),
                        "Texture coordinates must survive batching."
                    );
                    Assert.AreEqual(ShadowCastingMode.Off, renderer.shadowCastingMode);
                    Assert.AreEqual(FacadeArchitecture.DetailLayer, renderer.gameObject.layer);
                }
                CollectionAssert.AreEqual(
                    new[] { collider },
                    root.GetComponentsInChildren<Collider>()
                );
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }

        [Test]
        public void DistantModulesKeepIndependentDetailCullingAndBakeDoesNotDuplicateThem()
        {
            var root = new GameObject("Separate architecture cells");
            var mesh = Quad();
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            try
            {
                var batcher = root.AddComponent<StaticFacadeBatches>();
                batcher.Add(
                    root.transform,
                    material,
                    new[]
                    {
                        Part(mesh, new Vector3(5, 0, 5), 0),
                        Part(mesh, new Vector3(85, 0, 5), 0),
                    }
                );
                batcher.Bake();
                batcher.Bake();
                var groups = root.GetComponentsInChildren<LODGroup>();
                Assert.AreEqual(2, groups.Length);
                Assert.AreEqual(2, root.GetComponentsInChildren<MeshRenderer>().Length);
                foreach (var group in groups)
                {
                    Assert.AreEqual(10, group.size, .001f);
                    Assert.AreEqual(
                        .05f,
                        group.GetLODs()[0].screenRelativeTransitionHeight,
                        .0001f
                    );
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(material);
            }
        }

        static Mesh Quad()
        {
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, new Vector3(1, 1, 0), Vector3.up },
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
            };
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        static CombineInstance Part(Mesh mesh, Vector3 position, float angle) =>
            new CombineInstance
            {
                mesh = mesh,
                transform = Matrix4x4.TRS(
                    position,
                    Quaternion.Euler(0, angle, 0),
                    new Vector3(1, 2, .7f)
                ),
            };

        static Vector3[] WorldVertices(Transform source, CombineInstance[] parts) =>
            parts
                .SelectMany(part =>
                    part.mesh.vertices.Select(v =>
                        (source.localToWorldMatrix * part.transform).MultiplyPoint3x4(v)
                    )
                )
                .ToArray();
    }
}
