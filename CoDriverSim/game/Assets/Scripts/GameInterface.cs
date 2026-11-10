using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CoDriver
{
    public sealed class GameInterface : MonoBehaviour
    {
        CoDriverApp app;
        RectTransform root,
            menu,
            hud,
            fullComma;
        Text speed,
            warning,
            route,
            driveStatus,
            pageStatus,
            diagnostics,
            mapStreet;
        MapGraphic mini,
            fullMap;
        Text north;
        Text speedUnit;
        string shownPage = "";
        int bindingDevice;
        Vector2 mapCursor;
        RectTransform cursor;
        readonly RecordingViewer replay = new RecordingViewer();
        RawImage replayImage;
        Slider replaySlider;
        bool seeking;

        public void Initialize(CoDriverApp owner)
        {
            app = owner;
            var canvas = new GameObject(
                "CoDriverSim Canvas",
                typeof(RectTransform)
            ).AddComponent<Canvas>();
            canvas.transform.SetParent(transform, false);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            root = canvas.GetComponent<RectTransform>();
            var scale = canvas.gameObject.AddComponent<CanvasScaler>();
            scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scale.referenceResolution = new Vector2(1920, 1080);
            scale.matchWidthOrHeight = .5f;
            canvas.gameObject.AddComponent<GraphicRaycaster>();
            if (!EventSystem.current)
            {
                var events = new GameObject("Controller menu navigation");
                events.transform.SetParent(transform);
                events.AddComponent<EventSystem>();
                events.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            hud = UiDrawing.Rect(
                "Driving HUD",
                root,
                Vector2.one * .5f,
                Vector2.zero,
                Vector2.zero
            );
            UiDrawing.Fill(hud);
            var miniPanel = UiDrawing.Rect(
                "Navigation map",
                hud,
                Vector2.zero,
                new Vector2(232, 205),
                new Vector2(370, 285)
            );
            miniPanel.gameObject.AddComponent<Image>().color = new Color32(28, 33, 34, 235);
            miniPanel.gameObject.AddComponent<RectMask2D>();
            var miniContent = UiDrawing.Rect(
                "Map geometry",
                miniPanel,
                Vector2.one * .5f,
                Vector2.zero,
                Vector2.zero
            );
            UiDrawing.Fill(miniContent);
            mini = miniContent.gameObject.AddComponent<MapGraphic>();
            mini.Player = app.Vehicle.transform;
            mini.Navigation = app.Navigation;
            mini.raycastTarget = false;
            north = UiDrawing.Label(
                "N",
                hud,
                23,
                Vector2.zero,
                new Vector2(70, 365),
                new Vector2(95, 40)
            );
            route = UiDrawing.Label(
                "",
                hud,
                25,
                Vector2.zero,
                new Vector2(245, 42),
                new Vector2(400, 55)
            );
            speed = UiDrawing.Label(
                "0",
                hud,
                76,
                new Vector2(.5f, 0),
                new Vector2(0, 122),
                new Vector2(300, 100),
                TextAnchor.MiddleCenter
            );
            speedUnit = UiDrawing.Label(
                "mph",
                hud,
                25,
                new Vector2(.5f, 0),
                new Vector2(0, 66),
                new Vector2(200, 45),
                TextAnchor.MiddleCenter
            );
            var warningPanel = UiDrawing.Rect(
                "Camera co-driver panel",
                hud,
                new Vector2(1, 0),
                new Vector2(-375, 128),
                new Vector2(650, 150)
            );
            warningPanel.gameObject.AddComponent<RoundedPanel>().color = new Color(
                .025f,
                .035f,
                .04f,
                .90f
            );
            CompanionFace.Create(
                warningPanel,
                app.Companion,
                new Vector2(0, .5f),
                new Vector2(85, 0),
                new Vector2(125, 120)
            );
            UiDrawing.Label(
                "CAMERA CO-DRIVER",
                warningPanel,
                20,
                new Vector2(0, 1),
                new Vector2(365, -26),
                new Vector2(370, 35)
            );
            warning = UiDrawing.Label(
                "",
                warningPanel,
                29,
                Vector2.one * .5f,
                new Vector2(80, -15),
                new Vector2(430, 100)
            );
            driveStatus = UiDrawing.Label(
                "",
                hud,
                25,
                new Vector2(.5f, 1),
                new Vector2(0, -45),
                new Vector2(1650, 60),
                TextAnchor.MiddleCenter
            );
            fullComma = UiDrawing.Rect(
                "Full-screen Comma",
                root,
                Vector2.one * .5f,
                Vector2.zero,
                Vector2.zero
            );
            UiDrawing.Fill(fullComma);
            fullComma.gameObject.AddComponent<OpenpilotDisplay>().Initialize(app);
            menu = UiDrawing.Rect(
                "Application menu",
                root,
                Vector2.one * .5f,
                Vector2.zero,
                Vector2.zero
            );
            UiDrawing.Fill(menu);
        }

        void Update()
        {
            if (!app)
                return;
            bool drive = app.Page == "Drive";
            hud.gameObject.SetActive(drive && app.ViewMode != 3);
            fullComma.gameObject.SetActive(drive && app.ViewMode == 3);
            menu.gameObject.SetActive(!drive);
            mini.Map = app.Town.Map;
            speed.text = (app.Vehicle.Speed * 2.236936f).ToString("F0");
            speed.gameObject.SetActive(app.ViewMode != 1);
            speedUnit.gameObject.SetActive(app.ViewMode != 1);
            warning.text = app.Companion.Message;
            warning.color =
                app.Companion.Severity == "urgent" ? new Color(1, .35f, .2f)
                : app.Companion.Severity == "caution" ? new Color(1, .78f, .3f)
                : Color.white;
            route.text = app.Navigation.Instruction;
            float northAngle = -mini.Heading * Mathf.Deg2Rad;
            north.rectTransform.anchoredPosition = new Vector2(
                232 + Mathf.Sin(northAngle) * 153,
                205 + Mathf.Cos(northAngle) * 111
            );
            driveStatus.text =
                (
                    app.Mode == "qualification_driver" ? "SIMULATION TEST DRIVER"
                    : app.AutomationActive ? "COMMA ACTIVE"
                    : "HUMAN DRIVING"
                )
                + (app.Vehicle.Reverse ? "    ·    R" : "    ·    D")
                + "    ·    "
                + app.ScenarioStatus
                + (app.Arbiter.Notice.Length > 0 ? "\n" + app.Arbiter.Notice : "")
                + (app.GearNotice.Length > 0 ? "\n" + app.GearNotice : "");
            if (shownPage != app.Page)
            {
                shownPage = app.Page;
                BuildPage();
            }
            if (pageStatus)
                pageStatus.text =
                    app.Town.Map.name
                    + "    ·    "
                    + (Gamepad.current != null ? "Xbox connected" : "Keyboard controls");
            if (diagnostics)
                diagnostics.text =
                    $"RENDERING     {app.FramesPerSecond:F1} FPS\nCAMERA          {app.Observation?.camera_hz:F1} Hz\nDETECTION     {app.Observation?.detection_hz:F1} Hz\nLATENCY          {app.Observation?.latency_ms:F0} ms\n\nOBSERVER\n{app.ServiceStatus}\n\nCOMMA\n{app.CommaStatus}\n\nCONTROL AUTHORITY\n{app.Arbiter.Reason}\n\n{app.ScenarioSummary}";
            if (app.Page == "Map" && fullMap)
            {
                Vector2 move = Gamepad.current?.leftStick.ReadValue() ?? Vector2.zero;
                mapCursor += move * Time.unscaledDeltaTime * 400;
                mapCursor = Vector2.ClampMagnitude(mapCursor, 440);
                cursor.anchoredPosition = mapCursor;
                var lane = app.Town.Map.Nearest(fullMap.Unproject(mapCursor), routable: false);
                mapStreet.text = lane?.name ?? "District map";
            }
            if (app.Page == "Recordings" && replayImage)
            {
                replay.Tick();
                replayImage.texture = replay.CurrentTexture;
                if (replaySlider)
                {
                    replaySlider.maxValue = Mathf.Max(1, replay.Duration);
                    seeking = true;
                    replaySlider.value = replay.Position;
                    seeking = false;
                }
            }
        }

        Button AddButton(string text, float x, float y, float width, Action click) =>
            UiDrawing.Button(
                text,
                menu,
                new Vector2(0, 1),
                new Vector2(x + width * .5f, -y),
                new Vector2(width, 70),
                click
            );

        Text Title(string text, float x = 80, float y = 76, int font = 58) =>
            UiDrawing.Label(
                text,
                menu,
                font,
                new Vector2(0, 1),
                new Vector2(x + 750, -y),
                new Vector2(1500, 95)
            );

        void ClearPage()
        {
            foreach (Transform child in menu)
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
            pageStatus = diagnostics = mapStreet = null;
            replayImage = null;
            replaySlider = null;
            fullMap = null;
        }

        void BuildPage()
        {
            ClearPage();
            UiDrawing.Panel("Menu background", menu, new Color32(9, 9, 9, 248));
            string page = app.Page;
            Title(
                page == "Home" || page == "Pause" ? "CoDriverSim"
                : page == "Scenarios" ? "Scenario tests"
                : page
            );
            pageStatus = UiDrawing.Label(
                "",
                menu,
                27,
                new Vector2(0, 1),
                new Vector2(830, -145),
                new Vector2(1500, 55)
            );
            Button first = null;
            if (page == "Home" || page == "Pause")
            {
                string[] names =
                {
                    page == "Pause" ? "Resume drive" : "Play",
                    "Scenario tests",
                    "Recordings",
                    "Settings",
                    "Diagnostics",
                    "Quit",
                };
                for (int i = 0; i < names.Length; i++)
                {
                    int item = i;
                    var button = AddButton(
                        names[i],
                        80,
                        260 + i * 108,
                        420,
                        () =>
                        {
                            if (item == 0)
                            {
                                if (page == "Pause")
                                    app.ResumeDrive();
                                else
                                    app.StartDriving("Free drive");
                            }
                            else if (item == 5)
                                Application.Quit();
                            else
                                app.OpenPage(
                                    new[]
                                    {
                                        "",
                                        "Scenarios",
                                        "Recordings",
                                        "Settings",
                                        "Diagnostics",
                                    }[item]
                                );
                        }
                    );
                    if (first == null)
                        first = button;
                }
                var accent = UiDrawing.Rect(
                    "Accent",
                    menu,
                    new Vector2(0, 1),
                    new Vector2(620, -310),
                    new Vector2(6, 210)
                );
                accent.gameObject.AddComponent<Image>().color = UiDrawing.Green;
                UiDrawing.Label(
                    "Your drive.\nYour co-driver.",
                    menu,
                    72,
                    new Vector2(0, 1),
                    new Vector2(1150, -320),
                    new Vector2(960, 230)
                );
                UiDrawing.Label(
                    "A camera companion for the road ahead.\nDrive the district, practise road rules, and compare\nthe same scenario with stock Comma.",
                    menu,
                    32,
                    new Vector2(0, 1),
                    new Vector2(1150, -560),
                    new Vector2(960, 170)
                );
                UiDrawing.Label(
                    "RT / LT  accelerate & brake     Left stick  steer\nY  camera     X  Comma     View  map     Menu  pause\n\nKeyboard: W A S D · C camera · X Comma · Tab map",
                    menu,
                    26,
                    new Vector2(0, 1),
                    new Vector2(1150, -825),
                    new Vector2(960, 180)
                );
            }
            else if (page == "Scenarios")
            {
                string[] modes = { "Human · muted", "Human + co-driver", "Stock Comma" };
                for (int i = 0; i < 3; i++)
                {
                    int mode = i;
                    var b = AddButton(
                        modes[i],
                        80 + i * 580,
                        240,
                        540,
                        () =>
                        {
                            app.Mode = new[] { "human_muted", "human_ai", "comma" }[mode];
                            shownPage = "";
                        }
                    );
                    if (first == null)
                        first = b;
                }
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
                for (int i = 0; i < names.Length; i++)
                {
                    string name = names[i];
                    AddButton(
                        name,
                        80 + i % 3 * 580,
                        350 + i / 3 * 100,
                        540,
                        () => app.StartDriving(name)
                    );
                }
                UiDrawing.Label(
                    "Comparison: " + app.Mode + "    ·    " + app.ScenarioSummary,
                    menu,
                    25,
                    new Vector2(0, 0),
                    new Vector2(1050, 175),
                    new Vector2(1550, 120)
                );
            }
            else if (page == "Settings")
            {
                Title("District", 80, 240, 32);
                first = AddButton(
                    "Downtown Davis",
                    80,
                    325,
                    550,
                    () => app.SwitchMap("davis-downtown")
                );
                AddButton(
                    "Cedar Grove · regression town",
                    690,
                    325,
                    650,
                    () => app.SwitchMap("cedar-grove")
                );
                Title("Controls", 80, 435, 32);
                AddButton(
                    bindingDevice == 0 ? "Remap Xbox controls" : "Remap keyboard controls",
                    80,
                    520,
                    550,
                    () => app.OpenPage("Controls")
                );
                AddButton(
                    "Input device: " + (bindingDevice == 0 ? "Xbox" : "keyboard"),
                    690,
                    520,
                    650,
                    () =>
                    {
                        bindingDevice = 1 - bindingDevice;
                        shownPage = "";
                    }
                );
                UiDrawing.Label(
                    "Steering dead zone",
                    menu,
                    30,
                    new Vector2(0, 1),
                    new Vector2(300, -640),
                    new Vector2(440, 60)
                );
                var slider = Slider(
                    menu,
                    new Vector2(950, -640),
                    new Vector2(700, 40),
                    .05f,
                    .35f,
                    app.Input.Deadzone
                );
                slider.onValueChanged.AddListener(v =>
                {
                    app.Input.Deadzone = v;
                    PlayerPrefs.SetFloat("deadzone", v);
                });
                AddButton(
                    "Services and performance",
                    80,
                    790,
                    550,
                    () => app.OpenPage("Diagnostics")
                );
                AddButton("Cockpit view", 690, 790, 650, () => app.OpenPage("Cockpit"));
            }
            else if (page == "Cockpit")
            {
                string[] labels = { "Seat forward / back", "Seat height", "Field of view" };
                float[] values = { app.SeatForward, app.SeatHeight, app.CockpitFieldOfView };
                float[] minimum =  { -.20f, -.08f, 60 },
                    maximum =  { .12f, .08f, 85 };
                for (int i = 0; i < labels.Length; i++)
                {
                    int setting = i;
                    Title(labels[i], 80, 270 + i * 150, 32);
                    var control = Slider(
                        menu,
                        new Vector2(1050, -300 - i * 150),
                        new Vector2(800, 40),
                        minimum[i],
                        maximum[i],
                        values[i]
                    );
                    control.onValueChanged.AddListener(v =>
                    {
                        if (setting == 0)
                        {
                            app.SeatForward = v;
                            PlayerPrefs.SetFloat("seatForward", v);
                        }
                        else if (setting == 1)
                        {
                            app.SeatHeight = v;
                            PlayerPrefs.SetFloat("seatHeight", v);
                        }
                        else
                        {
                            app.CockpitFieldOfView = v;
                            PlayerPrefs.SetFloat("cockpitFov", v);
                        }
                    });
                }
                first = AddButton(
                    "Reset seat and view",
                    80,
                    790,
                    550,
                    () =>
                    {
                        app.SeatForward = app.SeatHeight = 0;
                        app.CockpitFieldOfView = 70;
                        PlayerPrefs.DeleteKey("seatForward");
                        PlayerPrefs.DeleteKey("seatHeight");
                        PlayerPrefs.DeleteKey("cockpitFov");
                        shownPage = "";
                    }
                );
                UiDrawing.Label(
                    "Camera position adjusts your view inside the cabin.",
                    menu,
                    27,
                    new Vector2(0, 1),
                    new Vector2(620, -690),
                    new Vector2(1080, 65)
                );
            }
            else if (page == "Controls")
            {
                var actions = new[]
                {
                    app.Input.Steering,
                    app.Input.Gas,
                    app.Input.Brake,
                    app.Input.Handbrake,
                    app.Input.Reverse,
                    app.Input.Camera,
                    app.Input.Comma,
                    app.Input.Left,
                    app.Input.Right,
                    app.Input.Horn,
                    app.Input.Pause,
                    app.Input.FullMap,
                    app.Input.CruiseUp,
                    app.Input.CruiseDown,
                    app.Input.Look,
                };
                var bindings = new List<(InputAction, int, string)>();
                foreach (var action in actions)
                {
                    if (bindingDevice == 1 && action == app.Input.Steering)
                    {
                        bindings.Add((action, 2, "Steer left"));
                        bindings.Add((action, 3, "Steer right"));
                    }
                    else if (bindingDevice == 1 && action == app.Input.Look)
                    {
                        for (int i = 2; i <= 5; i++)
                            bindings.Add(
                                (
                                    action,
                                    i,
                                    "Look " + new[] { "up", "down", "left", "right" }[i - 2]
                                )
                            );
                    }
                    else
                        bindings.Add((action, bindingDevice == 0 ? 0 : 1, action.name));
                }
                for (int i = 0; i < bindings.Count; i++)
                {
                    var binding = bindings[i];
                    var b = AddButton(
                        binding.Item3,
                        80 + i % 3 * 580,
                        250 + i / 3 * 105,
                        540,
                        () =>
                        {
                            Title("Press a control · Esc cancels", 80, 900, 30);
                            app.RebindControl(binding.Item1, binding.Item2, () => shownPage = "");
                        }
                    );
                    if (first == null)
                        first = b;
                }
            }
            else if (page == "Diagnostics")
            {
                diagnostics = UiDrawing.Label(
                    "",
                    menu,
                    29,
                    new Vector2(0, 1),
                    new Vector2(1150, -570),
                    new Vector2(1200, 710)
                );
                first = AddButton("Restart camera service", 80, 270, 400, app.RestartObserver);
                AddButton("Restart Comma", 80, 380, 400, app.RestartComma);
                AddButton("World activity checks", 80, 490, 400, () => app.OpenPage("Activities"));
            }
            else if (page == "Activities")
            {
                string[] systems = app.Town.Map.IsDavis
                    ? new[]
                    {
                        "Traffic",
                        "Pedestrians",
                        "Animals",
                        "Bus",
                        "Railway",
                        "Emergency",
                        "Parking",
                        "Construction",
                    }
                    : new[]
                    {
                        "Traffic",
                        "Pedestrians",
                        "Animals",
                        "Bus",
                        "Streetcar",
                        "Railway",
                        "Emergency",
                        "Parking",
                        "Construction",
                        "Buildings",
                    };
                for (int i = 0; i < systems.Length; i++)
                {
                    string system = systems[i];
                    var button = AddButton(
                        system,
                        80 + i % 3 * 580,
                        270 + i / 3 * 130,
                        540,
                        () => app.WatchActivity(system)
                    );
                    if (first == null)
                        first = button;
                }
                Title("Park the player car before opening an activity view.", 80, 850, 30);
            }
            else if (page == "Recordings")
            {
                string folder = Path.Combine(app.Root, "outputs", "recordings");
                string[] clips = Array.Empty<string>();
                try
                {
                    clips = Directory.Exists(folder)
                        ? Directory
                            .GetFiles(folder, "recording.json", SearchOption.AllDirectories)
                            .OrderByDescending(File.GetCreationTimeUtc)
                            .Take(6)
                            .ToArray()
                        : Array.Empty<string>();
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    Title("Recording storage unavailable: " + e.Message, 80, 850, 26);
                }
                for (int i = 0; i < clips.Length; i++)
                {
                    string clip = clips[i];
                    var b = AddButton(
                        new DirectoryInfo(Path.GetDirectoryName(clip)).Name,
                        80,
                        250 + i * 95,
                        450,
                        () => replay.Open(clip)
                    );
                    if (first == null)
                        first = b;
                }
                if (clips.Length == 0)
                    Title("Saved event clips will appear here.", 80, 260, 30);
                var picture = UiDrawing.Rect(
                    "Recorded camera",
                    menu,
                    new Vector2(0, 1),
                    new Vector2(1200, -520),
                    new Vector2(1100, 690)
                );
                replayImage = picture.gameObject.AddComponent<RawImage>();
                replayImage.texture = replay.CurrentTexture;
                replayImage.raycastTarget = false;
                AddButton("Play / pause replay", 650, 930, 400, replay.Toggle);
                replaySlider = Slider(
                    menu,
                    new Vector2(1440, -930),
                    new Vector2(630, 40),
                    0,
                    Mathf.Max(1, replay.Duration),
                    replay.Position
                );
                replaySlider.onValueChanged.AddListener(v =>
                {
                    if (!seeking)
                        replay.Seek(v);
                });
            }
            else if (page == "Map")
            {
                var area = UiDrawing.Rect(
                    "Full district map",
                    menu,
                    new Vector2(0, 1),
                    new Vector2(620, -620),
                    new Vector2(850, 850)
                );
                area.gameObject.AddComponent<Image>().color = new Color32(28, 33, 34, 255);
                area.gameObject.AddComponent<RectMask2D>();
                var mapContent = UiDrawing.Rect(
                    "Full map geometry",
                    area,
                    Vector2.one * .5f,
                    Vector2.zero,
                    Vector2.zero
                );
                UiDrawing.Fill(mapContent);
                fullMap = mapContent.gameObject.AddComponent<MapGraphic>();
                fullMap.Map = app.Town.Map;
                fullMap.Player = app.Vehicle.transform;
                fullMap.Navigation = app.Navigation;
                fullMap.Full = true;
                fullMap.Selected = SelectDestination;
                fullMap.Centre = (app.Town.Map.boundsMin + app.Town.Map.boundsMax) * .5f;
                fullMap.Range =
                    Mathf.Max(
                        app.Town.Map.boundsMax.x - app.Town.Map.boundsMin.x,
                        app.Town.Map.boundsMax.z - app.Town.Map.boundsMin.z
                    ) * .56f;
                var labelPositions = new List<Vector2>();
                foreach (
                    var street in app
                        .Town.Map.lanes.Where(l =>
                            !l.internalLane && !l.boundary && l.name != "Access road"
                        )
                        .GroupBy(l => l.name)
                )
                {
                    var lane = street.OrderByDescending(l => l.Path.Length).First();
                    var p = fullMap.Project(lane.Path.Position(lane.Path.Length * .5f));
                    if (labelPositions.Any(v => (v - p).sqrMagnitude < 1800))
                        continue;
                    labelPositions.Add(p);
                    UiDrawing
                        .Label(
                            street.Key,
                            area,
                            15,
                            Vector2.one * .5f,
                            p,
                            new Vector2(145, 25),
                            TextAnchor.MiddleCenter
                        )
                        .color = new Color(.85f, .88f, .86f);
                }
                foreach (var place in app.Town.Map.landmarks.Where(l => l.subtype == "station"))
                    UiDrawing
                        .Label(
                            "● " + place.name + " station",
                            area,
                            17,
                            Vector2.one * .5f,
                            fullMap.Project(place.position),
                            new Vector2(175, 30),
                            TextAnchor.MiddleCenter
                        )
                        .color = new Color(1, .85f, .4f);
                cursor = UiDrawing
                    .Label(
                        "+",
                        area,
                        45,
                        Vector2.one * .5f,
                        Vector2.zero,
                        new Vector2(60, 60),
                        TextAnchor.MiddleCenter
                    )
                    .rectTransform;
                mapCursor = Vector2.zero;
                mapStreet = UiDrawing.Label(
                    "",
                    menu,
                    40,
                    new Vector2(0, 1),
                    new Vector2(1500, -290),
                    new Vector2(650, 100)
                );
                UiDrawing.Label(
                    "Choose a destination\n\nClick a street, or move the cursor with\nthe left stick and select a destination.\n\nN ↑     North-up district view\n\n"
                        + app.Town.Map.attribution
                        + (
                            app.Town.Map.IsDavis
                                ? "\n\nBridge Loop: fictional simulation addition."
                                : ""
                        ),
                    menu,
                    28,
                    new Vector2(0, 1),
                    new Vector2(1510, -540),
                    new Vector2(660, 350)
                );
                first = AddButton(
                    "Set destination",
                    1190,
                    800,
                    620,
                    () => SelectDestination(fullMap.Unproject(mapCursor))
                );
                AddButton("Return to driving", 1190, 915, 620, app.ResumeDrive);
            }
            else if (page == "Loading")
                Title("Preparing the district…", 80, 350, 46);
            if (page != "Home" && page != "Pause" && page != "Map" && page != "Loading")
            {
                var back = AddButton("‹ Back", 80, 1000, 300, () => app.OpenPage("Pause"));
                if (first == null)
                    first = back;
            }
            if (first)
                EventSystem.current.SetSelectedGameObject(first.gameObject);
        }

        void SelectDestination(Vector3 point)
        {
            var lane = app.Town.Map.Nearest(point);
            if (lane == null)
                return;
            app.Navigation.SelectPoint(app.Vehicle.transform.position, point, lane.name);
            if (mapStreet)
                mapStreet.text = "Route to " + lane.name;
        }

        static Slider Slider(
            Transform parent,
            Vector2 position,
            Vector2 size,
            float min,
            float max,
            float value
        )
        {
            var r = UiDrawing.Rect("Slider", parent, new Vector2(0, 1), position, size);
            var bar = r.gameObject.AddComponent<Image>();
            bar.color = new Color(.18f, .18f, .18f);
            var slider = r.gameObject.AddComponent<Slider>();
            var handle = UiDrawing.Rect(
                "Handle",
                r,
                Vector2.one * .5f,
                Vector2.zero,
                new Vector2(35, 55)
            );
            handle.gameObject.AddComponent<Image>().color = Color.white;
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            return slider;
        }

        void OnDestroy() => replay.Dispose();
    }
}
