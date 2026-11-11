using NUnit.Framework;
using UnityEngine;

namespace CoDriver.Tests
{
    public sealed class BridgeExtensionTests
    {
        [Test]
        public void FictionalLoopConnectsToDavisInBothDirections()
        {
            var map = MapDefinition.Load("davis-downtown");
            var departure = map.LaneById[SimulatedBridgeExtension.Outbound];
            var arrival = map.LaneById[SimulatedBridgeExtension.Inbound];
            var bridge = map.LaneById[SimulatedBridgeExtension.LaneId];
            Assert.AreEqual(bridge.id, departure.next[0]);
            Assert.AreEqual(arrival.id, bridge.next[0]);
            Assert.IsFalse(departure.boundary || arrival.boundary);
            Assert.Less(Vector3.Distance(departure.points[departure.points.Length - 1], bridge.points[0]), .01f);
            Assert.Less(Vector3.Distance(arrival.points[0], bridge.points[bridge.points.Length - 1]), .01f);
            Assert.Greater(map.Route(departure, arrival).Count, 2);
            Assert.That(map.name, Does.Contain("fictional"));
            foreach (var p in bridge.points) Assert.IsTrue(map.Contains(p));
        }

        [Test]
        public void BridgeGradeHasNoRoadStepsOrSteepBreaks()
        {
            var lane = MapDefinition.Load("davis-downtown").LaneById[SimulatedBridgeExtension.LaneId];
            float top = 0;
            for (int i = 1; i < lane.points.Length; i++)
            {
                var delta = lane.points[i] - lane.points[i - 1];
                float horizontal = Vector3.ProjectOnPlane(delta, Vector3.up).magnitude;
                Assert.Greater(horizontal, .001f);
                Assert.LessOrEqual(Mathf.Abs(delta.y) / horizontal, .121f);
                top = Mathf.Max(top, lane.points[i].y);
            }
            Assert.AreEqual(4, top, .001f);
            Assert.AreEqual(0, lane.points[0].y, .001f);
            Assert.AreEqual(0, lane.points[lane.points.Length - 1].y, .001f);
        }
    }
}
