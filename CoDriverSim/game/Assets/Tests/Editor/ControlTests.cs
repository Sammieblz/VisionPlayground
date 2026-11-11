using NUnit.Framework;
using UnityEngine;

namespace CoDriver.Tests
{
    public class ControlTests
    {
        static CommaCommand Healthy() =>
            new CommaCommand
            {
                session_id = "test",
                active = true,
                healthy = true,
                engageable = true,
                steering_deg = 3,
                acceleration_mps2 = 1,
            };

        [Test]
        public void HumanOwnsVehicleByDefault()
        {
            var a = new ControlArbiter();
            Assert.IsFalse(a.Accept(Healthy(), "test", .02, 0, 0, false, false));
        }

        [Test]
        public void ExplicitRequestAndHealthyFreshCommandRequired()
        {
            var a = new ControlArbiter();
            a.Request();
            Assert.IsTrue(a.Accept(Healthy(), "test", .02, 0, 0, false, false));
        }

        [TestCase(.3f, 0f, false, false)]
        [TestCase(0f, .1f, false, false)]
        [TestCase(0f, 0f, true, false)]
        [TestCase(0f, 0f, false, true)]
        public void HumanTakeoverRevokesAuthority(
            float steering,
            float brake,
            bool pause,
            bool lost
        )
        {
            var a = new ControlArbiter();
            a.Request();
            Assert.IsFalse(a.Accept(Healthy(), "test", .01, steering, brake, pause, lost));
            Assert.IsFalse(a.Requested);
        }

        [Test]
        public void StaleCommandCancels()
        {
            var a = new ControlArbiter();
            a.Request();
            Assert.IsFalse(a.Accept(Healthy(), "test", .3, 0, 0, false, false));
            Assert.IsFalse(a.Requested);
        }

        [TestCase(.5f, false)]
        [TestCase(0f, true)]
        public void PedalAndHandbrakeTakeOver(float throttle, bool handbrake)
        {
            var a = new ControlArbiter();
            a.Request();
            Assert.IsFalse(
                a.Accept(Healthy(), "test", .02, 0, 0, false, false, throttle, handbrake)
            );
            Assert.IsFalse(a.Requested);
        }

        [Test]
        public void CrossSessionCommandRejected()
        {
            var a = new ControlArbiter();
            a.Request();
            Assert.IsFalse(a.Accept(Healthy(), "another", .01, 0, 0, false, false));
        }

        [Test]
        public void NonfiniteCommandRejected()
        {
            var c = Healthy();
            c.steering_deg = float.NaN;
            var a = new ControlArbiter();
            a.Request();
            Assert.IsFalse(a.Accept(c, "test", .01, 0, 0, false, false));
        }

        [Test]
        public void UnhealthyCommaDoesNotGetTrafficPlannerFallback()
        {
            var c = Healthy();
            c.healthy = false;
            var a = new ControlArbiter();
            a.Request();
            Assert.IsFalse(a.Accept(c, "test", .01, 0, 0, false, false));
            Assert.IsFalse(a.Requested);
        }

        [Test]
        public void RejectedRequestDoesNotLingerOrEngageOnRecovery()
        {
            var a = new ControlArbiter();
            var c = Healthy();
            c.active = false;
            c.healthy = false;
            c.status = "controlsdLagging";
            Assert.IsFalse(a.TryRequest(c, "test", .02, 10));
            Assert.IsFalse(a.Requested);
            Assert.That(a.Reason, Does.Contain("controlsdLagging"));
            Assert.IsFalse(a.Accept(Healthy(), "test", .01, 0, 0, false, false, now: 11));
            Assert.IsTrue(a.TryRequest(Healthy(), "test", .02, 12));
            Assert.IsTrue(a.Accept(Healthy(), "test", .01, 0, 0, false, false, now: 12.2));
        }

        [Test]
        public void HealthyButUnengagedRequestExpiresAndRequiresNewPress()
        {
            var a = new ControlArbiter();
            var c = Healthy();
            c.active = false;
            Assert.IsTrue(a.TryRequest(c, "test", .02, 10));
            Assert.IsFalse(a.Accept(c, "test", .01, 0, 0, false, false, now: 10.1));
            Assert.IsTrue(a.Requested);
            Assert.That(a.Reason, Does.Contain("Waiting"));
            Assert.IsFalse(a.Accept(c, "test", .01, 0, 0, false, false, now: 12));
            Assert.IsFalse(a.Requested);
            Assert.IsFalse(a.Accept(Healthy(), "test", .01, 0, 0, false, false, now: 12.1));
        }

        [Test]
        public void ReadinessRequiresUpstreamEngageableAndFreshSameSession()
        {
            var a = new ControlArbiter();
            var c = Healthy();
            c.engageable = false;
            Assert.IsFalse(a.TryRequest(c, "test", .02, 10));
            c.engageable = true;
            Assert.IsFalse(a.TryRequest(c, "other", .02, 10));
            Assert.IsFalse(a.TryRequest(c, "test", .3, 10));
            Assert.IsFalse(a.TryRequest(null, "test", .01, 10));
        }

        [Test]
        public void HealthRecoveryCannotReengageAfterDisengagement()
        {
            var a = new ControlArbiter();
            a.Request();
            Assert.IsTrue(a.Accept(Healthy(), "test", .01, 0, 0, false, false));
            var bad = Healthy();
            bad.healthy = false;
            Assert.IsFalse(a.Accept(bad, "test", .01, 0, 0, false, false));
            Assert.IsFalse(a.Requested);
            Assert.IsFalse(a.Accept(Healthy(), "test", .01, 0, 0, false, false));
            a.Request();
            Assert.IsTrue(a.Accept(Healthy(), "test", .01, 0, 0, false, false));
        }

        [Test]
        public void BridgeHasContinuousRoadHeight()
        {
            Assert.AreEqual(0, Town.Height(235), .001);
            Assert.AreEqual(4, Town.Height(285), .001);
            Assert.AreEqual(4, Town.Height(315), .001);
            Assert.AreEqual(0, Town.Height(365), .001);
            foreach (float join in new[] { 235f, 285f, 315f, 365f })
            {
                float incoming = (Town.Height(join) - Town.Height(join - .1f)) / .1f;
                float outgoing = (Town.Height(join + .1f) - Town.Height(join)) / .1f;
                Assert.Less(
                    Mathf.Abs(incoming - outgoing),
                    .002f,
                    "Bridge joins must not have sharp grade changes"
                );
            }
        }

        [Test]
        public void LaneTrackIsClosedAndHasFiniteTangents()
        {
            var p = new TrackPath(-200, -200, 0, 0);
            Assert.Less(Vector3.Distance(p.Position(0), p.Position(p.Length)), .001);
            for (float d = 0; d < p.Length; d += 1)
            {
                Assert.AreEqual(1, p.Direction(d).magnitude, .001);
                Assert.Less(Vector3.Distance(p.Position(d), p.Position(d + .1f)), .15);
            }
        }

        [Test]
        public void SeededTrafficRoutesUseConnectedGraphEdgesAndContinuousRightHandLanes()
        {
            var graph = new LaneGraph();
            for (int index = 0; index < 12; index++)
            {
                var nodes = graph.Circuit(21 + index * 103, index);
                Assert.GreaterOrEqual(nodes.Count, 4);
                for (int i = 0; i < nodes.Count; i++)
                    Assert.AreEqual(
                        200,
                        Vector3.Distance(nodes[i], nodes[(i + 1) % nodes.Count]),
                        .001
                    );
                var route = new TrackPath(nodes);
                Assert.Less(
                    Vector3.Distance(route.Position(0), route.Position(route.Length)),
                    .001
                );
                for (float d = 0; d < route.Length; d += 2)
                {
                    Assert.AreEqual(1, route.Direction(d).magnitude, .001);
                    Assert.Less(Vector3.Distance(route.Position(d), route.Position(d + .1f)), .15);
                }
            }
        }

        [Test]
        public void RailGateTracksLocalApproachAndTail()
        {
            var p = new TrackPath(-460, 112, 460, 470, 28);
            float at = p.NearestDistance(new Vector3(0, 0, 110));
            Assert.IsTrue(RailCrossing.Active(p, at - 60, 12, new Vector3(0, 0, 110)));
            Assert.IsTrue(RailCrossing.Active(p, at + 30, 12, new Vector3(0, 0, 110)));
            Assert.IsFalse(RailCrossing.Active(p, at + 60, 12, new Vector3(0, 0, 110)));
            Assert.IsFalse(RailCrossing.Active(p, at, 12, new Vector3(400, 0, 110)));
        }
    }
}
