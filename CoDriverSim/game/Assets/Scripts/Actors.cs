using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace CoDriver
{
    public interface ITransit
    {
        Transform transform { get; }
        bool DoorOpen { get; }
        Vector3 DoorPoint { get; }
        int StopVisits { get; }
        int BoardingUsers { get; set; }
    }

    public sealed class TrackPath
    {
        readonly List<Vector3> points = new List<Vector3>();
        readonly List<float> distances = new List<float>();
        readonly bool terrainHeight = true;
        readonly bool loop = true;
        public float Length { get; private set; }
        public bool Closed => loop;

        public TrackPath(IEnumerable<Vector3> polyline, bool closed, bool useTownHeight = false)
        {
            terrainHeight = useTownHeight;
            loop = closed;
            foreach (var point in polyline)
                Add(point);
            if (closed && (points[0] - points[points.Count - 1]).sqrMagnitude > .001f)
                Add(points[0]);
        }

        public TrackPath(float x0, float z0, float x1, float z1, float radius = 18)
        {
            // Four rounded connected turns; lane centres remain on the right of the road.
            Vector3[] corners =
            {
                new Vector3(x0 - 2, 0, z0 - 2),
                new Vector3(x1 + 2, 0, z0 - 2),
                new Vector3(x1 + 2, 0, z1 + 2),
                new Vector3(x0 - 2, 0, z1 + 2),
            };
            for (int i = 0; i < 4; i++)
            {
                Vector3 c = corners[i],
                    prev = corners[(i + 3) % 4],
                    next = corners[(i + 1) % 4];
                Vector3 a = c + (prev - c).normalized * radius,
                    b = c + (next - c).normalized * radius;
                for (int k = 0; k < 12; k++)
                {
                    float t = k / 11f;
                    Add((1 - t) * (1 - t) * a + 2 * (1 - t) * t * c + t * t * b);
                }
            }
            Add(points[0]);
        }

        public TrackPath(List<Vector3> nodes)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                Vector3 c = nodes[i],
                    incoming = (c - nodes[(i + nodes.Count - 1) % nodes.Count]).normalized,
                    outgoing = (nodes[(i + 1) % nodes.Count] - c).normalized;
                Vector3 a = c - incoming * 12 + Vector3.Cross(Vector3.up, incoming) * 2,
                    b = c + outgoing * 12 + Vector3.Cross(Vector3.up, outgoing) * 2;
                if (Vector3.Dot(incoming, outgoing) > .9f)
                {
                    Add(a);
                    Add(b);
                    continue;
                }
                Vector3 control =
                    c
                    + Vector3.Cross(Vector3.up, incoming) * 2
                    + Vector3.Cross(Vector3.up, outgoing) * 2;
                for (int j = 0; j <= 12; j++)
                {
                    float t = j / 12f;
                    Add((1 - t) * (1 - t) * a + 2 * t * (1 - t) * control + t * t * b);
                }
            }
            Add(points[0]);
        }

        void Add(Vector3 p)
        {
            if (points.Count > 0 && (p - points[points.Count - 1]).sqrMagnitude < .000001f)
                return;
            if (points.Count > 1)
            {
                var a = points[points.Count - 2];
                var b = points[points.Count - 1];
                if (
                    Vector3.Dot(b - a, p - b) > 0
                    && Vector3.Cross((b - a).normalized, (p - b).normalized).sqrMagnitude < .000001f
                )
                {
                    Length = distances[distances.Count - 2] + Vector3.Distance(a, p);
                    points[points.Count - 1] = p;
                    distances[distances.Count - 1] = Length;
                    return;
                }
            }
            if (points.Count > 0)
                Length += Vector3.Distance(points[points.Count - 1], p);
            points.Add(p);
            distances.Add(Length);
        }

        public Vector3 Position(float distance)
        {
            if (points.Count < 2 || Length < .0001f)
                return points.Count > 0 ? points[0] : Vector3.zero;
            distance = loop ? Mathf.Repeat(distance, Length) : Mathf.Clamp(distance, 0, Length);
            int i = distances.BinarySearch(distance);
            if (i < 0)
                i = ~i;
            i = Mathf.Clamp(i, 1, distances.Count - 1);
            Vector3 p = Vector3.Lerp(
                points[i - 1],
                points[i],
                Mathf.InverseLerp(distances[i - 1], distances[i], distance)
            );
            if (terrainHeight)
                p.y = Town.Height(p.z);
            return p;
        }

        public Vector3 Direction(float distance)
        {
            if (!loop)
                distance = Mathf.Clamp(distance, 0, Length);
            var delta = Position(distance + .15f) - Position(distance - .15f);
            if (delta.sqrMagnitude > .000001f)
                return delta.normalized;
            for (int i = 1; i < points.Count; i++)
                if ((points[i] - points[i - 1]).sqrMagnitude > .000001f)
                    return (points[i] - points[i - 1]).normalized;
            return Vector3.forward;
        }

        public float NearestDistance(Vector3 point, float hint = float.NaN)
        {
            float best = 0,
                error = float.PositiveInfinity;
            for (int i = 1; i < points.Count; i++)
            {
                Vector3 segment = points[i] - points[i - 1];
                float t = Mathf.Clamp01(
                    Vector3.Dot(point - points[i - 1], segment)
                        / Mathf.Max(.0001f, segment.sqrMagnitude)
                );
                float e = (points[i - 1] + segment * t - point).sqrMagnitude;
                float at = Mathf.Lerp(distances[i - 1], distances[i], t);
                if (float.IsFinite(hint))
                {
                    float delta = loop
                        ? Mathf.Repeat(at - hint + Length * .5f, Length) - Length * .5f
                        : at - hint;
                    if (Mathf.Abs(delta) > 45)
                        continue;
                }
                if (e < error)
                {
                    error = e;
                    best = Mathf.Lerp(distances[i - 1], distances[i], t);
                }
            }
            return float.IsPositiveInfinity(error) && float.IsFinite(hint)
                ? NearestDistance(point)
                : best;
        }
    }

    public sealed class TrafficActor : MonoBehaviour, ITransit
    {
        public string Kind,
            State = "Cruising";
        public float Speed,
            Distance;
        public bool DoorOpen => Kind == "Bus" && stopUntil > 0 && Speed < .2f;
        public Vector3 DoorPoint => transform.TransformPoint(new Vector3(1.3f, 0, 3.7f));
        public int StopVisits { get; private set; }
        public int BoardingUsers { get; set; }
        public bool Dispatching;
        public TrackPath Path;
        Town town;
        Rigidbody body;
        PhysicalDriver driver;
        public bool ReverseRequested => driver != null && driver.ReverseRequested;
        public float SignedSpeed => body ? Vector3.Dot(body.linearVelocity, transform.forward) : 0;
        public float RequestedSpeed,
            PathError;
        public string Contact = "",
            Clearance = "";
        public int Grounded => driver?.Dynamics.GroundedWheels ?? 0;
        public RvpVehicleDynamics Dynamics => driver?.Dynamics;

        void OnCollisionStay(Collision collision)
        {
            Contact = collision.collider.name;
        }

        void OnCollisionExit(Collision collision)
        {
            if (Contact == collision.collider.name)
                Contact = "";
        }

        TrackPath accessPath;
        Vector3 accessGoal;
        public Rigidbody Body => body;
        Transform door;
        Renderer[] flash;
        float stopUntil,
            dispatchAt = 70,
            lateral,
            stopClock;
        Signal clearedStop;
        int busStop,
            parkingStage;
        Vector3 parkingGoal;
        float parkingWait;
        Vector3 previous;
        int emergencyPhase;
        float stationJoin,
            sceneJoin,
            departAfter;
        Vector3 station = new Vector3(-173, .1f, -130),
            incident = new Vector3(17, .1f, -70);
        public string Activity =>
            Kind == "Emergency" ? "phase " + emergencyPhase + ": " + State : State;
        public string NavigationDetail =>
            $"at={body.position:F2} goal={accessGoal} error={Vector3.ProjectOnPlane(accessGoal - body.position, Vector3.up).magnitude:F2} remaining={(accessPath == null ? -1 : accessPath.Length - accessPath.NearestDistance(body.position)):F2} target={RequestedSpeed:F2} heading={transform.eulerAngles.y:F1} route={Path.Position(Distance)} direction={Path.Direction(Distance)} path_error={PathError:F2} exit={Path.Position(exitDistance)} {accessBlock} boarding={BoardingUsers} stops={StopVisits}";
        string accessBlock = "";
        public float ScenarioSpeed = -1,
            ScenarioLateral;
        public bool HoldActivity;
        bool busDeparting;
        public int Indicator;
        public bool DriverReady = true,
            OccupantDoorOpen,
            ScenarioOccupant;
        public int ParkingVisits;
        public bool IsParked => Kind == "Parking" && parkingStage == 0 && Speed < .2f;
        public Vector3 DriverDoorPoint => transform.TransformPoint(new Vector3(-1.8f, 0, -.7f));
        public Vector3 DriverSeat => transform.TransformPoint(new Vector3(-.4f, .1f, -.15f));
        Transform occupantDoor;
        float[] mappedBusStops;
        public MapLandmark[] ServedBusStops { get; private set; } =
            System.Array.Empty<MapLandmark>();
        bool busMustDepart;
        Vector3 lastBusStopPosition;
        readonly StopSignProgress mappedStops = new StopSignProgress();
        const float PlannerStep = .1f;
        float nextPlan;
        string exitLane;
        float exitDistance;
        float blockedSeconds,
            nextReplan;

        float RemainingTo(float goal)
        {
            float remaining = Mathf.Repeat(goal - Distance, Path.Length);
            return remaining > Path.Length - 8 ? remaining - Path.Length : remaining;
        }

        public void PrepareParkingConflict()
        {
            parkingStage = 2;
            parkingWait = Time.time;
            HoldActivity = true;
            ScenarioOccupant = true;
            DriverReady = true;
            body.position = new Vector3(-9, .1f, -43);
            body.rotation = Quaternion.Euler(0, 90, 0);
            if (town.Map.IsDavis)
            {
                parkingStage = 0;
                body.position = town.ParkingBay;
                body.rotation = Quaternion.LookRotation(
                    Path.Direction(Path.NearestDistance(body.position))
                );
            }
            body.linearVelocity = body.angularVelocity = Vector3.zero;
            transform.SetPositionAndRotation(body.position, body.rotation);
        }

        int identity;

        public void PlaceForScenario(TrackPath path, Vector3 position, float speed = 0)
        {
            Path = path;
            Distance = path.NearestDistance(position);
            Speed = speed;
            lateral = ScenarioLateral;
            body.position =
                path.Position(Distance)
                + Vector3.up * .1f
                + Vector3.Cross(Vector3.up, path.Direction(Distance)) * lateral;
            body.rotation = Quaternion.LookRotation(path.Direction(Distance));
            transform.SetPositionAndRotation(body.position, body.rotation);
            previous = body.position;
        }

        public void Initialize(Town t, string kind, int index)
        {
            town = t;
            identity = index;
            dispatchAt = Time.time + 70;
            Kind = kind;
            gameObject.name = kind + " " + index;
            gameObject.layer = TrafficClearance.TrafficLayer;
            int bx = index % 4,
                bz = (index / 4) % 4;
            if (kind == "Bus" || kind == "Emergency" || kind == "Parking")
            {
                bx = 1;
                bz = 1;
            }
            Path = new TrackPath(
                Town.Grid[bx],
                Town.Grid[bz],
                Town.Grid[bx + 1],
                Town.Grid[bz + 1]
            );
            if (kind == "Car" || kind == "Truck")
                Path = new TrackPath(t.TrafficGraph.Circuit(t.Seed + index * 103, index));
            if (t.Map.IsDavis)
            {
                Path = t.Map.Circuit(t.Seed, index);
                if (kind == "Emergency")
                {
                    station = t.EmergencyBay;
                    incident = t.IncidentBay;
                    Path = t.Map.RoundTrip(station, incident);
                }
                if (kind == "Parking")
                {
                    var nearbyLane = t.Map.Nearest(t.ParkingBay);
                    Path = t.Map.RoundTrip(
                        t.ParkingBay,
                        t.ParkingBay + nearbyLane.Path.Direction(0) * 180
                    );
                }
                if (kind == "Bus")
                {
                    var stops = t
                        .Map.landmarks.Where(l =>
                            l.kind == "transit_stop"
                            && l.subtype == "bus_stop"
                            && t.Map.Contains(l.position, 30)
                        )
                        .ToArray();
                    var parkStop =
                        stops.FirstOrDefault(s => s.sourceId == "node/13095780017") ?? stops[0];
                    // This simulated route uses connected, bus-envelope-safe streets.
                    // The Amtrak branch currently has excluded tight turns; do not
                    // silently replace an unreachable transit route with a random circuit.
                    var stationStop =
                        stops.FirstOrDefault(s => s.sourceId == "node/13095780019")
                        ?? stops[stops.Length - 1];
                    Path = t.Map.RoundTrip(parkStop.position, stationStop.position, true);
                    ServedBusStops = stops
                        .Where(s =>
                            (
                                Path.Position(Path.NearestDistance(s.position)) - s.position
                            ).sqrMagnitude < 144
                            && Vector3.Dot(
                                s.position - Path.Position(Path.NearestDistance(s.position)),
                                Vector3.Cross(
                                    Vector3.up,
                                    Path.Direction(Path.NearestDistance(s.position))
                                )
                            ) >= -.5f
                        )
                        .OrderBy(s => Path.NearestDistance(s.position))
                        .ToArray();
                    mappedBusStops = ServedBusStops
                        .Select(s => Path.NearestDistance(s.position))
                        .OrderBy(d => d)
                        .ToArray();
                }
            }
            Distance = index * 49 % Path.Length;
            if (kind == "Bus" && mappedBusStops != null && mappedBusStops.Length > 0)
                Distance = Mathf.Repeat(mappedBusStops[0] - 25, Path.Length);
            transform.position = Path.Position(Distance) + Vector3.up * .1f;
            transform.rotation = Quaternion.LookRotation(Path.Direction(Distance));
            var paint = Art.Material(
                "Vehicle " + index,
                Color.HSVToRGB(index * .137f % 1, .35f, .65f)
            );
            var modeledBus = kind == "Bus" ? Realism.Spawn("Bus", transform) : null;
            bool modeledUtility = false;
            if (kind == "Bus" && !modeledBus)
            {
                Art.Box(
                    "Bus chassis",
                    transform,
                    new Vector3(0, .6f, 0),
                    new Vector3(2.3f, .3f, 9.5f),
                    paint,
                    false
                );
                Art.Box(
                    "Bus roof",
                    transform,
                    new Vector3(0, 2.65f, 0),
                    new Vector3(2.35f, .18f, 9.5f),
                    paint,
                    false
                );
                Art.Box(
                    "Bus left wall",
                    transform,
                    new Vector3(-1.12f, 1.6f, 0),
                    new Vector3(.1f, 1.9f, 9.5f),
                    paint,
                    false
                );
                Art.Box(
                    "Bus right wall behind door",
                    transform,
                    new Vector3(1.12f, 1.6f, -.85f),
                    new Vector3(.1f, 1.9f, 7.8f),
                    paint,
                    false
                );
                Art.Box(
                    "Bus right front pillar",
                    transform,
                    new Vector3(1.12f, 1.6f, 4.55f),
                    new Vector3(.1f, 1.9f, .4f),
                    paint,
                    false
                );
                for (int s = -1; s <= 1; s += 2)
                {
                    Art.Box(
                        "Bus end",
                        transform,
                        new Vector3(0, 1.5f, s * 4.7f),
                        new Vector3(2.3f, 1.8f, .1f),
                        paint,
                        false
                    );
                    Art.Box(
                        "Bus windscreen",
                        transform,
                        new Vector3(0, 2, s * 4.76f),
                        new Vector3(2.1f, .9f, .02f),
                        Art.Glass,
                        false
                    );
                    Art.Wheel(transform, new Vector3(s * 1.04f, .34f, 3.2f));
                    Art.Wheel(transform, new Vector3(s * 1.04f, .34f, -3.2f));
                }
                for (int step = 0; step < 3; step++)
                    Art.Box(
                        "Boarding step",
                        transform,
                        new Vector3(1.45f - step * .2f, .15f + step * .22f, 3.7f),
                        new Vector3(.3f, .15f, 1.1f),
                        Art.Metal,
                        false
                    );
            }
            else if (kind == "Bus")
                RollingWheel.PrepareImported(modeledBus);
            else if (kind == "Parking")
                occupantDoor = Art.ParkingCar(transform, paint);
            else if (kind == "Truck" || kind == "Emergency")
            {
                modeledUtility = Realism.Car(transform, kind == "Truck" ? "Pickup" : "Ambulance");
                if (!modeledUtility)
                    Art.Car(transform, paint, true);
            }
            else
            {
                if (
                    !Realism.Car(
                        transform,
                        index % 7 == 2
                            ? "Concept"
                            : new[] { "Sedan", "Hatchback", "SUV" }[index % 3],
                        paint: paint
                    )
                )
                    Art.Car(transform, paint, true);
            }
            if ((kind == "Truck" || kind == "Emergency") && !modeledUtility)
            {
                Art.Box(
                    "Coach body",
                    transform,
                    new Vector3(0, 1.5f, kind == "Bus" ? 0 : -.8f),
                    new Vector3(2.3f, 2, kind == "Bus" ? 9.5f : 3.2f),
                    kind == "Emergency" ? Art.White : paint,
                    false
                );
            }
            if (kind == "Bus")
            {
                if (!modeledBus)
                    for (int s = -1; s <= 1; s += 2)
                    for (int i = 0; i < 7; i++)
                        Art.Box(
                            "Passenger window",
                            transform,
                            new Vector3(s * 1.16f, 2, -3.8f + i * 1.1f),
                            new Vector3(.025f, .8f, .8f),
                            Art.Glass,
                            false
                        );
                door = Art.Box(
                    "Bus folding door",
                    transform,
                    new Vector3(1.18f, 1.3f, 3.7f),
                    new Vector3(.05f, 2.1f, 1.1f),
                    Art.Glass,
                    false
                ).transform;
                if (modeledBus)
                {
                    door.GetComponent<Renderer>().enabled = false;
                    // Door vertices are in metres; the old thin-glass primitive
                    // carried scale. A separate unit-scale pivot preserves them.
                    var pivot = new GameObject("Modeled bus door pivot").transform;
                    pivot.SetParent(transform, false);
                    pivot.localPosition = door.localPosition;
                    Realism.AttachPanels(modeledBus, pivot);
                    door = pivot;
                }
            }
            if (kind == "Emergency")
            {
                flash = new Renderer[2];
                for (int i = 0; i < 2; i++)
                    flash[i] = Art.Box(
                            "Emergency light",
                            transform,
                            new Vector3(i == 0 ? -.5f : .5f, modeledUtility ? 1.8f : 2.6f, .25f),
                            new Vector3(.6f, .15f, .35f),
                            Art.White,
                            false
                        )
                        .GetComponent<Renderer>();
                var audio = gameObject.AddComponent<AudioSource>();
                audio.spatialBlend = 1;
                audio.maxDistance = 100;
                audio.rolloffMode = AudioRolloffMode.Linear;
                audio.loop = true;
                audio.volume = .13f;
                var samples = new float[44100 * 2];
                for (int i = 0; i < samples.Length; i++)
                {
                    float freq = i < 44100 ? 700 : 950;
                    samples[i] = Mathf.Sin(i * freq * 2 * Mathf.PI / 44100) * .3f;
                }
                var clip = AudioClip.Create("Two tone siren", samples.Length, 1, 44100, false);
                clip.SetData(samples, 0);
                audio.clip = clip;
            }
            var c = gameObject.AddComponent<BoxCollider>();
            bool tall = kind == "Bus" || kind == "Truck" || kind == "Emergency";
            c.center = Vector3.up * (tall ? 1.4f : .85f);
            c.size = new Vector3(tall ? 2.3f : 1.8f, tall ? 2.6f : 1.5f, kind == "Bus" ? 10 : 4.4f);
            if (modeledUtility)
            {
                c.size =
                    kind == "Truck"
                        ? new Vector3(1.93f, 1.45f, 4.4f)
                        : new Vector3(1.74f, 1.7f, 4.4f);
                c.center = Vector3.up * (c.size.y / 2);
            }
            body = gameObject.AddComponent<Rigidbody>();
            var profile = VehicleProfile.ForTraffic(kind);
            if (
                GetComponentsInChildren<AssetDefinition>()
                    .Any(a => a.assetId == "khronos-carconcept-npc")
            )
            {
                profile.id = "concept-traffic-v1";
                profile.track = 1.56f;
                profile.collisionCentre = new Vector3(0, .68f, .15f);
                profile.collisionSize = new Vector3(1.82f, 1.05f, 4.3f);
            }
            c.center = profile.collisionCentre;
            c.size = profile.collisionSize;
            driver = new PhysicalDriver(body, profile);
            body.position = transform.position;
            body.rotation = transform.rotation;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            gameObject.AddComponent<TrafficLights>().Initialize(this);
            previous = transform.position;
            if (kind == "Parking")
            {
                DriverReady = false;
                transform.position = new Vector3(-67, .1f, -51);
                transform.rotation = Quaternion.Euler(0, 180, 0);
                if (t.Map.IsDavis)
                {
                    transform.position = t.ParkingBay;
                    var lane = t.Map.Nearest(t.ParkingBay);
                    transform.rotation = Quaternion.LookRotation(
                        lane.Path.Direction(lane.Path.NearestDistance(t.ParkingBay))
                    );
                }
                body.position = transform.position;
                body.rotation = transform.rotation;
                Distance = Path.NearestDistance(body.position);
                parkingWait = Time.time + 8;
                parkingStage = 0;
            }
            if (kind == "Emergency")
            {
                stationJoin = Path.NearestDistance(new Vector3(-202, 0, -130));
                sceneJoin = Path.NearestDistance(new Vector3(2, 0, -70));
                if (t.Map.IsDavis)
                {
                    stationJoin = Path.NearestDistance(station) + 15;
                    sceneJoin = Path.NearestDistance(incident) - 15;
                }
                else
                {
                    sceneJoin -= 12;
                    stationJoin += 15;
                }
                transform.SetPositionAndRotation(station, Quaternion.Euler(0, 270, 0));
                if (t.Map.IsDavis)
                    transform.rotation = Quaternion.LookRotation(Path.Direction(stationJoin));
                body.position = transform.position;
                body.rotation = transform.rotation;
            }
        }

        public void ForceStop()
        {
            if (Kind == "Parking")
                parkingWait = Time.time;
            else if (Kind == "Bus")
            {
                if (town.Map.IsDavis && mappedBusStops != null && mappedBusStops.Length > 0)
                {
                    busStop = System.Array.FindIndex(
                        mappedBusStops,
                        d => Mathf.Abs(d - Distance) < 3
                    );
                    if (busStop < 0)
                        busStop = Enumerable
                            .Range(0, mappedBusStops.Length)
                            .OrderBy(i => Mathf.Repeat(mappedBusStops[i] - Distance, Path.Length))
                            .First();
                    return;
                }
                float nearest = float.PositiveInfinity;
                for (int i = 0; i < 4; i++)
                {
                    float ahead = Mathf.Repeat(100 + i * Path.Length / 4 - Distance, Path.Length);
                    if (ahead < nearest)
                    {
                        nearest = ahead;
                        busStop = i;
                    }
                }
            }
        }

        public void Dispatch()
        {
            if (Kind != "Emergency" || emergencyPhase != 0)
                return;
            Dispatching = true;
            emergencyPhase = 1;
            GetComponent<AudioSource>().Play();
            town.GarageDoor?.Open();
        }

        void FixedUpdate()
        {
            Speed = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude;
            if (Time.time < nextPlan)
            {
                driver.Continue();
                return;
            }
            nextPlan = Time.time + PlannerStep;
            Distance = Path.NearestDistance(body.position, Distance);
            PathError = Vector3
                .ProjectOnPlane(Path.Position(Distance) - body.position, Vector3.up)
                .magnitude;
            Indicator = 0;
            if (town.Map.IsDavis)
            {
                MappedDrive();
                return;
            }
            if (Kind == "Parking")
            {
                ParkingCycle();
                return;
            }
            if (Kind == "Emergency" && EmergencyActivity())
                return;
            float target =
                Kind == "Bus" ? 9
                : Kind == "Emergency" && Dispatching ? 16
                : 11.2f;
            State = "Cruising";
            float busLateral = 0;
            if (Kind == "Bus")
            {
                float stopDistance = (100 + busStop * Path.Length / 4) % Path.Length;
                float remaining = RemainingTo(stopDistance);
                if (remaining < 35)
                {
                    Indicator = 1;
                    busLateral = .5f * Mathf.InverseLerp(35, 4, remaining);
                    target = Mathf.Min(target, Mathf.Sqrt(Mathf.Max(0, remaining - 1) * 3));
                    State = "Approaching bus stop";
                }
                if (remaining < 1.5f && Speed < .3f && stopUntil == 0)
                {
                    stopUntil = Time.time + 10;
                    StopVisits++;
                }
                if (stopUntil > 0)
                {
                    target = 0;
                    State = "Boarding";
                    if (Time.time > stopUntil && BoardingUsers == 0)
                    {
                        busStop = (busStop + 1) % 4;
                        stopUntil = 0;
                        departAfter = Time.time + 1.5f;
                        busDeparting = true;
                        State = "Closing doors";
                    }
                }
                if (Time.time < departAfter)
                {
                    Indicator = -1;
                    target = 0;
                }
                // Hold at the curb until there is room behind and beside the bus.
                if (busDeparting && lateral > .05f)
                {
                    Indicator = -1;
                    if (!MergeClear(Path.Position(Distance)))
                    {
                        target = 0;
                        departAfter = Time.time + .3f;
                        State = "Waiting for merge gap";
                    }
                    else
                        State = "Merging from bus stop";
                }
                if (busDeparting && lateral <= .05f && Time.time >= departAfter)
                    busDeparting = false;
            }
            if (Kind == "Emergency" && (emergencyPhase == 2 || emergencyPhase == 6))
            {
                float arrival = emergencyPhase == 2 ? sceneJoin : stationJoin,
                    remaining = RemainingTo(arrival);
                if (remaining < 80)
                    target = Mathf.Min(target, Mathf.Sqrt(Mathf.Max(0, remaining - .4f) * 4));
                if (remaining < 1 && Speed < .2f)
                {
                    emergencyPhase = emergencyPhase == 2 ? 3 : 7;
                    return;
                }
            }
            var origin =
                transform.position + Vector3.up + transform.forward * (Kind == "Bus" ? 5.3f : 2.5f);
            float forwardLimit = TrafficClearance.ForwardSpeedLimit(
                GetComponent<BoxCollider>(),
                Speed,
                // Cedar's corner furniture is checked against the curved full
                // body path below, rather than a tangent projected past the turn.
                staticObstacles: false
            );
            if (forwardLimit < target)
            {
                target = forwardLimit;
                State = "Maintaining gap";
            }
            var signal = town.ClosestSignal(transform.position);
            Vector3 rel = signal.transform.position - transform.position;
            bool north = Mathf.Abs(transform.forward.z) > .7f;
            float ahead = Vector3.Dot(rel, transform.forward);
            if (signal.StopControlled && clearedStop != signal && ahead > 0 && ahead < 40)
            {
                target = Mathf.Min(target, Mathf.Sqrt(Mathf.Max(0, ahead - 15) * 4));
                State = "Stop sign";
                if (Speed < .2f)
                {
                    stopClock += PlannerStep;
                    if (stopClock >= 2)
                    {
                        clearedStop = signal;
                        stopClock = 0;
                    }
                }
            }
            if (
                clearedStop
                && (clearedStop.transform.position - transform.position).sqrMagnitude > 3600
            )
                clearedStop = null;
            if (ahead > 0 && ahead < 40 && !signal.Green(north) && !signal.ReservedBy(this))
            {
                target = Mathf.Min(target, Mathf.Sqrt(Mathf.Max(0, ahead - 15) * 4));
                State = "Signal queue";
            }
            if (ahead > 9 && ahead < 45 && signal.Green(north) && !town.MayEnter(this, signal))
            {
                target = Mathf.Min(target, Mathf.Sqrt(Mathf.Max(0, ahead - 17) * 6));
                State = "Yield intersection";
            }
            if (ahead > 10 && ahead < 60)
                target = Mathf.Min(target, 8);
            if (Vector3.Angle(Path.Direction(Distance), Path.Direction(Distance + 20)) > 15)
                target = Mathf.Min(target, 5);
            if (
                town.RailClosedAt(new Vector3(transform.position.x, 0, 110))
                && Mathf.Abs(transform.position.z - 110) < 32
                && Mathf.Abs(transform.forward.z) > .7f
            )
            {
                float railAhead = (110 - transform.position.z) * Mathf.Sign(transform.forward.z);
                if (railAhead > 7)
                {
                    target = Mathf.Min(target, Mathf.Sqrt(Mathf.Max(0, railAhead - 12) * 5));
                    State = "Railway gate";
                }
            }
            bool yield =
                town.Emergency != null
                && town.Emergency != this
                && town.Emergency.Dispatching
                && (town.Emergency.transform.position - transform.position).sqrMagnitude < 3600
                // Finish an already-reserved turn before pulling aside. Shifting
                // a long bus outward mid-turn can pin it against a signal pole.
                && !(signal.ReservedBy(this) && rel.sqrMagnitude < 35 * 35);
            if (yield)
            {
                target = Mathf.Min(target, 2);
                State = "Yield emergency";
            }
            float desiredLateral = Kind == "Bus" ? busLateral : ScenarioLateral;
            bool avenue =
                Mathf.Abs(Path.Position(Distance).z) < 3 && Mathf.Abs(transform.forward.x) > .95f;
            bool betweenJunctions = Mathf.Abs(rel.x) > 35;
            bool ordinaryLaneChoice =
                ScenarioSpeed < 0
                && (Kind == "Car" || Kind == "Truck")
                && avenue
                && (transform.position.x < -235 || transform.position.x > 35);
            if (ordinaryLaneChoice)
            {
                float along = Mathf.Repeat(transform.position.x + 400, 200);
                if (transform.forward.x < 0)
                    along = 200 - along;
                bool outer = betweenJunctions && identity % 2 == 0 && along > 25 && along < 125;
                float proposed = outer ? 4.3f : 0;
                var destination = Path.Position(Distance) + transform.right * proposed;
                bool clear = MergeClear(destination);
                if (clear)
                    desiredLateral = proposed;
                else
                {
                    desiredLateral = lateral;
                    // Do not enter a one-lane junction until the return merge is safe.
                    if (!outer && lateral > .1f && ahead > 15 && ahead < 45)
                        target = Mathf.Min(target, Mathf.Sqrt(Mathf.Max(0, ahead - 24) * 4));
                }
                if (Mathf.Abs(desiredLateral - lateral) > .15f)
                {
                    Indicator = desiredLateral > lateral ? 1 : -1;
                    State = "Changing lane";
                }
            }
            Vector3 roadPoint = Path.Position(Distance);
            if (
                Mathf.Abs(roadPoint.x - 202) < 1
                && transform.forward.z > .7f
                && roadPoint.z > -175
                && roadPoint.z < -40
            )
            {
                target = Mathf.Min(target, 4);
                bool oncoming = false;
                foreach (var other in town.Traffic)
                    if (
                        other != this
                        && other.transform.forward.z < -.7f
                        && Mathf.Abs(other.transform.position.x - 198) < 2
                        && other.transform.position.z > -145
                        && other.transform.position.z < 35
                    )
                        oncoming = true;
                if (roadPoint.z < -145 && oncoming)
                {
                    target = Mathf.Min(target, Mathf.Sqrt(Mathf.Max(0, -150 - roadPoint.z) * 4));
                    State = "Yield construction oncoming traffic";
                }
                else
                {
                    desiredLateral =
                        -4
                        * Mathf.Min(
                            Mathf.InverseLerp(-145, -125, roadPoint.z),
                            1 - Mathf.InverseLerp(-70, -45, roadPoint.z)
                        );
                    Indicator = desiredLateral < lateral ? -1 : 1;
                    State = "Construction merge";
                }
            }
            if (
                Mathf.Abs(roadPoint.x - 198) < 1
                && transform.forward.z < -.7f
                && roadPoint.z > -45
                && roadPoint.z < 35
            )
                foreach (var other in town.Traffic)
                    if (
                        other != this
                        && other.transform.forward.z > .7f
                        && other.lateral < -1
                        && Mathf.Abs(other.Path.Position(other.Distance).x - 202) < 1
                        && other.transform.position.z > -145
                        && other.transform.position.z < -45
                    )
                    {
                        target = Mathf.Min(target, Mathf.Sqrt(Mathf.Max(0, roadPoint.z + 40) * 4));
                        State = "Yield construction occupied lane";
                    }
            lateral = Mathf.MoveTowards(
                lateral,
                yield && MergeClear(Path.Position(Distance) + transform.right * .6f) ? .6f
                    : Kind == "Bus" && stopUntil > 0 ? .5f
                    : Kind == "Bus" && Time.time < departAfter ? lateral
                    : desiredLateral,
                PlannerStep * 1.2f * Mathf.Clamp01(Speed / 2)
            );
            foreach (var w in town.Walkers)
                if (w.Crossing)
                {
                    float limit = TrafficClearance.CrossingSpeedLimit(
                        GetComponent<BoxCollider>(),
                        w.transform.position
                    );
                    if (limit < target)
                    {
                        target = limit;
                        State = "Yield pedestrian";
                    }
                }
            if (ScenarioSpeed >= 0)
                target = Mathf.Min(target, ScenarioSpeed);
            float safeSpeed = TrafficClearance.SafeSpeed(
                Path,
                Distance,
                lateral,
                Speed,
                GetComponent<BoxCollider>(),
                staticObstacles: true
            );
            if (safeSpeed < target)
            {
                target = safeSpeed;
                State = "Maintaining lane clearance";
            }
            Clearance = TrafficClearance.LastBlocker;
            RequestedSpeed = target;
            driver.Follow(Path, target, lateral);
            if (door)
                door.localPosition = Vector3.Lerp(
                    door.localPosition,
                    new Vector3(1.18f, 1.3f, DoorOpen ? 4.6f : 3.7f),
                    PlannerStep * 3
                );
            if (flash != null)
                for (int i = 0; i < 2; i++)
                    flash[i].sharedMaterial =
                        Dispatching && ((Time.time * 5 % 2) < 1) == (i == 0)
                            ? Art.Material(
                                i == 0 ? "Blue beacon" : "Red beacon",
                                i == 0 ? Color.blue : Color.red,
                                true
                            )
                            : Art.White;
        }

        void MappedDrive()
        {
            if (Kind == "Emergency" && EmergencyActivity())
                return;
            if (Kind == "Parking" && parkingStage != 1)
            {
                // The driver boards before the vehicle can move; the same physical
                // bay manoeuvre is used on both maps, with map-specific endpoints.
                if (!DriverReady || OccupantDoorOpen || Time.time < parkingWait || HoldActivity)
                {
                    driver.Hold();
                    State = "Parked";
                    return;
                }
                Vector3 near = Path.Position(Path.NearestDistance(town.ParkingBay) - 15);
                Vector3 goal = parkingStage == 0 ? near : town.ParkingBay;
                State = parkingStage == 0 ? "Reversing out" : "Parking";
                if (
                    DriveAccess(
                        goal,
                        Path.Direction(Path.NearestDistance(goal)),
                        parkingStage == 0,
                        2
                    )
                )
                {
                    parkingStage = parkingStage == 0 ? 1 : 0;
                    Distance = Path.NearestDistance(body.position);
                    parkingWait = Time.time + 8;
                    if (parkingStage == 0)
                    {
                        ParkingVisits++;
                        DriverReady = false;
                    }
                }
                return;
            }
            var lane = town.Map.Nearest(body.position, transform.forward, false);
            float target = lane == null ? 5 : Mathf.Min(Kind == "Bus" ? 9 : 11.2f, lane.speedMps);
            State = "Following " + lane?.name;
            if (Kind == "Parking")
            {
                float remaining = RemainingTo(Path.NearestDistance(town.ParkingBay) - 30);
                State = "Departing / entering lot";
                target = Mathf.Min(target, 6);
                if (remaining < 45)
                {
                    State = "Searching for parking";
                    target = Mathf.Min(target, Mathf.Sqrt(Mathf.Max(0, remaining - .5f) * 3));
                }
                if (remaining < 1.5f && Speed < .3f)
                {
                    parkingStage = 2;
                    driver.Hold();
                    return;
                }
                if (Time.time < parkingWait || OccupantDoorOpen)
                    target = 0;
            }
            if (Kind == "Emergency" && (emergencyPhase == 2 || emergencyPhase == 6))
            {
                float remaining = Mathf.Repeat(
                    (emergencyPhase == 2 ? sceneJoin : stationJoin) - Distance,
                    Path.Length
                );
                remaining = RemainingTo(emergencyPhase == 2 ? sceneJoin : stationJoin);
                if (remaining < 65)
                    target = Mathf.Min(target, Mathf.Sqrt(Mathf.Max(0, remaining - .5f) * 4));
                if (remaining < 1.5f && Speed < .3f)
                {
                    emergencyPhase = emergencyPhase == 2 ? 3 : 7;
                    driver.Hold();
                    return;
                }
            }
            mappedStops.UpdatePosition(body.position);
            if (lane != null && !lane.internalLane)
            {
                float left = lane.Path.Length - lane.Path.NearestDistance(body.position);
                foreach (var stop in town.Map.landmarks)
                    if (stop.kind == "stop_sign" && stop.ControlsLane(lane.id))
                    {
                        var shape = GetComponent<BoxCollider>();
                        float ahead =
                            Vector3.Dot(stop.position - body.position, transform.forward)
                            - shape.center.z
                            - shape.size.z * .5f;
                        if (
                            mappedStops.RequiresStop(
                                stop.sourceId,
                                stop.position,
                                Speed,
                                ahead,
                                PlannerStep
                            )
                        )
                        {
                            target = Mathf.Min(target, Mathf.Sqrt(Mathf.Max(0, ahead - 1.5f) * 4));
                            State = "Stop sign";
                        }
                    }
                if (
                    !string.IsNullOrEmpty(lane.junction)
                    && town.MappedJunctions.TryGetValue(lane.junction, out var junction)
                )
                {
                    float ahead = Vector3.Dot(
                        junction.transform.position - body.position,
                        transform.forward
                    );
                    if (left > 0 && left < 40)
                    {
                        if (exitLane != lane.id)
                        {
                            exitLane = lane.id;
                            exitDistance = Distance + left + 25;
                            for (float d = left + 3; d < left + 75; d += 3)
                            {
                                var following = town.Map.Nearest(
                                    Path.Position(Distance + d),
                                    Path.Direction(Distance + d),
                                    false
                                );
                                if (
                                    following != null
                                    && following.id != lane.id
                                    && !following.internalLane
                                )
                                {
                                    exitDistance =
                                        Distance
                                        + d
                                        + driver.Dynamics.Profile.collisionSize.z * .5f
                                        + 2;
                                    break;
                                }
                            }
                        }
                        bool clearTurn =
                            TrafficClearance.ClearExit(
                                Path,
                                exitDistance,
                                GetComponent<BoxCollider>()
                            )
                            && TrafficClearance.ClearJunctionSweep(
                                Path,
                                Distance + left,
                                exitDistance,
                                GetComponent<BoxCollider>()
                            );
                        Clearance = clearTurn
                            ? "Clear turn; reservation=" + junction.Reservation
                            : "Blocked turn: " + TrafficClearance.LastBlocker;
                        if (!clearTurn)
                            junction.ReleaseBlockedApproach(this);
                        if (!clearTurn || !junction.Reserve(this))
                        {
                            // Leave room for the entire turning bus envelope,
                            // not only its centreline, before claiming the junction.
                            target = Mathf.Min(
                                target,
                                Mathf.Sqrt(Mathf.Max(0, left - junction.WaitingSetback) * 4)
                            );
                            State = "Yield junction";
                        }
                    }
                }
                if (
                    !string.IsNullOrEmpty(lane.signal)
                    && town.Map.SignalById.TryGetValue(lane.signal, out var signal)
                )
                {
                    char light = signal.State(lane.signalIndex, Time.time - town.Epoch);
                    if (light != 'G' && light != 'g')
                    {
                        target = Mathf.Min(target, Mathf.Sqrt(Mathf.Max(0, left - 3) * 5));
                        State = "Signal queue";
                    }
                    else if (light == 'g')
                        target = Mathf.Min(target, 4); // permissive turn, physical clearance still applies
                }
                if (lane.boundary)
                    target = Mathf.Min(target, Mathf.Sqrt(Mathf.Max(0, left - 10) * 4));
            }
            if (Kind == "Bus" && mappedBusStops != null && mappedBusStops.Length > 0)
            {
                if (busMustDepart && Vector3.Distance(body.position, lastBusStopPosition) > 20)
                    busMustDepart = false;
                float remaining = Mathf.Repeat(
                    mappedBusStops[busStop % mappedBusStops.Length] - Distance,
                    Path.Length
                );
                remaining = RemainingTo(mappedBusStops[busStop % mappedBusStops.Length]);
                if (!busMustDepart && remaining < 50)
                    target = Mathf.Min(target, Mathf.Sqrt(Mathf.Max(0, remaining - .5f) * 3));
                if (!busMustDepart && remaining < 1.5f && Speed < .3f && stopUntil == 0)
                {
                    stopUntil = Time.time + 10;
                    StopVisits++;
                }
                if (stopUntil > 0)
                {
                    target = 0;
                    State = "Boarding";
                    if (Time.time > stopUntil && BoardingUsers == 0)
                    {
                        busStop++;
                        stopUntil = 0;
                        busMustDepart = true;
                        lastBusStopPosition = body.position;
                        departAfter = Time.time + 1.5f;
                    }
                }
                if (Time.time < departAfter)
                    target = 0;
            }
            float yieldOffset = 0;
            if (
                town.Emergency
                && town.Emergency != this
                && town.Emergency.Dispatching
                && (town.Emergency.transform.position - body.position).sqrMagnitude < 1600
                && (lane == null || !lane.internalLane)
                && !town.Signals.Any(signal =>
                    signal.ReservedBy(this)
                    && (signal.transform.position - body.position).sqrMagnitude < 35 * 35
                )
            )
            {
                target = Mathf.Min(target, 2);
                State = "Yielding to emergency vehicle";
                float proposed = TrafficClearance.YieldOffset(
                    lane,
                    lane == null ? 0 : lane.Path.NearestDistance(body.position),
                    driver.Dynamics.Profile.collisionSize.x,
                    driver.Dynamics.Profile.collisionSize.z
                );
                var shape = GetComponent<BoxCollider>();
                if (
                    proposed > 0
                    && TrafficClearance.ClearExit(Path, Distance + 4, shape, proposed)
                    && TrafficClearance.ClearExit(Path, Distance + 9, shape, proposed)
                )
                    yieldOffset = proposed;
            }
            foreach (var walker in town.Walkers)
                if (walker.Crossing)
                {
                    float limit = TrafficClearance.CrossingSpeedLimit(
                        GetComponent<BoxCollider>(),
                        walker.transform.position
                    );
                    if (limit < target)
                    {
                        target = limit;
                        State = "Yielding to crossing";
                    }
                }
            if (ScenarioSpeed >= 0)
                target = Mathf.Min(target, ScenarioSpeed);
            lateral = Mathf.MoveTowards(
                lateral,
                ScenarioLateral + yieldOffset,
                PlannerStep * Mathf.Min(1.2f, Speed * .35f)
            );
            var envelope = GetComponent<BoxCollider>();
            float forwardLimit = TrafficClearance.ForwardSpeedLimit(
                envelope,
                Speed,
                immediate: true
            );
            string forwardBlocker = TrafficClearance.LastBlocker;
            float pathLimit = TrafficClearance.SafeSpeed(
                Path,
                Distance,
                lateral,
                Speed,
                envelope,
                staticObstacles: true
            );
            string pathBlocker = TrafficClearance.LastBlocker;
            if (
                Speed < 1.2f
                && forwardLimit < 1
                && pathLimit > forwardLimit
                && Mathf.Abs(driver.Dynamics.LastSteering) > 5
            )
            {
                float creep = TrafficClearance.TurningCreepLimit(
                    envelope,
                    driver.Dynamics.Profile.wheelbase,
                    driver.Dynamics.LastSteering
                );
                if (creep > forwardLimit)
                {
                    forwardLimit = creep;
                    forwardBlocker = "Checked turning clearance at walking speed";
                }
            }
            float railLimit = town.RailSpeedLimit(envelope);
            if (Mathf.Min(forwardLimit, pathLimit, railLimit) < target)
                Clearance =
                    railLimit <= Mathf.Min(forwardLimit, pathLimit)
                        ? "Railway warning or moving gate"
                    : forwardLimit <= pathLimit ? "Forward: " + forwardBlocker
                    : "Path: " + pathBlocker;
            target = Mathf.Min(target, forwardLimit, pathLimit, railLimit);
            RequestedSpeed = target;
            blockedSeconds =
                target < .1f
                && Speed < .15f
                && (State.StartsWith("Following") || State == "Yield junction")
                    ? blockedSeconds + PlannerStep
                    : 0;
            if (
                blockedSeconds > 20
                && Time.time >= nextReplan
                && (Kind == "Car" || Kind == "Truck")
            )
            {
                nextReplan = Time.time + 15;
                ReplanBlockedLane(lane);
            }
            driver.Follow(Path, target, lateral);
            if (door)
                door.localPosition = Vector3.Lerp(
                    door.localPosition,
                    new Vector3(1.18f, 1.3f, DoorOpen ? 4.6f : 3.7f),
                    PlannerStep * 3
                );
        }

        void ReplanBlockedLane(MapLane current)
        {
            if (current == null || current.internalLane)
                return;
            var blocked = town.Map.Nearest(
                Path.Position(Distance + 20),
                Path.Direction(Distance + 20)
            );
            var destination = town.Map.Nearest(
                Path.Position(Distance + 150),
                Path.Direction(Distance + 150)
            );
            if (
                blocked == null
                || blocked == current
                || destination == null
                || destination == blocked
            )
                return;
            var excluded = new HashSet<string> { blocked.id };
            var outward = town.Map.Route(current, destination, false, excluded);
            var inward = town.Map.Route(destination, current, false, excluded);
            if (outward.Count < 2 || inward.Count < 2)
                return;
            outward.AddRange(inward.Skip(1).Take(inward.Count - 2));
            Path = new TrackPath(outward.SelectMany(l => l.points), true);
            Distance = Path.NearestDistance(body.position);
            exitLane = null;
            blockedSeconds = 0;
            State = "Replanned around occupied lane";
        }

        bool EmergencyActivity()
        {
            if (flash != null)
                for (int i = 0; i < 2; i++)
                    flash[i].sharedMaterial =
                        Dispatching && ((Time.time * 5 % 2) < 1) == (i == 0)
                            ? Art.Material(
                                i == 0 ? "Blue beacon" : "Red beacon",
                                i == 0 ? Color.blue : Color.red,
                                true
                            )
                            : Art.White;
            switch (emergencyPhase)
            {
                case 0:
                    State = "At emergency station";
                    driver.Hold();
                    if (Time.time > dispatchAt)
                        Dispatch();
                    return true;
                case 1:
                    State = "Leaving station";
                    town.GarageDoor?.Open();
                    if (town.GarageDoor && !town.GarageDoor.IsPassable)
                    {
                        driver.Hold();
                        return true;
                    }
                    if (MoveAccess(Path.Position(stationJoin) + Vector3.up * .1f))
                    {
                        Distance = stationJoin;
                        emergencyPhase = 2;
                    }
                    return true;
                case 2:
                    State = "Responding to dispatch";
                    return false;
                case 3:
                    State = "Arriving at incident";
                    if (MoveAccess(incident))
                    {
                        emergencyPhase = 4;
                        stopUntil = Time.time + 25;
                        GetComponent<AudioSource>().Stop();
                    }
                    return true;
                case 4:
                    State = "On scene";
                    driver.Hold();
                    if (Time.time >= stopUntil)
                    {
                        emergencyPhase = 5;
                        Dispatching = false;
                    }
                    return true;
                case 5:
                    State = "Merging after response";
                    if (MoveAccess(Path.Position(sceneJoin + 30) + Vector3.up * .1f))
                    {
                        Distance = sceneJoin + 30;
                        emergencyPhase = 6;
                    }
                    return true;
                case 6:
                    State = "Returning to station";
                    return false;
                case 7:
                    State = "Entering station";
                    town.GarageDoor?.Open();
                    if (
                        town.GarageDoor
                        && !town.GarageDoor.IsPassable
                        && Vector3.Distance(transform.position, station) < 13
                    )
                    {
                        driver.Hold();
                        return true;
                    }
                    if (MoveAccess(station))
                    {
                        emergencyPhase = 0;
                        dispatchAt = Time.time + 90;
                        Speed = 0;
                    }
                    return true;
            }
            return false;
        }

        bool MoveAccess(Vector3 goal)
        {
            Vector3 facing =
                emergencyPhase == 7 && !town.Map.IsDavis
                    ? Vector3.left
                    : Path.Direction(
                        emergencyPhase == 1 || emergencyPhase == 7 ? stationJoin : sceneJoin
                    );
            return DriveAccess(goal, facing, emergencyPhase == 7, 2.5f);
        }

        bool DriveAccess(Vector3 goal, Vector3 facing, bool reverse, float speed)
        {
            if (accessPath == null || (accessGoal - goal).sqrMagnitude > .05f)
            {
                accessGoal = goal;
                accessPath = PhysicalDriver.AccessPath(
                    body.position,
                    transform.forward,
                    goal,
                    facing,
                    reverse
                );
            }
            float remaining = accessPath.Length - accessPath.NearestDistance(body.position);
            float error = Vector3.ProjectOnPlane(goal - body.position, Vector3.up).magnitude;
            accessBlock = "";
            if (error < .8f && Speed < .25f)
            {
                driver.Hold();
                accessPath = null;
                return true;
            }
            float target = Mathf.Min(speed, Mathf.Sqrt(Mathf.Max(0, remaining - .35f) * 2.5f));
            target = Mathf.Min(
                target,
                TrafficClearance.SafeSpeed(
                    accessPath,
                    accessPath.NearestDistance(body.position),
                    0,
                    Speed,
                    GetComponent<BoxCollider>()
                )
            );
            if (!AccessClear(transform.forward * (reverse ? -1 : 1), 3))
                target = 0;
            if (
                (
                    (emergencyPhase == 1 || emergencyPhase == 5)
                    && remaining < 12
                    && !MergeClear(goal)
                )
            )
            {
                target = 0;
                accessBlock = "waiting for merge";
            }
            driver.Follow(accessPath, target, 0, reverse);
            RequestedSpeed = target;
            return false;
        }

        bool MergeClear(Vector3 point)
        {
            foreach (var other in town.Traffic)
            {
                if (other == this)
                    continue;
                Vector3 offset = point - other.transform.position;
                float ahead = Vector3.Dot(offset, other.transform.forward);
                float clearance = other.Kind == "Bus" ? 9 : 6;
                if (
                    offset.magnitude < clearance
                    || (
                        ahead > 0
                        && ahead < clearance + other.Speed * 2
                        && Vector3.Cross(offset, other.transform.forward).magnitude < 4
                    )
                )
                    return false;
            }
            var player = town.Player;
            return !player || (player.position - point).sqrMagnitude > 144;
        }

        bool AccessClear(Vector3 direction, float distance)
        {
            foreach (
                var hit in Physics.BoxCastAll(
                    transform.position + Vector3.up,
                    new Vector3(.85f, .65f, 2.25f),
                    direction,
                    transform.rotation,
                    distance,
                    ~0,
                    QueryTriggerInteraction.Ignore
                )
            )
            {
                if (hit.collider.GetComponentInParent<TrafficActor>() == this)
                    continue;
                if (hit.collider.gameObject.layer != TrafficClearance.RoadLayer)
                {
                    accessBlock = "blocked by " + hit.collider.name;
                    return false;
                }
            }
            return true;
        }

        void ParkingCycle()
        {
            if (occupantDoor)
                occupantDoor.localRotation = Quaternion.RotateTowards(
                    occupantDoor.localRotation,
                    Quaternion.Euler(0, OccupantDoorOpen ? 65 : 0, 0),
                    PlannerStep * 65
                );
            if (HoldActivity)
            {
                driver.Hold();
                State = "Waiting at parking exit";
                return;
            }
            if (
                !DriverReady
                || OccupantDoorOpen
                || (
                    occupantDoor
                    && Quaternion.Angle(occupantDoor.localRotation, Quaternion.identity) > 5
                )
            )
            {
                driver.Hold();
                State = "Waiting for driver / door";
                return;
            }
            Vector3[] route =
            {
                new Vector3(-67, .1f, -43),
                new Vector3(-23, .1f, -43),
                new Vector3(2, .1f, -43),
                new Vector3(2, .1f, -35),
                new Vector3(-23, .1f, -35),
                new Vector3(-67, .1f, -35),
                new Vector3(-67, .1f, -51),
            };
            if (Time.time < parkingWait)
            {
                driver.Hold();
                State = "Parked · driver activity";
                return;
            }
            parkingGoal = route[parkingStage];
            Vector3 direction = parkingGoal - transform.position;
            bool reverse = parkingStage == 0;
            State =
                reverse ? "Reversing out"
                : parkingStage == 6 ? "Parking"
                : parkingStage == 4 ? "Searching for parking"
                : "Departing / entering lot";
            float distance = direction.magnitude;
            Vector3 endDirection =
                parkingStage == 0 ? Vector3.back
                : parkingStage == 1 || parkingStage == 2 ? Vector3.right
                : parkingStage == 3 ? Vector3.forward
                : parkingStage == 6 ? Vector3.back
                : Vector3.left;
            if (DriveAccess(parkingGoal, endDirection, reverse, 2))
            {
                parkingStage++;
                if (parkingStage >= route.Length)
                {
                    parkingStage = 0;
                    parkingWait = Time.time + 12;
                    ParkingVisits++;
                    DriverReady = false;
                }
                return;
            }
        }
    }

    public sealed class Walker : MonoBehaviour
    {
        public string State = "Walking";
        public bool IsAnimal,
            Crossing;
        public string Species;
        Town town;
        NavMeshAgent agent;
        Transform[] limbs;
        Transform head;
        Vector3 home,
            destination;
        Vector3 crossingTravel = Vector3.right * 12;
        Vector3 crossingProgressPosition;
        float crossingProgressAt,
            nextCrossingReplan;
        float nextAction,
            phase;
        int index,
            variant,
            lastVariant = -1;
        System.Random rng;
        RigMotion rig;
        AnimalMotion animalRig;
        float flightStart = -1,
            nextFlight,
            flightDuration;
        Vector3 flightFrom,
            flightTo;
        int boardedAt;
        int missedTransitStop = -1;
        float boardingApproachUntil;
        ITransit reservedTransit;
        Vector3 transitGoal;
        bool transitCounted;
        ActivityDoor visiting;
        float visitStarted,
            nextVisit;
        public bool VisitingBuilding => visiting != null;
        ParkingOccupant parkingOccupant;
        ActorLocomotion locomotion;
        public float ActualSpeed => locomotion ? locomotion.Speed : 0;
        public string NavigationDetail =>
            name
            + " "
            + State
            + " at="
            + transform.position.ToString("F2")
            + " crossing="
            + Crossing
            + " remaining="
            + (
                agent && agent.enabled && agent.isOnNavMesh
                    ? agent.remainingDistance.ToString("F2")
                        + " nav="
                        + agent.nextPosition.ToString("F2")
                        + " goal="
                        + agent.destination.ToString("F2")
                        + " path="
                        + agent.pathStatus
                        + " stopped="
                        + agent.isStopped
                        + " desired="
                        + agent.desiredVelocity.ToString("F2")
                    : "offmesh"
            )
            + (visiting ? " door=" + visiting.Approach.ToString("F2") : "");
        int workPhase = -1;
        bool scriptedCrossing,
            scriptedWaiting;
        Vector3 scriptedGoal;

        public void PrepareCrossing(Vector3 start, Vector3 finish)
        {
            scriptedWaiting = true;
            scriptedGoal = finish;
            agent.areaMask = NavMesh.AllAreas;
            agent.Warp(start);
            var body = GetComponent<Rigidbody>();
            body.position = start;
            body.linearVelocity = body.angularVelocity = Vector3.zero;
            agent.isStopped = true;
            State = "Waiting for scenario crossing";
        }

        public void BeginCrossing()
        {
            scriptedWaiting = false;
            scriptedCrossing = Crossing = true;
            State = "Crossing";
            agent.areaMask = NavMesh.AllAreas;
            Go(scriptedGoal);
            StartCrossingProgress();
        }

        void StartCrossingProgress()
        {
            crossingProgressPosition = transform.position;
            crossingProgressAt = Time.time;
            nextCrossingReplan = Time.time + 3;
        }

        void RecoverCrossingProgress(Vector3 goal)
        {
            if ((transform.position - crossingProgressPosition).sqrMagnitude > .15f * .15f)
            {
                crossingProgressPosition = transform.position;
                crossingProgressAt = Time.time;
            }
            if (
                Time.time - crossingProgressAt < 3
                || Time.time < nextCrossingReplan
                || agent.pathPending
            )
                return;
            // Re-query navigation after a rejected path or local-avoidance stall.
            // Locomotion still checks vehicles and applies bounded physical force;
            // this never changes an actor pose or permits crossing through a car.
            Go(goal);
            nextCrossingReplan = Time.time + 3;
        }

        public void Initialize(Town t, int id)
        {
            town = t;
            index = id;
            rng = new System.Random(t.Seed * 100 + id);
            IsAnimal = id >= 16 && id < 20 || id >= 52;
            int animalVariant = id % 4;
            Species =
                !IsAnimal ? "Person"
                : animalVariant == 0 ? "Dog"
                : animalVariant == 1 ? "Cat"
                : "Bird";
            gameObject.name = Species + " " + id;
            home = new Vector3(-170 + (id % 4) * 45, 0, -206 - (id / 4) * 200);
            if (home.z < -410)
                home.z = -6 - (id % 2) * 200;
            if (IsAnimal)
                home = new Vector3(-65 + (id - 16) * 14, 0, -6);
            if (id < 2)
                home = new Vector3(-105 + id * 3, 0, -206);
            if (id == 11)
                home = new Vector3(-78, 0, -60);
            if (id == 12)
                home = new Vector3(-96, .2f, 105);
            if (id == 13)
                home = new Vector3(-96, .2f, -4.5f);
            if (id == 14 || id == 15)
                home = new Vector3(211 + (id - 14) * 3, 0, -104);
            if (t.Map.IsDavis)
            {
                var crossings = t
                    .Map.landmarks.Where(l =>
                        l.kind == "crossing" && t.Map.Contains(l.position, 20)
                    )
                    .ToArray();
                if (id >= 20)
                    crossings = crossings
                        .OrderBy(c =>
                            (c.position - (t.Player ? t.Player.position : t.Map.Spawn)).sqrMagnitude
                        )
                        .Take(8)
                        .ToArray();
                var at = crossings[id * 5 % crossings.Length].position;
                var lane = t.Map.Nearest(at, routable: false);
                var forward = lane.Path.Direction(lane.Path.NearestDistance(at));
                home = at + Vector3.Cross(Vector3.up, forward) * 6;
                if (id >= 20)
                    home += forward * (3 + (id / 8) * 2.5f);
                if (id == 11)
                    home = t.ParkingBay + Vector3.right * 4;
                if (id == 14 || id == 15)
                    home = t.Construction.transform.position + new Vector3(3 + (id - 14) * 2, 0, 4);
                if (id < 2 || id == 13)
                {
                    var stops = t.Bus.ServedBusStops;
                    if (stops.Length > 0)
                    {
                        home = stops[id % stops.Length].position;
                        if (id == 13)
                            home -= t.Bus.Path.Direction(t.Bus.Path.NearestDistance(home)) * 1.5f;
                    }
                }
                if (id == 12)
                    home = t.Map.landmarks.First(l => l.subtype == "station").position;
            }
            transform.position = home;
            phase = id;
            Material coat = Art.Material(
                "Clothes " + id,
                Color.HSVToRGB(id * .173f % 1, .3f, .65f)
            );
            var person = IsAnimal
                ? Resources.Load<GameObject>(
                    "Scenery/Prepared/" + (Species == "Bird" ? "Eagle" : Species)
                )
                : Resources.Load<GameObject>(
                    "Actors/"
                        + (id == 14 || id == 15 ? "Worker" : "Person")
                        + (id % 2 == 0 ? "Male" : "Female")
                );
            if (person)
            {
                if (Species == "Cat")
                    person = Resources.Load<GameObject>("WorldArt/Animals/Cat") ?? person;
                if (!IsAnimal && id != 14 && id != 15)
                    person =
                        Resources.Load<GameObject>(
                            "WorldArt/Civilians/"
                                + (
                                    id % 5 == 0 ? "Civilian"
                                    : id % 2 == 0 ? "Citizen_A"
                                    : "Citizen_B"
                                )
                        ) ?? person;
                var model = Instantiate(person, transform);
                if (IsAnimal)
                {
                    animalRig = model.AddComponent<AnimalMotion>();
                    animalRig.Initialize();
                }
                else
                {
                    rig = model.AddComponent<RigMotion>();
                    rig.Initialize();
                }
                limbs = new Transform[0];
            }
            else if (!IsAnimal)
            {
                Art.Shape(
                    "Torso",
                    transform,
                    new Vector3(0, 1.15f, 0),
                    new Vector3(.43f, .5f, .25f),
                    coat,
                    PrimitiveType.Capsule
                );
                head = Art.Shape(
                    "Head",
                    transform,
                    new Vector3(0, 1.65f, 0),
                    new Vector3(.26f, .3f, .27f),
                    Art.Material(
                        "Skin " + id,
                        Color.Lerp(
                            new Color(.35f, .22f, .15f),
                            new Color(.78f, .58f, .43f),
                            (id % 5) / 4f
                        )
                    ),
                    PrimitiveType.Sphere
                ).transform;
                limbs = new Transform[4];
                for (int i = 0; i < 4; i++)
                {
                    var pivot = new GameObject(i < 2 ? "Hip" : "Shoulder").transform;
                    pivot.SetParent(transform, false);
                    pivot.localPosition = new Vector3(
                        (i % 2 == 0 ? -1 : 1) * (i < 2 ? .13f : .27f),
                        i < 2 ? .85f : 1.4f,
                        0
                    );
                    Art.Shape(
                        "Limb",
                        pivot,
                        new Vector3(0, -.32f, 0),
                        new Vector3(.13f, .34f, .15f),
                        i < 2 ? Art.Rubber : coat,
                        PrimitiveType.Capsule
                    );
                    limbs[i] = pivot;
                }
            }
            else
            {
                float size =
                    Species == "Dog" ? .55f
                    : Species == "Cat" ? .35f
                    : .22f;
                Art.Shape(
                    Species,
                    transform,
                    new Vector3(0, size, 0),
                    new Vector3(size, size * .65f, size * 1.8f),
                    coat,
                    PrimitiveType.Capsule
                ).transform.localRotation = Quaternion.Euler(90, 0, 0);
                head = Art.Shape(
                    "Head",
                    transform,
                    new Vector3(0, size * 1.4f, size * .7f),
                    Vector3.one * size * .7f,
                    coat,
                    PrimitiveType.Sphere
                ).transform;
                limbs = new Transform[4];
                for (int i = 0; i < 4; i++)
                    limbs[i] = Art.Box(
                        "Leg",
                        transform,
                        new Vector3(
                            (i % 2 == 0 ? -1 : 1) * size * .3f,
                            size * .4f,
                            (i < 2 ? -1 : 1) * size * .55f
                        ),
                        new Vector3(.07f, size * .7f, .07f),
                        coat,
                        false
                    ).transform;
            }
            var capsule = gameObject.AddComponent<CapsuleCollider>();
            gameObject.layer = TrafficClearance.WalkerLayer;
            capsule.radius =
                Species == "Cat" ? .14f
                : Species == "Bird" ? .12f
                : .25f;
            capsule.height =
                Species == "Cat" ? .35f
                : Species == "Bird" ? .24f
                : IsAnimal ? .6f
                : 1.8f;
            capsule.center = Vector3.up * capsule.height / 2;
            if (Species == "Dog" || Species == "Cat")
            {
                capsule.direction = 2;
                capsule.height = Species == "Dog" ? .9f : .58f;
                capsule.radius = Species == "Dog" ? .24f : .14f;
                // Keep the navigation/visual origin at the supporting foot plane.
                // A belly-centred capsule settles below it and falsely activates
                // off-mesh recovery in the middle of an otherwise valid crossing.
                capsule.center = Vector3.up * capsule.radius;
            }
            var rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = false;
            agent = gameObject.AddComponent<NavMeshAgent>();
            agent.areaMask = PedestrianNavigation.WalkingMask;
            agent.radius = .28f;
            agent.height = IsAnimal ? .6f : 1.8f;
            agent.speed = IsAnimal ? 1.8f : 1.25f;
            agent.angularSpeed = 180;
            agent.acceleration = 3;
            if (id >= 20)
            {
                Physics.SyncTransforms();
                for (int attempt = 0; attempt < 120; attempt++)
                {
                    float angle = attempt * 2.4f;
                    var candidate =
                        home
                        + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle))
                            * Mathf.Sqrt(attempt)
                            * 2.6f;
                    if (
                        NavMesh.SamplePosition(
                            candidate,
                            out var clear,
                            2,
                            PedestrianNavigation.WalkingMask
                        )
                        && PedestrianNavigation.TryWalkRoute(clear.position, rng, out _, 12)
                        && !Physics.CheckCapsule(
                            clear.position + Vector3.up * .4f,
                            clear.position + Vector3.up * 1.5f,
                            .35f,
                            ~(1 << TrafficClearance.RoadLayer),
                            QueryTriggerInteraction.Ignore
                        )
                    )
                    {
                        home = clear.position;
                        break;
                    }
                }
            }
            if (NavMesh.SamplePosition(home, out var hit, 10, PedestrianNavigation.WalkingMask))
                agent.Warp(hit.position);
            rb.position = transform.position;
            locomotion = gameObject.AddComponent<ActorLocomotion>();
            locomotion.Initialize(Species, person ? transform.GetChild(0) : null);
            destination = home;
            nextAction = Time.time + 3 + (id % 16) * .8f;
            WalkAbout();
            nextFlight = Time.time + 8 + id;
            if (id < 2 || id == 12 || id == 13)
            {
                State = "Waiting for transit";
                Go(home);
                nextAction = 0;
            }
            if (id == 11)
            {
                parkingOccupant = gameObject.AddComponent<ParkingOccupant>();
                parkingOccupant.Initialize(town, this, agent, rig);
            }
        }

        bool Go(Vector3 p)
        {
            if (agent.enabled && agent.isOnNavMesh)
            {
                var route = new NavMeshPath();
                if (
                    !NavMesh.SamplePosition(p, out var target, 3, agent.areaMask)
                    || !NavMesh.CalculatePath(
                        agent.nextPosition,
                        target.position,
                        agent.areaMask,
                        route
                    )
                    || route.status != NavMeshPathStatus.PathComplete
                )
                {
                    agent.isStopped = true;
                    return false;
                }
                agent.isStopped = false;
                agent.SetPath(route);
                return true;
            }
            return false;
        }

        public bool WalkAbout()
        {
            if (!agent || !agent.enabled || !agent.isOnNavMesh)
                return false;
            agent.areaMask = PedestrianNavigation.WalkingMask;
            Crossing = false;
            if (PedestrianNavigation.TryWalkRoute(agent.nextPosition, rng, out var route))
            {
                State = IsAnimal ? "Exploring" : "Walking";
                agent.isStopped = false;
                agent.SetPath(route);
                return true;
            }
            State = IsAnimal ? "Resting" : "Looking around";
            agent.isStopped = true;
            return false;
        }

        public void RequestCrossing()
        {
            if (
                agent == null
                || !agent.enabled
                || !agent.isOnNavMesh
                || (parkingOccupant && parkingOccupant.OwnsNavigation)
                || State == "Passenger"
                || transitCounted
                || visiting
            )
                return;
            if (Species == "Bird")
            {
                nextFlight = Time.time;
                return;
            }
            agent.areaMask = PedestrianNavigation.WalkingMask;
            if (
                !PedestrianNavigation.TryCrossing(
                    town,
                    agent.nextPosition,
                    out destination,
                    out var far
                )
            )
            {
                State = "Looking around";
                nextAction = Time.time + 5;
                agent.isStopped = true;
                return;
            }
            crossingTravel = far - destination;
            State = "Approaching crossing";
            Go(destination);
            nextAction = Time.time + 7;
        }

        void Update()
        {
            if (parkingOccupant && parkingOccupant.OwnsNavigation)
                return;
            if (locomotion && locomotion.Reacting)
            {
                rig?.Animate(0, "Impact recovery", index);
                animalRig?.Animate(0, "Impact recovery", index);
                return;
            }
            if (scriptedWaiting || scriptedCrossing)
            {
                if (scriptedCrossing)
                    RecoverCrossingProgress(scriptedGoal);
                float velocity = scriptedCrossing ? ActualSpeed : 0;
                rig?.Animate(velocity, State, index);
                animalRig?.Animate(velocity, State, index);
                if (
                    scriptedCrossing
                    && !agent.pathPending
                    && Vector3
                        .ProjectOnPlane(transform.position - scriptedGoal, Vector3.up)
                        .magnitude < .4f
                )
                {
                    scriptedCrossing = Crossing = false;
                    agent.areaMask = PedestrianNavigation.WalkingMask;
                    nextAction = Time.time + 4;
                    State = "Idling";
                }
                return;
            }
            if (index == 14 || index == 15)
            {
                WorkerActivity();
                return;
            }
            if (BuildingActivity())
                return;
            if (Species == "Bird" && BirdActivity())
                return;
            if (TransitActivity())
                return;
            if (agent == null || !agent.isOnNavMesh)
                return;
            float speed = ActualSpeed;
            phase += speed * Time.deltaTime * 6;
            for (int i = 0; i < limbs.Length; i++)
                limbs[i].localRotation = Quaternion.Euler(
                    Mathf.Sin(phase + (i % 2) * Mathf.PI) * Mathf.Min(30, speed * 24),
                    0,
                    0
                );
            if (rig)
                rig.Animate(speed, State, variant);
            if (animalRig)
                animalRig.Animate(speed, State, variant);
            if (
                IsAnimal
                && town.Player
                && (town.Player.position - transform.position).sqrMagnitude < 64
                && town.Player.GetComponent<Vehicle>().Speed > 1
                && State != "Startled"
            )
            {
                State = "Startled";
                Crossing = false;
                agent.speed = Species == "Dog" ? 3.5f : 3;
                Vector3 away = (transform.position - town.Player.position).normalized;
                if (
                    NavMesh.SamplePosition(
                        transform.position + away * 12,
                        out var refuge,
                        8,
                        NavMesh.AllAreas
                    )
                )
                    Go(refuge.position);
                nextAction = Time.time + 4;
                return;
            }
            if (head)
            {
                if (State == "Looking")
                    head.localRotation = Quaternion.Euler(0, Mathf.Sin(Time.time * 3) * 50, 0);
                else
                    head.localRotation = Quaternion.Slerp(
                        head.localRotation,
                        Quaternion.identity,
                        Time.deltaTime * 3
                    );
            }
            if (Time.time < nextAction)
                return;
            if (State == "Approaching crossing")
            {
                if (agent.remainingDistance > 1)
                {
                    nextAction = Time.time + 1;
                    return;
                }
                State = "Looking";
                agent.isStopped = true;
                nextAction = Time.time + 2;
                return;
            }
            if (State == "Looking" || State == "Waiting")
            {
                var sig = town.ClosestSignal(transform.position);
                bool clear = TrafficClearance.CrossingPathClear(
                    destination,
                    destination + crossingTravel
                );
                foreach (var car in town.Traffic)
                    if (
                        (car.transform.position - transform.position).sqrMagnitude < 180
                        && car.Speed > 1
                    )
                        clear = false;
                if (
                    town.Player
                    && (town.Player.position - transform.position).sqrMagnitude < 180
                    && town.Player.GetComponent<Vehicle>().Speed > 1
                )
                    clear = false;
                if (!sig.PedestrianGreen || !clear)
                {
                    State = "Waiting";
                    nextAction = Time.time + 1;
                    return;
                }
                Crossing = true;
                State = "Crossing";
                destination += crossingTravel;
                agent.areaMask = NavMesh.AllAreas;
                Go(destination);
                StartCrossingProgress();
                nextAction = Time.time + 1;
                return;
            }
            if (State == "Crossing")
            {
                RecoverCrossingProgress(destination);
                if (
                    Vector3.ProjectOnPlane(transform.position - destination, Vector3.up).magnitude
                        > .65f
                    || !NavMesh.SamplePosition(
                        transform.position,
                        out _,
                        .6f,
                        PedestrianNavigation.WalkingMask
                    )
                )
                {
                    nextAction = Time.time + 1;
                    return;
                }
                Crossing = false;
                agent.areaMask = PedestrianNavigation.WalkingMask;
            }
            if (IsAnimal)
                agent.speed = 1.8f;
            do
            {
                variant = rng.Next(6);
            } while (variant == lastVariant);
            lastVariant = variant;
            if (variant == 0)
            {
                State = IsAnimal ? "Resting" : "Idling";
                agent.isStopped = true;
            }
            else if (variant == 1)
            {
                bool partner = false;
                if (!IsAnimal)
                    foreach (var walker in town.Walkers)
                        if (
                            walker != this
                            && !walker.IsAnimal
                            && (walker.transform.position - transform.position).sqrMagnitude < 6.25f
                            && (
                                walker.State == "Idling"
                                || walker.State == "Conversing"
                                || walker.State == "Looking around"
                            )
                        )
                        {
                            partner = true;
                            Vector3 toward = walker.transform.position - transform.position;
                            toward.y = 0;
                            if (toward.sqrMagnitude > .1f)
                                transform.rotation = Quaternion.LookRotation(toward);
                            break;
                        }
                State =
                    IsAnimal ? "Grooming"
                    : partner ? "Conversing"
                    : "Looking around";
                agent.isStopped = true;
            }
            else if (variant == 2)
            {
                RequestCrossing();
                return;
            }
            else if (
                !IsAnimal
                && variant == 3
                && town.Bus.DoorOpen
                && (town.Bus.transform.position - transform.position).sqrMagnitude < 144
            )
            {
                State = "Waiting for transit";
            }
            else if (!IsAnimal && variant == 4)
            {
                VisitBuilding();
                return;
            }
            else
            {
                WalkAbout();
            }
            nextAction = Time.time + 4 + (float)rng.NextDouble() * 7;
        }

        public void VisitBuilding()
        {
            if (
                IsAnimal
                || index == 14
                || index == 15
                || State == "Passenger"
                || State.Contains("transit")
            )
                return;
            if (Time.time < nextVisit)
            {
                WalkAbout();
                nextAction = Time.time + 6;
                return;
            }
            // Choose a reachable, available entrance before taking its reservation.
            // A busy shop is a reason to continue the journey, not to crowd three
            // shared queue points for fifty seconds and repeat indefinitely.
            foreach (
                var door in town.Doors.OrderBy(d => (d.Approach - transform.position).sqrMagnitude)
            )
            {
                if (
                    (door.Approach - transform.position).sqrMagnitude > 10000
                    || !door.AvailableFor(this)
                    || !Go(door.Approach)
                )
                    continue;
                if (door.Reserve(this))
                {
                    visiting = door;
                    visitStarted = Time.time;
                    State = "Visiting building";
                    nextVisit = Time.time + 60 + (float)rng.NextDouble() * 30;
                    return;
                }
            }
            nextVisit = Time.time + 25 + (float)rng.NextDouble() * 20;
            WalkAbout();
            nextAction = Time.time + 6;
        }

        bool BuildingActivity()
        {
            if (!visiting || agent == null || !agent.enabled || !agent.isOnNavMesh)
                return false;
            if (
                Time.time - visitStarted > 50
                || (
                    !agent.pathPending
                    && agent.hasPath
                    && agent.pathStatus != NavMeshPathStatus.PathComplete
                    && Time.time - visitStarted > 5
                )
            )
            {
                visiting.Release(this);
                visiting = null;
                State = "Walking";
                WalkAbout();
                nextAction = Time.time + 10;
                return true;
            }
            if (rig)
                rig.Animate(ActualSpeed, State, variant);
            if (State == "Waiting for entrance")
            {
                if (visiting.Reserve(this))
                {
                    State = "Visiting building";
                    Go(visiting.Approach);
                }
                return true;
            }
            if (State == "Visiting building")
            {
                if (!agent.pathPending && agent.remainingDistance < .5f)
                {
                    visiting.Open();
                    agent.isStopped = true;
                    State = "Opening door";
                }
                return true;
            }
            if (State == "Opening door")
            {
                visiting.Open();
                if (visiting.IsPassable)
                {
                    State = "Entering building";
                    Go(visiting.Inside);
                }
                return true;
            }
            if (State == "Entering building")
            {
                visiting.Open();
                if (!agent.pathPending && agent.remainingDistance < .4f)
                {
                    State = "Inside building";
                    agent.isStopped = true;
                    nextAction = Time.time + 8;
                }
                return true;
            }
            if (State == "Inside building")
            {
                if (Time.time >= nextAction)
                {
                    visiting.Open();
                    if (visiting.IsPassable)
                    {
                        State = "Leaving building";
                        Go(visiting.Approach);
                    }
                }
                return true;
            }
            if (State == "Leaving building")
            {
                visiting.Open();
                if (!agent.pathPending && agent.remainingDistance < .4f)
                {
                    visiting.Release(this);
                    visiting = null;
                    State = "Walking";
                    WalkAbout();
                    nextAction = Time.time + 6;
                }
                return true;
            }
            visiting.Release(this);
            visiting = null;
            return false;
        }

        void WorkerActivity()
        {
            if (agent == null || !agent.isOnNavMesh)
                return;
            float cycle = (Time.time + index * 3) % 24;
            int phase =
                cycle < 8 ? 0
                : cycle < 12 ? 1
                : cycle < 18 ? 2
                : 3;
            if (phase != workPhase)
            {
                workPhase = phase;
                if (phase == 0 || phase == 2)
                    agent.isStopped = true;
                else
                    Go(home + Vector3.right * (phase == 1 ? 3 : 0));
            }
            State =
                phase == 0 ? "Working"
                : phase == 2 ? "Inspecting machinery"
                : "Walking between tasks";
            if (rig)
                rig.Animate(ActualSpeed, State, index);
        }

        bool BirdActivity()
        {
            if (agent == null)
                return false;
            if (flightStart < 0 && Time.time >= nextFlight)
            {
                Vector3 landing = home + Vector3.right * ((float)rng.NextDouble() * 70 - 35);
                if (
                    !NavMesh.SamplePosition(
                        landing,
                        out var ground,
                        8,
                        PedestrianNavigation.WalkingMask
                    )
                )
                {
                    nextFlight = Time.time + 5;
                    return false;
                }
                flightFrom = transform.position;
                flightTo = ground.position;
                flightStart = Time.time;
                flightDuration = Mathf.Max(8, Vector3.Distance(flightFrom, flightTo) / 3);
                agent.enabled = false;
                Crossing = false;
            }
            if (flightStart < 0)
                return false;
            float progress = Mathf.Clamp01((Time.time - flightStart) / flightDuration);
            Vector3 target =
                Vector3.Lerp(flightFrom, flightTo, progress)
                + Vector3.up * (Mathf.Sin(progress * Mathf.PI) * 7);
            locomotion.FlyToward(target);
            Vector3 direction = target - transform.position;
            if (direction.sqrMagnitude > .0001f)
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    Quaternion.LookRotation(direction),
                    Time.deltaTime * 4
                );
            State =
                progress < .15f ? "Taking off"
                : progress > .85f ? "Landing"
                : "Flying";
            animalRig?.Animate(3, State, index);
            if (progress >= 1 && Vector3.Distance(transform.position, flightTo) < .3f)
            {
                locomotion.Land();
                flightStart = -1;
                nextFlight = Time.time + 20 + (float)rng.NextDouble() * 20;
                agent.enabled = true;
                agent.Warp(transform.position);
                agent.isStopped = true;
                State = "Resting";
                nextAction = Time.time + 6;
            }
            return true;
        }

        bool TransitActivity()
        {
            if (agent == null || IsAnimal)
                return false;
            ITransit bus =
                index == 12 ? town.Train
                : index == 13 && !town.Map.IsDavis ? town.Tram
                : town.Bus;
            if (State == "Waiting for transit")
            {
                if (rig)
                    rig.Animate(ActualSpeed, "Idling", index);
                if (
                    bus.DoorOpen
                    && bus.StopVisits != missedTransitStop
                    && (bus.DoorPoint - transform.position).sqrMagnitude < 144
                    && NavMesh.SamplePosition(
                        bus.DoorPoint + bus.transform.right * .7f,
                        out var point,
                        3,
                        PedestrianNavigation.WalkingMask
                    )
                )
                {
                    if (Go(point.position))
                    {
                        State = "Boarding";
                        boardingApproachUntil = Time.time + 25;
                        reservedTransit = bus;
                        bus.BoardingUsers++;
                        transitCounted = true;
                    }
                }
                return true;
            }
            if (State == "Boarding")
            {
                if (!bus.DoorOpen || Time.time > boardingApproachUntil)
                {
                    ReleaseTransit();
                    missedTransitStop = bus.StopVisits;
                    State = "Waiting for transit";
                    if (agent.isOnNavMesh)
                        agent.isStopped = true;
                    return true;
                }
                if (rig)
                    rig.Animate(ActualSpeed, "Walking", index);
                if (!agent.pathPending && agent.remainingDistance < .35f)
                {
                    agent.enabled = false;
                    GetComponent<Collider>().enabled = false;
                    transitGoal = bus.DoorPoint - bus.transform.right * .9f + Vector3.up * .75f;
                    State = "Entering transit";
                }
                return true;
            }
            if (State == "Entering transit" || State == "Leaving transit")
            {
                // Contacts are disabled during the doorway animation. Advance the
                // kinematic body's pose, not its interpolated render transform;
                // interpolation would otherwise undo each incremental step.
                var transitBody = GetComponent<Rigidbody>();
                Vector3 delta = transitGoal - transitBody.position;
                transitBody.position = Vector3.MoveTowards(
                    transitBody.position,
                    transitGoal,
                    Time.deltaTime * 1.1f
                );
                if (delta.sqrMagnitude > .01f)
                    transitBody.rotation = Quaternion.RotateTowards(
                        transitBody.rotation,
                        Quaternion.LookRotation(new Vector3(delta.x, 0, delta.z)),
                        Time.deltaTime * 180
                    );
                if (rig)
                    rig.Animate(1.1f, "Walking", index);
                if (delta.magnitude < .1f)
                {
                    ReleaseTransit();
                    if (State == "Entering transit")
                    {
                        State = "Passenger";
                        boardedAt = bus.StopVisits;
                        foreach (var r in GetComponentsInChildren<Renderer>())
                            r.enabled = false;
                    }
                    else
                    {
                        agent.enabled = true;
                        agent.Warp(transform.position);
                        GetComponent<Collider>().enabled = true;
                        State = "Walking";
                        home = transform.position;
                        Go(home + Vector3.right * 8);
                        nextAction = Time.time + 8;
                    }
                }
                return true;
            }
            if (State == "Passenger")
            {
                if (
                    bus.DoorOpen
                    && bus.StopVisits > boardedAt
                    && NavMesh.SamplePosition(
                        bus.DoorPoint + bus.transform.right * 1.6f,
                        out var outside,
                        3,
                        PedestrianNavigation.WalkingMask
                    )
                )
                {
                    GetComponent<Rigidbody>().position =
                        bus.DoorPoint - bus.transform.right * .9f + Vector3.up * .75f;
                    transitGoal = outside.position;
                    State = "Leaving transit";
                    reservedTransit = bus;
                    bus.BoardingUsers++;
                    transitCounted = true;
                    foreach (var r in GetComponentsInChildren<Renderer>())
                        r.enabled = true;
                }
                return true;
            }
            return false;
        }

        void ReleaseTransit()
        {
            if (transitCounted && reservedTransit is Component owner && owner)
                reservedTransit.BoardingUsers = Mathf.Max(0, reservedTransit.BoardingUsers - 1);
            transitCounted = false;
            reservedTransit = null;
        }

        void OnDisable()
        {
            if (visiting)
                visiting.Release(this);
            ReleaseTransit();
        }
    }

    public sealed class RailVehicle : MonoBehaviour, ITransit
    {
        public bool Streetcar;
        public bool HoldForScenario;
        public float Speed;
        public string State = "Cruising";
        public TrackPath Path;
        public string Clearance { get; private set; } = "";
        public string NavigationDetail =>
            "at="
            + transform.position.ToString("F2")
            + " heading="
            + transform.eulerAngles.y.ToString("F1")
            + " speed="
            + Speed.ToString("F2")
            + " path="
            + distance.ToString("F2")
            + " clearance="
            + Clearance
            + " dynamics="
            + string.Join(
                ";",
                physicsCars.Select(d =>
                    "target="
                    + d.TargetSpeed.ToString("F2")
                    + " derailed="
                    + d.Derailed
                    + " contact="
                    + d.Contact
                )
            );
        Town town;
        float distance,
            stopUntil;
        int stopIndex;
        float[] stops;
        Transform door;
        readonly List<Transform> carriages = new List<Transform>();
        readonly List<RailDynamics> physicsCars = new List<RailDynamics>();
        BoxCollider clearanceBody;
        public BoxCollider[] CollisionBodies { get; private set; }
        public bool DoorOpen => State == "Boarding";
        public bool Derailed => physicsCars.Any(d => d.Derailed);
        public Vector3 BoardingPoint = new Vector3(1.34f, 0, 4);
        public Vector3 DoorPoint => transform.TransformPoint(BoardingPoint);
        public int StopVisits { get; private set; }
        public int BoardingUsers { get; set; }
        float departAfter;
        int mappedDirection = 1;

        public void Initialize(Town t, bool tram)
        {
            town = t;
            Streetcar = tram;
            Path = tram
                ? new TrackPath(-200, 0, 200, 200, 20)
                : new TrackPath(-460, 112, 460, 470, 28);
            if (t.Map.IsDavis && !tram)
            {
                var mapped = t
                    .Map.rail.Select(r => new TrackPath(r.points, false))
                    .OrderByDescending(p => p.Length)
                    .First();
                var clipped = new List<Vector3>();
                for (float d = 0; d < mapped.Length; d += 2)
                {
                    var p = mapped.Position(d);
                    if (t.Map.Contains(p, 15))
                        clipped.Add(p);
                }
                Path = new TrackPath(clipped, false);
            }
            stops = tram
                ? new[]
                {
                    Path.NearestDistance(new Vector3(-100, 0, -2)),
                    Path.NearestDistance(new Vector3(100, 0, 202)),
                }
                : new[]
                {
                    Path.NearestDistance(new Vector3(-100, 0, 110)),
                    Path.NearestDistance(new Vector3(100, 0, 472)),
                };
            distance = stops[0] - (tram ? 40 : 60);
            if (t.Map.IsDavis && !tram)
            {
                var station = t.Map.landmarks.First(l => l.subtype == "station");
                stops = new[]
                {
                    60f,
                    Mathf.Clamp(Path.NearestDistance(station.position) - 4, 80, Path.Length - 40),
                    Path.Length - 20,
                };
                distance = 65;
                stopIndex = 1;
            }
            int count = tram ? 1 : 3;
            for (int i = 0; i < count; i++)
            {
                var carriage = new GameObject("Carriage " + i).transform;
                carriage.SetParent(transform, false);
                carriages.Add(carriage);
                if (Realism.Rail(carriage, this, i == 0))
                {
                    var shell = carriage.gameObject.AddComponent<BoxCollider>();
                    // The swept body needs undercarriage clearance over crossings.
                    // Rail guidance supports the wheels; the body must not drag.
                    shell.center = new Vector3(0, 1.85f, 0);
                    shell.size = new Vector3(2.65f, 2.9f, 13.5f);
                    continue;
                }
                if (i > 0)
                    Art.Box(
                        "Closed carriage door",
                        carriage,
                        new Vector3(1.34f, 1.9f, 4),
                        new Vector3(.04f, 2.5f, 1.3f),
                        Art.Glass,
                        false
                    );
                var paint = tram ? Art.Paint : Art.Metal;
                Art.Box(
                    "Rail floor",
                    carriage,
                    new Vector3(0, .55f, 0),
                    new Vector3(2.65f, .35f, 13.5f),
                    paint
                );
                Art.Box(
                    "Rail roof",
                    carriage,
                    new Vector3(0, 3.2f, 0),
                    new Vector3(2.65f, .2f, 13.5f),
                    paint
                );
                Art.Box(
                    "Rail left wall",
                    carriage,
                    new Vector3(-1.28f, 1.9f, 0),
                    new Vector3(.1f, 2.5f, 13.5f),
                    paint
                );
                Art.Box(
                    "Rail right rear wall",
                    carriage,
                    new Vector3(1.28f, 1.9f, -1.7f),
                    new Vector3(.1f, 2.5f, 10.1f),
                    paint
                );
                Art.Box(
                    "Rail right front wall",
                    carriage,
                    new Vector3(1.28f, 1.9f, 5.7f),
                    new Vector3(.1f, 2.5f, 2.1f),
                    paint
                );
                for (int s = -1; s <= 1; s += 2)
                {
                    Art.Box(
                        "Rail cab end",
                        carriage,
                        new Vector3(0, 1.9f, s * 6.7f),
                        new Vector3(2.6f, 2.5f, .1f),
                        paint
                    );
                    for (int axle = -1; axle <= 1; axle += 2)
                        Art.Wheel(carriage, new Vector3(s * .9f, .34f, axle * 4.8f));
                    for (int j = 0; j < 7; j++)
                    {
                        if (s == 1 && j == 5)
                            continue;
                        Art.Box(
                            "Rail window",
                            carriage,
                            new Vector3(s * 1.34f, 2.3f, -5.3f + j * 1.75f),
                            new Vector3(.02f, .8f, 1.2f),
                            Art.Glass,
                            false
                        );
                    }
                }
            }
            door = Art.Box(
                "Transit door",
                transform,
                new Vector3(1.34f, 1.9f, 4),
                new Vector3(.04f, 2.5f, 1.3f),
                Art.Glass,
                false
            ).transform;
            if (GetComponentInChildren<RailDoorVisual>())
                door.GetComponent<Renderer>().enabled = false;
            var rb = gameObject.AddComponent<Rigidbody>();
            if (tram)
            {
                clearanceBody = gameObject.AddComponent<BoxCollider>();
                clearanceBody.size = new Vector3(2.65f, 3, 13.5f);
                clearanceBody.center = Vector3.up * 1.9f;
            }
            foreach (var part in GetComponentsInChildren<Transform>())
                part.gameObject.layer = TrafficClearance.RailLayer;
            CollisionBodies = GetComponentsInChildren<BoxCollider>();
            PlaceForScenario(Path.Position(distance));
            var locomotive = gameObject.AddComponent<RailDynamics>();
            locomotive.Initialize(Path, tram ? 28000 : 38000);
            physicsCars.Add(locomotive);
            Rigidbody previousBody = rb;
            for (int i = 1; i < carriages.Count; i++)
            {
                carriages[i].SetParent(town.transform, true);
                var dynamics = carriages[i].gameObject.AddComponent<RailDynamics>();
                dynamics.Initialize(Path, 30000);
                physicsCars.Add(dynamics);
                var joint = carriages[i].gameObject.AddComponent<SpringJoint>();
                joint.autoConfigureConnectedAnchor = false;
                joint.connectedBody = previousBody;
                joint.anchor = new Vector3(0, 1, 7);
                joint.connectedAnchor = new Vector3(0, 1, -7);
                joint.minDistance = .5f;
                joint.maxDistance = 1.2f;
                joint.spring = 180000;
                joint.damper = 60000;
                previousBody = dynamics.Body;
            }
        }

        public void Accelerate()
        {
            if (stopUntil > 0)
                stopUntil = Time.time;
        }

        public void PlaceForScenario(Vector3 position)
        {
            distance = Path.NearestDistance(position);
            Speed = 0;
            stopUntil = 0;
            transform.SetPositionAndRotation(
                Path.Position(distance),
                Quaternion.LookRotation(Path.Direction(distance))
            );
            var rootBody = GetComponent<Rigidbody>();
            if (rootBody)
            {
                rootBody.position = transform.position;
                rootBody.rotation = transform.rotation;
                rootBody.linearVelocity = rootBody.angularVelocity = Vector3.zero;
            }
            for (int i = 0; i < carriages.Count; i++)
                carriages[i]
                    .SetPositionAndRotation(
                        Path.Position(distance - i * 15),
                        Quaternion.LookRotation(Path.Direction(distance - i * 15))
                    );
            foreach (var dynamics in physicsCars)
            {
                dynamics.Body.position = dynamics.transform.position;
                dynamics.Body.rotation = dynamics.transform.rotation;
                dynamics.Body.linearVelocity = dynamics.Body.angularVelocity = Vector3.zero;
            }
        }

        // World-actor avoidance only. No rail/map truth enters the camera observer.
        // Forecast measured movement along the guide without changing any pose.
        public bool ApproachesWalkingPath(Vector3 centre, Vector3 actorExtent, Vector3 walking)
        {
            if (!gameObject.activeInHierarchy || CollisionBodies == null)
                return false;
            bool movingConflict = false,
                standingConflict = false;
            foreach (var collider in CollisionBodies)
            {
                var body = collider ? collider.attachedRigidbody : null;
                if (
                    !collider
                    || !collider.enabled
                    || !body
                    || body.linearVelocity.sqrMagnitude < .09f
                    || (collider.bounds.center - centre).sqrMagnitude > 3600
                )
                    continue;
                var dynamics = body.GetComponent<RailDynamics>();
                bool guided = dynamics && !dynamics.Derailed && dynamics.Path != null;
                float along = guided ? dynamics.Along : 0;
                var tangent = guided ? dynamics.Path.Direction(along) : body.transform.forward;
                float speed = Vector3.Dot(body.linearVelocity, tangent);
                var localCentre = body.transform.InverseTransformPoint(
                    collider.transform.TransformPoint(collider.center)
                );
                var localRotation = Quaternion.Inverse(body.rotation) * collider.transform.rotation;
                var extent =
                    Vector3.Scale(collider.size * .5f, collider.transform.lossyScale)
                    + actorExtent
                    + new Vector3(.25f, .05f, .25f);
                for (float seconds = .25f; seconds <= 3; seconds += .25f)
                {
                    var position = body.position + body.linearVelocity * seconds;
                    var rotation =
                        Quaternion.AngleAxis(
                            body.angularVelocity.y * seconds * Mathf.Rad2Deg,
                            Vector3.up
                        ) * body.rotation;
                    if (guided)
                    {
                        position =
                            body.position
                            + dynamics.Path.Position(along + speed * seconds)
                            - dynamics.Path.Position(along);
                        rotation =
                            Quaternion.FromToRotation(
                                tangent,
                                dynamics.Path.Direction(along + speed * seconds)
                            ) * body.rotation;
                    }
                    var inverse = Quaternion.Inverse(rotation * localRotation);
                    var stationary = inverse * (centre - (position + rotation * localCentre));
                    var proposed = stationary + inverse * (walking * seconds);
                    bool Inside(Vector3 p) =>
                        Mathf.Abs(p.x) <= extent.x
                        && Mathf.Abs(p.y) <= extent.y
                        && Mathf.Abs(p.z) <= extent.z;
                    movingConflict |= Inside(proposed);
                    standingConflict |= Inside(stationary);
                }
            }
            // If already in the rail envelope, preserve motion out of it rather
            // than freezing an actor in front of an approaching vehicle.
            return movingConflict && !standingConflict;
        }

        public bool CrossingOccupied(Vector3 crossing)
        {
            if (Streetcar)
                return false;
            // Tail clearance and a seven-second approach interval belong to this
            // crossing only. Another crossing kilometres away stays open.
            return RailCrossing.Active(
                Path,
                distance,
                Speed,
                crossing,
                town.Map.IsDavis ? mappedDirection : 1
            );
        }

        void OnDestroy()
        {
            foreach (var carriage in carriages)
                if (carriage && !carriage.IsChildOf(transform))
                {
                    if (Application.isPlaying)
                        Destroy(carriage.gameObject);
                    else
                        DestroyImmediate(carriage.gameObject);
                }
        }

        void FixedUpdate()
        {
            if (physicsCars.Count == 0)
                return;
            distance = physicsCars[0].Along;
            Speed = physicsCars[0].Body.linearVelocity.magnitude;
            if (town.Map.IsDavis && !Streetcar)
            {
                float left = (stops[stopIndex] - distance) * mappedDirection;
                float desired = HoldForScenario
                    ? 0
                    : Mathf.Min(10, Mathf.Sqrt(Mathf.Max(0, left - .5f) * 2));
                if (left < 1.5f && Speed < .3f && stopUntil == 0)
                {
                    stopUntil = Time.time + 12;
                    StopVisits++;
                }
                if (stopUntil > 0)
                {
                    desired = 0;
                    State = "Boarding";
                    if (Time.time > stopUntil && BoardingUsers == 0)
                    {
                        if (stopIndex == 0 || stopIndex == stops.Length - 1)
                            mappedDirection *= -1;
                        stopIndex += mappedDirection;
                        stopUntil = 0;
                    }
                }
                else
                    State = "Rail service (simulated timetable)";
                foreach (var dynamics in physicsCars)
                    dynamics.TargetSpeed = desired * mappedDirection;
                return;
            }
            float target = Streetcar ? 7 : 12,
                remaining = Mathf.Repeat(stops[stopIndex] - distance, Path.Length);
            State = "Cruising";
            if (remaining < 55)
            {
                target = Mathf.Min(target, Mathf.Sqrt(Mathf.Max(0, remaining - .5f) * 2));
                State = "Approaching station";
            }
            if (remaining < 1 && Speed < .2f && stopUntil == 0)
            {
                stopUntil = Time.time + 12;
                StopVisits++;
            }
            if (stopUntil > 0)
            {
                target = 0;
                State = "Boarding";
                if (Time.time >= stopUntil && BoardingUsers == 0)
                {
                    stopIndex = (stopIndex + 1) % stops.Length;
                    stopUntil = 0;
                    State = "Departing station";
                    departAfter = Time.time + 1.5f;
                }
            }
            if (Time.time < departAfter)
                target = 0;
            if (HoldForScenario)
            {
                target = 0;
                State = "Waiting for scenario approach";
            }
            if (Streetcar)
            {
                var sig = town.ClosestSignal(transform.position);
                float ahead = Vector3.Dot(
                    sig.transform.position - transform.position,
                    transform.forward
                );
                bool green = sig.Green(Mathf.Abs(transform.forward.z) > .7f);
                bool committed = sig.ReservedBy(this) && ahead < 19.5f;
                if (ahead > 8 && ahead < 45 && !green && !committed)
                {
                    target = Mathf.Min(target, Mathf.Sqrt(Mathf.Max(0, ahead - 20) * 3));
                    State = "Signal queue";
                }
                if (ahead > 8 && ahead < 45 && green && !sig.Reserve(this))
                {
                    target = Mathf.Min(target, Mathf.Sqrt(Mathf.Max(0, ahead - 20) * 3));
                    State = "Yield intersection";
                }
                float forwardClearance = TrafficClearance.ForwardSpeedLimit(
                    clearanceBody,
                    Speed,
                    2
                );
                string forwardBlocker = TrafficClearance.LastBlocker;
                float pathClearance = TrafficClearance.SafeSpeed(
                    Path,
                    distance,
                    0,
                    Speed,
                    clearanceBody,
                    2
                );
                float clearance = Mathf.Min(forwardClearance, pathClearance);
                Clearance =
                    forwardClearance < pathClearance
                        ? forwardBlocker
                        : TrafficClearance.LastBlocker;
                if (clearance < target)
                {
                    target = clearance;
                    State = "Road conflict";
                }
            }
            foreach (var dynamics in physicsCars)
                dynamics.TargetSpeed = target;
            door.localPosition = Vector3.Lerp(
                door.localPosition,
                new Vector3(1.34f, 1.9f, DoorOpen ? 5 : 4),
                Time.fixedDeltaTime * 3
            );
        }
    }

    public static class RailCrossing
    {
        // Physical gate geometry, used only by simulated traffic and the explicit
        // qualification driver. It never reaches the image observer or Comma.
        public static float GateSpeedLimit(Transform gate, BoxCollider body)
        {
            Vector3 local = gate.InverseTransformPoint(body.transform.TransformPoint(body.center));
            if (
                Vector3.Dot(gate.forward, body.transform.forward) < .5f
                || local.z > 0
                || local.z < -80
                || Mathf.Abs(local.y - body.center.y) > 2.5f
                || local.x < -8 - body.size.x * .5f
                || local.x > body.size.x * .5f
            )
                return float.PositiveInfinity;
            // Leave response distance for the progressive pedal controller as it
            // settles from its braking request; the body must remain well clear
            // of the arm throughout reopening, not merely avoid penetration.
            float clearance = Mathf.Max(0, -local.z - body.size.z * .5f - 4);
            return Mathf.Min(clearance / 1.7f, Mathf.Sqrt(6 * clearance));
        }

        public static bool Active(
            TrackPath path,
            float distance,
            float speed,
            Vector3 crossing,
            int direction = 1
        )
        {
            float approach = Mathf.Max(15, speed * 7);
            for (
                float d = direction > 0 ? -38 : -38 - approach;
                d <= (direction > 0 ? approach : 15);
                d += 3
            )
            {
                Vector3 p = path.Position(distance + d);
                if (Mathf.Abs(p.y - crossing.y) < 1.5f && (p - crossing).sqrMagnitude < 225)
                    return true;
            }
            return false;
        }
    }

    public sealed class RailGate : MonoBehaviour
    {
        Town town;
        Transform arm;
        Renderer[] warningLights;
        AudioSource bell;
        Material redLight;
        public bool ExplicitCrossing;
        public Vector3 CrossingPoint;
        public bool MustWait =>
            town
            && (
                town.RailClosedAt(
                    ExplicitCrossing
                        ? CrossingPoint
                        : transform.position + transform.rotation * new Vector3(-5, 0, 4)
                )
                || Quaternion.Angle(arm.localRotation, Quaternion.Euler(0, 0, -85)) > 5
            );

        public void Initialize(Town t)
        {
            town = t;
            town.RailGates.Add(this);
            Art.Box(
                "Gate pedestal",
                transform,
                Vector3.up * .7f,
                new Vector3(.45f, 1.4f, .5f),
                Art.Metal
            );
            arm = new GameObject("Gate hinge").transform;
            arm.SetParent(transform, false);
            arm.localPosition = Vector3.up * 1.2f;
            arm.localRotation = Quaternion.Euler(0, 0, -85);
            Art.Box("Barrier", arm, new Vector3(-4, 0, 0), new Vector3(8, .15f, .15f), Art.White);
            for (int i = 0; i < 8; i++)
                Art.Box(
                    "Red stripe",
                    arm,
                    new Vector3(-i - .3f, 0, 0),
                    new Vector3(.4f, .16f, .16f),
                    Art.Material("Gate red", Color.red),
                    false
                );
            redLight = Art.Material("Crossing warning red", new Color(1, .015f, .01f), true);
            warningLights = new Renderer[2];
            for (int i = 0; i < 2; i++)
            {
                Art.Box(
                    "Crossing lamp housing",
                    transform,
                    new Vector3(i * .5f - .25f, 2.1f, 0),
                    new Vector3(.36f, .36f, .12f),
                    Art.Rubber,
                    false
                );
                warningLights[i] = Art.Shape(
                        "Crossing lamp",
                        transform,
                        new Vector3(i * .5f - .25f, 2.1f, -.08f),
                        new Vector3(.26f, .26f, .05f),
                        Art.Rubber,
                        PrimitiveType.Sphere
                    )
                    .GetComponent<Renderer>();
            }
            bell = gameObject.AddComponent<AudioSource>();
            bell.spatialBlend = 1;
            bell.maxDistance = 65;
            bell.rolloffMode = AudioRolloffMode.Linear;
            bell.volume = .1f;
            bell.loop = true;
            var samples = new float[22050];
            for (int i = 0; i < samples.Length; i++)
            {
                float time = i / 22050f;
                samples[i] =
                    Mathf.Exp(-time * 7)
                    * (
                        Mathf.Sin(time * 2 * Mathf.PI * 880)
                        + .3f * Mathf.Sin(time * 2 * Mathf.PI * 1790)
                    )
                    * .35f;
            }
            bell.clip = AudioClip.Create("Railway warning bell", samples.Length, 1, 22050, false);
            bell.clip.SetData(samples, 0);
        }

        void Update()
        {
            Vector3 crossing = ExplicitCrossing
                ? CrossingPoint
                : transform.position + transform.rotation * new Vector3(-5, 0, 4);
            bool closed = town.RailClosedAt(crossing);
            for (int i = 0; i < 2; i++)
                warningLights[i].sharedMaterial =
                    closed && ((Time.time % 1 < .5f) == (i == 0)) ? redLight : Art.Rubber;
            if (closed && !bell.isPlaying)
                bell.Play();
            if (!closed && bell.isPlaying)
                bell.Stop();
            arm.localRotation = Quaternion.RotateTowards(
                arm.localRotation,
                Quaternion.Euler(0, 0, closed ? 0 : -85),
                Time.deltaTime * 25
            );
        }
    }

    public sealed class ConstructionSite : MonoBehaviour
    {
        public string State = "Digging";
        ExcavatorVisual machine;
        Transform boom,
            bucket;
        float start;

        public void Build()
        {
            for (int i = 0; i < 15; i++)
            {
                var cone = Realism.Spawn("Cone", transform, new Vector3(-1.3f, 0, -20 + i * 3));
                if (cone)
                {
                    var collider = cone.AddComponent<BoxCollider>();
                    collider.center = Vector3.up * .4f;
                    collider.size = new Vector3(.37f, .8f, .37f);
                    MovableProp.Add(cone, 3.5f);
                    continue;
                }
                cone = Art.Shape(
                    "Cone",
                    transform,
                    new Vector3(-1.3f, .35f, -20 + i * 3),
                    new Vector3(.35f, .35f, .35f),
                    Art.Material("Construction orange", new Color(1, .3f, .03f)),
                    PrimitiveType.Cylinder,
                    true
                );
                MovableProp.Add(cone, 3.5f);
            }
            var prefab = Resources.Load<GameObject>("WorldArt/Construction/Excavator");
            if (prefab)
            {
                var excavator = Instantiate(prefab, transform);
                excavator.transform.localPosition = new Vector3(6, .04f, 0);
                var tracks = excavator.AddComponent<BoxCollider>();
                tracks.center = new Vector3(0, .35f, 0);
                tracks.size = new Vector3(1.63f, .65f, 1.95f);
                var cabin = excavator.AddComponent<BoxCollider>();
                cabin.center = new Vector3(-.24f, 1.48f, .1f);
                cabin.size = new Vector3(.8f, 1.4f, 1.2f);
                MovableProp.Add(excavator, 3600); // Estimated simulation operating mass, not a vehicle specification.
                excavator.GetComponent<Rigidbody>().centerOfMass = new Vector3(0, .55f, 0);
                machine = excavator.GetComponent<ExcavatorVisual>();
                machine.Pose(0);
            }
            else
            {
                Art.Box(
                    "Excavator tracks",
                    transform,
                    new Vector3(6, .4f, 0),
                    new Vector3(3, .8f, 4),
                    Art.Rubber
                );
                Art.Box(
                    "Excavator cab",
                    transform,
                    new Vector3(6, 1.6f, 0),
                    new Vector3(2.5f, 1.8f, 2.5f),
                    Art.Yellow
                );
                boom = new GameObject("Boom hinge").transform;
                boom.SetParent(transform, false);
                boom.localPosition = new Vector3(6, 2, 1);
                Art.Box(
                    "Hydraulic boom",
                    boom,
                    new Vector3(0, 0, 2),
                    new Vector3(.4f, .5f, 4),
                    Art.Yellow,
                    false
                );
                bucket = Art.Box(
                    "Bucket",
                    boom,
                    new Vector3(0, -.5f, 4),
                    new Vector3(1.2f, .8f, 1),
                    Art.Metal,
                    false
                ).transform;
            }
            for (int i = 0; i < 3; i++)
                MovableProp.Add(
                    Art.Box(
                        "Concrete barrier",
                        transform,
                        new Vector3(2, .45f, -8 + i * 4),
                        new Vector3(.7f, .9f, 3.5f),
                        Art.Concrete
                    ),
                    1800
                );
        }

        public void Restart()
        {
            start = Time.time;
        }

        void Update()
        {
            float t = (Time.time - start) % 16;
            State =
                t < 5 ? "Digging"
                : t < 9 ? "Lifting"
                : t < 12 ? "Swinging"
                : "Unloading";
            if (machine)
            {
                if (t >= 14)
                    State = "Returning";
                machine.Pose(t);
                return;
            }
            float pitch,
                yaw,
                bend;
            if (t < 5)
            {
                pitch = 20 + 5 * Mathf.Sin(t / 5 * 2 * Mathf.PI);
                yaw = 0;
                bend = 8 * Mathf.Sin(t / 5 * 2 * Mathf.PI);
            }
            else if (t < 9)
            {
                pitch = Mathf.SmoothStep(20, -35, (t - 5) / 4);
                yaw = 0;
                bend = 0;
            }
            else if (t < 12)
            {
                pitch = -35;
                yaw = Mathf.SmoothStep(0, 60, (t - 9) / 3);
                bend = 0;
            }
            else if (t < 14)
            {
                pitch = -35;
                yaw = 60;
                bend = Mathf.SmoothStep(0, 60, (t - 12) / 2);
            }
            else
            {
                State = "Returning";
                pitch = Mathf.SmoothStep(-35, 20, (t - 14) / 2);
                yaw = Mathf.SmoothStep(60, 0, (t - 14) / 2);
                bend = Mathf.SmoothStep(60, 0, (t - 14) / 2);
            }
            boom.localRotation = Quaternion.Euler(pitch, yaw, 0);
            bucket.localRotation = Quaternion.Euler(bend, 0, 0);
        }
    }
}
