using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CoDriver.Tests
{
    public sealed class SignalPlacementTests
    {
        [Test]
        public void DavisMastsLeaveEveryApproachLaneClearIncludingFifthStreetReproduction()
        {
            var town = new GameObject("Signal clearance town").AddComponent<Town>();
            var vehicle = new GameObject("clearance fixture");
            try
            {
                Art.Initialize();
                town.Map = MapDefinition.Load("davis-downtown");
                foreach (var signal in town.Map.signals)
                    MappedSignalAssembly.Build(town, signal);
                var body = vehicle.AddComponent<BoxCollider>();
                body.center = new Vector3(0, 1.5f, 0);
                body.size = new Vector3(2.5f, 3, 10);
                foreach (
                    var lane in town.Map.lanes.Where(l =>
                        !l.internalLane && !string.IsNullOrEmpty(l.signal)
                    )
                )
                {
                    // A bus envelope must remain clear at and around each stop line,
                    // not only at the one location that failed the sustained run.
                    for (float offset = -8; offset <= 8; offset += 2)
                    {
                        var forward = lane.Path.Direction(lane.Path.Length);
                        vehicle.transform.SetPositionAndRotation(
                            lane.points.Last() + forward * offset,
                            Quaternion.LookRotation(forward)
                        );
                        Physics.SyncTransforms();
                        foreach (var collider in town.GetComponentsInChildren<Collider>())
                            Assert.IsFalse(
                                Physics.ComputePenetration(
                                    body,
                                    vehicle.transform.position,
                                    vehicle.transform.rotation,
                                    collider,
                                    collider.transform.position,
                                    collider.transform.rotation,
                                    out _,
                                    out _
                                ),
                                lane.id + " / " + collider.name
                            );
                    }
                }
                body.size = new Vector3(1.82f, 1.15f, 4.4f);
                body.center = Vector3.up * .85f;
                vehicle.transform.SetPositionAndRotation(
                    new Vector3(158.132675f, -.022403f, 251.659851f),
                    Quaternion.LookRotation(new Vector3(-.971082f, -.0151495f, -.238264f))
                );
                Physics.SyncTransforms();
                Assert.IsTrue(
                    float.IsPositiveInfinity(
                        TrafficClearance.ForwardSpeedLimit(body, 0, immediate: true)
                    ),
                    "The car stopped on 5th Street must have a clear physical path: "
                        + TrafficClearance.LastBlocker
                );
            }
            finally
            {
                Object.DestroyImmediate(vehicle);
                Object.DestroyImmediate(town.gameObject);
            }
        }
    }
}
