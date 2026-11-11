using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace CoDriver
{
    [Serializable]
    public sealed class DrivingCriterion
    {
        public string name,
            evidence = "Not yet observed";
        public bool passed,
            critical,
            applicable = true,
            unavoidable;
    }

    public static class ScenarioRules
    {
        public static float Percentage(IEnumerable<DrivingCriterion> criteria)
        {
            var relevant = criteria.Where(c => c.applicable && !c.unavoidable).ToArray();
            return relevant.Length == 0
                ? -1
                : 100f * relevant.Count(c => c.passed) / relevant.Length;
        }

        public static bool CriticalFailure(IEnumerable<DrivingCriterion> criteria) =>
            criteria.Any(c => c.applicable && !c.unavoidable && c.critical && !c.passed);

        // Test the leading bumper crossing the line, not merely being in a red junction.
        public static bool CrossedLine(float previousAhead, float ahead, float line = 13) =>
            previousAhead > line && ahead <= line && ahead > 0;

        public static bool InsideCourse(Vector3 position, float routeX) =>
            Mathf.Abs(position.x - routeX) <= 6;
    }

    [Serializable]
    public sealed class ComparisonControlEvidence
    {
        public float human_seconds,
            comma_seconds,
            human_moving_seconds,
            comma_moving_seconds;

        public void Record(float seconds, Vector3 velocity, bool commaApplied)
        {
            // Suspension settling at scenario reset is not manual driving.
            // Measure travel across the ground, excluding vertical fall/bounce.
            float speed = new Vector2(velocity.x, velocity.z).magnitude;
            if (commaApplied)
            {
                comma_seconds += seconds;
                if (speed > .5f)
                    comma_moving_seconds += seconds;
            }
            else
            {
                human_seconds += seconds;
                if (speed > .5f)
                    human_moving_seconds += seconds;
            }
        }

        public bool Matches(string requestedMode) =>
            requestedMode == "comma"
                ? comma_moving_seconds > 0 && human_moving_seconds == 0
                : (requestedMode == "human_ai" || requestedMode == "human_muted")
                    && comma_seconds == 0;
    }

    // This director and evaluator are simulation-only. Neither is reachable through
    // SensorLink, ObserverResult, or the portable Python observer.
    public sealed class ScenarioEvaluator
    {
        readonly string root,
            session;
        readonly Action<string> startCapture;
        readonly Action stopCapture;
        string captureId;
        double captureStart;
        readonly List<DrivingCriterion> criteria = new List<DrivingCriterion>();
        readonly List<Evidence> events = new List<Evidence>();
        StreamWriter actions;
        Town town;
        Vehicle vehicle;
        TrafficActor hazard;
        Walker crossing;
        Signal signal;
        string name,
            mode,
            directory;
        float started,
            lastSample,
            eventAt,
            finishZ,
            startZ,
            routeX,
            stopSeconds,
            speedingSeconds;
        float previousAhead,
            travelled;
        int baselineCollisions,
            seed;
        Vector3 previousPosition;
        ComparisonControlEvidence controls = new ComparisonControlEvidence();
        string lastAuthority;
        MappedScenarioCourse mappedCourse;
        string speedCriterion;
        float speedLimit = 30 * .44704f;

        Vector3 World(Vector3 local) => mappedCourse == null ? local : mappedCourse.World(local);

        Vector3 Local(Vector3 world) => mappedCourse == null ? world : mappedCourse.Local(world);

        float SignalLineZ =>
            mappedCourse == null
                ? signal.transform.position.z
                : Local(mappedCourse.StopLine).z + 13;

        bool SignalGreen =>
            mappedCourse == null
                ? signal.Green(true)
                : signal.Allows(vehicle.transform.position, vehicle.transform.forward);
        bool SignalYellow =>
            mappedCourse == null
                ? signal.Yellow(true)
                : signal.IsYellowFor(vehicle.transform.position, vehicle.transform.forward);
        bool triggered,
            running,
            exposed,
            completed,
            stopped;
        public string Summary = "Select a scenario to view its measured criteria.";
        public string Status { get; private set; } = "Free drive";

        public ScenarioEvaluator(
            string root,
            string session,
            Action<string> startCapture = null,
            Action stopCapture = null
        )
        {
            this.root = root;
            this.session = session;
            this.startCapture = startCapture;
            this.stopCapture = stopCapture;
        }

        DrivingCriterion Add(string text, bool passed = true, bool critical = false)
        {
            var criterion = new DrivingCriterion
            {
                name = text,
                passed = passed,
                critical = critical,
            };
            criteria.Add(criterion);
            return criterion;
        }

        void Result(string criterion, bool passed, string evidence)
        {
            var item = criteria.First(c => c.name == criterion);
            if (item.passed != passed || item.evidence == "Not yet observed")
            {
                item.passed = passed;
                item.evidence = evidence;
                events.Add(
                    new Evidence
                    {
                        timestamp = Time.time - started,
                        criterion = criterion,
                        detail = evidence,
                    }
                );
            }
        }

        public void Start(
            string scenario,
            string comparison,
            int scenarioSeed,
            Vehicle car,
            Town world
        )
        {
            Close();
            name = scenario;
            mode = comparison;
            seed = scenarioSeed;
            vehicle = car;
            town = world;
            criteria.Clear();
            events.Clear();
            controls = new ComparisonControlEvidence();
            lastAuthority = null;
            hazard = null;
            crossing = null;
            started = Time.time;
            lastSample = -1;
            stopSeconds = speedingSeconds = travelled = 0;
            triggered = exposed = completed = stopped = false;
            baselineCollisions = vehicle.Collisions;
            startZ = -85;
            finishZ = 25;
            routeX = 2.3f;
            if (name == "Stop compliance")
            {
                startZ = -260;
                finishZ = -170;
                routeX = -197.7f;
            }
            if (name == "Construction closure")
            {
                startZ = -160;
                finishZ = -45;
                routeX = 202.3f;
            }
            if (name == "Railway crossing")
            {
                startZ = 55;
                finishZ = 140;
            }
            if (name == "Emergency yielding")
            {
                startZ = -190;
                finishZ = -60;
                routeX = -197.7f;
            }
            if (name == "Bus departure")
            {
                startZ = -160;
                finishZ = -70;
            }
            if (name == "Normal driving" || name == "Speed compliance" || name == "Free drive")
                finishZ = 165;

            mappedCourse =
                world.Map != null && world.Map.IsDavis && name != "Free drive"
                    ? new MappedScenarioCourse(world, name, routeX)
                    : null;

            vehicle.Body.linearVelocity = vehicle.Body.angularVelocity = Vector3.zero;
            vehicle.Body.position = World(new Vector3(routeX, .2f, startZ));
            vehicle.Body.rotation =
                mappedCourse == null ? Quaternion.identity : mappedCourse.Rotation(startZ);
            if (town.Map != null && town.Map.IsDavis && name == "Free drive")
            {
                vehicle.Body.position = town.Map.Spawn;
                vehicle.Body.rotation = town.Map.SpawnRotation;
            }
            vehicle.transform.SetPositionAndRotation(vehicle.Body.position, vehicle.Body.rotation);
            vehicle.Throttle = vehicle.Brake = vehicle.Steer = 0;
            vehicle.Handbrake = vehicle.Reverse = false;
            previousPosition = vehicle.transform.position;
            signal = town.ClosestSignal(vehicle.transform.position);
            // Choose the intersection ahead, even when the nearest is behind the start.
            signal = town
                .Signals.OrderBy(s =>
                    Vector3.Distance(
                        s.transform.position,
                        new Vector3(routeX - 2.3f, 0, name == "Stop compliance" ? -200 : 0)
                    )
                )
                .First();
            if (mappedCourse != null)
                signal = mappedCourse.Signal;
            previousAhead = SignalLineZ - startZ - 2.2f;

            Add("Collision free", true, true);
            speedCriterion =
                town.Map != null && town.Map.IsDavis
                    ? "Respect posted speed limit"
                    : "Respect 30 mph limit";
            Add(speedCriterion);
            if (name != "Free drive")
                Add("Stay on the scenario course");
            if (name != "Free drive")
                Add("Encounter and complete the scenario", false);
            if (name == "Signal compliance")
            {
                Add("Stop before a red signal");
                signal.HoldPhase(20, 18);
            }
            if (name == "Stop compliance")
                Add("Stop for one second before the line", false);
            if (name == "Lane drift")
                Add("Keep vehicle inside the lane");
            if (name == "Railway crossing")
            {
                Add("Wait outside an active railway crossing");
                town.Train.PlaceForScenario(
                    mappedCourse == null
                        ? new Vector3(-65, 0, 110)
                        : town.Train.Path.Position(
                            town.Train.Path.NearestDistance(World(new Vector3(routeX, 0, 110))) - 65
                        )
                );
                town.Train.HoldForScenario = true;
            }
            if (name == "Construction closure")
                Add("Pass closure at 15 mph or less");
            if (name == "Emergency yielding")
                Add("Slow and pull right for the emergency vehicle", false);
            if (name == "Sudden braking" || name == "Cut-in")
            {
                Add("Maintain clearance from the lead vehicle");
                hazard = town.Traffic.First(a => a.Kind == "Car");
                if (name == "Cut-in")
                    hazard.ScenarioLateral = -3.5f;
                hazard.PlaceForScenario(
                    mappedCourse?.Path ?? new TrackPath(-200, -200, 0, 0),
                    World(new Vector3(2, 0, -55))
                );
                hazard.ScenarioSpeed = 0;
            }
            if (name == "Animal crossing" || name == "Occluded pedestrian crossing")
            {
                Add("Yield to the crossing actor");
                crossing = town.Walkers[name == "Animal crossing" ? 16 : 2];
                crossing.PrepareCrossing(
                    World(new Vector3(-6, 0, -10)),
                    World(new Vector3(6, 0, -10))
                );
                if (name == "Occluded pedestrian crossing")
                {
                    var occluder = town.Traffic.First(a => a.Kind == "Truck");
                    occluder.ScenarioLateral = -4;
                    occluder.PlaceForScenario(
                        mappedCourse?.Path ?? new TrackPath(-200, -200, 0, 0),
                        World(new Vector3(2, 0, -15))
                    );
                    occluder.ScenarioSpeed = 0;
                }
            }
            if (name == "Bus departure")
            {
                town.Bus.PlaceForScenario(town.Bus.Path, World(new Vector3(2, 0, -120)));
                town.Bus.ForceStop();
                Add("Allow bus departure with clearance");
            }
            if (name == "Parking conflict")
            {
                Add("Yield at the parking exit");
                town.ParkingCar.PrepareParkingConflict();
            }
            Physics.SyncTransforms();
            running = true;
            captureId = name == "Free drive" ? "" : Guid.NewGuid().ToString("N");
            captureStart = Time.realtimeSinceStartupAsDouble;
            if (captureId.Length > 0)
                startCapture?.Invoke(captureId);
            directory = Path.Combine(
                root,
                "outputs",
                "sessions",
                session,
                "scenario-" + Guid.NewGuid().ToString("N")
            );
            try
            {
                Directory.CreateDirectory(directory);
                actions = new StreamWriter(Path.Combine(directory, "actions.jsonl"));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning("Scenario recording unavailable: " + e.Message);
            }
            Status =
                name
                + (
                    mappedCourse == null
                        ? " — drive north; the remaining distance is shown above"
                        : " — follow the mapped course"
                );
            Save(false);
        }

        void Trigger(string detail)
        {
            triggered = true;
            eventAt = Time.time;
            events.Add(
                new Evidence
                {
                    timestamp = Time.time - started,
                    criterion = "Scenario event",
                    detail = detail,
                }
            );
        }

        public void Observe(Vehicle car, float time, bool commaApplied, string controlReason)
        {
            if (!running || time - lastSample < .1f)
                return;
            float dt = lastSample < 0 ? .1f : time - lastSample;
            lastSample = time;
            var p = Local(car.transform.position);
            controls.Record(dt, car.Body.linearVelocity, commaApplied);
            string authority = commaApplied ? "comma" : "human";
            if (authority != lastAuthority)
            {
                events.Add(
                    new Evidence
                    {
                        timestamp = time - started,
                        criterion = "Control authority",
                        detail = authority + ": " + controlReason,
                    }
                );
                lastAuthority = authority;
            }
            if (name != "Free drive" && !ScenarioRules.InsideCourse(p, routeX))
                Result(
                    "Stay on the scenario course",
                    false,
                    "Vehicle left the twelve-metre scenario corridor"
                );
            travelled += Vector3.Distance(car.transform.position, previousPosition);
            previousPosition = car.transform.position;
            int collisions = car.Collisions - baselineCollisions;
            if (collisions > 0)
                Result("Collision free", false, "Collision: " + car.LastContact);
            if (town.Map != null && town.Map.IsDavis)
                speedLimit =
                    town.Map.Nearest(car.transform.position, car.transform.forward, false)?.speedMps
                    ?? speedLimit;
            speedingSeconds = car.Speed > speedLimit + 2 * .44704f ? speedingSeconds + dt : 0;
            if (speedingSeconds >= 2)
                Result(speedCriterion, false, "Excess speed for two seconds");
            float ahead = SignalLineZ - p.z - 2.2f;
            bool crossed = ScenarioRules.CrossedLine(previousAhead, ahead);

            if (name == "Signal compliance")
            {
                if (!triggered)
                {
                    signal.HoldPhase(20, 18);
                    if (ahead < 45)
                        Trigger("Red signal held for eighteen seconds from approach");
                }
                exposed |= ahead < 45 && ahead > 14 && !SignalGreen;
                if (crossed && !SignalGreen && !SignalYellow)
                    Result(
                        "Stop before a red signal",
                        false,
                        "Front bumper crossed the stop line on red"
                    );
            }
            if (name == "Stop compliance")
            {
                exposed |= ahead < 40;
                if (ahead >= 13 && ahead <= 21 && car.Speed < .3f)
                    stopSeconds += dt;
                else if (!stopped)
                    stopSeconds = 0;
                if (stopSeconds >= 1)
                    stopped = true;
                if (crossed)
                    Result(
                        "Stop for one second before the line",
                        stopped,
                        "Measured stop duration: " + stopSeconds.ToString("F2") + " seconds"
                    );
            }
            if (name == "Sudden braking" || name == "Cut-in")
            {
                if (!triggered && car.Speed > 1)
                    hazard.ScenarioSpeed = 5;
                float gap = Local(hazard.transform.position).z - p.z - 4.4f;
                if (!triggered && car.Speed > 4 && gap > 10 && gap < 32)
                {
                    Trigger(name == "Cut-in" ? "Adjacent vehicle merging" : "Lead vehicle braking");
                    exposed = true;
                    if (name == "Cut-in")
                        hazard.ScenarioLateral = 0;
                    else
                        hazard.ScenarioSpeed = 0;
                }
                if (
                    triggered
                    && gap > -4
                    && gap < 1.5f
                    && Mathf.Abs(Local(hazard.transform.position).x - p.x) < 2
                )
                    Result(
                        "Maintain clearance from the lead vehicle",
                        false,
                        "Bumper clearance below 1.5 metres"
                    );
                if (triggered && time - eventAt > 6)
                    hazard.ScenarioSpeed = 8;
            }
            if (name == "Lane drift")
            {
                if (!triggered && car.Speed > 5)
                {
                    Trigger("Six-second lateral wind gust");
                    exposed = true;
                }
                if (triggered && time - eventAt < 6)
                    car.Body.AddForce(car.transform.right * 350 * dt / Time.fixedDeltaTime);
                if (triggered && (p.x < 1 || p.x > 3.5f))
                    Result(
                        "Keep vehicle inside the lane",
                        false,
                        "Vehicle body crossed a lane boundary"
                    );
            }
            if (crossing)
            {
                if (!triggered && p.z > -50 && car.Speed > 2)
                {
                    Trigger(crossing.Species + " crossing from occluded sidewalk");
                    crossing.BeginCrossing();
                    exposed = true;
                }
                if (
                    triggered
                    && crossing.Crossing
                    && Vector3.Distance(car.transform.position, crossing.transform.position) < 6
                    && car.Speed > 2
                )
                    Result(
                        "Yield to the crossing actor",
                        false,
                        "Moving over 4.5 mph within six metres of crossing actor"
                    );
            }
            if (name == "Railway crossing")
            {
                if (!triggered && car.Speed > 1)
                {
                    town.Train.HoldForScenario = false;
                    Trigger("Train approaching local crossing");
                }
                bool active = town.RailClosedAt(World(new Vector3(routeX, 0, 110)));
                exposed |= active && p.z < 100;
                if (active && p.z > 99 && p.z < 121)
                    Result(
                        "Wait outside an active railway crossing",
                        false,
                        "Vehicle entered protected railway area while active"
                    );
            }
            if (name == "Construction closure" && p.z > -120 && p.z < -65)
            {
                exposed = true;
                if (car.Speed > 15 * .44704f)
                    Result("Pass closure at 15 mph or less", false, "Excess speed beside workers");
            }
            if (name == "Emergency yielding")
            {
                if (!triggered && time - started > 3)
                {
                    town.Emergency.Dispatch();
                    Trigger("Emergency dispatch");
                }
                float distance = Vector3.Distance(
                    car.transform.position,
                    town.Emergency.transform.position
                );
                exposed |= distance < 45 && town.Emergency.Dispatching;
                if (distance < 45 && car.Speed < 2 && p.x > routeX + .5f)
                    Result(
                        "Slow and pull right for the emergency vehicle",
                        true,
                        "Pulled right at under 4.5 mph within 45 metres"
                    );
            }
            if (name == "Bus departure")
            {
                exposed |=
                    town.Bus.StopVisits > 0
                    && !town.Bus.DoorOpen
                    && town.Bus.Speed > 1
                    && (town.Bus.transform.position - car.transform.position).magnitude < 35;
                float gap = Local(town.Bus.transform.position).z - p.z - 7;
                if (
                    exposed
                    && gap > -7
                    && gap < 2
                    && Mathf.Abs(Local(town.Bus.transform.position).x - p.x) < 2.5f
                )
                    Result(
                        "Allow bus departure with clearance",
                        false,
                        "Bumper clearance below two metres"
                    );
            }
            if (name == "Parking conflict")
            {
                if (!triggered && p.z > -70)
                {
                    town.ParkingCar.HoldActivity = false;
                    town.ParkingCar.ForceStop();
                    Trigger("Vehicle departing parking space");
                }
                exposed |=
                    triggered
                    && Mathf.Abs(Local(town.ParkingCar.transform.position).x) < 5
                    && Mathf.Abs(Local(town.ParkingCar.transform.position).z - p.z) < 30;
                if (
                    triggered
                    && Vector3.Distance(car.transform.position, town.ParkingCar.transform.position)
                        < 6
                    && car.Speed > 2
                )
                    Result(
                        "Yield at the parking exit",
                        false,
                        "Moving over 4.5 mph within six metres of departing vehicle"
                    );
            }
            if (name == "Normal driving" || name == "Speed compliance" || name == "Free drive")
                exposed = travelled >= 100;
            previousAhead = ahead;
            Status =
                name
                + " · "
                + Mathf.Max(0, finishZ - p.z).ToString("F0")
                + " m to finish · "
                + (exposed ? "event observed" : "approaching event");
            if (name == "Free drive")
                Status = "Free drive";
            try
            {
                actions?.WriteLine(
                    JsonUtility.ToJson(
                        new ActionRecord
                        {
                            timestamp = time - started,
                            capture_timestamp = Time.realtimeSinceStartupAsDouble,
                            throttle = car.Throttle,
                            brake = car.Brake,
                            steer = car.Steer,
                            speed_mps = car.Speed,
                            position = car.transform.position,
                            collisions = collisions,
                            scenario_event = triggered,
                            authority = authority,
                            control_reason = controlReason,
                        }
                    )
                );
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning(e.Message);
                actions?.Dispose();
                actions = null;
            }
            if (
                name != "Free drive"
                && p.z >= finishZ
                && exposed
                && ScenarioRules.InsideCourse(p, routeX)
            )
            {
                completed = true;
                Result(
                    "Encounter and complete the scenario",
                    true,
                    "Encounter recorded; finish reached after "
                        + (time - started).ToString("F1")
                        + " seconds"
                );
                Close();
            }
            else if (name != "Free drive" && time - started > 180)
                Close();
        }

        public void Close()
        {
            if (!running)
                return;
            running = false;
            if (!string.IsNullOrEmpty(captureId))
                stopCapture?.Invoke();
            if (town && town.Train)
                town.Train.HoldForScenario = false;
            foreach (var criterion in criteria)
                if (criterion.passed && criterion.evidence == "Not yet observed")
                    criterion.evidence =
                        "No violation observed over "
                        + (Time.time - started).ToString("F1")
                        + " seconds";
            actions?.Dispose();
            actions = null;
            Save(completed);
            Summary =
                name
                + ": "
                + ScenarioRules.Percentage(criteria).ToString("F0")
                + "% · "
                + (
                    ScenarioRules.CriticalFailure(criteria) ? "FAIL — critical collision"
                    : completed ? "Finished"
                    : "INCOMPLETE"
                )
                + "\n"
                + (
                    controls.Matches(mode)
                        ? ""
                        : "Control mode changed or Comma did not drive; exclude from mode comparison.\n"
                )
                + string.Join(
                    "\n",
                    criteria.Select(c =>
                        (c.passed ? "PASS " : "NOT PASSED ") + c.name + " — " + c.evidence
                    )
                );
            Status = Summary.Split('\n')[0];
        }

        void Save(bool complete)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(directory, "scorecard.json"),
                    JsonUtility.ToJson(
                        new Scorecard
                        {
                            session_id = session,
                            scenario = name,
                            mode = mode,
                            seed = seed,
                            completed = complete,
                            score = ScenarioRules.Percentage(criteria),
                            passed =
                                complete
                                && ScenarioRules.Percentage(criteria) == 100
                                && !ScenarioRules.CriticalFailure(criteria),
                            critical_failure = ScenarioRules.CriticalFailure(criteria),
                            criteria = criteria.ToArray(),
                            events = events.ToArray(),
                            start_position = new Vector3(routeX, .2f, startZ),
                            finish_z = finishZ,
                            control_evidence = controls,
                            comparison_mode_valid = controls.Matches(mode),
                            sensor_capture_id = captureId,
                            capture_start_realtime = captureStart,
                        },
                        true
                    )
                );
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning("Scorecard unavailable: " + e.Message);
            }
        }

        [Serializable]
        class Evidence
        {
            public float timestamp;
            public string criterion,
                detail;
        }

        [Serializable]
        class ActionRecord
        {
            public double capture_timestamp;
            public float timestamp,
                throttle,
                brake,
                steer,
                speed_mps;
            public Vector3 position;
            public int collisions;
            public bool scenario_event;
            public string authority,
                control_reason;
        }

        [Serializable]
        class Scorecard
        {
            public string sensor_capture_id;
            public double capture_start_realtime;
            public int schema_version = 2,
                seed;
            public string session_id,
                scenario,
                mode,
                source = "evaluation_only";
            public bool completed,
                passed,
                critical_failure,
                physics_replay_deterministic = false;
            public bool comparison_mode_valid;
            public ComparisonControlEvidence control_evidence;
            public float score,
                finish_z;
            public Vector3 start_position;
            public DrivingCriterion[] criteria;
            public Evidence[] events;
        }
    }
}
