using NUnit.Framework;
using UnityEngine;

namespace CoDriver.Tests
{
    public sealed class RailGatePriorityTests
    {
        [Test]
        public void GateWarningBrakesBeforeTheArmAndDoesNotStopExitingOrGradeSeparatedTraffic()
        {
            var gate = new GameObject("test gate");
            var vehicle = new GameObject("test car");
            try
            {
                var body = vehicle.AddComponent<BoxCollider>();
                body.center = Vector3.up * .9f;
                body.size = new Vector3(2, 1.5f, 5);
                vehicle.transform.position = new Vector3(-5, 0, -16);
                Assert.Less(
                    RailCrossing.GateSpeedLimit(gate.transform, body),
                    8,
                    "Begin braking while the arm may still be raised"
                );
                vehicle.transform.position = new Vector3(-5, 0, -4.5f);
                Assert.AreEqual(0, RailCrossing.GateSpeedLimit(gate.transform, body));
                vehicle.transform.position = new Vector3(-5, 0, 1);
                Assert.IsTrue(
                    float.IsPositiveInfinity(RailCrossing.GateSpeedLimit(gate.transform, body))
                );
                vehicle.transform.position = new Vector3(-5, -5, -16);
                Assert.IsTrue(
                    float.IsPositiveInfinity(RailCrossing.GateSpeedLimit(gate.transform, body))
                );
                vehicle.transform.position = new Vector3(-5, 0, -16);
                vehicle.transform.rotation = Quaternion.Euler(0, 180, 0);
                Assert.IsTrue(
                    float.IsPositiveInfinity(RailCrossing.GateSpeedLimit(gate.transform, body))
                );
            }
            finally
            {
                Object.DestroyImmediate(vehicle);
                Object.DestroyImmediate(gate);
            }
        }

        [Test]
        public void CheckedTurningCreepStillRejectsAnActualBodyObstruction()
        {
            var vehicle = new GameObject("turning car");
            var obstacle = new GameObject("occupied path");
            try
            {
                var body = vehicle.AddComponent<BoxCollider>();
                body.center = Vector3.up * .85f;
                body.size = new Vector3(1.82f, 1.15f, 4.4f);
                var box = obstacle.AddComponent<BoxCollider>();
                box.size = Vector3.one;
                obstacle.transform.position = new Vector3(0, .8f, 3);
                Physics.SyncTransforms();
                Assert.Less(TrafficClearance.TurningCreepLimit(body, 2.7f, 15), .1f);
                obstacle.transform.position = new Vector3(12, .8f, 3);
                Physics.SyncTransforms();
                Assert.AreEqual(1, TrafficClearance.TurningCreepLimit(body, 2.7f, 15));
            }
            finally
            {
                Object.DestroyImmediate(vehicle);
                Object.DestroyImmediate(obstacle);
            }
        }
    }
}
