using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace CoDriver
{
    public sealed class Town : MonoBehaviour
    {
        public readonly List<TrafficActor> Traffic = new List<TrafficActor>();
        public readonly List<Walker> Walkers = new List<Walker>();
        public readonly List<Signal> Signals = new List<Signal>();
        public readonly List<RailGate> RailGates = new List<RailGate>();
        public readonly List<ActivityDoor> Doors = new List<ActivityDoor>();
        public readonly List<NavMeshBuildSource> WalkSurfaces = new List<NavMeshBuildSource>();
        public readonly LaneGraph TrafficGraph = new LaneGraph();
        public RailVehicle Train,
            Tram;
        public TrafficActor Bus,
            Emergency,
            ParkingCar;
        public ConstructionSite Construction;
        public ActivityDoor GarageDoor;
        public int Seed = 21;
        public TrafficDensity Density = TrafficDensity.Normal;
        const string DensityPreference = "CoDriver.TrafficDensity";
        public MapDefinition Map;
        public readonly Dictionary<string, Signal> MappedJunctions =
            new Dictionary<string, Signal>();
        public Vector3 EmergencyBay,
            IncidentBay,
            ParkingBay;
        public string Activity = "Town active";
        public bool RailClosed;
        public float Epoch { get; private set; }

        public bool RailClosedAt(Vector3 crossing)
        {
            if (Map != null && Map.IsDavis)
            {
                var road = Map.Nearest(crossing, routable: false);
                if (road != null && road.Path.Position(road.Path.NearestDistance(crossing)).y < -2)
                    return false;
            }
            return Train && Train.CrossingOccupied(crossing);
        }

        public float RailSpeedLimit(BoxCollider body)
        {
            float limit = float.PositiveInfinity;
            foreach (var gate in RailGates)
                if (gate.MustWait)
                    limit = Mathf.Min(limit, RailCrossing.GateSpeedLimit(gate.transform, body));
            return limit;
        }

        public Transform Player;
        System.Random random;
        NavMeshDataInstance navigation;
        NavMeshData navigationData;
        public static readonly float[] Grid = { -400, -200, 0, 200, 400 };

        public static float Height(float z)
        {
            if (z < 235 || z > 365)
                return 0;
            // Ease into the bridge grade at both ends. A discontinuous road
            // slope can strike the chassis even when the tire contacts are valid.
            float t =
                z < 285 ? (z - 235) / 50
                : z > 315 ? (365 - z) / 50
                : 1;
            return 4 * t * t * (3 - 2 * t);
        }

        public void Build(Transform player)
        {
            Epoch = Time.time;
            Map ??= MapDefinition.Cedar();
            Player = player;
            if (player)
                Density = (TrafficDensity)Mathf.Clamp(PlayerPrefs.GetInt(DensityPreference), 0, 1);
            random = new System.Random(Seed);
            Art.Initialize();
            var sun = new GameObject("Daylight").AddComponent<Light>();
            sun.transform.SetParent(transform, false);
            sun.type = LightType.Directional;
            sun.intensity = 1.35f;
            sun.transform.rotation = Quaternion.Euler(42, -35, 0);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = .7f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.6f, .72f, .85f);
            RenderSettings.ambientEquatorColor = new Color(.46f, .5f, .54f);
            RenderSettings.ambientGroundColor = new Color(.25f, .28f, .2f);
            Realism.Lighting();
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(.66f, .77f, .83f);
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 350;
            RenderSettings.fogEndDistance = 1050;
            if (Map.IsDavis)
            {
                DavisWorld.Build(this);
                FinishWorld();
                return;
            }
            Art.Box(
                "Ground",
                transform,
                new Vector3(0, -.65f, 0),
                new Vector3(1100, 1, 1100),
                Art.Grass
            );
            Art.Box(
                "River",
                transform,
                new Vector3(0, -.12f, 300),
                new Vector3(1000, .15f, 30),
                Art.Material("Water", new Color(.19f, .38f, .44f)),
                false
            );
            foreach (float x in Grid)
                Road(new Vector3(x, 0, -480), new Vector3(x, 0, 480), true);
            foreach (float z in Grid)
                Road(new Vector3(-480, 0, z), new Vector3(480, 0, z), false);
            RoadEnds();
            foreach (float x in Grid)
            foreach (float z in Grid)
            {
                // Rounded lane turns cross the corners between the two road strips.
                Art.Box(
                    "Intersection paving",
                    transform,
                    new Vector3(x, -.1f, z),
                    new Vector3(18, .2f, 18),
                    Art.Asphalt
                );
                var sig = new GameObject("Intersection").AddComponent<Signal>();
                sig.transform.SetParent(transform);
                sig.transform.position = new Vector3(x, 0, z);
                sig.Initialize(this, ((int)(x + z) / 200) & 1);
                Signals.Add(sig);
                Crosswalk(new Vector3(x, 0, z + 10), true);
                Crosswalk(new Vector3(x, 0, z - 10), true);
                Crosswalk(new Vector3(x + 10, 0, z), false);
                Crosswalk(new Vector3(x - 10, 0, z), false);
            }
            for (int ix = 0; ix < 4; ix++)
            for (int iz = 0; iz < 4; iz++)
                Block(Grid[ix], Grid[iz], ix == 1 && iz == 1 || ix == 2 && iz == 2);
            Bridge();
            RailCrossings();
            StreetcarIsland();
            Parking();
            EmergencyStation();
            BusStops();
            Construction = new GameObject("Construction activity").AddComponent<ConstructionSite>();
            Construction.transform.SetParent(transform);
            Construction.transform.position = new Vector3(203, 0, -90);
            Construction.Build();
            Realism.Spawn("Crane", Construction.transform, new Vector3(26, 0, -16), 90);
            for (int i = 0; i < 5; i++)
            {
                Realism.Spawn("Barrel", Construction.transform, new Vector3(3.5f, 0, -18 + i * 7));
                Realism.Spawn(
                    "Barrier",
                    Construction.transform,
                    new Vector3(17, 0, -18 + i * 4),
                    90
                );
            }
            var workpad = Art.Box(
                "Worker access area",
                Construction.transform,
                new Vector3(10, .05f, -14),
                new Vector3(12, .1f, 16),
                Art.Concrete
            );
            WalkSurfaces.Add(
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    transform = workpad.transform.localToWorldMatrix,
                    size = Vector3.one,
                    area = 0,
                }
            );
            FinishWorld();
        }

        void FinishWorld()
        {
            WorldBoundary.Build(this);
            GetComponent<StaticFacadeBatches>()?.Bake();
            PedestrianNavigation.AddStreetFurniture(this);
            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = .3f;
            settings.agentHeight = 1.8f;
            var nav = NavMeshBuilder.BuildNavMeshData(
                settings,
                WalkSurfaces,
                new Bounds(
                    (Map.boundsMin + Map.boundsMax) * .5f,
                    new Vector3(
                        Mathf.Max(1050, Map.boundsMax.x - Map.boundsMin.x + 20),
                        40,
                        Mathf.Max(1050, Map.boundsMax.z - Map.boundsMin.z + 20)
                    )
                ),
                Vector3.zero,
                Quaternion.identity
            );
            navigationData = nav;
            navigation = NavMesh.AddNavMeshData(nav);
            var staticObjects = new List<GameObject>();
            foreach (var mesh in GetComponentsInChildren<MeshFilter>())
                if (
                    !mesh.GetComponentInParent<Wind>()
                    && !mesh.GetComponentInParent<ActivityDoor>()
                    && !mesh.GetComponentInParent<Signal>()
                    && !mesh.GetComponentInParent<MappedSignalHead>()
                    && !mesh.GetComponentInParent<RailGate>()
                    && !mesh.GetComponentInParent<ConstructionSite>()
                    && !mesh.GetComponentInParent<Rigidbody>()
                )
                    staticObjects.Add(mesh.gameObject);
            gameObject.AddComponent<StaticWorldBatches>().Build(staticObjects);
            SpawnPopulation();
        }

        void Road(Vector3 start, Vector3 end, bool north)
        {
            bool avenue = !north && Mathf.Abs(start.z) < .1f;
            float width = avenue ? 16 : 9;
            float length = Vector3.Distance(start, end);
            Vector3 dir = (end - start).normalized;
            var cuts = new List<float>();
            for (float d = 0; d < length; d += 20)
                cuts.Add(d);
            cuts.Add(length);
            if (north)
                for (float z = 235; z <= 365; z += 2)
                {
                    float d = z - start.z;
                    if (d > 0 && d < length && !cuts.Contains(d))
                        cuts.Add(d);
                }
            cuts.Sort();
            for (int segment = 0; segment < cuts.Count - 1; segment++)
            {
                Vector3 a = start + dir * cuts[segment],
                    b = start + dir * cuts[segment + 1];
                a.y = Height(a.z);
                b.y = Height(b.z);
                var road = Art.Box(
                    "Asphalt road",
                    transform,
                    (a + b) * .5f - new Vector3(0, .1f, 0),
                    new Vector3(width, .2f, Vector3.Distance(a, b)),
                    Art.Asphalt
                );
                road.transform.rotation = Quaternion.LookRotation(b - a);
                // Physical support remains collidable; traffic look-ahead must not
                // interpret an uphill driving surface as a stationary obstacle.
                road.layer = TrafficClearance.RoadLayer;
                bool junction = Array.Exists(
                    Grid,
                    g => Mathf.Abs((north ? a.z : a.x) + 10 - g) < 16
                );
                if (!junction)
                {
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var line = Art.Marking(
                            "Double yellow",
                            road.transform,
                            new Vector3(s * .16f / width, .505f, 0),
                            new Vector2(.12f / width, .99f),
                            Art.Yellow
                        );
                        line.transform.localRotation = Quaternion.identity;
                        if (avenue)
                            for (int dash = 0; dash < 5; dash++)
                                Art.Marking(
                                    "Lane divider",
                                    road.transform,
                                    new Vector3(s * 4.6f / width, .51f, -.4f + dash * .2f),
                                    new Vector2(.12f / width, .1f),
                                    Art.White
                                );
                    }
                }
            }
            for (float d = 0; d < length; d += 4)
            {
                Vector3 a = start + dir * d,
                    b = start + dir * Mathf.Min(length, d + 4);
                a.y = Height(a.z);
                b.y = Height(b.z);
                if (
                    Array.Exists(
                        Grid,
                        g => Mathf.Abs((north ? (a.z + b.z) : (a.x + b.x)) * .5f - g) < 9
                    )
                )
                    continue;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 p =
                        (a + b) * .5f + Vector3.Cross(Vector3.up, dir) * side * (width / 2 + 1.8f);
                    // Flush entrances let vehicles cross the west sidewalk into the lot.
                    if (north && Mathf.Abs(start.x) < .1f && side < 0 && p.z > -49 && p.z < -29)
                        continue;
                    // Join the road edge to the curb. Leaving this 30 cm strip
                    // as the lowered terrain made small actors drop into a gap
                    // and face a 33 cm step at the crossing instead of an 18 cm curb.
                    var gutter = Art.Box(
                        "Curb gutter",
                        transform,
                        (a + b) * .5f
                            + Vector3.Cross(Vector3.up, dir) * side * (width / 2 + .15f)
                            - Vector3.up * .075f,
                        north ? new Vector3(.3f, .15f, 4) : new Vector3(4, .15f, .3f),
                        Art.Concrete
                    );
                    gutter.layer = TrafficClearance.RoadLayer;
                    if (north)
                        gutter.transform.rotation = Quaternion.FromToRotation(
                            Vector3.forward,
                            (b - a).normalized
                        );
                    var walk = Art.Box(
                        "Sidewalk",
                        transform,
                        p + Vector3.up * .09f,
                        north ? new Vector3(3, .18f, 4) : new Vector3(4, .18f, 3),
                        Art.Concrete
                    );
                    if (north)
                        walk.transform.rotation = Quaternion.FromToRotation(
                            Vector3.forward,
                            (b - a).normalized
                        );
                    WalkSurfaces.Add(
                        new NavMeshBuildSource
                        {
                            shape = NavMeshBuildSourceShape.Box,
                            transform = walk.transform.localToWorldMatrix,
                            size = Vector3.one,
                            area = 0,
                        }
                    );
                }
            }
        }

        void Crosswalk(Vector3 p, bool north)
        {
            float width = !north && Mathf.Abs(p.z) < .1f ? 16 : 9;
            int stripes = (int)width - 1;
            for (int i = 0; i < stripes; i++)
                Art.Marking(
                    "Crosswalk",
                    transform,
                    p
                        + new Vector3(
                            north ? i - (stripes - 1) / 2f : 0,
                            .004f,
                            north ? 0 : i - (stripes - 1) / 2f
                        ),
                    north ? new Vector2(.55f, 3) : new Vector2(3, .55f),
                    Art.White
                );
            float coordinate = north ? p.z : p.x;
            float side = Mathf.Sign(coordinate - Mathf.Round(coordinate / 200) * 200);
            Vector3 stop =
                p
                + (north ? Vector3.forward : Vector3.right) * (side * 3)
                + (north ? Vector3.left : Vector3.forward) * (side * width / 4);
            Art.Marking(
                "Stop line",
                transform,
                stop + Vector3.up * .004f,
                north ? new Vector2(width / 2, .3f) : new Vector2(.3f, width / 2),
                Art.White
            );
            WalkSurfaces.Add(
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    transform = Matrix4x4.TRS(
                        p + Vector3.up * .04f,
                        Quaternion.identity,
                        Vector3.one
                    ),
                    size = north ? new Vector3(width + 7, .1f, 3) : new Vector3(3, .1f, width + 7),
                    area = PedestrianNavigation.CrossingArea,
                }
            );
        }

        void RoadEnds()
        {
            for (int direction = 0; direction < 4; direction++)
                foreach (float lane in Grid)
                {
                    Quaternion rotation = Quaternion.Euler(0, direction * 90, 0);
                    var p = rotation * new Vector3(lane, 0, 480);
                    var end = new GameObject("Road ends / closed boundary").transform;
                    end.SetParent(transform);
                    end.SetPositionAndRotation(p, rotation);
                    Art.Box(
                        "End barrier",
                        end,
                        new Vector3(0, .65f, 0),
                        new Vector3(lane == 0 && direction % 2 == 1 ? 22 : 15, 1.3f, .75f),
                        Art.Concrete
                    );
                    for (int i = -4; i <= 4; i++)
                        Art.Box(
                            "Reflective barricade stripe",
                            end,
                            new Vector3(i, 1.1f, -.39f),
                            new Vector3(.45f, .28f, .03f),
                            Art.Yellow,
                            false
                        );
                    Art.Box(
                        "Road end sign",
                        end,
                        new Vector3(0, 2.25f, 0),
                        new Vector3(3, 1, .08f),
                        Art.Yellow,
                        false
                    );
                    Art.Text(
                        "ROAD ENDS",
                        end,
                        new Vector3(0, 2.25f, -.06f),
                        .15f,
                        Color.black,
                        Quaternion.identity
                    );
                    Art.Box(
                        "Speed sign pole",
                        end,
                        new Vector3(5.5f, 1.4f, -50),
                        new Vector3(.08f, 2.8f, .08f),
                        Art.Metal
                    );
                    Art.Box(
                        "Speed limit sign",
                        end,
                        new Vector3(5.5f, 2.35f, -50),
                        new Vector3(.65f, .95f, .05f),
                        Art.White,
                        false
                    );
                    Art.Text(
                        "SPEED",
                        end,
                        new Vector3(5.5f, 2.66f, -50.04f),
                        .035f,
                        Color.black,
                        Quaternion.identity
                    );
                    Art.Text(
                        "LIMIT",
                        end,
                        new Vector3(5.5f, 2.46f, -50.04f),
                        .035f,
                        Color.black,
                        Quaternion.identity
                    );
                    Art.Text(
                        "30",
                        end,
                        new Vector3(5.5f, 2.12f, -50.04f),
                        .095f,
                        Color.black,
                        Quaternion.identity
                    );
                }
            for (int side = -1; side <= 1; side += 2)
            {
                Art.Box(
                    "Town perimeter embankment",
                    transform,
                    new Vector3(side * 540, 1.2f, 0),
                    new Vector3(3, 2.4f, 1080),
                    Art.Concrete
                );
                Art.Box(
                    "Town perimeter embankment",
                    transform,
                    new Vector3(0, 1.2f, side * 540),
                    new Vector3(1080, 2.4f, 3),
                    Art.Concrete
                );
            }
        }

        void Block(float x, float z, bool commercial)
        {
            for (int i = 0; i < 6; i++)
            {
                float px = x + 25 + i * 29;
                Building(new Vector3(px, Height(z + 24), z + 24), commercial, 0);
                if (Mathf.Abs(z + 176 - 300) > 28)
                    Building(new Vector3(px, Height(z + 176), z + 176), commercial, 180);
            }
            // Street-facing lots on the north/south roads remove the empty strips
            // while leaving the existing transit, parking and construction yards.
            for (int i = 1; i <= 4; i++)
            {
                float pz = z + 25 + i * 29;
                if (Mathf.Abs(pz - 110) < 16 || (pz > 245 && pz < 355))
                    continue;
                if (!(x == 200 && z == -200))
                    Building(new Vector3(x + 24, Height(pz), pz), commercial, 90);
                if (!(x == -400 && z == -200))
                    Building(new Vector3(x + 176, Height(pz), pz), commercial, -90);
                Tree(new Vector3(x + 11, Height(pz), pz + 9));
            }
            for (int i = 0; i < 15; i++)
            {
                Vector3 p = new Vector3(
                    x + 14 + (float)random.NextDouble() * 172,
                    0,
                    z + 48 + (float)random.NextDouble() * 104
                );
                if (Mathf.Abs(p.z - 300) > 30 && Mathf.Abs(p.z - 110) > 9)
                    Tree(p);
            }
        }

        void Building(Vector3 p, bool commercial, float yaw)
        {
            if (Mathf.Abs(p.z - 300) < 25)
                return;
            int variant = Mathf.Abs(Mathf.RoundToInt(p.x * 7 + p.z * 3));
            if (variant % 4 != 0 && Realism.Building(transform, p, commercial, yaw, variant))
                return;
            var root = new GameObject(commercial ? "Town shop" : "House");
            root.transform.SetParent(transform);
            root.transform.position = p;
            root.transform.rotation = Quaternion.Euler(0, yaw, 0);
            float width = commercial ? 20 : 12,
                height = commercial ? 8 : 3.2f,
                depth = commercial ? 14 : 12;
            float entrance = width * .35f;
            Art.Box(
                "Rear wall",
                root.transform,
                new Vector3(0, height / 2, depth / 2),
                new Vector3(width, height, .3f),
                Art.Brick
            );
            for (int side = -1; side <= 1; side += 2)
            {
                Art.Box(
                    "Side wall",
                    root.transform,
                    new Vector3(side * width / 2, height / 2, 0),
                    new Vector3(.3f, height, depth),
                    Art.Brick
                );
                for (int window = 0; window < 2; window++)
                {
                    Art.Box(
                        "Side window frame",
                        root.transform,
                        new Vector3(
                            side * (width / 2 + .08f),
                            2.3f,
                            (window == 0 ? -1 : 1) * depth * .25f
                        ),
                        new Vector3(.16f, 1.7f, 2.2f),
                        Art.White,
                        false
                    );
                    Art.Box(
                        "Side window",
                        root.transform,
                        new Vector3(
                            side * (width / 2 + .18f),
                            2.3f,
                            (window == 0 ? -1 : 1) * depth * .25f
                        ),
                        new Vector3(.03f, 1.45f, 1.95f),
                        Art.Glass,
                        false
                    );
                }
            }
            float left = entrance - .6f + width / 2,
                right = width / 2 - entrance - .6f;
            Art.Box(
                "Front wall left",
                root.transform,
                new Vector3(-width / 2 + left / 2, height / 2, -depth / 2),
                new Vector3(left, height, .3f),
                Art.Brick
            );
            Art.Box(
                "Front wall right",
                root.transform,
                new Vector3(width / 2 - right / 2, height / 2, -depth / 2),
                new Vector3(right, height, .3f),
                Art.Brick
            );
            Art.Box(
                "Door lintel",
                root.transform,
                new Vector3(entrance, (height + 2.7f) / 2, -depth / 2),
                new Vector3(1.2f, height - 2.7f, .3f),
                Art.Brick
            );
            Art.Box(
                "Foundation",
                root.transform,
                new Vector3(0, .25f, 0),
                new Vector3(width + .6f, .5f, depth + .6f),
                Art.Concrete
            );
            if (commercial)
                Art.Box(
                    "Flat roof",
                    root.transform,
                    new Vector3(0, height + .2f, 0),
                    new Vector3(width + .8f, .4f, depth + .8f),
                    Art.Roof
                );
            else
            {
                Art.Gable(root.transform, width, depth, height, 2.8f);
                for (int s = -1; s <= 1; s += 2)
                {
                    var roof = Art.Box(
                        "Pitched roof",
                        root.transform,
                        new Vector3(s * width / 4, height + 1.4f, 0),
                        new Vector3(width * .59f, .2f, depth + 1),
                        Art.Roof
                    );
                    roof.transform.localRotation = Quaternion.Euler(0, 0, -s * 25);
                }
                Art.Box(
                    "Chimney",
                    root.transform,
                    new Vector3(3, height + 2, -2),
                    new Vector3(.8f, 2, .8f),
                    Art.Brick
                );
            }
            for (int floor = 0; floor < (commercial ? 2 : 1); floor++)
            for (int i = 0; i < (commercial ? 5 : 3); i++)
            {
                float wx = -width * .4f + i * width * .8f / (commercial ? 4 : 2);
                if (floor == 0 && Mathf.Abs(wx - entrance) < 1.7f)
                    continue;
                Art.Box(
                    "Window frame",
                    root.transform,
                    new Vector3(wx, 2 + floor * 3.5f, -depth / 2 - .06f),
                    new Vector3(2.2f, 1.7f, .16f),
                    Art.White,
                    false
                );
                Art.Box(
                    "Window",
                    root.transform,
                    new Vector3(wx, 2 + floor * 3.5f, -depth / 2 - .16f),
                    new Vector3(1.95f, 1.45f, .03f),
                    Art.Glass,
                    false
                );
            }
            var hinge = new GameObject("Door hinge").transform;
            hinge.SetParent(root.transform, false);
            hinge.localPosition = new Vector3(entrance - .6f, .5f, -depth / 2 - .2f);
            Art.Box(
                "Activity door",
                hinge,
                new Vector3(.6f, 1.1f, 0),
                new Vector3(1.17f, 2.2f, .12f),
                Art.Wood
            );
            var activity = hinge.gameObject.AddComponent<ActivityDoor>();
            Doors.Add(activity);
            // A fixed 12 m approach can project a raised walkway into the
            // avenue's outside traffic lane. End it on the sidewalk instead.
            var pathStart = root.transform.TransformPoint(new Vector3(entrance, 0, -depth / 2));
            var outward = -root.transform.forward;
            bool towardsVerticalRoad = Mathf.Abs(outward.x) > .5f;
            float pathLength = 12;
            foreach (float coordinate in Grid)
            {
                float ahead = towardsVerticalRoad
                    ? (coordinate - pathStart.x) / outward.x
                    : (coordinate - pathStart.z) / outward.z;
                float roadHalfWidth =
                    !towardsVerticalRoad && Mathf.Abs(coordinate) < .1f ? 8 : 4.5f;
                if (ahead > roadHalfWidth + 1.8f)
                    pathLength = Mathf.Min(pathLength, ahead - roadHalfWidth - 1.8f);
            }
            var approach = Art.Box(
                "Entrance path",
                root.transform,
                new Vector3(entrance, .1f, -depth / 2 - pathLength / 2),
                new Vector3(1.8f, .2f, pathLength),
                Art.Concrete
            );
            WalkSurfaces.Add(
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    transform = approach.transform.localToWorldMatrix,
                    size = Vector3.one,
                    area = 0,
                }
            );
            var entry = Art.Box(
                "Entrance floor",
                root.transform,
                new Vector3(entrance, .4f, -depth / 2 + 2),
                new Vector3(2.4f, .2f, 4.2f),
                Art.Wood
            );
            WalkSurfaces.Add(
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    transform = entry.transform.localToWorldMatrix,
                    size = Vector3.one,
                    area = 0,
                }
            );
            Art.Box(
                "Driveway",
                root.transform,
                new Vector3(width * .65f, .015f, -6),
                new Vector3(4, .03f, 23),
                Art.Concrete
            );
            if (commercial)
            {
                Art.Box(
                    "Shop fascia",
                    root.transform,
                    new Vector3(0, 4.1f, -depth / 2 - .3f),
                    new Vector3(width, .8f, .25f),
                    Art.Paint,
                    false
                );
                string[] names =
                {
                    "CEDAR MARKET",
                    "MAPLE CAFE",
                    "RIVERSIDE BOOKS",
                    "PARKSIDE HARDWARE",
                };
                Art.Text(
                    names[random.Next(names.Length)],
                    root.transform,
                    new Vector3(0, 4.1f, -depth / 2 - .45f),
                    .18f,
                    Color.white,
                    Quaternion.identity
                );
            }
        }

        void Tree(Vector3 p)
        {
            float h = 5 + (float)random.NextDouble() * 4;
            var g = new GameObject("Tree");
            g.transform.SetParent(transform);
            g.transform.position = p;
            var prefab = Resources.Load<GameObject>(
                "Realism/Prepared/" + (random.Next(3) == 0 ? "Pine" : "Oak")
            );
            if (!prefab)
                prefab = Resources.Load<GameObject>("Scenery/Prepared/Tree_1");
            if (prefab)
            {
                var model = Instantiate(prefab, g.transform);
                model.transform.localScale = Vector3.one * (h / 8);
                model.transform.localRotation = Quaternion.Euler(0, random.Next(360), 0);
                model.AddComponent<Wind>().Phase = (float)random.NextDouble() * 6;
                var trunk = g.AddComponent<CapsuleCollider>();
                trunk.radius = .25f;
                trunk.height = h * .7f;
                trunk.center = Vector3.up * h * .35f;
                var group = g.AddComponent<LODGroup>();
                group.SetLODs(
                    new[]
                    {
                        new LOD(.025f, g.GetComponentsInChildren<Renderer>()),
                        new LOD(.005f, new Renderer[0]),
                    }
                );
                group.RecalculateBounds();
                return;
            }
            Art.Shape(
                "Trunk",
                g.transform,
                new Vector3(0, h * .35f, 0),
                new Vector3(.5f, h * .35f, .5f),
                Art.Wood,
                PrimitiveType.Cylinder,
                true
            );
            var canopy = Art.Shape(
                "Crown",
                g.transform,
                new Vector3(0, h * .8f, 0),
                new Vector3(4, h * .7f, 4),
                Art.Foliage,
                PrimitiveType.Sphere
            );
            canopy.AddComponent<Wind>().Phase = (float)random.NextDouble() * 6;
            var lod = g.AddComponent<LODGroup>();
            lod.SetLODs(
                new[]
                {
                    new LOD(.025f, g.GetComponentsInChildren<Renderer>()),
                    new LOD(.005f, new Renderer[0]),
                }
            );
            lod.RecalculateBounds();
        }

        void Bridge()
        {
            foreach (float x in Grid)
            {
                // Import preparation aligns the source deck to y=0. Keep it just
                // below road paving and markings, with no duplicate road collider.
                Realism.Spawn("Bridge", transform, new Vector3(x, 3.99f, 300));
                for (int side = -1; side <= 1; side += 2)
                {
                    Art.Box(
                        "Bridge parapet",
                        transform,
                        new Vector3(x + side * 8, 4.5f, 300),
                        new Vector3(.35f, 1, 30),
                        Art.Concrete
                    );
                    for (int i = 0; i < 4; i++)
                        Art.Box(
                            "Bridge pier",
                            transform,
                            new Vector3(x + side * 5, 1, 288 + i * 8),
                            new Vector3(1.5f, 6, 1.5f),
                            Art.Concrete
                        );
                }
            }
        }

        void RailCrossings()
        {
            BuildTrack(new TrackPath(-460, 112, 460, 470, 28), false);
            BuildTrack(new TrackPath(-200, 0, 200, 200, 20), true);
            foreach (float x in Grid)
            {
                var gate = new GameObject("Rail gate").AddComponent<RailGate>();
                gate.transform.SetParent(transform);
                gate.transform.position = new Vector3(x + 5, 0, 106);
                gate.Initialize(this);
                var opposite = new GameObject("Opposite rail gate").AddComponent<RailGate>();
                opposite.transform.SetParent(transform);
                opposite.transform.SetPositionAndRotation(
                    new Vector3(x - 5, 0, 114),
                    Quaternion.Euler(0, 180, 0)
                );
                opposite.Initialize(this);
            }
            foreach (float x in new[] { -462f, 462f })
            foreach (float z in new[] { 200f, 400f })
            {
                var gate = new GameObject("Outer rail gate").AddComponent<RailGate>();
                gate.transform.SetParent(transform);
                gate.transform.SetPositionAndRotation(
                    new Vector3(x - 4, 0, z - 5),
                    Quaternion.Euler(0, 90, 0)
                );
                gate.Initialize(this);
            }
            foreach (
                var location in new[] { new Vector3(-100, .1f, 105), new Vector3(100, .1f, 477) }
            )
            {
                var platform = Art.Box(
                    "Station platform",
                    transform,
                    location,
                    new Vector3(70, .2f, 5),
                    Art.Concrete
                );
                WalkSurfaces.Add(
                    new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Box,
                        transform = platform.transform.localToWorldMatrix,
                        size = Vector3.one,
                        area = 0,
                    }
                );
                float road = location.z < 200 ? 6 : 406;
                var walk = Art.Box(
                    "Station footpath",
                    transform,
                    new Vector3(location.x - 32, .05f, (road + location.z) / 2),
                    new Vector3(2, .1f, Mathf.Abs(location.z - road)),
                    Art.Concrete
                );
                WalkSurfaces.Add(
                    new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Box,
                        transform = walk.transform.localToWorldMatrix,
                        size = Vector3.one,
                        area = 0,
                    }
                );
            }
            Art.Text(
                "CEDAR STATION",
                transform,
                new Vector3(-100, 3, 102),
                .3f,
                Color.white,
                Quaternion.identity
            );
        }

        void StreetcarIsland()
        {
            var island = Art.Box(
                "Streetcar boarding island",
                transform,
                new Vector3(-100, .1f, -4.5f),
                new Vector3(28, .2f, 1.6f),
                Art.Concrete
            );
            WalkSurfaces.Add(
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    transform = island.transform.localToWorldMatrix,
                    size = Vector3.one,
                    area = 0,
                }
            );
            Art.Box(
                "Streetcar stop pole",
                transform,
                new Vector3(-111, 1.5f, -4.5f),
                new Vector3(.08f, 3, .08f),
                Art.Metal
            );
            Art.Text(
                "STREETCAR",
                transform,
                new Vector3(-111, 2.6f, -4.6f),
                .1f,
                Color.white,
                Quaternion.identity
            );
        }

        void BuildTrack(TrackPath path, bool tram)
        {
            for (float d = 0; d < path.Length; d += 3)
            {
                Vector3 a = path.Position(d),
                    b = path.Position(Mathf.Min(path.Length, d + 3));
                Vector3 forward = (b - a).normalized;
                var rotation = Quaternion.LookRotation(forward);
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                for (int side = -1; side <= 1; side += 2)
                {
                    var rail = Art.Box(
                        tram ? "Streetcar rail" : "Train rail",
                        transform,
                        (a + b) * .5f + right * (side * .72f) + Vector3.up * .045f,
                        new Vector3(.075f, .08f, Vector3.Distance(a, b) + .05f),
                        Art.Metal,
                        false
                    );
                    rail.transform.rotation = rotation;
                }
                if (!tram)
                {
                    var tie = Art.Box(
                        "Rail tie",
                        transform,
                        a + Vector3.up * .015f,
                        new Vector3(2, .06f, .2f),
                        Art.Wood,
                        false
                    );
                    tie.transform.rotation = rotation;
                }
            }
        }

        void Parking()
        {
            var lot = Art.Box(
                "Parking lot",
                transform,
                new Vector3(-70, .02f, -52),
                new Vector3(70, .06f, 36),
                Art.Asphalt
            );
            WalkSurfaces.Add(
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    transform = lot.transform.localToWorldMatrix,
                    size = Vector3.one,
                    area = 0,
                }
            );
            var footpath = Art.Box(
                "Parking pedestrian access",
                transform,
                new Vector3(-73.5f, .05f, -22),
                new Vector3(2, .1f, 26),
                Art.Concrete
            );
            WalkSurfaces.Add(
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    transform = footpath.transform.localToWorldMatrix,
                    size = Vector3.one,
                    area = 0,
                }
            );
            foreach (float z in new[] { -43f, -35f })
            {
                var access = Art.Box(
                    "Parking driveway",
                    transform,
                    new Vector3(-18, .02f, z),
                    new Vector3(36, .06f, 8),
                    Art.Asphalt
                );
                WalkSurfaces.Add(
                    new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Box,
                        transform = access.transform.localToWorldMatrix,
                        size = Vector3.one,
                        area = 0,
                    }
                );
            }
            for (int i = 0; i < 12; i++)
            {
                Art.Box(
                    "Parking bay",
                    transform,
                    new Vector3(-100 + i * 5, .07f, -52),
                    new Vector3(.12f, .02f, 6),
                    Art.White,
                    false
                );
                if (i % 3 != 0)
                {
                    var parked = new GameObject("Parked car");
                    parked.layer = TrafficClearance.ParkedLayer;
                    parked.transform.SetParent(transform);
                    parked.transform.position = new Vector3(-97 + i * 5, .12f, -52);
                    Art.Car(
                        parked.transform,
                        Art.Material(
                            "Parked " + i,
                            Color.Lerp(Color.gray, Art.Paint.color, i / 12f)
                        )
                    );
                    var c = parked.AddComponent<BoxCollider>();
                    c.center = new VehicleProfile().collisionCentre;
                    c.size = new VehicleProfile().collisionSize;
                    parked.AddComponent<ParkedDynamics>();
                    WalkSurfaces.Add(
                        new NavMeshBuildSource
                        {
                            shape = NavMeshBuildSourceShape.Box,
                            transform = Matrix4x4.TRS(
                                parked.transform.position + c.center,
                                Quaternion.identity,
                                Vector3.one
                            ),
                            size = c.size,
                            area = 1,
                        }
                    );
                }
            }
            // Reserve the active bay too, so its driver walks around the parked body.
            WalkSurfaces.Add(
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    transform = Matrix4x4.TRS(
                        new Vector3(-67, .85f, -51),
                        Quaternion.identity,
                        Vector3.one
                    ),
                    size = new Vector3(1.8f, 1.5f, 4.4f),
                    area = 1,
                }
            );
        }

        void EmergencyStation()
        {
            var station = new GameObject("Emergency services station").transform;
            station.SetParent(transform);
            station.position = new Vector3(-171, 0, -130);
            Art.Box(
                "Station back",
                station,
                new Vector3(7, 2, 0),
                new Vector3(.5f, 4, 10),
                Art.Brick
            );
            for (int s = -1; s <= 1; s += 2)
                Art.Box(
                    "Station side",
                    station,
                    new Vector3(0, 2, s * 5),
                    new Vector3(14, 4, .5f),
                    Art.Brick
                );
            Art.Box(
                "Station roof",
                station,
                new Vector3(0, 4.1f, 0),
                new Vector3(15, .3f, 11),
                Art.Roof
            );
            var door = Art.Box(
                "Garage door",
                station,
                new Vector3(-7, 2, 0),
                new Vector3(.2f, 4, 9),
                Art.Metal
            );
            GarageDoor = door.AddComponent<ActivityDoor>();
            GarageDoor.Sliding = true;
            Art.Box(
                "Station apron",
                station,
                new Vector3(-17, .03f, 0),
                new Vector3(21, .06f, 10),
                Art.Concrete
            );
            Art.Text(
                "CEDAR FIRE / EMS",
                station,
                new Vector3(-7.2f, 4.8f, 0),
                .2f,
                Color.white,
                Quaternion.Euler(0, 90, 0)
            );
        }

        void BusStops()
        {
            var path = new TrackPath(-200, -200, 0, 0);
            for (int i = 0; i < 4; i++)
            {
                float distance = 100 + i * path.Length / 4;
                Vector3 forward = path.Direction(distance),
                    right = Vector3.Cross(Vector3.up, forward);
                Vector3 p = path.Position(distance) + right * 4.5f;
                var stop = new GameObject("Bus stop " + (i + 1)).transform;
                stop.SetParent(transform);
                stop.SetPositionAndRotation(p, Quaternion.LookRotation(forward));
                Art.Box(
                    "Shelter roof",
                    stop,
                    new Vector3(0, 2.6f, 0),
                    new Vector3(2.5f, .12f, 5),
                    Art.Paint,
                    false
                );
                for (int s = -1; s <= 1; s += 2)
                    Art.Box(
                        "Shelter pillar",
                        stop,
                        new Vector3(.9f, 1.3f, s * 2),
                        new Vector3(.08f, 2.6f, .08f),
                        Art.Metal
                    );
                Art.Box(
                    "Bus stop bench",
                    stop,
                    new Vector3(.6f, .5f, 0),
                    new Vector3(.5f, .15f, 3),
                    Art.Wood
                );
                Art.Box(
                    "Bus stop pole",
                    stop,
                    new Vector3(-.4f, 1.6f, 3),
                    new Vector3(.07f, 3.2f, .07f),
                    Art.Metal
                );
                Art.Text(
                    "BUS\n15 MIN",
                    stop,
                    new Vector3(-.45f, 2.8f, 3),
                    .11f,
                    Color.white,
                    Quaternion.Euler(0, 90, 0)
                );
            }
        }

        TrafficActor AddTraffic(string kind, int index)
        {
            var t = new GameObject(kind).AddComponent<TrafficActor>();
            t.transform.SetParent(transform);
            t.Initialize(this, kind, index);
            if (kind != "Parking" && kind != "Emergency")
            {
                bool placed = false;
                var body = t.GetComponent<BoxCollider>();
                for (int attempt = 0; attempt < 128; attempt++)
                {
                    bool clear = true;
                    if (Map.IsDavis)
                    {
                        var lane = Map.Nearest(t.transform.position, t.transform.forward, false);
                        clear =
                            lane != null
                            && !lane.internalLane
                            && lane.Path.NearestDistance(t.transform.position) > 8
                            && lane.Path.Length - lane.Path.NearestDistance(t.transform.position)
                                > 12;
                    }
                    foreach (var other in Traffic)
                        if ((other.transform.position - t.transform.position).sqrMagnitude < 196)
                            clear = false;
                    if (Player && (Player.position - t.transform.position).sqrMagnitude < 196)
                        clear = false;
                    // Rail is placed first and synchronized before this search.
                    // Include its full body and a departure gap, not just a point
                    // on the track; ordinary routes can share the streetcar lane.
                    if (
                        Physics.CheckBox(
                            t.transform.TransformPoint(body.center),
                            body.size * .5f + new Vector3(.4f, .05f, 3),
                            t.transform.rotation,
                            1 << TrafficClearance.RailLayer,
                            QueryTriggerInteraction.Ignore
                        )
                    )
                        clear = false;
                    if (clear)
                    {
                        placed = true;
                        break;
                    }
                    t.PlaceForScenario(t.Path, t.Path.Position(t.Distance + 31));
                }
                if (!placed)
                    throw new System.InvalidOperationException(
                        "No clear initial road placement for " + t.name
                    );
            }
            // Initial placement may have advanced to a clear lane. Begin service
            // at the next stop ahead, never skip stops to complete an empty lap.
            if (kind == "Bus")
                t.ForceStop();
            Traffic.Add(t);
            return t;
        }

        void SpawnPopulation()
        {
            Train = new GameObject("Train").AddComponent<RailVehicle>();
            Train.transform.SetParent(transform);
            Train.Initialize(this, false);
            Tram = new GameObject("Streetcar").AddComponent<RailVehicle>();
            Tram.transform.SetParent(transform);
            Tram.Initialize(this, true);
            // Davis has no source-mapped streetcar; Cedar retains that system.
            if (Map.IsDavis)
                Tram.gameObject.SetActive(false);
            Physics.SyncTransforms();
            for (int i = 0; i < 12; i++)
                AddTraffic(i % 4 == 0 ? "Truck" : "Car", i);
            Bus = AddTraffic("Bus", 12);
            Emergency = AddTraffic("Emergency", 13);
            ParkingCar = AddTraffic("Parking", 14);
            // Preserve the existing scenario actors' indices. Extra citizens are
            // persistent actors, created behind the loading screen, never teleported.
            for (int i = 15; i < TrafficPopulation.OrdinaryCount(Density) + 3; i++)
                AddTraffic(i % 4 == 0 ? "Truck" : "Car", i);
            for (int i = 0; i < 56; i++)
            {
                bool animal = i >= 16 && i < 20 || i >= 52;
                var w = new GameObject(animal ? "Animal" : "Pedestrian").AddComponent<Walker>();
                w.transform.SetParent(transform);
                w.Initialize(this, i);
                Walkers.Add(w);
            }
        }

        public void ResetActivities()
        {
            // Called with the simulation paused and the menu covering the scene.
            foreach (var actor in Traffic)
            {
                actor.gameObject.SetActive(false);
                Destroy(actor.gameObject);
            }
            foreach (var actor in Walkers)
            {
                actor.gameObject.SetActive(false);
                Destroy(actor.gameObject);
            }
            Train.gameObject.SetActive(false);
            Tram.gameObject.SetActive(false);
            Destroy(Train.gameObject);
            Destroy(Tram.gameObject);
            Traffic.Clear();
            Walkers.Clear();
            Epoch = Time.time;
            foreach (var signal in Signals)
                signal.ResetPhase();
            Construction?.Restart();
            SpawnPopulation();
        }

        public void SetDensity(TrafficDensity density)
        {
            density = density == TrafficDensity.RushHour ? density : TrafficDensity.Normal;
            if (Density == density)
                return;
            Density = density;
            PlayerPrefs.SetInt(DensityPreference, (int)density);
            PlayerPrefs.Save();
            // Settings covers the paused scene. During a drive defer the new
            // population until its next reset; never replace visible traffic.
            if (Time.timeScale == 0 && Traffic.Count > 0)
                ResetActivities();
        }

        public Signal ClosestSignal(Vector3 p)
        {
            Signal best = null;
            float d = 100000;
            foreach (var s in Signals)
            {
                float v = (s.transform.position - p).sqrMagnitude;
                if (v < d)
                {
                    d = v;
                    best = s;
                }
            }
            return best;
        }

        public bool MayEnter(TrafficActor actor, Signal signal)
        {
            if (
                !signal.Allows(actor.transform.position, actor.transform.forward)
                && !signal.ReservedBy(actor)
            )
                return signal.Reserve(actor, requireGreen: true, crossingChecked: true);
            if (
                signal.ReservedBy(actor)
                && Vector3.Dot(
                    signal.transform.position - actor.transform.position,
                    actor.transform.forward
                ) <= 14
            )
                return signal.Reserve(actor, requireGreen: true, crossingChecked: true);
            // Cedar needs the same exit discipline as mapped traffic: a queued
            // vehicle must not reserve a turn it cannot physically clear.
            // Include the complete curved vehicle envelope and crossing actors.
            float centre = actor.Path.NearestDistance(signal.transform.position, actor.Distance);
            float begin = actor.Distance;
            float finish = centre + 24;
            if (actor.Path.Closed && finish < begin)
                finish += actor.Path.Length;
            var body = actor.GetComponent<BoxCollider>();
            bool clear =
                TrafficClearance.ClearExit(actor.Path, finish, body)
                && TrafficClearance.ClearJunctionSweep(actor.Path, begin, finish, body);
            return signal.Reserve(actor, clear, requireGreen: true, crossingChecked: true);
        }

        public ActivityDoor NearestDoor(Vector3 position)
        {
            ActivityDoor nearest = null;
            float distance = 10000;
            foreach (var door in Doors)
            {
                float d = (door.Approach - position).sqrMagnitude;
                if (d < distance)
                {
                    distance = d;
                    nearest = door;
                }
            }
            return nearest;
        }

        public void Exercise(string system)
        {
            Activity = "Exercising " + system;
            if (system == "Emergency")
                Emergency.Dispatch();
            if (system == "Bus")
                Bus.ForceStop();
            if (system == "Parking")
                ParkingCar.ForceStop();
            if (system == "Construction")
                Construction.Restart();
            if (system == "Buildings")
                Walkers[3].VisitBuilding();
            if (system == "Railway")
                Train.Accelerate();
            if (system == "Streetcar")
                Tram.Accelerate();
            if (system == "Pedestrians" || system == "Animals")
                Walkers[system == "Animals" ? 16 : 2].RequestCrossing();
        }

        void OnDestroy()
        {
            ReleaseNavigation();
        }

        public void ReleaseNavigation()
        {
            if (navigation.valid)
                navigation.Remove();
            if (navigationData)
            {
                if (Application.isPlaying)
                    Destroy(navigationData);
                else
                    DestroyImmediate(navigationData);
                navigationData = null;
            }
        }
    }

    public sealed class Wind : MonoBehaviour
    {
        public float Phase;
        Quaternion initial;

        void Start()
        {
            initial = transform.localRotation;
        }

        void Update()
        {
            // Standalone world tests and preview scenes have no player camera.
            if (!CoDriverApp.Instance || !CoDriverApp.Instance.Vehicle)
                return;
            if (
                (transform.position - CoDriverApp.Instance.Vehicle.transform.position).sqrMagnitude
                < 22500
            )
                transform.localRotation =
                    initial
                    * Quaternion.Euler(
                        Mathf.Sin(Time.time * 1.4f + Phase) * 1.2f,
                        0,
                        Mathf.Sin(Time.time * .8f) * 2
                    );
        }
    }

    public sealed class ActivityDoor : MonoBehaviour
    {
        Quaternion closed;
        Vector3 position;
        float until;
        Walker visitor;

        public bool AvailableFor(Walker actor) => !visitor || visitor == actor;

        public bool Reserve(Walker actor)
        {
            if (!AvailableFor(actor))
                return false;
            visitor = actor;
            return true;
        }

        public void Release(Walker actor)
        {
            if (visitor == actor)
                visitor = null;
        }

        public bool Sliding;
        public Vector3 Approach =>
            transform.parent.TransformPoint(position + new Vector3(.6f, 0, -2));
        public Vector3 Inside => transform.parent.TransformPoint(position + new Vector3(.6f, 0, 2));
        public bool IsPassable =>
            Sliding
                ? transform.localPosition.y - position.y > 3.1f
                : Quaternion.Angle(closed, transform.localRotation) > 65;

        void Start()
        {
            closed = transform.localRotation;
            position = transform.localPosition;
        }

        public void Open()
        {
            until = Time.time + 4;
        }

        void Update()
        {
            if (Sliding)
                transform.localPosition = Vector3.Lerp(
                    transform.localPosition,
                    position + Vector3.up * (Time.time < until ? 4 : 0),
                    Time.deltaTime * 2
                );
            else
                transform.localRotation = Quaternion.Slerp(
                    transform.localRotation,
                    closed * Quaternion.Euler(0, Time.time < until ? 75 : 0, 0),
                    Time.deltaTime * 3
                );
        }
    }

    public sealed class Signal : MonoBehaviour
    {
        public Town Town;
        public int Offset;
        Renderer[] lamps = new Renderer[12];
        int displayedPhase = -1;
        Component occupant;
        MapLane enteredFrom;

        sealed class TurnRequest
        {
            public Component Actor;
            public float LastSeen;
            public bool Clear;
        }

        readonly List<TurnRequest> requests = new List<TurnRequest>();
        public float WaitingSetback =>
            occupant is TrafficActor actor && actor.Kind == "Bus" ? 22 : 12;
        public string Reservation =>
            occupant is TrafficActor traffic ? Town.Traffic.IndexOf(traffic) + ":" + traffic.Kind
            : occupant is RailVehicle ? "Streetcar"
            : occupant ? "Qualification vehicle"
            : "None";

        public bool ReservedBy(Component actor) => occupant == actor;

        public bool StopControlled => transform.position.x == -200 && transform.position.z == -200;
        public MapSignal Mapped;
        public bool Uncontrolled;

        static bool Dispatching(Component actor) =>
            actor is TrafficActor traffic && traffic.Dispatching;

        bool AtFrontOfApproach(Component actor)
        {
            Vector3 forward = actor.transform.forward;
            float ahead = Vector3.Dot(transform.position - actor.transform.position, forward);
            if (Town.Map?.IsDavis == true)
            {
                var lane = Town.Map.Nearest(actor.transform.position, forward, false);
                if (lane != null && !lane.internalLane)
                    ahead = lane.Path.Length - lane.Path.NearestDistance(actor.transform.position);
            }
            if (ahead > (Town.Map?.IsDavis == true ? 28 : 22) || ahead < 0)
                return false;
            foreach (var other in Town.Traffic)
                if (AheadInQueue(actor, other, ahead))
                    return false;
            if (Town.Tram && AheadInQueue(actor, Town.Tram, ahead))
                return false;
            return !Town.Player || !AheadInQueue(actor, Town.Player.GetComponent<Vehicle>(), ahead);
        }

        static bool AheadInQueue(Component actor, Component other, float ahead)
        {
            if (!other || other == actor || !other.gameObject.activeInHierarchy)
                return false;
            Vector3 forward = actor.transform.forward;
            Vector3 relative = other.transform.position - actor.transform.position;
            float along = Vector3.Dot(relative, forward);
            var ownBody = actor.GetComponent<BoxCollider>();
            var otherBody = other.GetComponent<BoxCollider>();
            float side = Vector3.Dot(relative, actor.transform.right);
            bool parallel = Vector3.Dot(other.transform.forward, forward) > .7f;
            float combinedLength =
                (ownBody ? ownBody.size.z * .5f : 2.2f)
                + (otherBody ? otherBody.size.z * .5f : 2.2f);
            // A passing emergency vehicle cannot sweep across a car still
            // beside it at a junction. Let that car clear first, even if the
            // emergency vehicle's nose has edged slightly ahead.
            if (
                parallel
                && Mathf.Abs(side) > 1.5f
                && Mathf.Abs(side) < 4.5f
                && Mathf.Abs(along) < combinedLength + 1
            )
            {
                if (Dispatching(actor) && !Dispatching(other) && side > 0)
                    return true;
                if (!Dispatching(actor) && Dispatching(other) && side < 0)
                    return false;
            }
            // Compare physical fronts: a long bus can lead a shorter
            // vehicle even when the shorter vehicle's centre is ahead.
            float frontLead =
                along
                + (otherBody ? otherBody.size.z * .5f : 0)
                - (ownBody ? ownBody.size.z * .5f : 0);
            if (
                parallel
                && Mathf.Abs(side) < 4.5f
                && along < ahead
                && (
                    frontLead > .5f
                    || (Mathf.Abs(frontLead) <= .5f && Dispatching(other) && !Dispatching(actor))
                )
            )
                return true;
            return false;
        }

        public void ReleaseBlockedApproach(Component actor)
        {
            if (occupant != actor)
                return;
            if (Town.Map?.IsDavis != true)
            {
                // Yield an uncommitted reservation if its exit becomes blocked.
                // Once inside, keep priority until the physical body clears.
                if (
                    Vector3.Dot(
                        transform.position - actor.transform.position,
                        actor.transform.forward
                    ) > 14
                )
                    occupant = null;
                return;
            }
            if (enteredFrom == null)
                return;
            float remaining =
                enteredFrom.Path.Length
                - enteredFrom.Path.NearestDistance(actor.transform.position);
            float halfLength = actor.GetComponent<BoxCollider>()?.size.z * .5f ?? 3;
            if (remaining > halfLength + 1)
            {
                occupant = null;
                enteredFrom = null;
            }
        }

        public bool Reserve(
            Component actor,
            bool approachClear = true,
            bool requireGreen = false,
            bool crossingChecked = false
        )
        {
            // Only the explicit qualification harness calls this with a Vehicle.
            // This grants a junction reservation, never vehicle control.
            if (
                !(actor is TrafficActor)
                && !(actor is Vehicle)
                && !(actor is RailVehicle rail && rail.Streetcar)
            )
                return false;
            if (!approachClear)
                ReleaseBlockedApproach(actor);
            if (occupant)
            {
                Vector3 toCentre = transform.position - occupant.transform.position;
                float approachBoundary = occupant is RailVehicle ? 19.5f : 14;
                float speed =
                    occupant is TrafficActor traffic ? traffic.Speed
                    : occupant is RailVehicle train ? train.Speed
                    : ((Vehicle)occupant).Speed;
                // Reservations begin within 22 m. Once outside the 24 m exit
                // boundary, later turns elsewhere must not retain this junction.
                bool left = toCentre.sqrMagnitude > 24 * 24;
                bool committedMappedTurn = false;
                if (Town.Map?.IsDavis == true && enteredFrom != null)
                {
                    var current = Town.Map.Nearest(
                        occupant.transform.position,
                        occupant.transform.forward,
                        false
                    );
                    float halfLength = occupant.GetComponent<BoxCollider>()?.size.z * .5f ?? 3;
                    // A large mapped junction can have an internal turn more
                    // than 28 m from its centre. It is already committed, not
                    // a distant queued approach. Retain it through a red or
                    // temporary stop until its rear clears the exit lane.
                    committedMappedTurn = current != null && current.internalLane;
                    left =
                        current != null
                        && current != enteredFrom
                        && !current.internalLane
                        && current.Path.NearestDistance(occupant.transform.position)
                            > halfLength + 2;
                    left |= toCentre.sqrMagnitude > 120 * 120;
                }
                bool waitingForRed =
                    !committedMappedTurn
                    && toCentre.sqrMagnitude > approachBoundary * approachBoundary
                    && speed < .3f
                    && !GreenFor(occupant);
                bool queuedBehindAnother =
                    !committedMappedTurn
                    && Vector3.Dot(toCentre, occupant.transform.forward) > approachBoundary
                    && !AtFrontOfApproach(occupant);
                if (
                    left
                    || waitingForRed
                    || queuedBehindAnother
                    || !occupant.gameObject.activeInHierarchy
                )
                    occupant = null;
            }
            // Remember arrival order while the junction is occupied. Otherwise
            // a vehicle whose Update runs first repeatedly beats other queues.
            // Blocked approaches retain their place but do not hold up a clear
            // green approach. Requests must keep being renewed by the planner.
            requests.RemoveAll(r =>
                !r.Actor || !r.Actor.gameObject.activeInHierarchy || Time.time - r.LastSeen > 1
            );
            if (occupant != actor)
            {
                var request = requests.Find(r => r.Actor == actor);
                if (AtFrontOfApproach(actor))
                {
                    if (request == null)
                    {
                        request = new TurnRequest { Actor = actor };
                        requests.Add(request);
                    }
                    request.LastSeen = Time.time;
                    request.Clear = approachClear;
                }
                else if (request != null)
                    requests.Remove(request);
            }
            if (occupant && occupant != actor)
                return false;
            if (!approachClear)
                return false;
            // Arrival registration is independent of signal permission. A red
            // approach keeps its queue position without claiming the junction.
            bool committed =
                occupant == actor
                && (
                    Town.Map?.IsDavis == true && enteredFrom != null
                        ? Town.Map.Nearest(
                            actor.transform.position,
                            actor.transform.forward,
                            false
                        )?.internalLane == true
                        : Vector3.Dot(
                            transform.position - actor.transform.position,
                            actor.transform.forward
                        ) <= (actor is RailVehicle ? 19.5f : 14)
                );
            if (requireGreen && !committed && !GreenFor(actor))
                return false;
            if (Town.Map?.IsDavis != true)
            {
                var remaining = transform.position - actor.transform.position;
                bool entering = Vector3.Dot(remaining, actor.transform.forward) > 14;
                // A crossing can outlast the green while its far half is already
                // clear of this route. Ordinary NPCs check their full swept turn
                // in MayEnter, so a person on another crossing cannot reserve the
                // entire junction indefinitely. Other callers keep this fallback.
                if (
                    (!occupant || entering)
                    && !crossingChecked
                    && Town.Walkers.Any(w =>
                        w.gameObject.activeInHierarchy
                        && w.Crossing
                        && (w.transform.position - transform.position).sqrMagnitude < 20 * 20
                    )
                )
                    return false;
            }
            if (!occupant)
            {
                if (!AtFrontOfApproach(actor))
                    return false;
                foreach (var request in requests)
                {
                    if (request.Actor == actor)
                        break;
                    if (
                        request.Clear
                        && GreenFor(request.Actor)
                        && AtFrontOfApproach(request.Actor)
                    )
                        return false;
                }
                occupant = actor;
                requests.RemoveAll(r => r.Actor == actor);
                enteredFrom =
                    Town.Map?.IsDavis == true
                        ? Town.Map.Nearest(actor.transform.position, actor.transform.forward, false)
                        : null;
            }
            return true;
        }

        float forcedUntil,
            forcedPhase;

        public void ResetPhase()
        {
            occupant = null;
            enteredFrom = null;
            requests.Clear();
            forcedUntil = 0;
            if (Mapped != null)
                Mapped.ForcedRedUntil = 0;
        }

        public void HoldPhase(float phase, float seconds)
        {
            forcedPhase = phase;
            forcedUntil = Time.time + seconds;
            if (Mapped != null)
                Mapped.ForcedRedUntil = Time.time - Town.Epoch + seconds;
        }

        public float Phase =>
            // The widest Cedar crossing is 22 m. Keep 28 s of all-red clearance
            // after the last walk start; the former six seconds repeatedly
            // consumed the next north/south green before walkers could finish.
            Time.time < forcedUntil
                ? forcedPhase
                : (Time.time - Town.Epoch + Offset * 8) % 64;
        public bool PedestrianGreen
        {
            get
            {
                if (Mapped == null && occupant && occupant.gameObject.activeInHierarchy)
                {
                    var remaining = transform.position - occupant.transform.position;
                    if (
                        remaining.sqrMagnitude < 24 * 24
                        && Vector3.Dot(remaining, occupant.transform.forward) < 14
                    )
                        return false;
                }
                return Uncontrolled
                    || (
                        Mapped != null
                            ? Mapped.Walk(Time.time - Town.Epoch)
                            : Phase >= 32 && Phase < 36
                    );
            }
        }

        bool GreenFor(Component actor) => Allows(actor.transform.position, actor.transform.forward);

        public bool Allows(Vector3 position, Vector3 forward)
        {
            if (Mapped == null)
                return Green(Mathf.Abs(forward.z) > .7f);
            var lane = Town.Map.Nearest(position, forward, false);
            char state = Mapped.State(lane?.signalIndex ?? -1, Time.time - Town.Epoch);
            return state == 'G' || state == 'g';
        }

        public bool IsYellowFor(Vector3 position, Vector3 forward)
        {
            if (Mapped == null)
                return Yellow(Mathf.Abs(forward.z) > .7f);
            var lane = Town.Map.Nearest(position, forward, false);
            return Mapped.State(lane?.signalIndex ?? -1, Time.time - Town.Epoch) == 'y';
        }

        public bool Green(bool north) =>
            Uncontrolled || StopControlled || (north ? Phase < 13 : Phase >= 16 && Phase < 29);

        public bool Yellow(bool north) =>
            north ? Phase >= 13 && Phase < 16 : Phase >= 29 && Phase < 32;

        public void Initialize(Town town, int offset)
        {
            Town = town;
            Offset = offset;
            if (Mapped != null || Uncontrolled)
                return;
            if (StopControlled)
            {
                for (int i = 0; i < 4; i++)
                {
                    var sign = new GameObject("Stop sign").transform;
                    sign.SetParent(transform, false);
                    sign.localRotation = Quaternion.Euler(0, i * 90, 0);
                    sign.localPosition = sign.localRotation * new Vector3(5.5f, 0, -11);
                    Art.Box(
                        "Sign pole",
                        sign,
                        Vector3.up * 1.3f,
                        new Vector3(.07f, 2.6f, .07f),
                        Art.Metal
                    );
                    Art.Octagon(sign, Vector3.up * 2.1f, .43f, Art.White);
                    Art.Octagon(
                        sign,
                        new Vector3(0, 2.1f, -.012f),
                        .39f,
                        Art.Material("Stop red", new Color(.65f, .025f, .02f))
                    );
                    Art.Text(
                        "STOP",
                        sign,
                        new Vector3(0, 2.1f, -.05f),
                        .065f,
                        Color.white,
                        Quaternion.identity
                    );
                }
                return;
            }
            for (int a = 0; a < 4; a++)
            {
                Vector3 p = Quaternion.Euler(0, a * 90, 0) * new Vector3(5, 0, 10);
                var detailed = Realism.Spawn(
                    "Signal",
                    transform,
                    p + Vector3.up * 3.9f,
                    a * 90 + 180
                );
                Art.Box(
                    "Signal pole",
                    transform,
                    p + Vector3.up * 2.5f,
                    new Vector3(.15f, 5, .15f),
                    Art.Metal
                );
                var housing = Art.Box(
                    "Traffic signal",
                    transform,
                    p + Vector3.up * 4.5f,
                    new Vector3(.42f, 1.2f, .32f),
                    Art.Rubber,
                    false
                );
                housing.transform.localRotation = Quaternion.Euler(0, a * 90, 0);
                if (detailed)
                {
                    housing.GetComponent<Renderer>().enabled = false;
                    housing.transform.localScale = Vector3.one;
                }
                for (int j = 0; j < 3; j++)
                    lamps[a * 3 + j] = Art.Shape(
                            "Signal lens",
                            housing.transform,
                            detailed
                                ? new Vector3(0, .4f - j * .4f, -.18f)
                                : new Vector3(0, .3f - j * .3f, -.55f),
                            detailed ? new Vector3(.3f, .3f, .05f) : new Vector3(.65f, .18f, .1f),
                            Art.Rubber,
                            PrimitiveType.Sphere
                        )
                        .GetComponent<Renderer>();
            }
        }

        void Update()
        {
            if (Mapped != null || Uncontrolled)
                return;
            if (StopControlled)
                return;
            int state =
                (Green(true) ? 1 : 0)
                | (Yellow(true) ? 2 : 0)
                | (Green(false) ? 4 : 0)
                | (Yellow(false) ? 8 : 0);
            if (state == displayedPhase)
                return;
            displayedPhase = state;
            for (int a = 0; a < 4; a++)
            for (int j = 0; j < 3; j++)
            {
                bool lit =
                    j == 2 ? Green(a % 2 == 0)
                    : j == 1 ? Yellow(a % 2 == 0)
                    : !Green(a % 2 == 0) && !Yellow(a % 2 == 0);
                lamps[a * 3 + j].sharedMaterial = lit
                    ? Art.Material(
                        "Signal " + j,
                        j == 0 ? Color.red
                            : j == 1 ? Color.yellow
                            : Color.green,
                        true
                    )
                    : Art.Rubber;
            }
        }
    }
}
