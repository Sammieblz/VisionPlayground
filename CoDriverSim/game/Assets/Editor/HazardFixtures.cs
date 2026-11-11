using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CoDriver.Editor
{
    // Scripted camera sequences for evaluator-only warning labels. These are
    // small rendered probes, not a substitute for natural driving/real footage.
    public static class HazardFixtures
    {
        [Serializable]
        class Annotation
        {
            public string object_id,
                label;
            public float[] box;
        }

        [Serializable]
        class Frame
        {
            public string session_id,
                image;
            public int frame_id;
            public float timestamp,
                speed_mps;
            public Annotation[] objects;
        }

        [Serializable]
        class Hazard
        {
            public string object_id,
                hazard_id,
                session_id,
                kind;
            public string[] acceptable_kinds;
            public float observable_from,
                deadline,
                end;
        }

        [Serializable]
        class Manifest
        {
            public int schema_version = 2;
            public bool annotations_complete = true,
                hazards_complete = true;
            public string source = "Predeclared Unity camera warning probes",
                split =
                    "Four scripted sequences, 26 frames each at 5 Hz; no inference-driven selection";
            public List<Frame> frames = new List<Frame>();
            public List<Hazard> hazards = new List<Hazard>();
        }

        public static void Capture()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Art.Initialize();
            var ground = new GameObject("Warning probe scene").transform;
            Art.Box(
                "Verge",
                ground,
                new Vector3(0, -.2f, 25),
                new Vector3(100, .2f, 150),
                Art.Grass
            );
            Art.Box(
                "Road",
                ground,
                new Vector3(0, -.1f, 25),
                new Vector3(9, .2f, 150),
                Art.Asphalt
            );
            for (int i = 0; i < 15; i++)
                Art.Box(
                    "Centre marking",
                    ground,
                    new Vector3(-2, .01f, i * 6),
                    new Vector3(.15f, .01f, 3),
                    Art.Yellow,
                    false
                );
            var light = new GameObject("Daylight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            light.transform.rotation = Quaternion.Euler(38, -32, 0);
            RenderSettings.ambientLight = new Color(.65f, .68f, .72f);
            var camera = new GameObject("Independent fixture camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 1.3f, 0);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.63f, .76f, .85f);
            camera.fieldOfView = 2 * Mathf.Atan(604f / 910) * Mathf.Rad2Deg;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 180;
            var target = new RenderTexture(964, 604, 24);
            target.Create();
            camera.targetTexture = target;
            var manifest = new Manifest();
            string output = Path.GetFullPath("../outputs/qualification/rendered-hazards");
            Directory.CreateDirectory(output);
            try
            {
                foreach (string sequence in new[] { "lead", "person", "dog", "quiet" })
                {
                    GameObject actor = null;
                    if (sequence == "lead")
                    {
                        actor = new GameObject("Visible lead vehicle");
                        Art.Car(actor.transform, Art.Paint, true);
                    }
                    else if (sequence != "quiet")
                    {
                        string resource =
                            sequence == "person" ? "Actors/PersonFemale" : "Scenery/Prepared/Dog";
                        var prefab = Resources.Load<GameObject>(resource);
                        if (!prefab)
                            throw new InvalidOperationException(
                                "Missing fixture actor: " + resource
                            );
                        actor = UnityEngine.Object.Instantiate(prefab);
                        actor.transform.rotation = Quaternion.Euler(0, 90, 0);
                    }
                    string session = "warning-probe-" + sequence;
                    // Deadlines are script checkpoints set before inference, not
                    // claims about safe real-world reaction time.
                    if (sequence != "quiet")
                        manifest.hazards.Add(
                            new Hazard
                            {
                                hazard_id = sequence,
                                object_id = sequence,
                                session_id = session,
                                kind = sequence == "lead" ? "closing" : "crossing",
                                observable_from = 0,
                                acceptable_kinds =
                                    sequence == "lead"
                                        ? new[] { "closing", "following" }
                                        : new[] { "crossing", "closing", "following" },
                                deadline = sequence == "lead" ? 4 : 2.5f,
                                end = 5,
                            }
                        );
                    for (int index = 0; index <= 25; index++)
                    {
                        float time = index * .2f;
                        if (actor)
                        {
                            actor.transform.position =
                                sequence == "lead" ? new Vector3(0, 0, 40 - 6 * time)
                                : sequence == "person"
                                    ? new Vector3(-3 + 1.2f * time, 0, 24 - 4 * time)
                                : new Vector3(-2.5f + time, 0, 20 - 3 * time);
                            var animator = actor.GetComponentInChildren<Animator>();
                            if (animator && animator.runtimeAnimatorController)
                            {
                                var clip =
                                    animator.runtimeAnimatorController.animationClips.FirstOrDefault(
                                        c => c.name.EndsWith("Walk_Loop")
                                    );
                                if (clip)
                                {
                                    animator.Play(clip.name, 0, time % 1);
                                    animator.Update(0);
                                }
                            }
                        }
                        var frame = new Frame
                        {
                            session_id = session,
                            frame_id = index,
                            timestamp = time,
                            speed_mps =
                                sequence == "lead" ? 8
                                : sequence == "person" ? 4
                                : sequence == "dog" ? 3
                                : 8,
                            image = sequence + "-" + index.ToString("D3") + ".jpg",
                            objects = Array.Empty<Annotation>(),
                        };
                        for (int warmup = 0; warmup < 4; warmup++)
                            camera.Render();
                        if (actor)
                        {
                            var renderers = actor.GetComponentsInChildren<Renderer>();
                            var bounds = renderers[0].bounds;
                            foreach (var renderer in renderers.Skip(1))
                                bounds.Encapsulate(renderer.bounds);
                            frame.objects = new[]
                            {
                                new Annotation
                                {
                                    object_id = sequence,
                                    label = sequence == "lead" ? "car" : sequence,
                                    box = Project(camera, bounds),
                                },
                            };
                        }
                        var previous = RenderTexture.active;
                        RenderTexture.active = target;
                        var pixels = new Texture2D(964, 604, TextureFormat.RGB24, false);
                        pixels.ReadPixels(new Rect(0, 0, 964, 604), 0, 0);
                        pixels.Apply();
                        File.WriteAllBytes(
                            Path.Combine(output, frame.image),
                            pixels.EncodeToJPG(95)
                        );
                        UnityEngine.Object.DestroyImmediate(pixels);
                        RenderTexture.active = previous;
                        manifest.frames.Add(frame);
                    }
                    if (actor)
                        UnityEngine.Object.DestroyImmediate(actor);
                }
                File.WriteAllText(
                    Path.Combine(output, "labels.json"),
                    JsonUtility.ToJson(manifest, true)
                );
                Debug.Log("CODRIVER_HAZARD_FIXTURES " + manifest.frames.Count);
            }
            finally
            {
                camera.targetTexture = null;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        static float[] Project(Camera camera, Bounds bounds)
        {
            Vector2 min = new Vector2(964, 604),
                max = Vector2.zero;
            for (int i = 0; i < 8; i++)
            {
                var corner =
                    bounds.center
                    + Vector3.Scale(
                        bounds.extents,
                        new Vector3(
                            (i & 1) == 0 ? -1 : 1,
                            (i & 2) == 0 ? -1 : 1,
                            (i & 4) == 0 ? -1 : 1
                        )
                    );
                var screen = camera.WorldToScreenPoint(corner);
                var point = new Vector2(
                    Mathf.Clamp(screen.x, 0, 964),
                    Mathf.Clamp(604 - screen.y, 0, 604)
                );
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            return new[] { min.x, min.y, max.x, max.y };
        }
    }
}
