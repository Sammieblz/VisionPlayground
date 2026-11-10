using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoDriver
{
    // Evaluator-only measurements; never included in the camera protocol.
    public sealed class DrivingDiagnostics : MonoBehaviour
    {
        CoDriverApp app;
        float next;
        StreamWriter writer;
        string path;
        OpenpilotDisplay[] displays;

        public void Initialize(CoDriverApp owner)
        {
            app = owner;
            displays = FindObjectsByType<OpenpilotDisplay>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );
            try
            {
                string dir = Path.Combine(app.Root, "outputs", "sessions", app.Session);
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, "driving-diagnostics.jsonl");
                writer = new StreamWriter(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning("Diagnostics storage unavailable: " + e.Message);
            }
        }

        void Update()
        {
            if (app == null || Time.unscaledTime < next)
                return;
            next = Time.unscaledTime + .25f;
            var v = app.Vehicle;
            var row = new Sample
            {
                timestamp = Time.realtimeSinceStartupAsDouble,
                paused = app.Paused,
                controller = Gamepad.current?.displayName ?? "keyboard",
                controller_available = DriverInput.HasAvailableGamepad,
                focused = Application.isFocused,
                input_update_mode = InputSystem.settings.updateMode.ToString(),
                page = app.Page,
                view_mode = app.ViewMode,
                reverse_presses = app.Input.ReversePressCount,
                pause_presses = app.Input.PausePressCount,
                camera_presses = app.Input.CameraPressCount,
                map_presses = app.Input.MapPressCount,
                comma_presses = app.Input.CommaPressCount,
                position = v.transform.position,
                rotation = v.transform.eulerAngles,
                speed_mps = v.Speed,
                throttle = v.Throttle,
                brake = v.Brake,
                steer = v.Steer,
                handbrake = v.Handbrake,
                reverse = v.Reverse,
                grounded_wheels = v.GroundedWheels,
                wheel_rpm = v.WheelRPM,
                contact = v.LastContact,
                control = app.Arbiter.Reason,
                raw_throttle = app.Input.Gas.ReadValue<float>(),
                raw_brake = app.Input.Brake.ReadValue<float>(),
                comma_requested = app.Arbiter.Requested,
                comma_active = app.CommandFresh && (app.Command?.active ?? false),
                comma_healthy = app.CommandFresh && (app.Command?.healthy ?? false),
                comma_engageable = app.CommandFresh && (app.Command?.engageable ?? false),
                comma_command_age =
                    app.Command == null ? -1 : Time.realtimeSinceStartupAsDouble - app.CommandAt,
                observer_fresh = app.ObserverFresh,
                observer_age =
                    app.Observation == null
                        ? -1
                        : Time.realtimeSinceStartupAsDouble - app.Observation.timestamp,
                observer_generation = app.ObserverGeneration,
                observer_transport = app.ObserverTransportStatus,
                observer_received_frames = app.Observation?.received_frames ?? 0,
                observer_processed_frames = app.Observation?.processed_frames ?? 0,
                camera_reordered_drops = app.Sensors.ReorderedDropped,
                map = app.Town.Map.id,
                comma_acceleration = app.Command?.acceleration_mps2 ?? 0,
                comma_status = app.CommandFresh
                    ? app.Command.status
                    : "Stale/unavailable: " + app.CommaStatus,
                automation_accepted = app.AutomationActive,
                model_frame_age =
                    app.CommaUi?.road == null
                        ? -1
                        : Time.realtimeSinceStartupAsDouble - app.CommaUi.road.timestamp,
                model_frame_cached =
                    app.CommaUi?.road != null
                    && app.Sensors.FrameTexture(app.CommaUi.road.frame_id),
            };
            foreach (var display in displays)
                if (display && display.isActiveAndEnabled)
                {
                    row.matching_model_geometry |= display.HasMatchingGeometry;
                    row.model_geometry_vertices = Mathf.Max(
                        row.model_geometry_vertices,
                        display.GeometryVertexCount
                    );
                }
            try
            {
                if (writer != null)
                {
                    writer.WriteLine(JsonUtility.ToJson(row));
                    writer.Flush();
                    if (writer.BaseStream.Length > 16 * 1024 * 1024)
                    {
                        writer.Dispose();
                        writer = null;
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning("Diagnostics stopped: " + e.Message);
                writer?.Dispose();
                writer = null;
            }
        }

        void OnDestroy()
        {
            writer?.Dispose();
        }

        [Serializable]
        class Sample
        {
            public bool controller_available,
                focused;
            public string input_update_mode,
                map,
                page;
            public int view_mode,
                reverse_presses,
                pause_presses,
                camera_presses,
                map_presses,
                comma_presses;
            public double timestamp;
            public string controller,
                contact,
                control;
            public Vector3 position,
                rotation;
            public float speed_mps,
                throttle,
                brake,
                steer,
                raw_throttle,
                raw_brake;
            public bool paused,
                handbrake,
                reverse;
            public int grounded_wheels;
            public float[] wheel_rpm;
            public bool comma_requested,
                comma_active,
                comma_healthy,
                comma_engageable;
            public float comma_acceleration;
            public string comma_status;
            public bool automation_accepted,
                matching_model_geometry,
                model_frame_cached;
            public int model_geometry_vertices;
            public double model_frame_age;
            public double comma_command_age,
                observer_age;
            public bool observer_fresh;
            public int observer_generation,
                observer_received_frames,
                observer_processed_frames,
                camera_reordered_drops;
            public string observer_transport;
        }
    }
}
