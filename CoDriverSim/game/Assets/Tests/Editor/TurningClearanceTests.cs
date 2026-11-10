using NUnit.Framework;
using UnityEngine;

namespace CoDriver.Tests
{
    public class TurningClearanceTests
    {
        [Test]
        public void StraightBusExitLeavesOpposingLaneUsableButRejectsAnActualObstruction()
        {
            var bus = new GameObject("clearance bus");
            var oncoming = new GameObject("oncoming ambulance");
            try
            {
                var shape = bus.AddComponent<BoxCollider>();
                shape.size = new Vector3(2.35f, 2.8f, 10);
                shape.center = Vector3.up * 1.5f;
                oncoming.layer = TrafficClearance.ParkedLayer;
                var obstacle = oncoming.AddComponent<BoxCollider>();
                obstacle.size = new Vector3(2, 2.5f, 5.5f);
                obstacle.center = Vector3.up * 1.3f;
                var route = new TrackPath(new[] { Vector3.zero, new Vector3(0, 0, 40) }, false);
                oncoming.transform.position = new Vector3(3.2f, 0, 20);
                Physics.SyncTransforms();
                Assert.IsTrue(
                    TrafficClearance.ClearJunctionSweep(route, 10, 25, shape),
                    "A straight exit must not retain a turning allowance across the opposing lane"
                );
                oncoming.transform.position = new Vector3(2, 0, 20);
                Physics.SyncTransforms();
                Assert.IsFalse(
                    TrafficClearance.ClearJunctionSweep(route, 10, 25, shape),
                    "Whole-body collision clearance must still stop an obstructed bus"
                );
            }
            finally
            {
                Object.DestroyImmediate(bus);
                Object.DestroyImmediate(oncoming);
            }
        }
    }
}
