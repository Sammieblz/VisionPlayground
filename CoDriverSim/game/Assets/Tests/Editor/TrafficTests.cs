using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CoDriver.Tests
{
    public class TrafficTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void EarlierClearApproachWinsEvenWhenLaterQueueUpdatesFirst(bool temporarilyBlocked)
        {
            var town = new GameObject("Fair queue town").AddComponent<Town>();
            var signal = new GameObject("Fair junction").AddComponent<Signal>();
            var occupied = new GameObject("Occupied turn").AddComponent<TrafficActor>();
            var earlier = new GameObject("Earlier approach").AddComponent<TrafficActor>();
            var later = new GameObject("Later approach").AddComponent<TrafficActor>();
            try
            {
                signal.Town = town;
                signal.Uncontrolled = true;
                occupied.transform.position = new Vector3(0, 0, -8);
                earlier.transform.SetPositionAndRotation(
                    new Vector3(-20, 0, 0),
                    Quaternion.Euler(0, 90, 0)
                );
                later.transform.SetPositionAndRotation(
                    new Vector3(0, 0, 20),
                    Quaternion.Euler(0, 180, 0)
                );
                Assert.IsTrue(signal.Reserve(occupied));
                Assert.IsFalse(signal.Reserve(earlier, !temporarilyBlocked));
                Assert.IsFalse(signal.Reserve(later));
                Assert.IsFalse(signal.Reserve(earlier, true));
                occupied.transform.position = new Vector3(0, 0, 40);
                Assert.IsFalse(
                    signal.Reserve(later),
                    "Update order cannot skip the older clear approach"
                );
                Assert.IsTrue(signal.Reserve(earlier));
                earlier.transform.position = new Vector3(40, 0, 0);
                Assert.IsTrue(signal.Reserve(later));
            }
            finally
            {
                Object.DestroyImmediate(later.gameObject);
                Object.DestroyImmediate(earlier.gameObject);
                Object.DestroyImmediate(occupied.gameObject);
                Object.DestroyImmediate(signal.gameObject);
                Object.DestroyImmediate(town.gameObject);
            }
        }

        [TestCase("blocked")]
        [TestCase("red")]
        [TestCase("inactive")]
        public void UnreadyEarlierApproachDoesNotPreventClearTraffic(string reason)
        {
            var town = new GameObject("Available queue town").AddComponent<Town>();
            var signal = new GameObject("Available junction").AddComponent<Signal>();
            var occupied = new GameObject("Occupied turn").AddComponent<TrafficActor>();
            var earlier = new GameObject("Unready approach").AddComponent<TrafficActor>();
            var later = new GameObject("Clear approach").AddComponent<TrafficActor>();
            try
            {
                signal.Town = town;
                signal.Uncontrolled = reason != "red";
                signal.HoldPhase(0, 100);
                occupied.transform.position = new Vector3(0, 0, -8);
                earlier.transform.SetPositionAndRotation(
                    new Vector3(-20, 0, 0),
                    Quaternion.Euler(0, 90, 0)
                );
                later.transform.SetPositionAndRotation(
                    new Vector3(0, 0, 20),
                    Quaternion.Euler(0, 180, 0)
                );
                Assert.IsTrue(signal.Reserve(occupied));
                Assert.IsFalse(signal.Reserve(earlier, reason != "blocked"));
                if (reason == "inactive")
                    earlier.gameObject.SetActive(false);
                Assert.IsFalse(signal.Reserve(later));
                occupied.transform.position = new Vector3(0, 0, 40);
                Assert.IsTrue(
                    signal.Reserve(later),
                    "An unready request must not lock an empty junction"
                );
                if (reason == "blocked")
                {
                    Assert.IsFalse(signal.Reserve(earlier, true));
                    later.transform.position = new Vector3(0, 0, -40);
                    Assert.IsTrue(
                        signal.Reserve(earlier),
                        "The blocked approach can proceed when it clears"
                    );
                }
            }
            finally
            {
                Object.DestroyImmediate(later.gameObject);
                Object.DestroyImmediate(earlier.gameObject);
                Object.DestroyImmediate(occupied.gameObject);
                Object.DestroyImmediate(signal.gameObject);
                Object.DestroyImmediate(town.gameObject);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LongMappedTurnRetainsReservationUntilRearClears(bool red)
        {
            var town = new GameObject("Recorded long junction").AddComponent<Town>();
            var signal = new GameObject("Recorded junction reservation").AddComponent<Signal>();
            var truck = new GameObject("Entering truck").AddComponent<TrafficActor>();
            var car = new GameObject("Conflicting car").AddComponent<TrafficActor>();
            try
            {
                town.Map = MapDefinition.Load("davis-downtown");
                truck.gameObject.AddComponent<BoxCollider>().size = VehicleProfile
                    .ForTraffic("Truck")
                    .collisionSize;
                car.gameObject.AddComponent<BoxCollider>().size =
                    new VehicleProfile().collisionSize;
                signal.Town = town;
                signal.Uncontrolled = !red;
                signal.transform.position = new Vector3(89.19f, 0, -366.16f);
                town.Traffic.Add(truck);
                town.Traffic.Add(car);
                truck.transform.SetPositionAndRotation(
                    new Vector3(130.1272f, .11945f, -356.9485f),
                    Quaternion.LookRotation(new Vector3(-.97878f, 0, -.20478f))
                );
                car.transform.SetPositionAndRotation(
                    new Vector3(66.9994f, -.0224f, -376.9546f),
                    Quaternion.LookRotation(new Vector3(.97239f, 0, .23286f))
                );
                Assert.IsTrue(signal.Reserve(truck));
                truck.transform.SetPositionAndRotation(
                    new Vector3(126.91174f, .11942f, -357.61115f),
                    Quaternion.LookRotation(new Vector3(-.979526f, 0, -.201297f))
                );
                Assert.IsTrue(
                    town.Map.Nearest(
                        truck.transform.position,
                        truck.transform.forward,
                        false
                    ).internalLane,
                    "Recorded truck has crossed onto the junction turn"
                );
                if (red)
                    signal.HoldPhase(40, 100);
                Assert.IsFalse(
                    signal.Reserve(car),
                    "Distance from the junction centre, a new red, or another approach cannot release a committed turn"
                );
                var exit = town.Map.lanes.Single(l => l.id == "10746177#1_0");
                truck.transform.SetPositionAndRotation(
                    exit.Path.Position(10),
                    Quaternion.LookRotation(exit.Path.Direction(10))
                );
                signal.Uncontrolled = true;
                Assert.IsTrue(
                    signal.Reserve(car),
                    "The reservation releases after the rear clears the exit"
                );
            }
            finally
            {
                Object.DestroyImmediate(truck.gameObject);
                Object.DestroyImmediate(car.gameObject);
                Object.DestroyImmediate(signal.gameObject);
                Object.DestroyImmediate(town.gameObject);
            }
        }

        [Test]
        public void CrossingPriorityStopsApproachesButLetsOccupiedCrossingClear()
        {
            var vehicle = new GameObject("crossing bus");
            try
            {
                var body = vehicle.AddComponent<BoxCollider>();
                body.size = new Vector3(2.35f, 2.8f, 10);
                body.center = Vector3.up * 1.5f;
                Assert.AreEqual(
                    2,
                    TrafficClearance.CrossingSpeedLimit(body, new Vector3(1.9f, 0, 3))
                );
                Assert.AreEqual(
                    0,
                    TrafficClearance.CrossingSpeedLimit(body, new Vector3(1.9f, 0, 6))
                );
                Assert.Less(
                    TrafficClearance.CrossingSpeedLimit(body, new Vector3(1.9f, 0, 25)),
                    13.4f,
                    "Brake before a crossing from 30 mph, not only inside a ten metre radius"
                );
                Assert.IsTrue(
                    float.IsPositiveInfinity(
                        TrafficClearance.CrossingSpeedLimit(body, new Vector3(0, -5.8f, 8))
                    ),
                    "An underpass pedestrian does not block an overpass vehicle"
                );
                Assert.IsTrue(
                    float.IsPositiveInfinity(
                        TrafficClearance.CrossingSpeedLimit(body, new Vector3(0, 0, -7))
                    )
                );
                body.size = new Vector3(1.93f, 1.2f, 4.4f);
                Assert.AreEqual(
                    0,
                    TrafficClearance.CrossingSpeedLimit(body, new Vector3(0, 0, 2.496f)),
                    "The front-corner clearance exception must never release a truck into a person directly ahead"
                );
            }
            finally
            {
                Object.DestroyImmediate(vehicle);
            }
        }

        [Test]
        public void CrosswalkAndCommittedTurnCannotBeGrantedTogether()
        {
            var town = new GameObject("crosswalk town").AddComponent<Town>();
            var signal = new GameObject("crosswalk junction").AddComponent<Signal>();
            var car = new GameObject("waiting car").AddComponent<TrafficActor>();
            var walker = new GameObject("crossing person").AddComponent<Walker>();
            try
            {
                signal.Town = town;
                town.Traffic.Add(car);
                town.Walkers.Add(walker);
                car.transform.position = new Vector3(0, 0, -20);
                walker.transform.position = new Vector3(-10, 0, -2);
                walker.Crossing = true;
                Assert.IsFalse(
                    signal.Reserve(car),
                    "A green approach must still yield to a crossing"
                );
                walker.Crossing = false;
                Assert.IsTrue(signal.Reserve(car));
                car.transform.position = new Vector3(0, 0, -6);
                signal.HoldPhase(33, 100);
                Assert.IsFalse(
                    signal.PedestrianGreen,
                    "A committed turn must finish before walkers enter"
                );
                car.transform.position = new Vector3(0, 0, 26);
                Assert.IsTrue(
                    signal.PedestrianGreen,
                    "A stale reservation beyond the junction cannot block walking"
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
        public void QualificationVehicleSharesJunctionWithTrafficAndQueueLeader()
        {
            var town = new GameObject("qualification town").AddComponent<Town>();
            var signal = new GameObject("qualification junction").AddComponent<Signal>();
            var car = new GameObject("oncoming traffic").AddComponent<TrafficActor>();
            var vehicle = new GameObject("qualification vehicle").AddComponent<Vehicle>();
            try
            {
                Art.Initialize();
                vehicle.Initialize();
                town.Player = vehicle.transform;
                town.Traffic.Add(car);
                signal.Town = town;
                signal.HoldPhase(0, 100);
                car.transform.SetPositionAndRotation(
                    new Vector3(0, 0, 20),
                    Quaternion.Euler(0, 180, 0)
                );
                vehicle.transform.position = new Vector3(0, 0, -20);
                Assert.IsTrue(signal.Reserve(car));
                Assert.IsFalse(
                    signal.Reserve(vehicle),
                    "Harness must wait for an oncoming reservation"
                );
                car.transform.position = new Vector3(30, 0, 0);
                Assert.IsTrue(signal.Reserve(vehicle));
                vehicle.transform.position = new Vector3(0, 0, -5);
                car.transform.SetPositionAndRotation(
                    new Vector3(-20, 0, 0),
                    Quaternion.Euler(0, 90, 0)
                );
                Assert.IsFalse(signal.Reserve(car), "Harness retains the junction while turning");
                vehicle.transform.position = new Vector3(0, 0, 26);
                Assert.IsTrue(signal.Reserve(car));
                signal.ResetPhase();
                vehicle.transform.position = new Vector3(0, 0, -16);
                car.transform.SetPositionAndRotation(new Vector3(0, 0, -21), Quaternion.identity);
                Assert.IsFalse(
                    signal.Reserve(car),
                    "NPC cannot reserve past a queued human vehicle"
                );
            }
            finally
            {
                Object.DestroyImmediate(vehicle.gameObject);
                Object.DestroyImmediate(car.gameObject);
                Object.DestroyImmediate(signal.gameObject);
                Object.DestroyImmediate(town.gameObject);
            }
        }

        [Test]
        public void StreetcarSharesQueuePriorityAndRetainsItsJunctionUntilClear()
        {
            var town = new GameObject("tram queue town").AddComponent<Town>();
            var signal = new GameObject("tram queue signal").AddComponent<Signal>();
            var car = new GameObject("queue car").AddComponent<TrafficActor>();
            var tram = new GameObject("queue tram").AddComponent<RailVehicle>();
            try
            {
                signal.Town = town;
                town.Tram = tram;
                town.Traffic.Add(car);
                tram.Streetcar = true;
                tram.gameObject.AddComponent<BoxCollider>().size = new Vector3(2.65f, 3, 13.5f);
                car.gameObject.AddComponent<BoxCollider>().size = new Vector3(1.8f, 1.5f, 4.4f);
                signal.HoldPhase(0, 100);
                tram.transform.position = new Vector3(0, 0, -21);
                car.transform.position = new Vector3(0, 0, -8);
                Assert.IsFalse(signal.Reserve(tram), "Streetcar must not lock out a car ahead");
                car.transform.position = new Vector3(0, 0, 26);
                Assert.IsTrue(signal.Reserve(tram));
                tram.transform.position = new Vector3(0, 0, -17);
                signal.HoldPhase(20, 100);
                car.transform.SetPositionAndRotation(
                    new Vector3(-20, 0, 0),
                    Quaternion.Euler(0, 90, 0)
                );
                Assert.IsFalse(
                    signal.Reserve(car),
                    "Changing light cannot release an entering streetcar"
                );
                tram.transform.position = new Vector3(0, 0, 26);
                Assert.IsTrue(signal.Reserve(car));
                signal.ResetPhase();
                signal.HoldPhase(0, 100);
                car.transform.SetPositionAndRotation(new Vector3(0, 0, -21), Quaternion.identity);
                tram.transform.position = new Vector3(0, 0, -10);
                Assert.IsFalse(
                    signal.Reserve(car),
                    "Rear car must not lock out the leading streetcar"
                );
                Assert.IsTrue(signal.Reserve(tram));
            }
            finally
            {
                Object.DestroyImmediate(car.gameObject);
                Object.DestroyImmediate(tram.gameObject);
                Object.DestroyImmediate(signal.gameObject);
                Object.DestroyImmediate(town.gameObject);
            }
        }

        [Test]
        public void StreetcarStopsBeforeStationaryTrafficWithItsOwnBrakingLimit()
        {
            var route = new TrackPath(0, 0, 200, 200, 24);
            var tram = new GameObject("braking streetcar");
            var car = new GameObject("stopped traffic");
            try
            {
                tram.layer = TrafficClearance.RailLayer;
                car.layer = TrafficClearance.TrafficLayer;
                var body = tram.AddComponent<BoxCollider>();
                body.size = new Vector3(2.65f, 3, 13.5f);
                body.center = Vector3.up * 1.9f;
                var other = car.AddComponent<BoxCollider>();
                other.size = new Vector3(1.8f, 1.5f, 4.4f);
                other.center = Vector3.up * .85f;
                car.transform.SetPositionAndRotation(
                    route.Position(100) + Vector3.up * .1f,
                    Quaternion.LookRotation(route.Direction(100))
                );
                float distance = 50,
                    speed = 7;
                for (int i = 0; i < 800; i++)
                {
                    tram.transform.SetPositionAndRotation(
                        route.Position(distance),
                        Quaternion.LookRotation(route.Direction(distance))
                    );
                    Physics.SyncTransforms();
                    Assert.IsFalse(
                        Physics.ComputePenetration(
                            body,
                            tram.transform.position,
                            tram.transform.rotation,
                            other,
                            car.transform.position,
                            car.transform.rotation,
                            out _,
                            out _
                        )
                    );
                    float target = Mathf.Min(
                        7,
                        TrafficClearance.SafeSpeed(route, distance, 0, speed, body, 2)
                    );
                    speed = Mathf.MoveTowards(speed, target, .02f * (target < speed ? 2 : 1));
                    distance += speed * .02f;
                }
                Assert.Less(speed, .1f);
                Assert.Less(distance, 90, "Streetcar nose must stop short of the car's rear");
            }
            finally
            {
                Object.DestroyImmediate(tram);
                Object.DestroyImmediate(car);
            }
        }

        [Test]
        public void IntersectionReservationCoversApproachTurnAndExit()
        {
            var town = new GameObject("reservation town").AddComponent<Town>();
            var signal = new GameObject("reservation signal").AddComponent<Signal>();
            var first = new GameObject("first vehicle").AddComponent<TrafficActor>();
            var second = new GameObject("second vehicle").AddComponent<TrafficActor>();
            try
            {
                signal.Town = town;
                signal.HoldPhase(0, 100);
                first.transform.position = new Vector3(0, 0, -20);
                second.transform.position = new Vector3(0, 0, 20);
                second.transform.rotation = Quaternion.Euler(0, 180, 0);
                Assert.IsTrue(signal.Reserve(first));
                Assert.IsFalse(
                    signal.Reserve(second),
                    "Approaching holder must retain the junction"
                );
                first.transform.position = Vector3.zero;
                Assert.IsFalse(signal.Reserve(second), "Occupied junction cannot be reassigned");
                first.transform.SetPositionAndRotation(
                    new Vector3(25, 0, 0),
                    Quaternion.Euler(0, 90, 0)
                );
                Assert.IsTrue(
                    signal.Reserve(second),
                    "Reservation releases after a turning vehicle clears"
                );
                second.transform.SetPositionAndRotation(
                    new Vector3(100, 0, 100),
                    Quaternion.LookRotation(new Vector3(-1, 0, -1))
                );
                first.transform.SetPositionAndRotation(new Vector3(0, 0, -20), Quaternion.identity);
                Assert.IsTrue(
                    signal.Reserve(first),
                    "An old owner turning elsewhere cannot retain a distant junction"
                );
            }
            finally
            {
                Object.DestroyImmediate(first.gameObject);
                Object.DestroyImmediate(second.gameObject);
                Object.DestroyImmediate(signal.gameObject);
                Object.DestroyImmediate(town.gameObject);
            }
        }

        [Test]
        public void JunctionServesQueueHeadEvenWhenRearVehicleUpdatesFirst()
        {
            var town = new GameObject("queued town").AddComponent<Town>();
            var signal = new GameObject("queued signal").AddComponent<Signal>();
            var rear = new GameObject("rear truck").AddComponent<TrafficActor>();
            var front = new GameObject("front bus").AddComponent<TrafficActor>();
            try
            {
                signal.Town = town;
                signal.HoldPhase(0, 100);
                town.Traffic.Add(rear);
                town.Traffic.Add(front);
                rear.transform.position = new Vector3(0, 0, -21);
                front.transform.position = new Vector3(0, 0, -16);
                Assert.IsFalse(
                    signal.Reserve(rear),
                    "Rear vehicle must not lock out its queue leader"
                );
                Assert.IsTrue(signal.Reserve(front));
                Assert.IsFalse(signal.Reserve(rear));
                front.transform.position = new Vector3(0, 0, 25);
                Assert.IsTrue(signal.Reserve(rear), "Queue advances after the leader clears");
                signal.ResetPhase();
                rear.transform.position = new Vector3(0, 0, -40);
                Assert.IsFalse(
                    signal.Reserve(rear),
                    "Distant approaches cannot reserve a junction"
                );
            }
            finally
            {
                Object.DestroyImmediate(rear.gameObject);
                Object.DestroyImmediate(front.gameObject);
                Object.DestroyImmediate(signal.gameObject);
                Object.DestroyImmediate(town.gameObject);
            }
        }

        [TestCase(10f)]
        [TestCase(4.4f)]
        public void AdjacentQueueLeaderClearsBeforePassingEmergencyTurns(float length)
        {
            var town = new GameObject("bus queue town").AddComponent<Town>();
            var signal = new GameObject("bus queue signal").AddComponent<Signal>();
            var bus = new GameObject("leading bus").AddComponent<TrafficActor>();
            var emergency = new GameObject("passing emergency").AddComponent<TrafficActor>();
            try
            {
                signal.Town = town;
                signal.HoldPhase(0, 100);
                town.Traffic.Add(bus);
                town.Traffic.Add(emergency);
                bus.gameObject.AddComponent<BoxCollider>().size = new Vector3(2.3f, 2.6f, length);
                emergency.gameObject.AddComponent<BoxCollider>().size = new Vector3(
                    2.3f,
                    2.6f,
                    4.4f
                );
                bus.transform.position = new Vector3(1.5f, 0, -17);
                emergency.transform.position = new Vector3(-1.05f, 0, -15.6f);
                emergency.Dispatching = true;
                Assert.IsFalse(signal.Reserve(emergency));
                Assert.IsTrue(signal.Reserve(bus));
                bus.transform.position = new Vector3(1.5f, 0, 26);
                Assert.IsTrue(signal.Reserve(emergency));
            }
            finally
            {
                Object.DestroyImmediate(bus.gameObject);
                Object.DestroyImmediate(emergency.gameObject);
                Object.DestroyImmediate(signal.gameObject);
                Object.DestroyImmediate(town.gameObject);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void QueueLeaderCanClearTurnBesideWaitingEmergencyVehicle(bool isBus)
        {
            var route = isBus
                ? new TrackPath(-200, -200, 0, 0)
                : new TrackPath(new LaneGraph().Circuit(21, 0));
            var emergencyRoute = new TrackPath(-200, -200, 0, 0);
            var bus = new GameObject("turning bus");
            var emergency = new GameObject("waiting emergency");
            try
            {
                bus.layer = emergency.layer = TrafficClearance.TrafficLayer;
                var body = bus.AddComponent<BoxCollider>();
                body.size = new Vector3(2.3f, 2.6f, isBus ? 10 : 4.4f);
                body.center = Vector3.up * 1.4f;
                var other = emergency.AddComponent<BoxCollider>();
                other.size = new Vector3(2.3f, 2.6f, 4.4f);
                other.center = body.center;
                float distance = route.NearestDistance(new Vector3(-202, 0, -183));
                float emergencyDistance = emergencyRoute.NearestDistance(
                    new Vector3(-202, 0, -184.4f)
                );
                Vector3 ef = emergencyRoute.Direction(emergencyDistance);
                emergency.transform.SetPositionAndRotation(
                    emergencyRoute.Position(emergencyDistance)
                        + Vector3.Cross(Vector3.up, ef) * -1.05f
                        + Vector3.up * .1f,
                    Quaternion.LookRotation(ef)
                );
                float initial = distance,
                    speed = 0;
                for (int step = 0; step < 1500 && distance - initial < 30; step++)
                {
                    Vector3 forward = route.Direction(distance);
                    bus.transform.SetPositionAndRotation(
                        route.Position(distance)
                            + Vector3.Cross(Vector3.up, forward) * 1.5f
                            + Vector3.up * .1f,
                        Quaternion.LookRotation(forward)
                    );
                    Physics.SyncTransforms();
                    float target = Mathf.Min(
                        2,
                        TrafficClearance.SafeSpeed(route, distance, 1.5f, speed, body)
                    );
                    speed = Mathf.MoveTowards(speed, target, .02f * (target < speed ? 4 : 1.8f));
                    distance += speed * .02f;
                }
                Assert.Greater(
                    distance - initial,
                    25,
                    "Queue leader must physically clear the waiting vehicle"
                );
            }
            finally
            {
                Object.DestroyImmediate(bus);
                Object.DestroyImmediate(emergency);
            }
        }

        [Test]
        public void UphillRoadSupportsTrafficWithoutBeingAnObstacle()
        {
            var car = new GameObject("bridge approach");
            var ramp = new GameObject("inclined road");
            var wall = new GameObject("road barrier");
            try
            {
                var body = car.AddComponent<BoxCollider>();
                body.size = new Vector3(1.8f, 1.5f, 4.4f);
                body.center = Vector3.up;
                ramp.layer = TrafficClearance.RoadLayer;
                ramp.transform.SetPositionAndRotation(
                    new Vector3(0, 1, 15),
                    Quaternion.Euler(-10, 0, 0)
                );
                ramp.AddComponent<BoxCollider>().size = new Vector3(9, .2f, 30);
                wall.transform.position = new Vector3(0, 1, 80);
                wall.AddComponent<BoxCollider>().size = new Vector3(4, 2, 1);
                Physics.SyncTransforms();
                Assert.IsTrue(
                    float.IsPositiveInfinity(TrafficClearance.ForwardSpeedLimit(body, 10))
                );
                wall.transform.position = new Vector3(0, 1, 10);
                Physics.SyncTransforms();
                Assert.Less(
                    TrafficClearance.ForwardSpeedLimit(body, 10),
                    5,
                    "Ignoring support surfaces must not hide an actual barrier"
                );
            }
            finally
            {
                Object.DestroyImmediate(car);
                Object.DestroyImmediate(ramp);
                Object.DestroyImmediate(wall);
            }
        }

        [Test]
        public void FastEmergencyVehicleCanStopBehindStationaryBus()
        {
            var route = new TrackPath(0, 0, 200, 200, 24);
            var emergency = new GameObject("test emergency");
            var bus = new GameObject("test stopped bus");
            try
            {
                emergency.layer = bus.layer = TrafficClearance.TrafficLayer;
                var body = emergency.AddComponent<BoxCollider>();
                body.size = new Vector3(2.3f, 2.6f, 4.4f);
                body.center = Vector3.up * 1.4f;
                var obstacle = bus.AddComponent<BoxCollider>();
                obstacle.size = new Vector3(2.3f, 2.6f, 10);
                obstacle.center = body.center;
                bus.transform.SetPositionAndRotation(
                    route.Position(100) + Vector3.up * .1f,
                    Quaternion.LookRotation(route.Direction(100))
                );
                float progress = 50,
                    speed = 16;
                for (int step = 0; step < 1000; step++)
                {
                    emergency.transform.SetPositionAndRotation(
                        route.Position(progress) + Vector3.up * .1f,
                        Quaternion.LookRotation(route.Direction(progress))
                    );
                    Physics.SyncTransforms();
                    Assert.IsFalse(
                        Physics.ComputePenetration(
                            body,
                            emergency.transform.position,
                            emergency.transform.rotation,
                            obstacle,
                            bus.transform.position,
                            bus.transform.rotation,
                            out _,
                            out _
                        ),
                        "Traffic braking failed before reaching a stopped bus"
                    );
                    float target = Mathf.Min(
                        16,
                        TrafficClearance.SafeSpeed(route, progress, 0, speed, body)
                    );
                    speed = Mathf.MoveTowards(speed, target, .02f * (target < speed ? 4 : 1.8f));
                    progress += speed * .02f;
                }
                Assert.Less(speed, .1f);
            }
            finally
            {
                Object.DestroyImmediate(emergency);
                Object.DestroyImmediate(bus);
            }
        }

        [TestCase(TrafficClearance.PlayerLayer)]
        [TestCase(TrafficClearance.RailLayer)]
        [TestCase(0)]
        public void TrafficSeesVehicleOnCurvedLaneBeforeContact(int layer)
        {
            var route = new TrackPath(0, 0, 100, 100, 24);
            var ego = new GameObject("test traffic");
            var obstacle = new GameObject("test player");
            try
            {
                ego.layer = TrafficClearance.TrafficLayer;
                obstacle.layer = layer;
                var body = ego.AddComponent<BoxCollider>();
                body.center = Vector3.up * .85f;
                body.size = new Vector3(1.8f, 1.5f, 4.4f);
                var other = obstacle.AddComponent<BoxCollider>();
                other.center = body.center;
                other.size = body.size;
                ego.transform.SetPositionAndRotation(
                    route.Position(0) + Vector3.up * .1f,
                    Quaternion.LookRotation(route.Direction(0))
                );
                obstacle.transform.SetPositionAndRotation(
                    route.Position(17) + Vector3.up * .1f,
                    Quaternion.LookRotation(route.Direction(17))
                );
                Physics.SyncTransforms();
                Assert.Less(
                    TrafficClearance.SafeSpeed(route, 0, 0, 11, body, staticObstacles: layer == 0),
                    8
                );
                obstacle.transform.position += Vector3.up * 8;
                Physics.SyncTransforms();
                Assert.IsTrue(
                    float.IsPositiveInfinity(
                        TrafficClearance.SafeSpeed(
                            route,
                            0,
                            0,
                            11,
                            body,
                            staticObstacles: layer == 0
                        )
                    )
                );
            }
            finally
            {
                Object.DestroyImmediate(ego);
                Object.DestroyImmediate(obstacle);
            }
        }
    }
}
