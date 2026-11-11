using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace CoDriver
{
    // This log is simulator truth for world qualification. It never enters SensorLink.
    public sealed class WorldDiagnostics : MonoBehaviour
    {
        CoDriverApp app;
        StreamWriter writer;
        float next;

        public void Initialize(CoDriverApp owner)
        {
            app = owner;
            try
            {
                string dir = Path.Combine(app.Root, "outputs", "sessions", app.Session);
                Directory.CreateDirectory(dir);
                writer = new StreamWriter(Path.Combine(dir, "world-activities.jsonl"));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning(e.Message);
            }
        }

        void Update()
        {
            if (app == null || Time.unscaledTime < next)
                return;
            next = Time.unscaledTime + 1;
            var town = app.Town;
            var row = new Snapshot
            {
                timestamp = Time.time,
                monotonic_timestamp = Time.realtimeSinceStartupAsDouble,
                player_position = app.Vehicle.transform.position,
                bus = town.Bus.State,
                bus_stop_visits = town.Bus.StopVisits,
                bus_boarding = town.Bus.BoardingUsers,
                train_stop_visits = town.Train.StopVisits,
                tram_stop_visits = town.Tram.StopVisits,
                train_boarding = town.Train.BoardingUsers,
                tram_boarding = town.Tram.BoardingUsers,
                emergency = town.Emergency.Activity,
                parking = town.ParkingCar.State,
                parking_visits = town.ParkingCar.ParkingVisits,
                train = town.Train.State,
                tram = town.Tram.gameObject.activeInHierarchy
                    ? town.Tram.State
                    : "Not present on this map",
                construction = town.Construction.State,
                people = town.Walkers.Select(w => w.Species + ":" + w.State).ToArray(),
                traffic = town.Traffic.Select(t => t.Kind + ":" + t.State).ToArray(),
                vehicle_positions = town.Traffic.Select(t => t.transform.position).ToArray(),
                vehicle_forwards = town.Traffic.Select(t => t.transform.forward).ToArray(),
                vehicle_speeds = town.Traffic.Select(t => t.Speed).ToArray(),
                signed_vehicle_speeds = town.Traffic.Select(t => t.SignedSpeed).ToArray(),
                reverse_requested = town.Traffic.Select(t => t.ReverseRequested).ToArray(),
                nearby_people = town.Walkers.Count(w =>
                    !w.IsAnimal
                    && (w.transform.position - app.Vehicle.transform.position).sqrMagnitude <= 40000
                ),
                nearby_moving_vehicles = town.Traffic.Count(t =>
                    t.Speed > .5f
                    && (t.transform.position - app.Vehicle.transform.position).sqrMagnitude <= 40000
                ),
                requested_speeds = town.Traffic.Select(t => t.RequestedSpeed).ToArray(),
                path_errors = town.Traffic.Select(t => t.PathError).ToArray(),
                contacts = town.Traffic.Select(t => t.Contact).ToArray(),
                clearances = town.Traffic.Select(t => t.Clearance).ToArray(),
                grounded = town.Traffic.Select(t => t.Grounded).ToArray(),
                actor_positions = town.Walkers.Select(w => w.transform.position).ToArray(),
                blocked_walkers = town
                    .Walkers.Where(w => w.Crossing && w.ActualSpeed < .05f)
                    .Select(w =>
                        w.NavigationDetail + " " + w.GetComponent<ActorLocomotion>().Diagnostic
                    )
                    .ToArray(),
                traffic_overlaps = TrafficOverlaps(town),
                rail_overlaps = RailOverlaps(town),
                train_position = town.Train.transform.position,
                tram_position = town.Tram.transform.position,
                train_speed = town.Train.Speed,
                tram_speed = town.Tram.Speed,
                junction_reservations = town
                    .Signals.Select(s => s.transform.position.ToString() + ":" + s.Reservation)
                    .ToArray(),
            };
            try
            {
                writer?.WriteLine(JsonUtility.ToJson(row));
                writer?.Flush();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning(e.Message);
                writer?.Dispose();
                writer = null;
            }
        }

        void OnDestroy()
        {
            writer?.Dispose();
        }

        static string[] TrafficOverlaps(Town town)
        {
            var pairs = new System.Collections.Generic.List<string>();
            for (int i = 0; i < town.Traffic.Count; i++)
            for (int j = i + 1; j < town.Traffic.Count; j++)
            {
                var a = town.Traffic[i];
                var b = town.Traffic[j];
                if ((a.transform.position - b.transform.position).sqrMagnitude > 225)
                    continue;
                var ac = a.GetComponent<BoxCollider>();
                var bc = b.GetComponent<BoxCollider>();
                if (
                    Physics.ComputePenetration(
                        ac,
                        a.transform.position,
                        a.transform.rotation,
                        bc,
                        b.transform.position,
                        b.transform.rotation,
                        out _,
                        out float depth
                    )
                    && depth > .15f
                )
                    pairs.Add(
                        i + ":" + a.Kind + "/" + j + ":" + b.Kind + " depth=" + depth.ToString("F2")
                    );
            }
            return pairs.ToArray();
        }

        [Serializable]
        class Snapshot
        {
            public float[] requested_speeds,
                path_errors;
            public string[] contacts,
                clearances;
            public int[] grounded;

            // These measurements share this activity sample. Do not join scaled
            // simulation time to DrivingDiagnostics' real elapsed time.
            public double monotonic_timestamp;
            public Vector3 player_position,
                train_position,
                tram_position;
            public float train_speed,
                tram_speed;
            public string[] rail_overlaps;
            public float timestamp;
            public string bus,
                emergency,
                parking,
                train,
                tram,
                construction;
            public int bus_stop_visits,
                bus_boarding;
            public int parking_visits;
            public int train_stop_visits,
                tram_stop_visits,
                train_boarding,
                tram_boarding;
            public string[] people,
                traffic;
            public Vector3[] vehicle_positions,
                vehicle_forwards,
                actor_positions;
            public float[] vehicle_speeds;
            public float[] signed_vehicle_speeds;
            public bool[] reverse_requested;
            public int nearby_people,
                nearby_moving_vehicles;
            public string[] traffic_overlaps;
            public string[] blocked_walkers;
            public string[] junction_reservations;
        }

        static string[] RailOverlaps(Town town)
        {
            var overlaps = new System.Collections.Generic.List<string>();
            foreach (var rail in new[] { town.Train, town.Tram })
                for (int i = 0; i < town.Traffic.Count; i++)
                {
                    var actor = town.Traffic[i];
                    if (!rail.gameObject.activeInHierarchy || !actor.gameObject.activeInHierarchy)
                        continue;
                    var body = actor.GetComponent<BoxCollider>();
                    foreach (var other in rail.CollisionBodies)
                    {
                        if (
                            !other
                            || !other.enabled
                            || other.isTrigger
                            || !body.bounds.Intersects(other.bounds)
                        )
                            continue;
                        if (
                            Physics.ComputePenetration(
                                body,
                                actor.transform.position,
                                actor.transform.rotation,
                                other,
                                other.transform.position,
                                other.transform.rotation,
                                out _,
                                out float depth
                            )
                            && depth > .15f
                        )
                        {
                            overlaps.Add(
                                i
                                    + ":"
                                    + actor.Kind
                                    + "/"
                                    + (rail.Streetcar ? "Streetcar" : "Train")
                                    + " depth="
                                    + depth.ToString("F2")
                            );
                            break;
                        }
                    }
                }
            return overlaps.ToArray();
        }
    }
}
