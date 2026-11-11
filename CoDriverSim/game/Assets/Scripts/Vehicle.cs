using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoDriver
{
    [Serializable]
    public class CommaCommand
    {
        public string configuration_sha256;
        public string session_id;
        public long frame_id;
        public bool active,
            healthy,
            engageable;
        public float steering_deg,
            acceleration_mps2;
        public string status = "Waiting for Comma";
    }

    // Simulation-only authority. The observer protocol has no access to this type.
    public sealed class ControlArbiter
    {
        bool wasActive;
        double requestedAt;
        public bool Requested { get; private set; }
        public string Reason { get; private set; } = "Human driving";
        public string Notice =>
            Reason.StartsWith("Comma ") || Reason.StartsWith("Waiting for stock") ? Reason : "";

        public void Request(double now = 0)
        {
            Requested = true;
            wasActive = false;
            requestedAt = now;
            Reason = "Waiting for stock Comma engagement";
        }

        public bool TryRequest(CommaCommand command, string session, double age, double now)
        {
            if (
                command == null
                || !double.IsFinite(age)
                || age < 0
                || age > .25
                || command.session_id != session
                || !float.IsFinite(command.steering_deg)
                || !float.IsFinite(command.acceleration_mps2)
            )
            {
                Cancel("Comma unavailable: waiting for a fresh connection");
                return false;
            }
            if (!command.healthy || !command.engageable)
            {
                Cancel("Comma unavailable: " + command.status + ". Press X again when ready.");
                return false;
            }
            Request(now);
            return true;
        }

        public void Cancel(string reason)
        {
            Requested = false;
            wasActive = false;
            Reason = reason;
        }

        public bool Accept(
            CommaCommand command,
            string session,
            double age,
            float steer,
            float brake,
            bool paused,
            bool controllerLost,
            float throttle = 0,
            bool handbrake = false,
            double now = 0
        )
        {
            if (!Requested)
                return false;
            if (
                paused
                || controllerLost
                || brake > .08f
                || Mathf.Abs(steer) > .25f
                || throttle > .15f
                || handbrake
            )
            {
                Cancel("Human takeover");
                return false;
            }
            if (
                command == null
                || !double.IsFinite(age)
                || age > .25
                || age < 0
                || command.session_id != session
                || !float.IsFinite(command.steering_deg)
                || !float.IsFinite(command.acceleration_mps2)
            )
            {
                Cancel("Comma connection stale");
                return false;
            }
            if (!command.healthy)
            {
                Cancel(
                    "Comma rejected/disengaged: " + command.status + ". Press X again when ready."
                );
                return false;
            }
            if (!command.active)
            {
                if (wasActive)
                    Cancel("Comma disengaged: " + command.status);
                else if (now - requestedAt >= 2)
                    Cancel("Comma did not engage. Release the controls and press X to retry.");
                return false;
            }
            wasActive = true;
            Reason = "Stock Comma driving · simulation only";
            return true;
        }
    }

    public sealed class DriverInput : IDisposable
    {
        public readonly InputActionMap Map = new InputActionMap("Driving");
        public InputAction Steering,
            Gas,
            Brake,
            Look,
            Handbrake,
            Reverse,
            Camera,
            Comma,
            Pause,
            Left,
            Right,
            Horn,
            CruiseUp,
            CruiseDown,
            FullMap;
        public float Deadzone = .12f;
        public int ReversePressCount,
            PausePressCount,
            CameraPressCount,
            MapPressCount,
            CommaPressCount;

        public static bool HasAvailableGamepad
        {
            get
            {
                foreach (var pad in Gamepad.all)
                    if (pad.added && pad.enabled)
                        return true;
                return false;
            }
        }

        public DriverInput(bool loadPreferences = true)
        {
            Steering = Map.AddAction("Steering", InputActionType.Value, "<Gamepad>/leftStick/x");
            Steering
                .AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/a")
                .With("Positive", "<Keyboard>/d");
            Gas = Axis("Accelerator", "<Gamepad>/rightTrigger", "<Keyboard>/w");
            Brake = Axis("Brake", "<Gamepad>/leftTrigger", "<Keyboard>/s");
            Look = Map.AddAction("Look", InputActionType.Value, "<Gamepad>/rightStick");
            Look.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");
            Handbrake = Button("Handbrake", "buttonSouth", "space");
            Reverse = Button("Forward / reverse", "buttonEast", "r");
            Camera = Button("Camera", "buttonNorth", "c");
            Comma = Button("Comma toggle", "buttonWest", "x");
            Pause = Button("Pause", "start", "escape");
            Left = Button("Left indicator", "leftShoulder", "q");
            Right = Button("Right indicator", "rightShoulder", "e");
            Horn = Button("Horn", "leftStickPress", "h");
            CruiseUp = Button("Cruise up", "dpad/up", "equals");
            CruiseDown = Button("Cruise down", "dpad/down", "minus");
            FullMap = Button("Full map", "select", "tab");
            Reverse.performed += _ => ReversePressCount++;
            Pause.performed += _ => PausePressCount++;
            Camera.performed += _ => CameraPressCount++;
            FullMap.performed += _ => MapPressCount++;
            Comma.performed += _ => CommaPressCount++;
            string saved = loadPreferences ? PlayerPrefs.GetString("bindings", "") : "";
            if (saved.Length > 0)
                Map.LoadBindingOverridesFromJson(saved);
            if (loadPreferences && PlayerPrefs.HasKey("binding_overrides_v2"))
                RestoreBindings(PlayerPrefs.GetString("binding_overrides_v2"));
            Deadzone = loadPreferences ? PlayerPrefs.GetFloat("deadzone", .12f) : .12f;
            Map.Enable();
        }

        InputAction Axis(string name, string pad, string key)
        {
            var a = Map.AddAction(name, InputActionType.Value, pad);
            a.AddBinding(key);
            return a;
        }

        InputAction Button(string name, string pad, string key)
        {
            var action = Map.AddAction(name, InputActionType.Button, "<Gamepad>/" + pad);
            action.AddBinding("<Keyboard>/" + key);
            return action;
        }

        public float Steer
        {
            get
            {
                float v = Steering.ReadValue<float>();
                return Mathf.Abs(v) < Deadzone ? 0 : v;
            }
        }

        public void Rebind(InputAction action, int binding, Action done)
        {
            action.Disable();
            action
                .PerformInteractiveRebinding(binding)
                .WithControlsHavingToMatchPath(binding == 0 ? "<Gamepad>/*" : "<Keyboard>/*")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnComplete(op =>
                {
                    op.Dispose();
                    action.Enable();
                    PlayerPrefs.SetString("binding_overrides_v2", ExportBindings());
                    PlayerPrefs.Save();
                    done();
                })
                .OnCancel(op =>
                {
                    op.Dispose();
                    action.Enable();
                    done();
                })
                .Start();
        }

        // Runtime-created InputAction binding GUIDs change each launch. Persist
        // our versioned action names/indices rather than those transient GUIDs.
        public string ExportBindings()
        {
            var rows = new List<BindingOverride>();
            foreach (var action in Map.actions)
                for (int i = 0; i < action.bindings.Count; i++)
                    if (action.bindings[i].overridePath != null)
                        rows.Add(
                            new BindingOverride
                            {
                                action = action.name,
                                index = i,
                                path = action.bindings[i].overridePath,
                            }
                        );
            return JsonUtility.ToJson(new BindingSet { bindings = rows.ToArray() });
        }

        public void RestoreBindings(string json)
        {
            var settings = JsonUtility.FromJson<BindingSet>(json);
            if (settings == null || settings.version != 2 || settings.bindings == null)
                return;
            foreach (var row in settings.bindings)
            {
                var action = Map.FindAction(row.action);
                if (action != null && row.index >= 0 && row.index < action.bindings.Count)
                    action.ApplyBindingOverride(row.index, row.path);
            }
        }

        [Serializable]
        class BindingSet
        {
            public int version = 2;
            public BindingOverride[] bindings;
        }

        [Serializable]
        class BindingOverride
        {
            public string action,
                path;
            public int index;
        }

        public void Dispose()
        {
            Map.Disable();
            Map.Dispose();
        }
    }

    public sealed class Vehicle : MonoBehaviour
    {
        public Rigidbody Body { get; private set; }
        public float Speed => Body.linearVelocity.magnitude;
        public float SignedSpeed => Vector3.Dot(Body.linearVelocity, transform.forward);
        public float SteerAngle { get; private set; }
        public float Throttle,
            Brake,
            Steer;
        public bool Handbrake,
            Reverse;
        public int Indicator;
        public AudioSource Horn;
        public int Collisions { get; private set; }
        public string LastContact = "None";
        public VehicleProfile Profile = new VehicleProfile();
        public AssetDefinition VisualDefinition { get; private set; }
        public RvpVehicleDynamics Dynamics { get; private set; }
        public int GroundedWheels => Dynamics?.GroundedWheels ?? 0;
        public float[] WheelRPM => Dynamics?.Rpm ?? new float[4];
        public float EngineRpm => Dynamics?.EngineRpm ?? 0;
        public int Gear => Dynamics?.Gear ?? 1;
        public float CommandedRoadAngle = float.NaN;

        // The caller has already placed and stopped the rigidbody. Reset commands
        // and retained drivetrain state before paused camera capture can resume.
        public void ResetForPlacement()
        {
            Throttle = Brake = Steer = SteerAngle = 0;
            CommandedRoadAngle = float.NaN;
            Handbrake = Reverse = false;
            Indicator = 0;
            Dynamics?.ResetForPlacement();
        }

        public bool TryToggleDirection()
        {
            if (Speed >= .3f)
                return false;
            Reverse = !Reverse;
            return true;
        }

        Transform[] visuals = new Transform[4];
        readonly WheelVisualBinding[] wheelBindings = new WheelVisualBinding[4];
        Renderer[] blinkers;
        Material amber,
            dark;

        public void ApplyDesiredAcceleration(float desired)
        {
            float grade = -Vector3.Dot(Physics.gravity, transform.forward);
            float resistance =
                Profile.rollingResistance * 9.81f + Speed * Speed * Profile.drag / Profile.mass;
            float force = Profile.mass * (desired + resistance + grade);
            int gear = Mathf.Clamp(Gear, 1, Profile.gears.Length);
            float available =
                Profile.engineTorque
                * Profile.gears[gear - 1]
                * Profile.finalDrive
                * Profile.efficiency
                / Profile.radius;
            Throttle = Mathf.Clamp01(force / Mathf.Max(1, available));
            Brake = Mathf.Clamp01(-force / Profile.brakeForce);
        }

        public void Initialize()
        {
            gameObject.layer = TrafficClearance.PlayerLayer;
            Body = gameObject.AddComponent<Rigidbody>();
            Dynamics = new RvpVehicleDynamics(Body, Profile);
            var shape = gameObject.AddComponent<BoxCollider>();
            shape.center = Profile.collisionCentre;
            shape.size = Profile.collisionSize;
            var authored =
                Profile.visualResource.Length > 0
                    ? Resources.Load<GameObject>(Profile.visualResource)
                    : null;
            if (authored)
                VisualDefinition = Instantiate(authored, transform).GetComponent<AssetDefinition>();
            else
                Art.Car(transform, Art.Paint, false);
            // Exterior glass is opaque at this quality level. The driver's view
            // excludes it; the independent forward camera retains its own mask.
            foreach (var renderer in GetComponentsInChildren<Renderer>())
                if (renderer.name == "Cabin")
                    renderer.gameObject.layer = 8;
            var interior = new GameObject("Interior");
            interior.transform.SetParent(transform, false);
            interior.AddComponent<Cockpit>().Initialize(this);
            Horn = gameObject.AddComponent<AudioSource>();
            Horn.loop = true;
            Horn.volume = .16f;
            Horn.spatialBlend = .7f;
            var samples = new float[22050];
            for (int i = 0; i < samples.Length; i++)
                samples[i] =
                    .3f
                    * (
                        Mathf.Sin(i * 2 * Mathf.PI * 350 / 22050)
                        + Mathf.Sin(i * 2 * Mathf.PI * 440 / 22050)
                    );
            Horn.clip = AudioClip.Create("Car horn", samples.Length, 1, 22050, false);
            Horn.clip.SetData(samples, 0);
            for (int i = 0; i < 4; i++)
            {
                visuals[i] =
                    VisualDefinition && VisualDefinition.wheels.Length == 4
                        ? VisualDefinition.wheels[i]
                        : Art.Wheel(
                            transform,
                            new Vector3(
                                (i % 2 == 0 ? -1 : 1) * Profile.track / 2,
                                Profile.radius,
                                (i < 2 ? 1 : -1) * Profile.wheelbase / 2
                            )
                        );
                wheelBindings[i] = WheelVisualBinding.Bind(transform, visuals[i], i, Profile);
            }
            if (VisualDefinition)
                VisualDefinition.wheelBindings = wheelBindings;
            amber = Art.Material("Indicator", new Color(1, .55f, .05f), true);
            dark = Art.Material("Indicator off", new Color(.15f, .09f, .02f));
            blinkers = new Renderer[4];
            for (int i = 0; i < 4; i++)
                blinkers[i] = Art.Box(
                        "Indicator",
                        transform,
                        new Vector3(i % 2 == 0 ? -.68f : .68f, .7f, i < 2 ? 2.14f : -2.14f),
                        new Vector3(.12f, .035f, .02f),
                        dark,
                        false
                    )
                    .GetComponent<Renderer>();
        }

        void FixedUpdate()
        {
            if (Dynamics == null)
                return;
            float target = float.IsFinite(CommandedRoadAngle)
                ? CommandedRoadAngle
                : Steer * Mathf.Lerp(Profile.maxSteering, 10, Mathf.Clamp01(Speed / 30));
            SteerAngle = Mathf.MoveTowards(SteerAngle, target, 110 * Time.fixedDeltaTime);
            Dynamics.Step(
                new VehicleControls
                {
                    throttle = Throttle,
                    brake = Brake,
                    roadWheelAngle = SteerAngle,
                    reverse = Reverse,
                    handbrake = Handbrake,
                },
                Time.fixedDeltaTime
            );
        }

        void Update()
        {
            for (int i = 0; i < 4; i++)
                if (Dynamics != null && visuals[i])
                {
                    wheelBindings[i].Apply(transform, Dynamics);
                }
            for (int i = 0; i < 4; i++)
                if (blinkers != null)
                    blinkers[i].sharedMaterial =
                        (Indicator == (i % 2 == 0 ? -1 : 1) && Time.time % 1 < .5f) ? amber : dark;
        }

        void OnCollisionEnter(Collision collision)
        {
            LastContact = collision.collider.name;
            if (
                collision.relativeVelocity.magnitude > 1.5f
                || (
                    collision.collider.GetComponentInParent<Walker>()
                    && collision.relativeVelocity.magnitude > .3f
                )
            )
            {
                Collisions++;
                Debug.Log(
                    "CODRIVER_CONTACT "
                        + LastContact
                        + " at "
                        + transform.position
                        + " relative speed "
                        + collision.relativeVelocity.magnitude
                );
            }
        }
    }
}
