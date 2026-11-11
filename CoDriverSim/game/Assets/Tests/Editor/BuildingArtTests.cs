using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CoDriver.Tests
{
    public sealed class BuildingArtTests
    {
        [Test]
        public void PreparedLowriseFitsMappedHeightAndKeepsCollisionWithTheMap()
        {
            var prefab = Resources.Load<GameObject>("WorldArt/Buildings/Lowrise");
            Assert.IsNotNull(prefab);
            Assert.IsTrue(prefab.GetComponent<AssetDefinition>().publicRedistribution);
            Assert.AreEqual(2, prefab.GetComponent<LODGroup>().GetLODs().Length);
            Assert.IsEmpty(
                prefab.GetComponentsInChildren<Collider>(),
                "The mapped footprint owns collision, not decorative meshes."
            );
            var map = MapDefinition.Load("davis-downtown");
            var building = map.buildings.Single(b => b.sourceId == "way/45208383");
            var root = new GameObject("Mapped building test");
            try
            {
                Assert.IsTrue(MappedBuildingArt.TryAdd(root.transform, building));
                var renderers = root.GetComponentsInChildren<Renderer>();
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers)
                {
                    bounds.Encapsulate(renderer.bounds);
                    Assert.IsTrue(renderer.sharedMaterials.All(m => m && m.shader));
                }
                Assert.That(bounds.min.y, Is.EqualTo(0).Within(.02f));
                Assert.That(bounds.max.y, Is.EqualTo(building.height).Within(.02f));
                Assert.Greater(bounds.size.x, 10);
                foreach (var p in building.rings[0].points)
                    Assert.Less(Vector3.Distance(bounds.ClosestPoint(p), p), .2f);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
