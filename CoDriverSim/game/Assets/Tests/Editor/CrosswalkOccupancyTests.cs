using NUnit.Framework;
using UnityEngine;

namespace CoDriver.Tests
{
    public sealed class CrosswalkOccupancyTests
    {
        [Test]
        public void StoppedVehicleMustClearBeforePeopleEnterCrosswalk()
        {
            var car = new GameObject("Stationary crosswalk occupant");
            try
            {
                car.layer = TrafficClearance.TrafficLayer;
                car.transform.position = new Vector3(8000, 1, 0);
                car.AddComponent<BoxCollider>().size = new Vector3(4.4f, 1.5f, 1.8f);
                Physics.SyncTransforms();
                var start = new Vector3(8000, 0, -6);
                var end = new Vector3(8000, 0, 6);
                Assert.IsFalse(TrafficClearance.CrossingPathClear(start, end));
                car.transform.position += Vector3.right * 10;
                Physics.SyncTransforms();
                Assert.IsTrue(TrafficClearance.CrossingPathClear(start, end));
            }
            finally { Object.DestroyImmediate(car); }
        }
    }
}
