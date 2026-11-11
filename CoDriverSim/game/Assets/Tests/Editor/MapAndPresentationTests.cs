using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CoDriver.Tests
{
    public sealed class MapAndPresentationTests
    {
        [Test]
        public void DavisRailwayGatePedestalsLeaveMappedDrivingEnvelopesClear()
        {
            var map = MapDefinition.Load("davis-downtown");
            int checkedGates = 0;
            foreach (var crossing in map.landmarks.Where(l => l.subtype == "level_crossing"))
            {
                var approach = map.Nearest(crossing.position, routable: false);
                var forward = approach.Path.Direction(
                    approach.Path.NearestDistance(crossing.position)
                );
                foreach (int side in new[] { -1, 1 })
                {
                    var point = DavisWorld.RailwayGatePosition(map, crossing, forward, side);
                    checkedGates++;
                    // Estimate placements from the road corridor, retaining a
                    // roadside base and an arm before the source crossing.
                    float before = Vector3.Dot(crossing.position - point, forward * side);
                    Assert.That(before, Is.InRange(4f, 12f), crossing.sourceId);
                    foreach (var lane in map.lanes)
                    {
                        var centre = lane.Path.Position(lane.Path.NearestDistance(point));
                        if (Mathf.Abs(centre.y - point.y) > 2)
                            continue;
                        float clearance =
                            Vector3.ProjectOnPlane(centre - point, Vector3.up).magnitude
                            - lane.width * .5f;
                        Assert.GreaterOrEqual(
                            clearance,
                            .7f,
                            $"{crossing.sourceId} side={side}: pedestal at {point} obstructs {lane.id}"
                        );
                    }
                }
            }
            Assert.AreEqual(12, checkedGates);
        }

        [Test]
        public void DavisConnectionsAreContinuousAndKeepTheirSourceIdentity()
        {
            var map = MapDefinition.Load("davis-downtown");
            Assert.Greater(map.lanes.Length, 100);
            Assert.IsTrue(map.Contains(map.Spawn, 20));
            foreach (var lane in map.lanes)
            {
                Assert.IsNotEmpty(lane.sourceId);
                foreach (string next in lane.next)
                {
                    Assert.IsTrue(map.LaneById.ContainsKey(next), lane.id + " -> " + next);
                    float gap = Vector3.Distance(
                        lane.points.Last(),
                        map.LaneById[next].points.First()
                    );
                    Assert.Less(gap, 1.5f, lane.id + " -> " + next + " has a navigation gap");
                }
            }
        }

        [Test]
        public void ActivityBayApproachesFitOnOneStreetOutsideJunctions()
        {
            var map = MapDefinition.Load("davis-downtown");
            Assert.GreaterOrEqual(map.parkingAccessBayIds.Length, 3);
            foreach (string id in map.parkingAccessBayIds)
            {
                var bay = map.landmarks.Single(l => l.sourceId == id);
                var lane = map.Nearest(bay.position);
                Assert.IsFalse(lane.internalLane, id);
                float along = lane.Path.NearestDistance(bay.position);
                Assert.GreaterOrEqual(along, 35, id);
                Assert.GreaterOrEqual(lane.Path.Length - along, 20, id);
            }
        }

        [Test]
        public void RoutingHonoursVehicleEnvelopeAndTemporaryBlockages()
        {
            var map = MapDefinition.Load("davis-downtown");
            var starts = map
                .lanes.Where(l => !l.internalLane && !l.boundary)
                .Where((l, i) => i % 20 == 0)
                .ToArray();
            int checkedRoutes = 0;
            foreach (bool bus in new[] { false, true })
            foreach (var start in starts)
            foreach (var finish in starts.Take(8))
            {
                var route = map.Route(start, finish, bus);
                Assert.IsTrue(route.Skip(1).All(l => map.FitsVehicle(l.id, bus)));
                if (route.Count < 5)
                    continue;
                checkedRoutes++;
                string blocked = route[route.Count / 2].id;
                var alternative = map.Route(
                    start,
                    finish,
                    bus,
                    new System.Collections.Generic.HashSet<string> { blocked }
                );
                Assert.IsFalse(alternative.Any(l => l.id == blocked));
            }
            Assert.Greater(checkedRoutes, 30);
        }

        [Test]
        public void EmergencyYieldKeepsTheCompleteVehicleInsideItsLaneAndOutOfJunctions()
        {
            var lane = new MapLane
            {
                width = 3.2f,
                Path = new TrackPath(new[] { Vector3.zero, Vector3.forward * 100 }, false),
            };
            float offset = TrafficClearance.YieldOffset(lane, 40, 1.82f, 4.4f);
            Assert.Greater(offset, 0);
            Assert.LessOrEqual(offset + 1.82f / 2, lane.width / 2 - .2f);
            Assert.AreEqual(0, TrafficClearance.YieldOffset(lane, 98, 1.82f, 4.4f));
            lane.internalLane = true;
            Assert.AreEqual(0, TrafficClearance.YieldOffset(lane, 40, 1.82f, 4.4f));
        }

        [Test]
        public void RichardsTunnelIsBelowTheRailDeckAndGpsUsesDavisProjection()
        {
            var map = MapDefinition.Load("davis-downtown");
            var tunnel = map.roads.Where(r => r.sourceId == "way/25313910").ToArray();
            Assert.IsNotEmpty(tunnel);
            foreach (var road in tunnel)
            {
                Assert.IsTrue(road.tunnel);
                foreach (string lane in road.laneIds)
                    Assert.Less(map.LaneById[lane].points.Min(p => p.y), -5);
            }
            map.Gps(Vector3.zero, out double latitude, out double longitude);
            Assert.AreEqual(38.5453, latitude, 1e-7);
            Assert.AreEqual(-121.74165, longitude, 1e-7);
            map.Gps(new Vector3(100, 0, 100), out latitude, out longitude);
            Assert.Greater(latitude, 38.5453);
            Assert.Greater(longitude, -121.74165);
        }

        [Test]
        public void MapProjectionRoundTripsAndNorthMovesLeftWhenHeadingEast()
        {
            var go = new GameObject("Map", typeof(RectTransform));
            try
            {
                var map = go.AddComponent<MapGraphic>();
                map.rectTransform.sizeDelta = new Vector2(400, 400);
                map.Range = 100;
                map.Heading = 90;
                map.Centre = new Vector3(20, 0, 30);
                var target = new Vector3(65, 0, 80);
                Assert.Less(Vector3.Distance(target, map.Unproject(map.Project(target))), .001f);
                Assert.Less(map.Project(map.Centre + Vector3.forward * 20).x, 0);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void PresentationCannotCommandAndGeometryRequiresTheExactCapturedFrame()
        {
            string[] forbidden =
            {
                "steering_deg",
                "acceleration_mps2",
                "throttle",
                "brake",
                "torque",
            };
            Assert.IsFalse(
                typeof(OpenpilotUiSnapshot).GetFields().Any(f => forbidden.Contains(f.Name))
            );
            Assert.IsFalse(
                typeof(ObserverResult)
                    .GetFields()
                    .Any(f =>
                        f.FieldType == typeof(MapDefinition) || f.FieldType == typeof(CommaCommand)
                    )
            );
            var ui = new OpenpilotUiSnapshot
            {
                schema_version = 1,
                session_id = "test",
                timestamp = 10,
                road = new UiModelGeometry
                {
                    schema_version = 2,
                    session_id = "test",
                    frame_id = 42,
                    timestamp = 10,
                },
            };
            Assert.IsTrue(ui.GeometryFresh("test", 42, 10.1));
            Assert.IsTrue(ui.GeometryFresh("test", 42, 10.49));
            Assert.IsFalse(ui.GeometryFresh("test", 42, 10.51));
            Assert.IsFalse(ui.GeometryFresh("test", 43, 10.1));
            Assert.IsFalse(ui.GeometryFresh("another", 42, 10.1));
            Assert.IsFalse(ui.GeometryFresh("test", 42, 10.6));
            ui.road.timestamp = 11;
            Assert.IsFalse(ui.GeometryFresh("test", 42, 10.1));
        }
    }
}
