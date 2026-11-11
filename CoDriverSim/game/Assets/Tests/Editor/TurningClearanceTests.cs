using NUnit.Framework;
using UnityEngine;

namespace CoDriver.Tests
{
    public class TurningClearanceTests
    {
        [Test]
        public void ParkingExitRequiresWholeBodyInTheIntendedLane()
        {
            var car = new GameObject("parking envelope");
            try
            {
                var body = car.AddComponent<BoxCollider>();
                body.size = new Vector3(1.8f, 1.5f, 4.4f);
                body.center = Vector3.up;
                var lane = new MapLane
                {
                    width = 3.2f,
                    Path = new TrackPath(new[] { Vector3.zero, Vector3.forward * 100 }, false),
                };
                car.transform.position = Vector3.forward * 40;
                Assert.IsTrue(TrafficClearance.AlignedInsideLane(body, lane));
                car.transform.position += Vector3.right * .7f;
                Assert.IsFalse(
                    TrafficClearance.AlignedInsideLane(body, lane),
                    "Rear corner still outside lane"
                );
                car.transform.position = Vector3.forward * 40;
                car.transform.rotation = Quaternion.Euler(0, 15, 0);
                Assert.IsFalse(TrafficClearance.AlignedInsideLane(body, lane), "Not aligned yet");
                car.transform.rotation = Quaternion.Euler(0, 180, 0);
                Assert.IsFalse(
                    TrafficClearance.AlignedInsideLane(body, lane),
                    "Opposing lane direction"
                );
                car.transform.rotation = Quaternion.identity;
                car.transform.position += Vector3.down * 5;
                Assert.IsFalse(
                    TrafficClearance.AlignedInsideLane(body, lane),
                    "Different road level"
                );
                car.transform.position = Vector3.forward;
                Assert.IsFalse(TrafficClearance.AlignedInsideLane(body, lane), "Still in junction");
                car.transform.position = Vector3.forward * 40;
                lane.internalLane = true;
                Assert.IsFalse(TrafficClearance.AlignedInsideLane(body, lane));
            }
            finally
            {
                Object.DestroyImmediate(car);
            }
        }

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
