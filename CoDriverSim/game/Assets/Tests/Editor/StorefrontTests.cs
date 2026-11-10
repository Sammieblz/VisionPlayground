using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoDriver.Tests
{
    public sealed class StorefrontTests
    {
        [Test]
        public void MappedActivityDoorsHavePhysicalOpeningsAndSidewalkAccess()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                Art.Initialize();
                var town = new GameObject("Storefront inspection").AddComponent<Town>();
                SceneManager.MoveGameObjectToScene(town.gameObject, scene);
                town.Map = MapDefinition.Load("davis-downtown");
                var build = typeof(DavisWorld).GetMethod(
                    "Building",
                    BindingFlags.Static | BindingFlags.NonPublic
                );
                int inspected = 0;
                foreach (var building in town.Map.buildings)
                {
                    var entry = MappedStorefront.Find(town.Map, building);
                    if (entry == null)
                        continue;
                    build.Invoke(null, new object[] { town, building });
                    inspected++;
                    var door = town.Doors.Last();
                    door.transform.localRotation = Quaternion.Euler(0, 75, 0);
                    Physics.SyncTransforms();
                    var physics = scene.GetPhysicsScene();
                    Assert.IsFalse(
                        physics.Raycast(
                            entry.Centre + entry.Outward * 1.5f + Vector3.up,
                            -entry.Outward,
                            out var hit,
                            3.5f
                        ),
                        building.sourceId + " blocked by " + hit.collider
                    );
                    Assert.LessOrEqual(entry.WalkDistance, 8.75f);
                    Assert.GreaterOrEqual(entry.Width, 2.4f);
                    Assert.IsTrue(
                        physics.Raycast(door.Approach + Vector3.up, Vector3.down, out var floor, 2),
                        building.sourceId + " requires a physical floor at its approach"
                    );
                    Assert.Greater(floor.normal.y, .7f);
                }
                Assert.GreaterOrEqual(
                    inspected,
                    7,
                    "The downtown activity block needs several usable entrances"
                );
                Assert.AreEqual(inspected, town.Doors.Count);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
