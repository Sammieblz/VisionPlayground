using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoDriver
{
    public sealed class CoDriverApp : MonoBehaviour
    {
        public static CoDriverApp Instance;
        public Vehicle Vehicle;
        public Town Town;
        public SensorRig Sensors;
        public readonly ControlArbiter Arbiter = new ControlArbiter();
        public DriverInput Input;
        public bool Paused = true;
        public string Session = Guid.NewGuid().ToString("N"),
            Root;
        public string Page = "Home",
            Scenario = "Free drive",
            Mode = "human_ai";
        public CommaCommand Command;
        public double CommandAt;
        public Action<bool> CommaRequest;
        ServiceSupervisor services;
        CommaBridge comma;
        Task<bool> commaShutdown;
        long commaRevision;
        Camera view;
        int viewMode = 1;
        public int ViewMode => viewMode;
        public bool ActivityCameraActive => activityView || tourSystems;
        public float SeatForward,
            SeatHeight,
            CockpitFieldOfView = 70;
        bool automationAccepted;
        public bool AutomationActive => automationAccepted && !Paused && Arbiter.Requested;
        string gearNotice = "";
        float gearNoticeUntil;
        public string GearNotice => Time.unscaledTime < gearNoticeUntil ? gearNotice : "";
        public OpenpilotUiSnapshot CommaUi => comma?.UiSnapshot;
        public Companion Companion => companion;
        public Navigation Navigation => navigation;
        public string DriveStatus => status;
        public string ScenarioStatus => evaluator?.Status ?? "Free drive";
        public string ScenarioSummary => evaluator?.Summary ?? "";
        public string ServiceStatus => services?.Status ?? "Starting";
        public string CommaStatus => comma?.Status ?? "Starting";
        public ObserverResult Observation => services?.Link?.Result;
        public float FramesPerSecond => fps;
        public bool FullMapOpen;
        GameInterface interfaceView;
        Vector2 look;
        bool controllerWasAvailable,
            rebinding;
        float fps,
            averageDt,
            benchmarkSeconds,
            elapsed;
        double previousTime;
        readonly List<float> times = new List<float>();
        string status = "Human driving";
        GUIStyle title,
            heading,
            label,
            small,
            button;
        bool styles;
        Vector2 scroll;
        ScenarioEvaluator evaluator;
        readonly RecordingViewer recordings = new RecordingViewer();
        Companion companion;
        long lastObservation = -1;
        readonly List<float> warningLatencies = new List<float>();
        readonly List<float> observerLatencies = new List<float>();
        float drivingSeconds,
            pausedSeconds;
        bool tourSystems;
        int tourShot = -1;
        readonly Navigation navigation = new Navigation();
        int bindingDevice;
        string recordingStatus = "";
        QualificationDriver qualification;
        string initialScenario;
        float captureAfter = -1;
        bool captured;
        bool qualificationInterrupted;
        float movingSeconds;
        int menuSelection,
            menuIndex,
            menuCount,
            menuActivatedFrame = -1;
        string menuPage;
        Transform activityView;
        string activityName;
        int recordingPage;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-runTests") >= 0)
                return;
            if (!Instance)
                new GameObject("CoDriverSim application").AddComponent<CoDriverApp>();
        }

        void Awake()
        {
            Instance = this;
            SeatForward = Mathf.Clamp(PlayerPrefs.GetFloat("seatForward", 0), -.20f, .12f);
            SeatHeight = Mathf.Clamp(PlayerPrefs.GetFloat("seatHeight", 0), -.08f, .08f);
            CockpitFieldOfView = Mathf.Clamp(PlayerPrefs.GetFloat("cockpitFov", 70), 60, 85);
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            // The imported multi-material models exhibited cross-material buffer
            // corruption with the SRP batcher on this D3D11 setup. Spatial mesh
            // batches and material instancing remain active.
            UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching = false;
            Time.fixedDeltaTime = .01f;
            QualitySettings.shadowDistance = 90;
            Root = FindRoot();
            Art.Initialize();
            // Button edges are consumed in Update; physics consumes the latched
            // vehicle controls in FixedUpdate, including while rendering slows.
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
            Input = new DriverInput();
            controllerWasAvailable = DriverInput.HasAvailableGamepad;
            Debug.Log(
                "CODRIVER_CONTROLLER " + (Gamepad.current?.description.product ?? "Keyboard only")
            );
            InputSystem.onDeviceChange += DeviceChanged;
            Vehicle = new GameObject("Human driven vehicle").AddComponent<Vehicle>();
            string selectedVehicle = PlayerPrefs.GetString("vehicle_profile", "gls-simulation-v1");
            var vehicleArgs = Environment.GetCommandLineArgs();
            int vehicleArg = Array.IndexOf(vehicleArgs, "-vehicle");
            if (vehicleArg >= 0 && vehicleArg + 1 < vehicleArgs.Length)
                selectedVehicle = vehicleArgs[vehicleArg + 1];
            if (
                selectedVehicle == "gls-simulation-v1"
                && Resources.Load<GameObject>("WorldArt/Local/MercedesVehicle")
            )
                Vehicle.Profile = VehicleProfile.GlsSimulation();
            string selectedMap = PlayerPrefs.GetString("map", "davis-downtown");
            var arguments = Environment.GetCommandLineArgs();
            int mapArg = Array.IndexOf(arguments, "-map");
            if (mapArg >= 0 && mapArg + 1 < arguments.Length)
                selectedMap = arguments[mapArg + 1];
            var definition = MapDefinition.Load(selectedMap);
            Vehicle.transform.SetPositionAndRotation(definition.Spawn, definition.SpawnRotation);
            Vehicle.Initialize();
            Vehicle.Body.position = Vehicle.transform.position;
            Town = new GameObject("Cedar Grove").AddComponent<Town>();
            Town.Map = definition;
            Town.Build(Vehicle.transform);
            navigation.Map = definition;
            navigation.Instruction = "Explore " + definition.name;
            view = new GameObject("Driver view").AddComponent<Camera>();
            view.gameObject.AddComponent<AudioListener>();
            view.nearClipPlane = .05f;
            view.farClipPlane = 900;
            view.fieldOfView = 70;
            view.backgroundColor = new Color(.63f, .76f, .85f);
            view.clearFlags = CameraClearFlags.Skybox;
            services = new ServiceSupervisor(Root);
            services.Start();
            Sensors = gameObject.AddComponent<SensorRig>();
            Sensors.Initialize(Vehicle, Session, services.Token);
            Sensors.Map = definition;
            comma = new CommaBridge(Root, services.Token, Vehicle.Profile);
            comma.Start();
            Sensors.Captured = comma.Submit;
            CommaRequest = value => comma.Requested = value;
            evaluator = new ScenarioEvaluator(
                Root,
                Session,
                services.StartCapture,
                services.StopCapture
            );
            companion = gameObject.AddComponent<Companion>();
            companion.Initialize();
            var cockpit = Vehicle.GetComponentInChildren<Cockpit>();
            cockpit.AttachDisplay(this);
            interfaceView = gameObject.AddComponent<GameInterface>();
            interfaceView.Initialize(this);
            gameObject.AddComponent<DrivingDiagnostics>().Initialize(this);
            gameObject.AddComponent<WorldDiagnostics>().Initialize(this);
            gameObject.AddComponent<PerformanceProbe>().Initialize(this);
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-qualification-driver")
                    qualification = new QualificationDriver();
                if (args[i] == "-scenario" && i + 1 < args.Length)
                    initialScenario = args[++i];
                if (args[i] == "-tour-systems")
                    tourSystems = true;
                if (args[i] == "-autoplay")
                {
                    Page = "Drive";
                    Paused = false;
                }
                if (args[i] == "-benchmark-seconds" && i + 1 < args.Length)
                    float.TryParse(args[i + 1], out benchmarkSeconds);
                if (args[i] == "-capture-after" && i + 1 < args.Length)
                    float.TryParse(args[i + 1], out captureAfter);
                if (args[i] == "-view" && i + 1 < args.Length)
                    int.TryParse(args[i + 1], out viewMode);
            }
            Time.timeScale = Paused ? 0 : 1;
            previousTime = Time.realtimeSinceStartupAsDouble;
        }

        void Start()
        {
            if (initialScenario != null)
                StartDriving(initialScenario);
            if (qualification != null)
            {
                qualification.Initialize(Vehicle, Town);
                Mode = "qualification_driver";
                Page = "Drive";
                Paused = false;
                Time.timeScale = 1;
                Arbiter.Cancel(qualification.Status);
            }
        }

        static string FindRoot()
        {
            var dir = new DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
                if (
                    File.Exists(Path.Combine(dir.FullName, "codriversim-app.json"))
                    || (
                        File.Exists(Path.Combine(dir.FullName, "pyproject.toml"))
                        && Directory.Exists(Path.Combine(dir.FullName, "src", "codriversim"))
                    )
                )
                    return dir.FullName;
            return PlayerPrefs.GetString(
                "backend_root",
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."))
            );
        }

        void CancelQualification()
        {
            if (qualification != null)
                qualificationInterrupted = true;
            qualification = null;
            if (Mode == "qualification_driver")
                Mode = "human_ai";
        }

        void DeviceChanged(InputDevice device, InputDeviceChange change)
        {
            if (device is Gamepad)
                Debug.Log("CODRIVER_CONTROLLER " + change + " " + device.displayName);
            if (
                device is Gamepad
                && (
                    change == InputDeviceChange.Disconnected
                    || change == InputDeviceChange.Removed
                    || change == InputDeviceChange.Disabled
                )
            )
            {
                controllerWasAvailable = false;
                PauseForControllerLoss();
            }
            if (
                device is Gamepad
                && (
                    change == InputDeviceChange.Added
                    || change == InputDeviceChange.Reconnected
                    || change == InputDeviceChange.Enabled
                )
            )
                controllerWasAvailable = DriverInput.HasAvailableGamepad;
        }

        void PauseForControllerLoss()
        {
            Paused = true;
            CancelQualification();
            FullMapOpen = false;
            Page = "Pause";
            Time.timeScale = 0;
            automationAccepted = false;
            Arbiter.Cancel("Controller unavailable — simulation paused");
            CommaRequest?.Invoke(false);
            if (Vehicle)
            {
                Vehicle.Throttle = Vehicle.Steer = 0;
                Vehicle.Brake = 1;
                Vehicle.CommandedRoadAngle = float.NaN;
                Vehicle.Horn?.Stop();
            }
            status = Arbiter.Reason;
        }

        void Update()
        {
            bool controllerAvailable = DriverInput.HasAvailableGamepad;
            if (controllerWasAvailable && !controllerAvailable)
                PauseForControllerLoss();
            controllerWasAvailable = controllerAvailable;
            PollCommaRestart();
            if (Paused && !rebinding)
            {
                if (
                    Gamepad.current?.dpad.down.wasPressedThisFrame == true
                    || Keyboard.current?.downArrowKey.wasPressedThisFrame == true
                )
                    menuSelection = (menuSelection + 1) % Mathf.Max(1, menuCount);
                if (
                    Gamepad.current?.dpad.up.wasPressedThisFrame == true
                    || Keyboard.current?.upArrowKey.wasPressedThisFrame == true
                )
                    menuSelection =
                        (menuSelection + Mathf.Max(1, menuCount) - 1) % Mathf.Max(1, menuCount);
                if (
                    Gamepad.current?.buttonEast.wasPressedThisFrame == true
                    && Page != "Home"
                    && Page != "Pause"
                )
                    Page = "Pause";
            }
            services.Poll();
            if (captureAfter >= 0 && !captured && Time.realtimeSinceStartup >= captureAfter)
            {
                captured = true;
                var directory = System.IO.Path.Combine(
                    Root,
                    "outputs",
                    "qualification",
                    "overhaul"
                );
                System.IO.Directory.CreateDirectory(directory);
                ScreenCapture.CaptureScreenshot(
                    System.IO.Path.Combine(
                        directory,
                        Session
                            + "-view"
                            + viewMode
                            + "-"
                            + Screen.width
                            + "x"
                            + Screen.height
                            + ".png"
                    )
                );
                // Qualification-only capture from the independent sensor target.
                // UI fixtures use this image, avoiding editor-only camera rendering.
                var prior = RenderTexture.active;
                var sensorTarget = Sensors.LatestTexture as RenderTexture;
                if (sensorTarget)
                {
                    RenderTexture.active = sensorTarget;
                    var picture = new Texture2D(
                        sensorTarget.width,
                        sensorTarget.height,
                        TextureFormat.RGB24,
                        false
                    );
                    picture.ReadPixels(
                        new Rect(0, 0, sensorTarget.width, sensorTarget.height),
                        0,
                        0
                    );
                    picture.Apply();
                    File.WriteAllBytes(
                        Path.Combine(directory, Session + "-forward.png"),
                        picture.EncodeToPNG()
                    );
                    Destroy(picture);
                    RenderTexture.active = prior;
                }
            }
            Sensors.Observer = services.Link;
            var observation = services.Link?.Result;
            bool observationFresh =
                observation != null
                && observation.session_id == Session
                && Time.realtimeSinceStartupAsDouble - observation.timestamp < 1;
            companion.Observe(observation, observationFresh, Mode == "human_muted" || Paused);
            if (observationFresh && observation.frame_id != lastObservation)
            {
                lastObservation = observation.frame_id;
                float latency = (float)(
                    (Time.realtimeSinceStartupAsDouble - observation.timestamp) * 1000
                );
                if (observerLatencies.Count < 120000)
                    observerLatencies.Add(latency);
                if (
                    observation.risks != null
                    && observation.risks.Length > 0
                    && warningLatencies.Count < 120000
                )
                    warningLatencies.Add(latency);
            }
            if (commaShutdown == null && comma.Revision != commaRevision)
            {
                commaRevision = comma.Revision;
                Command = comma.Command;
                CommandAt = Time.realtimeSinceStartupAsDouble;
            }
            comma.Requested = Arbiter.Requested && !Paused;
            comma.Steer = Input.Steer;
            comma.Brake = Input.Brake.ReadValue<float>();
            comma.Throttle = Input.Gas.ReadValue<float>();
            comma.Indicator = Vehicle.Indicator;
            averageDt = Mathf.Lerp(averageDt, Time.unscaledDeltaTime, .04f);
            fps = 1 / Mathf.Max(.001f, averageDt);
            elapsed += Time.unscaledDeltaTime;
            if (Paused)
                pausedSeconds += Time.unscaledDeltaTime;
            else
                drivingSeconds += Time.unscaledDeltaTime;
            if (!Paused && Vehicle.Speed > .5f)
                movingSeconds += Time.unscaledDeltaTime;
            if (times.Count < 120000)
                times.Add(Time.unscaledDeltaTime * 1000);
            if (benchmarkSeconds > 0 && elapsed >= benchmarkSeconds)
            {
                Application.Quit();
            }
            if (Input.Pause.WasPressedThisFrame() && !rebinding)
            {
                CancelQualification();
                Paused = !Paused;
                Page = Paused ? "Pause" : "Drive";
                Time.timeScale = Paused ? 0 : 1;
                Arbiter.Cancel("Paused / resumed in human mode");
                CommaRequest?.Invoke(false);
            }
            if (Input.FullMap.WasPressedThisFrame() && !rebinding)
            {
                FullMapOpen = !FullMapOpen;
                if (FullMapOpen)
                {
                    CancelQualification();
                    Paused = true;
                    Page = "Map";
                    Time.timeScale = 0;
                    Arbiter.Cancel("Map opened");
                    CommaRequest?.Invoke(false);
                }
                else
                    ResumeDrive();
            }
            if (Paused)
            {
                AudioListener.pause = true;
                return;
            }
            AudioListener.pause = false;
            if (Input.Camera.WasPressedThisFrame())
            {
                if (activityView)
                {
                    activityView = null;
                    Mode = "human_ai";
                }
                else
                    viewMode = (viewMode + 1) % 4;
            }
            if (Input.Reverse.WasPressedThisFrame())
            {
                if (Vehicle.TryToggleDirection())
                {
                    Arbiter.Cancel("Gear selected by driver");
                    status = Vehicle.Reverse
                        ? "Reverse selected · RT to accelerate"
                        : "Drive selected · RT to accelerate";
                }
                else
                    status = "Hold LT until stopped, then press B to change gear";
                gearNotice = status;
                gearNoticeUntil = Time.unscaledTime + 6;
                Debug.Log(
                    $"CODRIVER_GEAR_REQUEST speed={Vehicle.Speed:F3} reverse={Vehicle.Reverse} {status}"
                );
            }
            if (Input.Left.WasPressedThisFrame())
                Vehicle.Indicator = Vehicle.Indicator == -1 ? 0 : -1;
            if (Input.Right.WasPressedThisFrame())
                Vehicle.Indicator = Vehicle.Indicator == 1 ? 0 : 1;
            if (Input.Comma.WasPressedThisFrame())
                ToggleComma();
            if (Input.CruiseUp.WasPressedThisFrame())
                comma.CruiseDelta++;
            if (Input.CruiseDown.WasPressedThisFrame())
                comma.CruiseDelta--;
            if (Input.Horn.IsPressed() && !Vehicle.Horn.isPlaying)
                Vehicle.Horn.Play();
            if (!Input.Horn.IsPressed() && Vehicle.Horn.isPlaying)
                Vehicle.Horn.Stop();
            float steer = Input.Steer,
                brake = Input.Brake.ReadValue<float>();
            double age = Time.realtimeSinceStartupAsDouble - CommandAt;
            bool automated = Arbiter.Accept(
                Command,
                Session,
                age,
                steer,
                brake,
                Paused,
                false,
                Input.Gas.ReadValue<float>(),
                Input.Handbrake.IsPressed(),
                Time.realtimeSinceStartupAsDouble
            );
            automationAccepted = automated;
            comma.Requested = Arbiter.Requested;
            Vehicle.Steer = steer;
            Vehicle.CommandedRoadAngle = automated
                ? -Command.steering_deg / Vehicle.Profile.steeringRatio
                : float.NaN;
            if (automated)
                Vehicle.ApplyDesiredAcceleration(Command.acceleration_mps2);
            else
            {
                Vehicle.Throttle = Input.Gas.ReadValue<float>();
                Vehicle.Brake = brake;
            }
            Vehicle.Handbrake = Input.Handbrake.IsPressed();
            if (qualification != null)
            {
                if (
                    Mathf.Abs(steer) > .1f
                    || brake > .05f
                    || Input.Gas.ReadValue<float>() > .05f
                    || Input.Handbrake.IsPressed()
                    || Input.Comma.WasPressedThisFrame()
                )
                {
                    CancelQualification();
                    Arbiter.Cancel("Human took over the qualification test");
                    CommaRequest?.Invoke(false);
                }
                else
                {
                    Arbiter.Cancel(qualification.Status);
                    qualification.Apply(Vehicle, Town);
                }
            }
            if (activityView)
            {
                Arbiter.Cancel("Watching town activity; vehicle parked");
                comma.Requested = false;
                Vehicle.Throttle = Vehicle.Steer = 0;
                Vehicle.Brake = 1;
                Vehicle.Handbrake = true;
            }
            look += Input.Look.ReadValue<Vector2>() * Time.unscaledDeltaTime * 80;
            look.x = Mathf.Clamp(look.x, -100, 100);
            look.y = Mathf.Clamp(look.y, -30, 30);
            if (Input.Look.ReadValue<Vector2>().sqrMagnitude < .01f)
                look = Vector2.Lerp(look, Vector2.zero, Time.unscaledDeltaTime * 3);
            evaluator.Observe(Vehicle, Time.time, automated, Arbiter.Reason);
            navigation.Update(Vehicle.transform.position, Vehicle.transform.forward);
            Sensors.RouteInstruction = navigation.Instruction;
        }

        void ToggleComma()
        {
            if (Vehicle.Reverse)
            {
                Arbiter.Cancel("Select forward gear before requesting Comma");
                CommaRequest?.Invoke(false);
                return;
            }
            if (Arbiter.Requested)
            {
                Arbiter.Cancel("Human driving");
                CommaRequest?.Invoke(false);
            }
            else
            {
                bool accepted = Arbiter.TryRequest(
                    Command,
                    Session,
                    Time.realtimeSinceStartupAsDouble - CommandAt,
                    Time.realtimeSinceStartupAsDouble
                );
                CommaRequest?.Invoke(accepted);
                status = Arbiter.Reason;
            }
            Debug.Log("CODRIVER_COMMA_REQUEST " + Arbiter.Reason);
        }

        void LateUpdate()
        {
            if (!view || !Vehicle)
                return;
            if (activityView)
            {
                view.cullingMask = ~0;
                view.transform.position = activityView.position + new Vector3(-7, 3, -8);
                view.transform.LookAt(activityView.position + Vector3.up);
                return;
            }
            if (tourSystems)
            {
                Transform[] targets =
                {
                    Town.Bus.transform,
                    Town.Emergency.transform,
                    Town.Train.transform,
                    Town.Tram.transform,
                    Town.ParkingCar.transform,
                    Town.Walkers[11].transform,
                    Town.Construction.transform,
                    Town.Walkers[16].transform,
                    Town.Walkers[17].transform,
                    Town.Walkers[18].transform,
                };
                targets = Array.FindAll(targets, t => t && t.gameObject.activeInHierarchy);
                if (targets.Length == 0)
                    return;
                int selection = (int)(Time.time / 20) % targets.Length;
                var target = targets[selection];
                view.cullingMask = ~0;
                view.transform.position = target.position + new Vector3(-8, 4, -9);
                view.transform.LookAt(target.position + Vector3.up);
                if ((int)Time.time % 20 == 15 && tourShot != (int)(Time.time / 20))
                {
                    tourShot = (int)(Time.time / 20);
                    string folder = Path.Combine(Root, "outputs", "qualification");
                    Directory.CreateDirectory(folder);
                    ScreenCapture.CaptureScreenshot(
                        Path.Combine(folder, "world-" + Session + "-" + selection + ".png")
                    );
                }
                return;
            }
            Vector3 offset =
                viewMode == 0 ? new Vector3(0, 1.45f, .8f)
                : viewMode == 1
                    ? Vehicle.Profile.driverEye + new Vector3(0, SeatHeight, SeatForward)
                : new Vector3(0, 3.8f, -7);
            view.fieldOfView = viewMode == 1 ? CockpitFieldOfView : 70;
            view.transform.position = Vehicle.transform.TransformPoint(offset);
            view.cullingMask =
                viewMode == 2 ? ~(1 << 9)
                : viewMode == 1 ? ~(1 << 8)
                : ~((1 << 8) | (1 << 9));
            view.transform.rotation =
                Vehicle.transform.rotation
                * Quaternion.Euler(
                    -look.y
                        + (
                            viewMode == 2 ? 10
                            : viewMode == 1 ? 6
                            : 0
                        ),
                    look.x,
                    0
                );
        }

        public void StartDriving(string scenario)
        {
            activityView = null;
            Scenario = scenario;
            Paused = true;
            Time.timeScale = 0;
            evaluator.Close();
            Town.ResetActivities();
            evaluator.Start(scenario, Mode, Town.Seed, Vehicle, Town);
            Page = "Drive";
            Paused = false;
            Time.timeScale = 1;
            Arbiter.Cancel("Human driving");
            if (Mode == "comma")
                ToggleComma();
        }

        public void ResumeDrive()
        {
            CancelQualification();
            FullMapOpen = false;
            Page = "Drive";
            Paused = false;
            Time.timeScale = 1;
            Arbiter.Cancel("Resumed in human mode");
            CommaRequest?.Invoke(false);
        }

        public void OpenPage(string page)
        {
            CancelQualification();
            Page = page;
            Paused = true;
            Time.timeScale = 0;
            Arbiter.Cancel("Menu opened");
            CommaRequest?.Invoke(false);
        }

        public void RestartObserver()
        {
            services.Start();
            Sensors.Observer = null;
        }

        public void RestartComma() => BeginCommaRestart();

        public void RebindControl(InputAction action, int binding, Action done)
        {
            rebinding = true;
            Input.Rebind(
                action,
                binding,
                () =>
                {
                    rebinding = false;
                    done?.Invoke();
                }
            );
        }

        public void SwitchMap(string mapId) => StartCoroutine(ChangeMap(mapId));

        System.Collections.IEnumerator ChangeMap(string mapId)
        {
            OpenPage("Loading");
            evaluator.Close();
            yield return null;
            Town.ReleaseNavigation();
            Town.gameObject.SetActive(false);
            Destroy(Town.gameObject);
            var map = MapDefinition.Load(mapId);
            Vehicle.Body.linearVelocity = Vehicle.Body.angularVelocity = Vector3.zero;
            Vehicle.Body.position = map.Spawn;
            Vehicle.Body.rotation = map.SpawnRotation;
            Vehicle.transform.SetPositionAndRotation(map.Spawn, map.SpawnRotation);
            Vehicle.Throttle = Vehicle.Brake = Vehicle.Steer = 0;
            Vehicle.CommandedRoadAngle = float.NaN;
            Vehicle.Reverse = Vehicle.Handbrake = false;
            Town = new GameObject(map.name).AddComponent<Town>();
            Town.Map = map;
            Town.Build(Vehicle.transform);
            Sensors.Map = map;
            navigation.Map = map;
            navigation.Route.Clear();
            navigation.Instruction = "Explore " + map.name;
            PlayerPrefs.SetString("map", mapId);
            Scenario = "Free drive";
            Page = "Home";
        }

        void Styles()
        {
            if (styles)
                return;
            styles = true;
            title = new GUIStyle(GUI.skin.label) { fontSize = 46, fontStyle = FontStyle.Bold };
            title.normal.textColor = Color.white;
            heading = new GUIStyle(title) { fontSize = 25 };
            label = new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true };
            label.normal.textColor = new Color(.9f, .94f, .96f);
            small = new GUIStyle(label) { fontSize = 14 };
            button = new GUIStyle(GUI.skin.button)
            {
                fontSize = 19,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(18, 14, 10, 10),
            };
            GUI.skin.button = button;
        }

        static void Panel(Rect r, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        void OnGUI()
        {
            if (interfaceView)
                return;
            Styles();
            menuIndex = 0;
            if (menuPage != Page)
            {
                menuPage = Page;
                menuSelection = 0;
            }
            GUI.matrix = Matrix4x4.TRS(
                Vector3.zero,
                Quaternion.identity,
                new Vector3(Screen.width / 1280f, Screen.height / 720f, 1)
            );
            if (Page == "Drive")
            {
                HUD();
                return;
            }
            Panel(new Rect(0, 0, 1280, 720), new Color(.025f, .055f, .07f, .94f));
            Panel(new Rect(48, 46, 5, 84), new Color(.2f, .8f, .68f));
            GUI.Label(new Rect(72, 42, 700, 65), "CoDriverSim", title);
            GUI.Label(
                new Rect(76, 107, 700, 35),
                "CEDAR GROVE  /  DRIVE · OBSERVE · EVALUATE",
                small
            );
            GUI.Label(
                new Rect(850, 54, 380, 55),
                "LOCAL DEVELOPMENT BUILD\nCamera perception · virtual controls only",
                small
            );
            if (Page == "Home" || Page == "Pause")
            {
                string[] items =
                {
                    Page == "Pause" ? "Resume drive" : "Play",
                    "Scenario Tests",
                    "Recordings",
                    "Settings",
                    "Quit",
                };
                for (int i = 0; i < items.Length; i++)
                    if (MenuButton(new Rect(76, 185 + i * 70, 340, 54), items[i]))
                    {
                        if (i == 0)
                        {
                            if (Page == "Pause")
                            {
                                Page = "Drive";
                                Paused = false;
                                Time.timeScale = 1;
                                Arbiter.Cancel("Resumed in human mode");
                                CommaRequest?.Invoke(false);
                            }
                            else
                                StartDriving(Scenario);
                        }
                        if (i == 1)
                            Page = "Scenarios";
                        if (i == 2)
                            Page = "Recordings";
                        if (i == 3)
                            Page = "Settings";
                        if (i == 4)
                            Application.Quit();
                    }
                GUI.Label(new Rect(500, 192, 650, 50), "Your drive. Your co-driver.", heading);
                GUI.Label(
                    new Rect(500, 255, 630, 150),
                    "Explore the town, practise road rules, and compare camera-based warnings with the same drive under Comma.\n\nHuman control is the default. The dashcam observes and records.",
                    label
                );
                GUI.Label(
                    new Rect(500, 440, 630, 110),
                    "W / A / S / D  drive     C  camera     R  forward / reverse\nQ / E  indicators     Space  handbrake     X  Comma\nXbox controller: triggers drive, left stick steers.",
                    small
                );
                if (
                    Page == "Pause"
                    && MenuButton(new Rect(500, 560, 330, 45), "Return vehicle to town")
                )
                {
                    Arbiter.Cancel("Driver reset vehicle");
                    comma.Requested = false;
                    evaluator.Close();
                    Vehicle.Body.linearVelocity = Vector3.zero;
                    Vehicle.Body.angularVelocity = Vector3.zero;
                    Vehicle.Body.position = new Vector3(2.3f, .2f, -85);
                    Vehicle.Body.rotation = Quaternion.identity;
                    Vehicle.transform.SetPositionAndRotation(
                        Vehicle.Body.position,
                        Vehicle.Body.rotation
                    );
                    Vehicle.Throttle = Vehicle.Brake = Vehicle.Steer = 0;
                    Vehicle.Handbrake = Vehicle.Reverse = false;
                    Scenario = "Free drive";
                    Mode = "human_ai";
                    look = Vector2.zero;
                }
            }
            else if (Page == "Scenarios")
            {
                string[] names =
                {
                    "Normal driving",
                    "Sudden braking",
                    "Cut-in",
                    "Lane drift",
                    "Signal compliance",
                    "Stop compliance",
                    "Speed compliance",
                    "Occluded pedestrian crossing",
                    "Animal crossing",
                    "Bus departure",
                    "Parking conflict",
                    "Construction closure",
                    "Railway crossing",
                    "Emergency yielding",
                };
                GUI.Label(
                    new Rect(76, 160, 700, 35),
                    "Choose a scenario and comparison mode",
                    heading
                );
                string[] modes = { "Human · muted", "Human + AI", "Stock Comma" };
                for (int i = 0; i < 3; i++)
                    if (MenuButton(new Rect(76 + i * 300, 210, 280, 42), modes[i]))
                        Mode =
                            i == 0 ? "human_muted"
                            : i == 1 ? "human_ai"
                            : "comma";
                GUI.Label(new Rect(980, 215, 220, 30), "Mode: " + Mode, small);
                for (int i = 0; i < names.Length; i++)
                    if (
                        MenuButton(
                            new Rect(76 + (i % 3) * 390, 280 + i / 3 * 62, 370, 48),
                            names[i]
                        )
                    )
                        StartDriving(names[i]);
                if (MenuButton(new Rect(856, 528, 370, 48), "Town activity checks"))
                    Page = "Activities";
            }
            else if (Page == "Activities")
            {
                GUI.Label(new Rect(76, 160, 1100, 40), "Town activity checks", heading);
                GUI.Label(
                    new Rect(76, 205, 1100, 70),
                    "Watch complete activity cycles. The vehicle stays parked and its forward camera remains independent. C / Y returns to driving.\nStop the vehicle before opening an activity view.",
                    label
                );
                string[] systems =
                {
                    "Traffic",
                    "Pedestrians",
                    "Animals",
                    "Buildings",
                    "Parking",
                    "Construction",
                    "Bus",
                    "Streetcar",
                    "Railway",
                    "Emergency",
                };
                GUI.enabled = Vehicle.Speed < .3f;
                for (int i = 0; i < systems.Length; i++)
                    if (
                        MenuButton(
                            new Rect(76 + i % 3 * 390, 300 + i / 3 * 66, 370, 50),
                            systems[i]
                        )
                    )
                        WatchActivity(systems[i]);
                GUI.enabled = true;
            }
            else if (Page == "Settings")
            {
                GUI.Label(new Rect(76, 165, 600, 40), "Controls and services", heading);
                GUI.Label(
                    new Rect(76, 220, 270, 30),
                    "Stick dead zone: " + Input.Deadzone.ToString("F2"),
                    label
                );
                Input.Deadzone = GUI.HorizontalSlider(
                    new Rect(370, 232, 300, 25),
                    Input.Deadzone,
                    .05f,
                    .35f
                );
                PlayerPrefs.SetFloat("deadzone", Input.Deadzone);
                GUI.Label(
                    new Rect(76, 270, 610, 30),
                    rebinding
                        ? "Press a new control, or Esc to cancel"
                        : "Controller: "
                            + (Gamepad.current?.displayName ?? "Disconnected / keyboard"),
                    label
                );
                if (
                    MenuButton(
                        new Rect(850, 265, 340, 36),
                        bindingDevice == 0 ? "Editing: Xbox" : "Editing: keyboard"
                    )
                )
                    bindingDevice = 1 - bindingDevice;
                InputAction[] actions =
                {
                    Input.Steering,
                    Input.Gas,
                    Input.Brake,
                    Input.Handbrake,
                    Input.Reverse,
                    Input.Camera,
                    Input.Comma,
                    Input.Left,
                    Input.Right,
                    Input.Horn,
                    Input.Pause,
                    Input.CruiseUp,
                    Input.CruiseDown,
                    Input.Look,
                };
                var remappings = new List<(InputAction action, int binding, string name)>();
                foreach (var action in actions)
                {
                    if (bindingDevice == 1 && action == Input.Steering)
                    {
                        remappings.Add((action, 2, "Steer left"));
                        remappings.Add((action, 3, "Steer right"));
                    }
                    else if (bindingDevice == 1 && action == Input.Look)
                    {
                        string[] names = { "Look up", "Look down", "Look left", "Look right" };
                        for (int i = 0; i < names.Length; i++)
                            remappings.Add((action, i + 2, names[i]));
                    }
                    else
                        remappings.Add((action, bindingDevice == 0 ? 0 : 1, action.name));
                }
                for (int i = 0; i < remappings.Count; i++)
                {
                    int j = i;
                    if (
                        MenuButton(
                            new Rect(76 + i % 4 * 280, 315 + i / 4 * 43, 270, 40),
                            remappings[i].name
                        ) && !rebinding
                    )
                    {
                        rebinding = true;
                        Input.Rebind(
                            remappings[j].action,
                            remappings[j].binding,
                            () => rebinding = false
                        );
                    }
                }
                if (MenuButton(new Rect(76, 560, 350, 45), "Restart camera service"))
                {
                    services.Start();
                    Sensors.Observer = null;
                }
                if (MenuButton(new Rect(450, 560, 350, 45), "Restart Comma"))
                {
                    BeginCommaRestart();
                }
                string[] destinations = { "Station", "Construction", "Residential" };
                for (int i = 0; i < 3; i++)
                    if (
                        MenuButton(
                            new Rect(76 + i * 370, 610, 350, 33),
                            "Route: " + destinations[i]
                        )
                    )
                        navigation.Select(Vehicle.transform.position, destinations[i]);
            }
            else if (Page == "Recordings")
            {
                GUI.Label(new Rect(76, 165, 350, 40), "Recordings", heading);
                string path = Path.Combine(Root, "outputs", "recordings");
                string[] clips = Array.Empty<string>();
                try
                {
                    Directory.CreateDirectory(path);
                    clips = Directory.GetDirectories(path);
                    Array.Sort(
                        clips,
                        (a, b) =>
                            Directory
                                .GetCreationTimeUtc(a)
                                .CompareTo(Directory.GetCreationTimeUtc(b))
                    );
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    recordingStatus = "Recording storage unavailable: " + e.Message;
                }
                GUI.Label(
                    new Rect(76, 215, 350, 55),
                    clips.Length + " recordings with camera frames, observations and warnings.",
                    label
                );
                if (MenuButton(new Rect(76, 320, 350, 42), "Open recordings folder"))
                    try
                    {
                        System.Diagnostics.Process.Start(
                            new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }
                        );
                    }
                    catch (Exception e)
                        when (e is IOException || e is System.ComponentModel.Win32Exception)
                    {
                        recordingStatus = "Cannot open recordings: " + e.Message;
                    }
                if (MenuButton(new Rect(76, 270, 350, 42), "Save camera event clip"))
                {
                    try
                    {
                        File.WriteAllText(
                            Path.ChangeExtension(services.ReadyFile, "save"),
                            "manual"
                        );
                        recordingStatus = "Capture requested: keep driving for 10 seconds.";
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                    {
                        recordingStatus = e.Message;
                    }
                }
                GUI.Label(new Rect(76, 365, 350, 50), recordingStatus, small);
                if (recordings.Loaded)
                    recordings.Draw(new Rect(475, 175, 700, 350), small);
                else
                    GUI.Label(new Rect(500, 370, 700, 160), evaluator.Summary, label);
                int pages = Mathf.Max(1, (clips.Length + 3) / 4);
                recordingPage = Mathf.Clamp(recordingPage, 0, pages - 1);
                for (int i = 0; i < Mathf.Min(4, clips.Length - recordingPage * 4); i++)
                {
                    string clip = clips[recordingPage * 4 + i];
                    if (
                        MenuButton(
                            new Rect(76, 390 + i * 52, 350, 40),
                            "Replay " + Directory.GetCreationTime(clip).ToString("MMM d, HH:mm:ss")
                        )
                    )
                        recordings.Open(Path.Combine(clip, "recording.json"));
                }
                if (pages > 1)
                {
                    if (MenuButton(new Rect(76, 600, 165, 36), "Newer"))
                        recordingPage = Mathf.Max(0, recordingPage - 1);
                    if (MenuButton(new Rect(261, 600, 165, 36), "Older"))
                        recordingPage = Mathf.Min(pages - 1, recordingPage + 1);
                }
            }
            if (Page != "Home" && Page != "Pause")
                if (MenuButton(new Rect(76, 650, 160, 40), "Back"))
                    Page = "Pause";
            GUI.Label(
                new Rect(500, 644, 730, 32),
                "Camera: " + (services.Link != null ? services.Link.Status : services.Status),
                small
            );
            GUI.Label(
                new Rect(500, 681, 730, 36),
                "Comma: "
                    + (
                        commaShutdown != null
                            ? "Restarting; waiting for previous instance to close"
                            : comma.Status
                    ),
                small
            );
            menuCount = menuIndex;
        }

        public void WatchActivity(string system)
        {
            if (Vehicle.Speed >= .3f)
                return;
            qualification = null;
            evaluator.Close();
            Town.Exercise(system);
            activityName = system;
            activityView =
                system == "Bus" ? Town.Bus.transform
                : system == "Streetcar" ? Town.Tram.transform
                : system == "Railway" ? Town.Train.transform
                : system == "Emergency" ? Town.Emergency.transform
                : system == "Parking" ? Town.ParkingCar.transform
                : system == "Construction" ? Town.Construction.transform
                : system == "Animals" ? Town.Walkers[16].transform
                : system == "Buildings" ? Town.Walkers[3].transform
                : system == "Pedestrians" ? Town.Walkers[2].transform
                : Town.Traffic[0].transform;
            Arbiter.Cancel("Watching town activity; vehicle parked");
            comma.Requested = false;
            Mode = "activity_check";
            Page = "Drive";
            Paused = false;
            Time.timeScale = 1;
        }

        bool MenuButton(Rect bounds, string text)
        {
            int index = menuIndex++;
            Color previous = GUI.backgroundColor;
            if (index == menuSelection)
                GUI.backgroundColor = new Color(.35f, .85f, .72f);
            bool clicked = GUI.Button(bounds, text);
            GUI.backgroundColor = previous;
            bool activate =
                !rebinding
                && index == menuSelection
                && Event.current.type == EventType.Repaint
                && menuActivatedFrame != Time.frameCount
                && (
                    Gamepad.current?.buttonSouth.wasPressedThisFrame == true
                    || Keyboard.current?.enterKey.wasPressedThisFrame == true
                );
            if (clicked || activate)
            {
                menuActivatedFrame = Time.frameCount;
                return true;
            }
            return false;
        }

        void HUD()
        {
            Panel(new Rect(20, 20, 250, 110), new Color(.025f, .055f, .07f, .9f));
            GUI.Label(
                new Rect(38, 26, 210, 64),
                (Vehicle.Speed * 2.23694f).ToString("0") + " mph",
                title
            );
            GUI.Label(
                new Rect(40, 92, 210, 28),
                (Vehicle.Reverse ? "R" : "D")
                    + "   "
                    + (
                        Vehicle.Indicator == -1 ? "←"
                        : Vehicle.Indicator == 1 ? "→"
                        : ""
                    )
                    + "   "
                    + fps.ToString("0")
                    + " FPS",
                small
            );
            Panel(new Rect(870, 20, 390, 142), new Color(.025f, .055f, .07f, .9f));
            GUI.Label(new Rect(892, 30, 350, 30), "CO-DRIVER", heading);
            companion.Draw(new Rect(1200, 28, 44, 44));
            var result = services.Link?.Result;
            if (!string.IsNullOrEmpty(result?.recording_error))
                GUI.Label(
                    new Rect(870, 170, 390, 70),
                    "Recording unavailable: " + result.recording_error,
                    small
                );
            bool fresh =
                result != null
                && result.session_id == Session
                && Time.realtimeSinceStartupAsDouble - result.timestamp < 1;
            string warning = fresh
                ? "Watching the forward camera"
                : "Camera observations unavailable";
            if (fresh && result.risks != null && result.risks.Length > 0 && Mode != "human_muted")
            {
                var risk = result.risks[0];
                foreach (var r in result.risks)
                    if (r.severity == "urgent")
                        risk = r;
                warning = risk.message;
                if (risk.severity == "urgent")
                    Panel(new Rect(870, 20, 6, 142), new Color(1, .35f, .13f));
            }
            GUI.Label(new Rect(892, 72, 345, 76), warning, label);
            Panel(new Rect(12, 643, 840, 64), new Color(.025f, .055f, .07f, .92f));
            GUI.Label(
                new Rect(20, 650, 830, 56),
                Scenario
                    + "  ·  "
                    + Arbiter.Reason
                    + "\nEsc menu   X Comma   C camera   "
                    + (fresh ? result.latency_ms.ToString("0") + " ms detection" : services.Status),
                small
            );
            GUI.Label(new Rect(300, 24, 550, 36), navigation.Instruction, small);
            GUI.Label(new Rect(300, 62, 550, 60), comma.Status, small);
            GUI.Label(
                new Rect(300, 135, 550, 65),
                activityView
                    ? activityName + " activity view · C / Y returns to driving"
                    : evaluator.Status,
                small
            );
            Panel(new Rect(1070, 510, 190, 190), new Color(.025f, .055f, .07f, .9f));
            foreach (float x in Town.Grid)
            {
                Panel(
                    new Rect(1085 + (x + 440) / 880 * 160, 525, 2, 160),
                    new Color(.35f, .46f, .5f)
                );
                Panel(
                    new Rect(1085, 525 + (x + 440) / 880 * 160, 160, 2),
                    new Color(.35f, .46f, .5f)
                );
            }
            for (int i = 1; i < navigation.Route.Count; i++)
            {
                var a = navigation.Route[i - 1];
                var b = navigation.Route[i];
                float x1 = 1085 + (a.x + 440) / 880 * 160;
                float x2 = 1085 + (b.x + 440) / 880 * 160;
                float y1 = 525 + (440 - a.z) / 880 * 160;
                float y2 = 525 + (440 - b.z) / 880 * 160;
                Panel(
                    new Rect(
                        Mathf.Min(x1, x2) - 1,
                        Mathf.Min(y1, y2) - 1,
                        Mathf.Max(3, Mathf.Abs(x2 - x1)),
                        Mathf.Max(3, Mathf.Abs(y2 - y1))
                    ),
                    new Color(.9f, .75f, .2f)
                );
            }
            Panel(
                new Rect(
                    1085 + (Vehicle.transform.position.x + 440) / 880 * 160,
                    525 + (440 - Vehicle.transform.position.z) / 880 * 160,
                    6,
                    6
                ),
                new Color(.2f, 1, .7f)
            );
        }

        void SavePerformance()
        {
            if (times.Count == 0)
                return;
            var sorted = times.ToArray();
            Array.Sort(sorted);
            var latencies = warningLatencies.ToArray();
            Array.Sort(latencies);
            var observations = observerLatencies.ToArray();
            Array.Sort(observations);
            string path = Path.Combine(Root, "outputs", "qualification");
            Directory.CreateDirectory(path);
            var report = new Performance
            {
                build = BuildIdentity.Current,
                map_id = Town.Map.id,
                width = Screen.width,
                height = Screen.height,
                view_mode = viewMode,
                gpu = SystemInfo.graphicsDeviceName,
                qualification_interrupted = qualificationInterrupted,
                session = Session,
                seconds = elapsed,
                frames = times.Count,
                mean_fps = times.Count / elapsed,
                p95_ms = sorted[(int)(sorted.Length * .95)],
                p99_ms = sorted[(int)(sorted.Length * .99)],
                camera_frames = Sensors.Captures,
                camera_drops = Sensors.Dropped,
                sensor_to_warning_p95_ms =
                    latencies.Length > 0 ? latencies[(int)(latencies.Length * .95)] : -1,
                warning_samples = latencies.Length,
                sensor_to_observer_p95_ms =
                    observations.Length > 0 ? observations[(int)(observations.Length * .95)] : -1,
                observer_updates = observations.Length,
                driving_seconds = drivingSeconds,
                paused_seconds = pausedSeconds,
                camera_received = services.Link?.Result?.received_frames ?? 0,
                detector_processed = services.Link?.Result?.processed_frames ?? 0,
                camera_hz = services.Link?.Result?.camera_hz ?? 0,
                detection_hz = services.Link?.Result?.detection_hz ?? 0,
                moving_seconds = movingSeconds,
                driver_mode = Mode,
                collision_count = Vehicle.Collisions,
            };
            File.WriteAllText(
                Path.Combine(path, "performance-" + Session + ".json"),
                JsonUtility.ToJson(report, true)
            );
        }

        void BeginCommaRestart()
        {
            if (commaShutdown != null)
                return;
            Arbiter.Cancel("Restarting Comma");
            comma.Requested = false;
            Command = null;
            Sensors.Captured = null;
            var previous = comma;
            previous.Status = "Stopping previous Comma instance";
            commaShutdown = Task.Run(() =>
            {
                previous.Dispose();
                return !previous.Running;
            });
        }

        void PollCommaRestart()
        {
            if (commaShutdown == null || !commaShutdown.IsCompleted)
                return;
            bool stopped = false;
            try
            {
                stopped = commaShutdown.GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                comma.Status = "Comma restart failed: " + e.Message;
            }
            commaShutdown = null;
            if (!stopped)
                return;
            comma = new CommaBridge(Root, services.Token, Vehicle.Profile);
            commaRevision = 0;
            comma.Start();
            Sensors.Captured = comma.Submit;
        }

        void OnApplicationQuit()
        {
            try
            {
                SavePerformance();
                evaluator?.Close();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning("Unable to save session: " + e.Message);
            }
            finally
            {
                recordings.Dispose();
                if (Sensors)
                    Sensors.Captured = null;
                try
                {
                    // A restart already owns disposal. Never race two shutdowns.
                    // If it exceeds this wait, the launch helper still observes
                    // this game's process exit and signals the owned Linux tree.
                    if (commaShutdown != null)
                    {
                        try
                        {
                            commaShutdown.Wait(12000);
                        }
                        catch (AggregateException e)
                        {
                            Debug.LogWarning("Comma shutdown: " + e.GetBaseException().Message);
                        }
                    }
                    else
                        comma?.Dispose();
                }
                finally
                {
                    services?.Dispose();
                    InputSystem.onDeviceChange -= DeviceChanged;
                    Input?.Dispose();
                }
            }
        }

        [Serializable]
        class Performance
        {
            public BuildIdentity build;
            public string map_id;
            public string gpu;
            public int width,
                height,
                view_mode;
            public bool qualification_interrupted;
            public string session;
            public float seconds,
                mean_fps,
                p95_ms,
                p99_ms;
            public int frames,
                camera_frames,
                camera_drops;
            public int observer_updates;
            public string driver_mode;
            public float moving_seconds;
            public int collision_count;
            public float sensor_to_warning_p95_ms;
            public float sensor_to_observer_p95_ms;
            public int warning_samples;
            public int camera_received,
                detector_processed;
            public float driving_seconds,
                paused_seconds,
                camera_hz,
                detection_hz;
        }
    }
}
