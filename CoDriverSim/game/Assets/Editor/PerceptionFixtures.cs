using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CoDriver.Editor
{
    // Offline evaluator fixtures. Labels never enter CameraFrame or the observer.
    // All cases are defined before inference, including off/unknown lights.
    public static class PerceptionFixtures
    {
        [Serializable]
        class Item
        {
            public string image,
                label,
                signal_state;
            public int speed_limit_mph;
            public float[] box;
        }

        [Serializable]
        class Manifest
        {
            public int schema_version = 1;
            public string source =
                "Unity rendered sign smoke cases; no accuracy claim beyond these fixtures";
            public List<Item> cases = new List<Item>();
        }

        static bool heldOut;

        public static void Capture() => CaptureSet(false);

        public static void CaptureHoldout() => CaptureSet(true);

        static void CaptureSet(bool holdout)
        {
            heldOut = holdout;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Art.Initialize();
            var scene = new GameObject("Held-out sign scene").transform;
            var sun = new GameObject("Fixture daylight").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(45, -25, 0);
            sun.intensity = heldOut ? 1.4f : 1.8f;
            RenderSettings.ambientLight = new Color(.65f, .68f, .72f);
            Art.Box(
                "Grass",
                scene,
                new Vector3(0, -.25f, 25),
                new Vector3(100, .2f, 180),
                Art.Grass
            );
            Art.Box("Road", scene, new Vector3(0, -.1f, 25), new Vector3(9, .2f, 160), Art.Asphalt);
            for (int i = 0; i < 18; i++)
                Art.Box(
                    "Lane marking",
                    scene,
                    new Vector3(0, .01f, -30 + i * 6),
                    new Vector3(.15f, .01f, 3),
                    Art.Yellow,
                    false
                );
            Art.Box(
                "Distant shop",
                scene,
                new Vector3(-15, 3, 38),
                new Vector3(12, 6, 15),
                Art.Brick
            );
            var camera = new GameObject("Fixture camera").AddComponent<Camera>();
            camera.backgroundColor = new Color(.63f, .76f, .85f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.fieldOfView = 2 * Mathf.Atan(604f / 910) * Mathf.Rad2Deg;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 200;
            var target = new RenderTexture(964, 604, 24);
            target.Create();
            camera.targetTexture = target;
            string output = Path.GetFullPath(
                "../outputs/qualification/rendered-signs" + (heldOut ? "-holdout" : "")
            );
            Directory.CreateDirectory(output);
            var manifest = new Manifest();
            try
            {
                foreach (
                    float distance in heldOut ? new[] { 8f, 16f, 28f } : new[] { 12f, 22f, 32f }
                )
                foreach (string state in new[] { "red", "yellow", "green", "unknown" })
                {
                    var fixture = new GameObject("Light fixture").transform;
                    fixture.position = new Vector3(3.5f, 0, distance);
                    Art.Box(
                        "Pole",
                        fixture,
                        new Vector3(0, 1.5f, 0),
                        new Vector3(.12f, 3, .12f),
                        Art.Metal,
                        false
                    );
                    var housing = Art.Box(
                        "Housing",
                        fixture,
                        new Vector3(0, 3.3f, 0),
                        new Vector3(.5f, 1.5f, .3f),
                        Art.Rubber,
                        false
                    );
                    string[] names = { "red", "yellow", "green" };
                    Color[] colors = { Color.red, Color.yellow, Color.green };
                    for (int i = 0; i < 3; i++)
                        Art.Shape(
                            "Lens",
                            fixture,
                            new Vector3(0, 3.75f - i * .45f, -.18f),
                            new Vector3(.32f, .32f, .12f),
                            Art.Material(
                                "Fixture " + names[i] + (state == names[i] ? " lit" : " off"),
                                state == names[i] ? colors[i] : new Color(.025f, .025f, .025f),
                                state == names[i]
                            ),
                            PrimitiveType.Sphere
                        );
                    Save(
                        camera,
                        target,
                        housing.GetComponent<Renderer>().bounds,
                        output,
                        manifest,
                        new Item { label = "traffic light", signal_state = state }
                    );
                    UnityEngine.Object.DestroyImmediate(fixture.gameObject);
                }
                foreach (float distance in heldOut ? new[] { 8f, 16f } : new[] { 12f, 22f })
                foreach (int speed in heldOut ? new[] { 20, 35, 50 } : new[] { 25, 30, 45 })
                {
                    var fixture = new GameObject("Speed sign fixture").transform;
                    fixture.position = new Vector3(3.5f, 0, distance);
                    Art.Box(
                        "Pole",
                        fixture,
                        new Vector3(0, 1, 0),
                        new Vector3(.08f, 2, .08f),
                        Art.Metal,
                        false
                    );
                    var face = Art.Box(
                        "Sign face",
                        fixture,
                        new Vector3(0, 2.25f, 0),
                        new Vector3(.9f, 1.3f, .06f),
                        Art.White,
                        false
                    );
                    Art.Text(
                        "SPEED",
                        fixture,
                        new Vector3(0, 2.66f, -.04f),
                        .05f,
                        Color.black,
                        Quaternion.identity
                    );
                    Art.Text(
                        "LIMIT",
                        fixture,
                        new Vector3(0, 2.39f, -.04f),
                        .05f,
                        Color.black,
                        Quaternion.identity
                    );
                    Art.Text(
                        speed.ToString(),
                        fixture,
                        new Vector3(0, 1.94f, -.04f),
                        .14f,
                        Color.black,
                        Quaternion.identity
                    );
                    Save(
                        camera,
                        target,
                        face.GetComponent<Renderer>().bounds,
                        output,
                        manifest,
                        new Item { label = "speed limit sign", speed_limit_mph = speed }
                    );
                    UnityEngine.Object.DestroyImmediate(fixture.gameObject);
                }
                foreach (float distance in heldOut ? new[] { 8f, 16f } : new[] { 12f, 22f })
                {
                    var fixture = new GameObject("Stop sign fixture").transform;
                    fixture.position = new Vector3(3.5f, 0, distance);
                    Art.Box(
                        "Pole",
                        fixture,
                        new Vector3(0, 1.1f, 0),
                        new Vector3(.08f, 2.2f, .08f),
                        Art.Metal,
                        false
                    );
                    Art.Octagon(fixture, new Vector3(0, 2.2f, 0), .5f, Art.White);
                    Art.Octagon(
                        fixture,
                        new Vector3(0, 2.2f, -.01f),
                        .45f,
                        Art.Material("Fixture stop red", Color.red)
                    );
                    Art.Text(
                        "STOP",
                        fixture,
                        new Vector3(0, 2.2f, -.03f),
                        .07f,
                        Color.white,
                        Quaternion.identity
                    );
                    Save(
                        camera,
                        target,
                        new Bounds(fixture.position + Vector3.up * 2.2f, new Vector3(1, 1, .03f)),
                        output,
                        manifest,
                        new Item { label = "stop sign" }
                    );
                    UnityEngine.Object.DestroyImmediate(fixture.gameObject);
                }
                File.WriteAllText(
                    Path.Combine(output, "fixtures.json"),
                    JsonUtility.ToJson(manifest, true)
                );
                Debug.Log("CODRIVER_FIXTURES " + manifest.cases.Count);
            }
            finally
            {
                camera.targetTexture = null;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        static void Save(
            Camera camera,
            RenderTexture target,
            Bounds bounds,
            string output,
            Manifest manifest,
            Item item
        )
        {
            camera.transform.SetPositionAndRotation(
                new Vector3(heldOut ? .5f : -.7f, 1.3f, 0),
                Quaternion.Euler(0, heldOut ? -4 : 0, 0)
            );
            // Complete initial material/texture uploads before taking the evaluated view.
            for (int warmup = 0; warmup < 4; warmup++)
                camera.Render();
            var old = RenderTexture.active;
            RenderTexture.active = target;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            pixels.Apply();
            item.image = "case-" + manifest.cases.Count.ToString("D2") + ".jpg";
            File.WriteAllBytes(Path.Combine(output, item.image), pixels.EncodeToJPG(95));
            UnityEngine.Object.DestroyImmediate(pixels);
            RenderTexture.active = old;
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < 8; i++)
            {
                var p =
                    bounds.center
                    + Vector3.Scale(
                        bounds.extents,
                        new Vector3(
                            (i & 1) == 0 ? -1 : 1,
                            (i & 2) == 0 ? -1 : 1,
                            (i & 4) == 0 ? -1 : 1
                        )
                    );
                var screen = camera.WorldToScreenPoint(p);
                var point = new Vector2(screen.x, target.height - screen.y);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            item.box = new[] { min.x, min.y, max.x, max.y };
            manifest.cases.Add(item);
        }
    }
}
