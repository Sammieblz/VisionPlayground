using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace CoDriver.Editor
{
    public static class PresentationCaptures
    {
        public static void Capture()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            string[] args = System.Environment.GetCommandLineArgs();
            int sourceFlag = System.Array.IndexOf(args, "-ui-background");
            if (sourceFlag < 0 || sourceFlag + 1 >= args.Length)
                throw new System.ArgumentException(
                    "Supply -ui-background with a player-captured forward sensor PNG"
                );
            string sourcePath = args[sourceFlag + 1];
            var source = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (
                !source.LoadImage(File.ReadAllBytes(sourcePath))
                || source.width != 964
                || source.height != 604
            )
                throw new System.ArgumentException(
                    "UI background must be a 964x604 forward camera frame"
                );
            var displayCamera = new GameObject("UI fixture renderer").AddComponent<Camera>();
            displayCamera.transform.position = Vector3.down * 5000;
            displayCamera.orthographic = true;
            displayCamera.orthographicSize = 540;
            displayCamera.cullingMask = 1 << 16;
            displayCamera.clearFlags = CameraClearFlags.SolidColor;
            var go = new GameObject("UI fixture", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = displayCamera;
            canvas.planeDistance = 10;
            var scale = go.AddComponent<CanvasScaler>();
            scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scale.referenceResolution = new Vector2(1920, 1080);
            scale.matchWidthOrHeight = .5f;
            var display = go.AddComponent<OpenpilotDisplay>();
            display.Initialize(null);
            var label = UiDrawing.Label(
                "UI LAYOUT FIXTURE",
                go.transform,
                20,
                new Vector2(.5f, 1),
                new Vector2(0, -18),
                new Vector2(600, 35),
                TextAnchor.MiddleCenter
            );
            foreach (var child in go.GetComponentsInChildren<Transform>())
                child.gameObject.layer = 16;
            var folder = Path.GetFullPath("../outputs/qualification/overhaul/ui");
            Directory.CreateDirectory(folder);
            foreach (int height in new[] { 720, 1080 })
            {
                int width = height * 16 / 9;
                var target = new RenderTexture(width, height, 24);
                target.Create();
                displayCamera.targetTexture = target;
                foreach (
                    string state in new[]
                    {
                        "manual",
                        "ready",
                        "active",
                        "takeover",
                        "takeover-full",
                        "stale-camera",
                        "service-failure",
                    }
                )
                {
                    var snapshot = new OpenpilotUiSnapshot
                    {
                        schema_version = 1,
                        session_id = "layout-fixture",
                        timestamp = 100,
                        engageable = state != "manual",
                        enabled = state == "active" || state == "takeover",
                        active = state == "active",
                        healthy = true,
                        cruise_speed_kph = 40.2336f,
                        calibration = new Calibration(),
                        alert_size = "none",
                        road = new UiModelGeometry
                        {
                            schema_version = 2,
                            session_id = "layout-fixture",
                            frame_id = 42,
                            timestamp = 100,
                            model = "synthetic-layout-fixture",
                            path = new ModelLine
                            {
                                x = new[] { 5f, 10, 20, 40, 70 },
                                y = new float[5],
                                z = new float[5],
                            },
                        },
                    };
                    if (state == "takeover")
                    {
                        snapshot.alert_size = "mid";
                        snapshot.alert_status = "userPrompt";
                        snapshot.alert_text_1 = "Take Control";
                        snapshot.alert_text_2 = "Turn Exceeds Steering Limit";
                    }
                    if (state == "takeover-full")
                    {
                        snapshot.alert_size = "full";
                        snapshot.alert_status = "critical";
                        snapshot.alert_text_1 = "Take Control Immediately";
                        snapshot.alert_text_2 = "System Unavailable";
                    }
                    if (state == "stale-camera")
                        snapshot.road.timestamp = 99;
                    if (state == "service-failure")
                        snapshot = null;
                    label.text = "UI LAYOUT FIXTURE · " + state;
                    Canvas.ForceUpdateCanvases();
                    display.Present(
                        snapshot,
                        "layout-fixture",
                        100.1,
                        11.176f,
                        state == "active",
                        false,
                        false,
                        source,
                        42,
                        id => source,
                        state == "service-failure"
                            ? "Camera observations unavailable"
                            : "Watching the forward camera",
                        state == "service-failure" ? "unknown" : "calm"
                    );
                    Canvas.ForceUpdateCanvases();
                    displayCamera.Render();
                    RenderTexture.active = target;
                    var picture = new Texture2D(width, height, TextureFormat.RGB24, false);
                    picture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    picture.Apply();
                    File.WriteAllBytes(
                        Path.Combine(folder, $"fixture-{state}-{width}x{height}.png"),
                        picture.EncodeToPNG()
                    );
                    Object.DestroyImmediate(picture);
                    var graphic = go.GetComponentInChildren<ModelOverlayGraphic>();
                    var geometry = graphic.Geometry;
                    var mesh = graphic.canvasRenderer.GetMesh();
                    Debug.Log(
                        $"CODRIVER_UI_GEOMETRY {state} vertices={mesh.vertexCount} "
                            + $"rect={graphic.rectTransform.rect} crop={graphic.Crop} "
                            + $"fresh={geometry != null} culled={graphic.canvasRenderer.cull} "
                            + $"bounds={mesh.bounds} layer={graphic.gameObject.layer}"
                    );
                    if (state == "active" && mesh.vertexCount < 12)
                        throw new System.Exception("Fresh model path has no renderable geometry");
                    if (
                        (state == "stale-camera" || state == "service-failure")
                        && (geometry != null || mesh.vertexCount != 0)
                    )
                        throw new System.Exception("Stale geometry displayed");
                }
                RenderTexture.active = null;
                target.Release();
                Object.DestroyImmediate(target);
            }
            File.WriteAllText(
                Path.Combine(folder, "provenance.json"),
                JsonUtility.ToJson(
                    new CaptureSource { camera = Path.GetFileName(sourcePath) },
                    true
                )
            );
            Object.DestroyImmediate(source);
            Debug.Log("CODRIVER_UI_CAPTURES_OK " + folder);
        }

        [System.Serializable]
        sealed class CaptureSource
        {
            public string type =
                "synthetic UI layout fixtures over a player-captured independent forward camera frame";
            public string reference = "openpilot v0.9.7 50dd024 annotated_camera.cc / alerts.cc";
            public string camera;
            public bool controller_execution = false;
            public string[] resolutions = { "1280x720", "1920x1080" };
        }
    }
}
