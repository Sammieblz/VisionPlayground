using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CoDriver.Tests
{
    public sealed class TrafficArrivalAndBoundaryTests
    {
        [Test]
        public void CedarTurnYieldsToItsSweptCrosswalkThenResumesWhenClear()
        {
            var town = new GameObject("Crosswalk clearance town").AddComponent<Town>();
            var signal = new GameObject("Junction").AddComponent<Signal>();
            var car = new GameObject("Waiting car").AddComponent<TrafficActor>();
            var walker = new GameObject("Crossing walker").AddComponent<Walker>();
            try
            {
                town.Map = MapDefinition.Cedar();
                town.Traffic.Add(car);
                town.Walkers.Add(walker);
                signal.Town = town;
                signal.HoldPhase(20, 100);
                car.transform.SetPositionAndRotation(
                    new Vector3(-20, 0, -2),
                    Quaternion.Euler(0, 90, 0)
                );
                car.gameObject.layer = TrafficClearance.TrafficLayer;
                var envelope = car.gameObject.AddComponent<BoxCollider>();
                envelope.size = new Vector3(1.8f, 1.4f, 4.4f);
                envelope.center = Vector3.up * .8f;
                car.Path = new TrackPath(
                    new[] { new Vector3(-40, 0, -2), new Vector3(40, 0, -2) },
                    false
                );
                car.Distance = 20;
                walker.gameObject.layer = TrafficClearance.WalkerLayer;
                var shape = walker.gameObject.AddComponent<CapsuleCollider>();
                shape.radius = .24f;
                shape.height = 1.8f;
                shape.center = Vector3.up * .9f;
                walker.Crossing = true;
                walker.transform.position = new Vector3(-10, 0, -2);
                Physics.SyncTransforms();
                Assert.IsFalse(
                    town.MayEnter(car, signal),
                    "An actual crossing actor must block the complete planned turn."
                );
                walker.transform.position = new Vector3(-10, 0, 4);
                Physics.SyncTransforms();
                Assert.IsTrue(
                    town.MayEnter(car, signal),
                    "A walker clear of this lane must not consume every later green phase."
                );
            }
            finally
            {
                Object.DestroyImmediate(walker.gameObject);
                Object.DestroyImmediate(car.gameObject);
                Object.DestroyImmediate(signal.gameObject);
                Object.DestroyImmediate(town.gameObject);
            }
        }

        [Test]
        public void RedApproachKeepsItsPlaceWithoutClaimingEmptyJunction()
        {
            var town = new GameObject("Traffic arrival town").AddComponent<Town>();
            var signal = new GameObject("Junction").AddComponent<Signal>();
            var earlier = new GameObject("Earlier red approach").AddComponent<TrafficActor>();
            var later = new GameObject("Later opposite approach").AddComponent<TrafficActor>();
            try
            {
                town.Map = MapDefinition.Cedar();
                signal.Town = town;
                signal.HoldPhase(0, 100);
                earlier.transform.SetPositionAndRotation(
                    new Vector3(-20, 0, -2),
                    Quaternion.Euler(0, 90, 0)
                );
                later.transform.SetPositionAndRotation(
                    new Vector3(20, 0, 2),
                    Quaternion.Euler(0, -90, 0)
                );
                town.Traffic.Add(earlier);
                town.Traffic.Add(later);
                Assert.IsFalse(
                    signal.Reserve(earlier, requireGreen: true),
                    "Registering during red cannot grant entry."
                );
                Assert.AreEqual("None", signal.Reservation);
                Assert.IsFalse(signal.Reserve(later, requireGreen: true));
                signal.HoldPhase(16, 100);
                Assert.IsFalse(
                    signal.Reserve(later, requireGreen: true),
                    "A new green cannot replace arrival order with Update order."
                );
                Assert.IsTrue(signal.Reserve(earlier, requireGreen: true));
                earlier.transform.position = new Vector3(-8, 0, -2);
                signal.HoldPhase(0, 100);
                Assert.IsTrue(
                    signal.Reserve(earlier, requireGreen: true),
                    "An already committed turn must finish through red."
                );
            }
            finally
            {
                Object.DestroyImmediate(earlier.gameObject);
                Object.DestroyImmediate(later.gameObject);
                Object.DestroyImmediate(signal.gameObject);
                Object.DestroyImmediate(town.gameObject);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PerimeterPhysicallySealsRoadShouldersAndCorners(bool davis)
        {
            Art.Initialize();
            var town = new GameObject("Boundary test town").AddComponent<Town>();
            try
            {
                town.Map = davis ? MapDefinition.Load("davis-downtown") : MapDefinition.Cedar();
                WorldBoundary.Build(town);
                Physics.SyncTransforms();
                var bounds = WorldBoundary.Envelopes(town.Map);
                var colliders = town.GetComponentsInChildren<BoxCollider>();
                Assert.AreEqual(4, colliders.Length);
                Assert.AreEqual(4, town.WalkSurfaces.Count(s => s.area == 1));
                // Sample each continuous side at centres and both corner joins,
                // at ground level and below grade. This catches shoulder gaps
                // and underpass escapes, rather than just testing centrepoints.
                for (int side = 0; side < bounds.Length; side++)
                    foreach (float fraction in new[] { .001f, .25f, .5f, .75f, .999f })
                    foreach (float height in new[] { -4f, .8f, 2f })
                    {
                        var wall = bounds[side];
                        bool vertical = side < 2;
                        Vector3 point = vertical
                            ? new Vector3(
                                wall.center.x,
                                height,
                                Mathf.Lerp(wall.min.z, wall.max.z, fraction)
                            )
                            : new Vector3(
                                Mathf.Lerp(wall.min.x, wall.max.x, fraction),
                                height,
                                wall.center.z
                            );
                        Vector3 outward =
                            side == 0 ? Vector3.left
                            : side == 1 ? Vector3.right
                            : side == 2 ? Vector3.back
                            : Vector3.forward;
                        Assert.IsTrue(
                            Physics.Raycast(point - outward * 2, outward, out var hit, 4)
                        );
                        Assert.IsTrue(hit.collider.transform.IsChildOf(town.transform));
                    }
                if (davis)
                    foreach (var point in town.Map.LaneById[SimulatedBridgeExtension.LaneId].points)
                        Assert.IsFalse(
                            bounds.Any(b => b.Contains(point + Vector3.up)),
                            "The fictional bridge remains inside the physical boundary."
                        );
            }
            finally
            {
                Object.DestroyImmediate(town.gameObject);
            }
        }
    }
}
