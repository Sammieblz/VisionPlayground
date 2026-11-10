using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoDriver.Editor
{
    // Actual world/cabin rendering, without starting external services or claiming a drive test.
    public static class RealismCaptures
    {
        public static void CaptureConstruction()
        {
            ConstructionArtImporter.Prepare();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            Art.Initialize();
            GraphicsSettings.useScriptableRenderPipelineBatching = false;
            Realism.Lighting();
            var sun = new GameObject("Construction inspection daylight").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.35f;
            sun.transform.rotation = Quaternion.Euler(42, -35, 0);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.6f, .72f, .85f);
            RenderSettings.ambientEquatorColor = new Color(.46f, .5f, .54f);
            RenderSettings.ambientGroundColor = new Color(.25f, .28f, .2f);
            var root = Object.Instantiate(
                Resources.Load<GameObject>("WorldArt/Construction/Excavator")
            );
            var visual = root.GetComponent<ExcavatorVisual>();
            root.GetComponent<LODGroup>().ForceLOD(0);
            Art.Box(
                "Construction pad",
                null,
                Vector3.down * .5f,
                new Vector3(80, 1, 80),
                Art.Concrete,
                false
            );
            var camera = new GameObject("Construction inspection camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = 48;
            camera.transform.position = new Vector3(-4.5f, 3.2f, 5.5f);
            camera.transform.LookAt(new Vector3(0, 1.1f, .7f));
            var target = new RenderTexture(1280, 720, 24);
            camera.targetTexture = target;
            var folder = Path.GetFullPath("../outputs/qualification/realism/visual");
            Directory.CreateDirectory(folder);
            foreach (int time in new[] { 0, 5, 9, 12, 14, 16 })
            {
                visual.Pose(time);
                Save(camera, target, Path.Combine(folder, $"construction-{time:00}-720.png"));
            }
            target.Release();
            Object.DestroyImmediate(target);
            Debug.Log("CONSTRUCTION_CAPTURES_READY");
        }

        public static void CapturePublicCockpit()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            Art.Initialize();
            GraphicsSettings.useScriptableRenderPipelineBatching = false;
            var map = MapDefinition.Load("davis-downtown");
            var vehicle = new GameObject("Public cockpit inspection").AddComponent<Vehicle>();
            vehicle.Profile = new VehicleProfile();
            vehicle.transform.SetPositionAndRotation(map.Spawn, map.SpawnRotation);
            vehicle.Initialize();
            var town = new GameObject("Davis cockpit backdrop").AddComponent<Town>();
            town.Map = map;
            town.Build(vehicle.transform);
            vehicle.GetComponentInChildren<CockpitInstruments>().SendMessage("LateUpdate");
            var camera = new GameObject("Driver eye inspection").AddComponent<Camera>();
            camera.nearClipPlane = .04f;
            camera.farClipPlane = 900;
            camera.fieldOfView = 70;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.cullingMask = ~((1 << 8) | (1 << 16));
            camera.transform.SetPositionAndRotation(
                vehicle.transform.TransformPoint(vehicle.Profile.driverEye),
                vehicle.transform.rotation * Quaternion.Euler(6, 0, 0)
            );
            var folder = Path.GetFullPath("../outputs/qualification/realism/visual");
            Directory.CreateDirectory(folder);
            foreach (int height in new[] { 720, 1080 })
            {
                var target = new RenderTexture(height * 16 / 9, height, 24);
                camera.targetTexture = target;
                Save(camera, target, Path.Combine(folder, $"public-instruments-{height}.png"));
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        public static void CaptureVegetation()
        {
            RealismImporter.PrepareFoliageTextures();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            Art.Initialize();
            GraphicsSettings.useScriptableRenderPipelineBatching = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.6f, .72f, .85f);
            RenderSettings.ambientEquatorColor = new Color(.46f, .5f, .54f);
            RenderSettings.ambientGroundColor = new Color(.25f, .28f, .2f);
            Realism.Lighting();
            var sun = new GameObject("Daylight").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.35f;
            sun.transform.rotation = Quaternion.Euler(42, -35, 0);
            var root = new GameObject("Imported vegetation comparison").transform;
            Realism.Spawn("Pine", root, Vector3.left * 6);
            Realism.Spawn("Oak", root, Vector3.right * 6);
            Art.Box("Ground", root, Vector3.down * .5f, new Vector3(150, 1, 150), Art.Grass, false);
            var camera = new GameObject("Vegetation inspection camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = 60;
            var target = new RenderTexture(1280, 720, 24);
            camera.targetTexture = target;
            var folder = Path.GetFullPath("../outputs/qualification/realism/visual");
            Directory.CreateDirectory(folder);
            foreach (int distance in new[] { 24, 65 })
            {
                camera.transform.position = new Vector3(0, 4, -distance);
                camera.transform.LookAt(Vector3.up * 3.5f);
                Save(camera, target, Path.Combine(folder, $"vegetation-{distance}m-720.png"));
            }
            target.Release();
            Object.DestroyImmediate(target);
        }

        public static void InspectFootways()
        {
            var map = MapDefinition.Load("davis-downtown");
            foreach (var building in map.buildings)
            {
                var entrance = MappedStorefront.Find(map, building);
                if (entrance != null)
                    Debug.Log(
                        "FOOTWAY "
                            + building.sourceId
                            + " centre="
                            + entrance.Centre.ToString("F2")
                            + " outward="
                            + entrance.Outward.ToString("F2")
                            + " sidewalk="
                            + entrance.SidewalkPoint.ToString("F2")
                    );
            }
        }

        public static void InspectBridge()
        {
            var source = Resources.Load<GameObject>("Realism/Prepared/Bridge");
            var filter = source.GetComponentInChildren<MeshFilter>();
            var mesh = filter.sharedMesh;
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            var bins = new System.Collections.Generic.Dictionary<int, float>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]];
                var b = vertices[triangles[i + 1]];
                var c = vertices[triangles[i + 2]];
                var centre = (a + b + c) / 3;
                if (centre.y < .5f || centre.y > 3 || Mathf.Abs(centre.z) > 10)
                    continue;
                int x = Mathf.RoundToInt(centre.x * 10);
                bins[x] =
                    (bins.TryGetValue(x, out float area) ? area : 0)
                    + Vector3.Cross(b - a, c - a).magnitude * .5f;
            }
            Debug.Log(
                "BRIDGE_UPRIGHTS "
                    + string.Join(
                        ";",
                        bins.OrderByDescending(p => p.Value)
                            .Take(12)
                            .Select(p => p.Key * .1f + ":" + p.Value)
                    )
            );
        }

        public static void Capture()
        {
            ActorImporter.Prepare();
            WorldArtImporter.Prepare();
            RealismImporter.Prepare();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            Art.Initialize();
            GraphicsSettings.useScriptableRenderPipelineBatching = false;
            var map = MapDefinition.Load("davis-downtown");
            var vehicle = new GameObject("GLS visual inspection").AddComponent<Vehicle>();
            vehicle.Profile = VehicleProfile.GlsSimulation();
            vehicle.transform.SetPositionAndRotation(map.Spawn, map.SpawnRotation);
            vehicle.Initialize();
            var town = new GameObject("Davis inspection").AddComponent<Town>();
            town.Map = map;
            town.Build(vehicle.transform);
            var ambient = new Color[3];
            RenderSettings.ambientProbe.Evaluate(
                new[] { Vector3.up, Vector3.forward, Vector3.right },
                ambient
            );
            Debug.Log(
                "DAYLIGHT_AMBIENT up="
                    + ambient[0]
                    + " forward="
                    + ambient[1]
                    + " right="
                    + ambient[2]
            );
            var camera = new GameObject("Inspection camera").AddComponent<Camera>();
            camera.nearClipPlane = .04f;
            camera.farClipPlane = 900;
            camera.fieldOfView = 70;
            camera.clearFlags = CameraClearFlags.Skybox;
            var folder = Path.GetFullPath("../outputs/qualification/realism/visual");
            Directory.CreateDirectory(folder);
            foreach (int height in new[] { 720, 1080 })
            {
                var target = new RenderTexture(height * 16 / 9, height, 24);
                camera.targetTexture = target;
                camera.cullingMask = ~((1 << 8) | (1 << 16));
                camera.transform.SetPositionAndRotation(
                    vehicle.transform.TransformPoint(vehicle.Profile.driverEye),
                    vehicle.transform.rotation * Quaternion.Euler(6, 0, 0)
                );
                Save(camera, target, Path.Combine(folder, $"cockpit-{height}.png"));
                camera.cullingMask = ~((1 << 9) | (1 << 16));
                camera.transform.position = vehicle.transform.TransformPoint(
                    new Vector3(-5, 2.6f, 6)
                );
                camera.transform.LookAt(vehicle.transform.TransformPoint(Vector3.up));
                Save(camera, target, Path.Combine(folder, $"exterior-{height}.png"));
                var entry = map
                    .buildings.Select(b => MappedStorefront.Find(map, b))
                    .First(e => e != null);
                camera.transform.position = entry.Centre + entry.Outward * 7 + Vector3.up * 2;
                camera.transform.LookAt(entry.Centre + Vector3.up * 1.8f);
                Save(camera, target, Path.Combine(folder, $"storefront-{height}.png"));
                var lowrise = town.GetComponentsInChildren<AssetDefinition>()
                    .FirstOrDefault(a => a.assetId == "shalmon-prop-lowrise-retextured");
                if (lowrise)
                {
                    camera.transform.position =
                        lowrise.transform.position
                        + lowrise.transform.rotation * new Vector3(-17, 8, -19);
                    camera.transform.LookAt(lowrise.transform.position + Vector3.up * 2.8f);
                    Save(camera, target, Path.Combine(folder, $"lowrise-{height}.png"));
                }
                var bridge = map.LaneById[SimulatedBridgeExtension.LaneId].Path;
                camera.transform.position =
                    bridge.Position(75) + Vector3.up * 17 + Vector3.right * 25;
                camera.transform.LookAt(bridge.Position(75));
                Save(camera, target, Path.Combine(folder, $"bridge-{height}.png"));
                var station = map.landmarks.First(l => l.subtype == "station").position;
                var railPoint = map
                    .rail.Select(r => new TrackPath(r.points, false))
                    .Select(p => p.Position(p.NearestDistance(station)))
                    .Where(p => map.Contains(p, 40))
                    .OrderBy(p => (p - station).sqrMagnitude)
                    .First();
                camera.transform.position = railPoint + new Vector3(6, 2.2f, 8);
                camera.transform.LookAt(railPoint + Vector3.up * .3f);
                Save(camera, target, Path.Combine(folder, $"railway-{height}.png"));
                foreach (
                    string resource in new[] { "WorldArt/Fleet/Concept", "WorldArt/Animals/Cat" }
                )
                {
                    var sample = Object.Instantiate(Resources.Load<GameObject>(resource));
                    sample.transform.position = vehicle.transform.TransformPoint(
                        new Vector3(0, 0, 7)
                    );
                    sample.transform.position = new Vector3(
                        sample.transform.position.x,
                        0,
                        sample.transform.position.z
                    );
                    sample.transform.rotation = vehicle.transform.rotation;
                    var animator = sample.GetComponentInChildren<Animator>();
                    bool cat = animator;
                    // Manual editor renders do not advance the normal LOD update.
                    foreach (var lod in sample.GetComponentsInChildren<LODGroup>())
                        lod.ForceLOD(0);
                    if (animator)
                    {
                        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                        animator.Rebind();
                        var idle = animator.runtimeAnimatorController.animationClips.First(c =>
                            c.name.EndsWith("Idle_Breathe")
                        );
                        animator.Play(idle.name, 0, .2f);
                        animator.Update(.02f);
                    }
                    camera.transform.position = sample.transform.TransformPoint(
                        cat ? new Vector3(.8f, .65f, 1.1f) : new Vector3(-4, 1.9f, 5)
                    );
                    camera.transform.LookAt(
                        sample.transform.position + Vector3.up * (cat ? .25f : .65f)
                    );
                    Save(
                        camera,
                        target,
                        Path.Combine(folder, $"{(cat ? "cat" : "fleet")}-{height}.png")
                    );
                    Object.DestroyImmediate(sample);
                }
                foreach (string name in new[] { "Civilian", "Citizen_A", "Citizen_B" })
                {
                    var civilian = Resources.Load<GameObject>("WorldArt/Civilians/" + name);
                    if (civilian)
                    {
                        var actor = Object.Instantiate(civilian);
                        actor.transform.position = vehicle.transform.TransformPoint(
                            new Vector3(0, 0, 6)
                        );
                        actor.transform.position = new Vector3(
                            actor.transform.position.x,
                            0,
                            actor.transform.position.z
                        );
                        var animator = actor.GetComponent<Animator>();
                        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                        animator.Rebind();
                        var walking = animator.runtimeAnimatorController.animationClips.First(c =>
                            c.name.EndsWith("Walk_Loop")
                        );
                        animator.Play(walking.name, 0, .25f);
                        animator.Update(.2f);
                        var bounds = actor.GetComponentsInChildren<Renderer>()[0].bounds;
                        Debug.Log("CIVILIAN_CAPTURE_BOUNDS " + bounds);
                        camera.transform.position =
                            actor.transform.position + new Vector3(2, 1.6f, 3.5f);
                        camera.transform.LookAt(actor.transform.position + Vector3.up);
                        Save(
                            camera,
                            target,
                            Path.Combine(folder, $"{name.ToLowerInvariant()}-{height}.png")
                        );
                        Object.DestroyImmediate(actor);
                    }
                }
                target.Release();
                Object.DestroyImmediate(target);
            }
            Debug.Log("REALISM_CAPTURES_READY " + folder);
        }

        static void Save(Camera camera, RenderTexture target, string path)
        {
            Canvas.ForceUpdateCanvases();
            camera.Render();
            camera.Render();
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            RenderTexture.active = null;
            Object.DestroyImmediate(image);
        }
    }
}
