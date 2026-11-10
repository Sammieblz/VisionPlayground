using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoDriver.Editor
{
    public sealed class RealismImporter : AssetPostprocessor
    {
        public const string Root = "Assets/External/Resources/Realism/";

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Root))
                return;
            var importer = (ModelImporter)assetImporter;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = true;
            importer.importAnimation = assetPath.Contains("person_walking");
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        }

        void OnPreprocessTexture()
        {
            if (
                assetPath.StartsWith("Assets/Resources/Maps/") && assetPath.EndsWith("-minimap.png")
            )
            {
                var map = (TextureImporter)assetImporter;
                map.mipmapEnabled = true;
                map.filterMode = FilterMode.Trilinear;
                map.textureCompression = TextureImporterCompression.Uncompressed;
                map.maxTextureSize = 2048;
                map.wrapMode = TextureWrapMode.Clamp;
                return;
            }
            if (!assetPath.Contains("/External/Resources/"))
                return;
            var importer = (TextureImporter)assetImporter;
            string name = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            if (name.Contains("normal"))
                importer.textureType = TextureImporterType.NormalMap;
            if (name.Contains("roughness") || name.Contains("occlusion"))
                importer.sRGBTexture = false;
            if (name.Contains("roughness"))
                importer.isReadable = true;
            if (name == "daylight")
            {
                importer.textureShape = TextureImporterShape.TextureCube;
                importer.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
            }
            importer.maxTextureSize = 2048;
            importer.mipmapEnabled = true;
        }

        public static Bounds Bounds(GameObject model)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            var result = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1))
                result.Encapsulate(renderer.bounds);
            return result;
        }

        public static void Audit()
        {
            AssetDatabase.Refresh();
            foreach (
                var file in Directory
                    .GetFiles(Root + "Source", "*", SearchOption.AllDirectories)
                    .Where(p => new[] { ".obj", ".dae", ".glb" }.Contains(Path.GetExtension(p)))
            )
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(file.Replace('\\', '/'));
                if (!source)
                {
                    Debug.LogError("REALISM_MISSING " + file);
                    continue;
                }
                var model = UnityEngine.Object.Instantiate(source);
                var bounds = Bounds(model);
                Debug.Log(
                    "REALISM_AUDIT "
                        + file
                        + " size="
                        + bounds.size.ToString("F3")
                        + " center="
                        + bounds.center.ToString("F3")
                        + " renderers="
                        + model.GetComponentsInChildren<Renderer>().Length
                        + " materials="
                        + string.Join(
                            ";",
                            model
                                .GetComponentsInChildren<Renderer>()
                                .SelectMany(r => r.sharedMaterials)
                                .Where(m => m)
                                .Distinct()
                                .Select(m =>
                                    m.name + ":" + (m.mainTexture ? m.mainTexture.name : "NONE")
                                )
                        )
                );
                UnityEngine.Object.DestroyImmediate(model);
            }
        }

        static readonly string Prepared = Root + "Prepared/";

        public static void Prepare()
        {
            Directory.CreateDirectory(Prepared);
            AssetDatabase.Refresh();
            PrepareSurfaces();
            Model("Sedan", "prius_hybrid/meshes/Hybrid.obj", new Vector3(-90, 0, 0), 2, 4.4f);
            Model("PlayerSedan", "prius_hybrid/meshes/Hybrid.obj", new Vector3(-90, 0, 0), 2, 4.4f);
            Model(
                "ParkingSedan",
                "prius_hybrid/meshes/Hybrid.obj",
                new Vector3(-90, 0, 0),
                2,
                4.4f
            );
            Model("Hatchback", "hatchback/meshes/hatchback.obj", new Vector3(-90, 0, 0), 2, 4.2f);
            Model("SUV", "suv/meshes/suv.obj", new Vector3(-90, 180, 0), 2, 4.4f);
            Model("Ambulance", "ambulance/meshes/ambulance.obj", new Vector3(-90, 0, 0), 2, 4.4f);
            Model("Bus", "bus/meshes/bus.obj", new Vector3(-90, 0, 0), 2, 9.5f);
            Model("Pickup", "pickup/meshes/pickup.dae", Vector3.zero, 2, 4.4f);
            for (int i = 1; i <= 3; i++)
                Model(
                    "House" + i,
                    "house_" + i + "/meshes/house_" + i + ".dae",
                    Vector3.zero,
                    0,
                    13
                );
            Model("Apartment", "apartment/meshes/apartment.dae", Vector3.zero, 0, 19);
            Model("Shop", "grocery_store/meshes/grocery_store.dae", Vector3.zero, 0, 20);
            Model("Office", "law_office/meshes/law_office.dae", Vector3.zero, 0, 11);
            Model("Hydrant", "fire_hydrant/meshes/fire_hydrant.dae", Vector3.zero, 1, .9f);
            Model("Mailbox", "mailbox/meshes/mailbox.dae", Vector3.zero, 1, 1.2f);
            Model("Barrier", "jersey_barrier/meshes/jersey_barrier.dae", Vector3.zero, 1, .8f);
            Model("Cone", "construction_cone/meshes/construction_cone.dae", Vector3.zero, 1, .8f);
            Model(
                "Barrel",
                "construction_barrel/meshes/construction_barrel.dae",
                Vector3.zero,
                1,
                1
            );
            Model("Crane", "tower_crane/meshes/tower_crane.dae", Vector3.zero, 1, 28);
            Model("Bridge", "truss_bridge/meshes/truss_bridge.dae", Vector3.zero, 2, 30);
            Model("Signal", "stop_light/meshes/stop_light.obj", new Vector3(-90, 0, 0), 1, 1.2f);
            Model("Oak", "oak_tree/meshes/oak_tree.dae", Vector3.zero, 1, 8);
            Model("Pine", "pine_tree/meshes/pine_tree.dae", Vector3.zero, 1, 8);
            Model("Metro", "metro/Metro.glb", new Vector3(0, 90, 0), 2, 13.5f);
            AssetDatabase.SaveAssets();
        }

        static void Save(UnityEngine.Object value, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (existing)
            {
                EditorUtility.CopySerialized(value, existing);
                UnityEngine.Object.DestroyImmediate(value);
            }
            else
                AssetDatabase.CreateAsset(value, path);
        }

        static Material FixMaterial(Material original, string modelFolder, string output, int index)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var texture = original.mainTexture;
            // Older Collada exports reference workstation paths. Resolve only
            // within the model's licensed texture directory, never by global name.
            var files = Directory.Exists(modelFolder + "/materials/textures")
                ? Directory.GetFiles(modelFolder + "/materials/textures")
                : Array.Empty<string>();
            if (!texture)
            {
                var file = files.FirstOrDefault(p =>
                    (output == "Oak" || output == "Pine")
                    && Path.GetFileName(p)
                        .ToLowerInvariant()
                        .Contains(
                            original.name.ToLowerInvariant().Contains("bark") ? "bark" : "branch"
                        )
                    && p.EndsWith(".png")
                );
                file ??= files.FirstOrDefault(p =>
                    Path.GetFileName(p).ToLowerInvariant().Contains("diffuse")
                    && !p.EndsWith(".meta")
                );
                if (file != null)
                    texture = AssetDatabase.LoadAssetAtPath<Texture2D>(file.Replace('\\', '/'));
            }
            material.name = original.name;
            material.mainTexture = texture;
            material.color = texture ? Color.white : original.color;
            material.SetFloat(
                "_Smoothness",
                output is "Sedan" or "SUV" or "Hatchback" or "Ambulance" or "Bus" ? .4f : .2f
            );
            if (original.name.ToLowerInvariant().Contains("window"))
            {
                material.color = new Color(.12f, .2f, .25f, 1);
                material.SetFloat("_Smoothness", .9f);
                material.SetFloat("_Metallic", .25f);
            }
            var normal = files.FirstOrDefault(p =>
                Path.GetFileName(p).ToLowerInvariant().Contains("normal") && !p.EndsWith(".meta")
            );
            if (normal != null)
            {
                material.SetTexture(
                    "_BumpMap",
                    AssetDatabase.LoadAssetAtPath<Texture2D>(normal.Replace('\\', '/'))
                );
                material.EnableKeyword("_NORMALMAP");
            }
            material.enableInstancing = true;
            if (
                (output == "Oak" || output == "Pine")
                && original.name.ToLowerInvariant().Contains("branch")
            )
            {
                material.SetFloat("_AlphaClip", 1);
                material.SetFloat("_Cutoff", .45f);
                material.SetFloat("_Cull", 0);
                material.EnableKeyword("_ALPHATEST_ON");
                material.SetOverrideTag("RenderType", "TransparentCutout");
                material.renderQueue = 2450;
                material.SetFloat("_Smoothness", .05f);
            }
            string path = Prepared + output + "-material-" + index + ".mat";
            Save(material, path);
            return AssetDatabase.LoadAssetAtPath<Material>(path);
        }

        static void Model(string name, string file, Vector3 angles, int axis, float dimension)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Source/" + file);
            if (!source)
                throw new InvalidOperationException("Missing licensed asset: " + file);
            var holder = new GameObject(name);
            var model = UnityEngine.Object.Instantiate(source, holder.transform);
            model.transform.localRotation =
                Quaternion.Euler(angles) * model.transform.localRotation;
            var bounds = Bounds(model);
            model.transform.localScale *= dimension / bounds.size[axis];
            bounds = Bounds(model);
            if (name == "Metro")
            {
                // Compact railcar variant fits the existing validated station and
                // crossing envelope; retain a full-height passenger compartment.
                holder.transform.localScale = new Vector3(
                    2.65f / bounds.size.x,
                    3.3f / bounds.size.y,
                    1
                );
                bounds = Bounds(model);
            }
            model.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            var result = new GameObject(name);
            var materials = new Dictionary<Material, Material>();
            var groups = new Dictionary<string, List<CombineInstance>>();
            var groupMaterials = new Dictionary<string, Material>();
            string folder = Root + "Source/" + file.Split('/')[0];
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (!renderer)
                    continue;
                for (int sub = 0; sub < filter.sharedMesh.subMeshCount; sub++)
                {
                    var original = renderer.sharedMaterials[
                        Mathf.Min(sub, renderer.sharedMaterials.Length - 1)
                    ];
                    if (!original)
                        continue;
                    if (!materials.TryGetValue(original, out var material))
                    {
                        material =
                            name == "Metro"
                                ? original
                                : FixMaterial(original, folder, name, materials.Count);
                        materials[original] = material;
                    }
                    string part = renderer.name;
                    bool moving =
                        part.ToLowerInvariant().Contains("wheel") || part.Contains("sliding door");
                    string key =
                        name == "Metro" && !moving ? "Body-" + material.name : part + "-" + sub;
                    if (!groups.ContainsKey(key))
                        groups[key] = new List<CombineInstance>();
                    groups[key]
                        .Add(
                            new CombineInstance
                            {
                                mesh = filter.sharedMesh,
                                subMeshIndex = sub,
                                transform = filter.transform.localToWorldMatrix,
                            }
                        );
                    groupMaterials[key] = material;
                }
            }
            int meshIndex = 0;
            foreach (var group in groups)
            {
                var mesh = new Mesh
                {
                    name = name + " " + group.Key,
                    indexFormat = IndexFormat.UInt32,
                };
                mesh.CombineMeshes(group.Value.ToArray(), true, true);
                mesh.RecalculateBounds();
                if (name == "Bridge")
                {
                    var vertices = mesh.vertices;
                    var triangles = mesh.triangles;
                    var levels = new Dictionary<int, float>();
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        var a = vertices[triangles[i]];
                        var b = vertices[triangles[i + 1]];
                        var c = vertices[triangles[i + 2]];
                        var centre = (a + b + c) / 3;
                        var normal = Vector3.Cross(b - a, c - a);
                        if (
                            normal.normalized.y < .95f
                            || Mathf.Abs(centre.x) > 4.5f
                            || Mathf.Abs(centre.z) > 12
                        )
                            continue;
                        int level = Mathf.RoundToInt(centre.y * 100);
                        levels[level] = levels.TryGetValue(level, out var area)
                            ? area + normal.magnitude
                            : normal.magnitude;
                    }
                    float deck = levels.OrderByDescending(p => p.Value).First().Key / 100f;
                    for (int i = 0; i < vertices.Length; i++)
                        vertices[i].y -= deck;
                    mesh.vertices = vertices;
                    mesh.RecalculateBounds();
                    Debug.Log("REALISM_BRIDGE_DECK " + deck);
                }
                bool wheel =
                    group.Key.ToLowerInvariant().Contains("wheel")
                    || groupMaterials[group.Key].name.ToLowerInvariant().Contains("wheel");
                if (name == "PlayerSedan" && !wheel)
                {
                    var cabin = MeshPanels.Cut(
                        mesh,
                        new Bounds(new Vector3(0, 3.02f, 0), new Vector3(10, 4, 10))
                    );
                    UnityEngine.Object.DestroyImmediate(mesh);
                    mesh = cabin.outside;
                    if (cabin.inside.vertexCount > 0)
                    {
                        string cabinPath = Prepared + name + "-cabin-" + meshIndex + ".asset";
                        Save(cabin.inside, cabinPath);
                        var upper = new GameObject("Upper cabin " + group.Key);
                        upper.transform.SetParent(result.transform, false);
                        upper.AddComponent<MeshFilter>().sharedMesh =
                            AssetDatabase.LoadAssetAtPath<Mesh>(cabinPath);
                        upper.AddComponent<MeshRenderer>().sharedMaterial = groupMaterials[
                            group.Key
                        ];
                    }
                    else
                        UnityEngine.Object.DestroyImmediate(cabin.inside);
                }
                if (name == "Sedan" && group.Key.Contains("Wheel_Front"))
                {
                    string side = mesh.bounds.center.x > 0 ? "Right" : "Left";
                    var tire = UnityEngine.Object.Instantiate(mesh);
                    var vertices = tire.vertices;
                    var normals = tire.normals;
                    var rotation = Quaternion.Euler(0, 0, -90);
                    float scale = .68f / tire.bounds.size.y;
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        vertices[i] = rotation * (vertices[i] - tire.bounds.center) * scale;
                        normals[i] = rotation * normals[i];
                    }
                    tire.vertices = vertices;
                    tire.normals = normals;
                    tire.RecalculateBounds();
                    tire.RecalculateTangents();
                    string tirePath = Prepared + "Wheel" + side + ".asset";
                    Save(tire, tirePath);
                    var tireObject = new GameObject("Wheel" + side);
                    tireObject.AddComponent<MeshFilter>().sharedMesh =
                        AssetDatabase.LoadAssetAtPath<Mesh>(tirePath);
                    tireObject.AddComponent<MeshRenderer>().sharedMaterial = groupMaterials[
                        group.Key
                    ];
                    PrefabUtility.SaveAsPrefabAsset(
                        tireObject,
                        Prepared + "Wheel" + side + ".prefab"
                    );
                    UnityEngine.Object.DestroyImmediate(tireObject);
                }
                if (name == "Pickup" && wheel)
                {
                    var axle = MeshPanels.Cut(
                        mesh,
                        new Bounds(new Vector3(0, 0, 5), new Vector3(10, 10, 10))
                    );
                    UnityEngine.Object.DestroyImmediate(mesh);
                    mesh = axle.outside;
                    string pathFront = Prepared + "Pickup-front-wheels.asset";
                    Save(axle.inside, pathFront);
                    var front = new GameObject("Wheel front axle");
                    front.transform.SetParent(result.transform, false);
                    front.AddComponent<MeshFilter>().sharedMesh =
                        AssetDatabase.LoadAssetAtPath<Mesh>(pathFront);
                    front.AddComponent<MeshRenderer>().sharedMaterial = groupMaterials[group.Key];
                }
                if ((name == "Bus" || name == "ParkingSedan") && !wheel)
                {
                    var box =
                        name == "Bus"
                            ? new Bounds(
                                new Vector3(1.1f, 1.45f, 3.7f),
                                new Vector3(.8f, 2.1f, 1.15f)
                            )
                            : new Bounds(
                                new Vector3(-.95f, .97f, -.08f),
                                new Vector3(.5f, .94f, 1.62f)
                            );
                    var cut = MeshPanels.Cut(mesh, box);
                    UnityEngine.Object.DestroyImmediate(mesh);
                    mesh = cut.outside;
                    if (cut.inside.vertexCount > 0)
                    {
                        string panelPath = Prepared + name + "-panel-" + meshIndex + ".asset";
                        Save(cut.inside, panelPath);
                        var panel = new GameObject("DoorPanel");
                        panel.transform.SetParent(result.transform, false);
                        panel.AddComponent<MeshFilter>().sharedMesh =
                            AssetDatabase.LoadAssetAtPath<Mesh>(panelPath);
                        panel.AddComponent<MeshRenderer>().sharedMaterial = groupMaterials[
                            group.Key
                        ];
                    }
                    else
                        UnityEngine.Object.DestroyImmediate(cut.inside);
                }
                string path = Prepared + name + "-mesh-" + meshIndex++ + ".asset";
                Save(mesh, path);
                var part = new GameObject(
                    name == "Pickup" && wheel ? "Wheel rear axle" : group.Key
                );
                part.transform.SetParent(result.transform, false);
                part.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(
                    path
                );
                part.AddComponent<MeshRenderer>().sharedMaterial = groupMaterials[group.Key];
            }
            PrefabUtility.SaveAsPrefabAsset(result, Prepared + name + ".prefab");
            Debug.Log(
                "REALISM_PREPARED "
                    + name
                    + " "
                    + Bounds(result).size.ToString("F3")
                    + " draw parts="
                    + groups.Count
            );
            UnityEngine.Object.DestroyImmediate(holder);
            UnityEngine.Object.DestroyImmediate(result);
        }

        static void PrepareSurfaces()
        {
            var skyImporter = (TextureImporter)AssetImporter.GetAtPath(Root + "Daylight.hdr");
            if (skyImporter && skyImporter.textureShape != TextureImporterShape.TextureCube)
            {
                skyImporter.textureShape = TextureImporterShape.TextureCube;
                skyImporter.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
                skyImporter.SaveAndReimport();
            }
            foreach (
                string name in new[]
                {
                    "Asphalt",
                    "Concrete",
                    "Grass",
                    "Brick",
                    "Roof",
                    "Ballast",
                    "Plaster",
                }
            )
            {
                var roughImporter = (TextureImporter)
                    AssetImporter.GetAtPath(
                        "Assets/External/Resources/Materials/" + name + "Roughness.jpg"
                    );
                if (roughImporter && !roughImporter.isReadable)
                {
                    roughImporter.isReadable = true;
                    roughImporter.sRGBTexture = false;
                    roughImporter.SaveAndReimport();
                }
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.mainTexture = Resources.Load<Texture2D>("Materials/" + name);
                material.color = Color.white;
                material.SetFloat("_Smoothness", .22f);
                material.SetTexture(
                    "_BumpMap",
                    Resources.Load<Texture2D>("Materials/" + name + "Normal")
                );
                material.EnableKeyword("_NORMALMAP");
                material.SetTexture(
                    "_OcclusionMap",
                    Resources.Load<Texture2D>("Materials/" + name + "Occlusion")
                );
                material.SetFloat("_BumpScale", name == "Asphalt" ? .3f : .65f);
                if (name == "Asphalt")
                {
                    material.SetFloat("_SpecularHighlights", 0);
                    material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                }
                material.EnableKeyword("_OCCLUSIONMAP");
                material.enableInstancing = true;
                var rough = Resources.Load<Texture2D>("Materials/" + name + "Roughness");
                if (rough && rough.isReadable)
                {
                    var pixels = rough.GetPixels();
                    for (int i = 0; i < pixels.Length; i++)
                        pixels[i] = new Color(0, 0, 0, 1 - pixels[i].r);
                    var packed = new Texture2D(
                        rough.width,
                        rough.height,
                        TextureFormat.RGBA32,
                        true,
                        true
                    );
                    packed.SetPixels(pixels);
                    packed.Apply();
                    string path = Prepared + name + "-smoothness.asset";
                    Save(packed, path);
                    material.SetTexture(
                        "_MetallicGlossMap",
                        AssetDatabase.LoadAssetAtPath<Texture2D>(path)
                    );
                    material.SetFloat(
                        "_Smoothness",
                        name == "Grass" ? .12f
                            : name == "Asphalt" ? .12f
                            : .35f
                    );
                    material.EnableKeyword("_METALLICSPECGLOSSMAP");
                }
                if (name == "Asphalt")
                {
                    material.SetFloat("_BumpScale", .3f);
                    material.SetFloat("_SpecularHighlights", 0);
                    material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                    material.SetFloat("_EnvironmentReflections", 0);
                    material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                }
                if (name == "Ballast")
                    material.mainTextureScale = Vector2.one * (5f / 2.3f);
                if (name == "Plaster")
                {
                    material.SetFloat("_Smoothness", .08f);
                    material.SetFloat("_BumpScale", .3f);
                }
                Save(material, Prepared + name + ".mat");
            }
            var sky = new Material(Shader.Find("Skybox/Cubemap"));
            sky.SetTexture("_Tex", Resources.Load<Cubemap>("Realism/Daylight"));
            sky.SetFloat("_Exposure", .8f);
            Save(sky, Prepared + "Sky.mat");
        }

        public static void Preview()
        {
            Prepare();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            light.transform.rotation = Quaternion.Euler(45, -30, 0);
            RenderSettings.ambientLight = new Color(.65f, .68f, .72f);
            RenderSettings.skybox = Resources.Load<Material>("Realism/Prepared/Sky");
            var camera = new GameObject("Preview").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = 40;
            camera.farClipPlane = 500;
            var target = new RenderTexture(960, 640, 24);
            target.Create();
            camera.targetTexture = target;
            string folder = Path.GetFullPath("../outputs/qualification/realism-previews");
            Directory.CreateDirectory(folder);
            foreach (
                string name in new[]
                {
                    "Sedan",
                    "Hatchback",
                    "SUV",
                    "Bus",
                    "Ambulance",
                    "House1",
                    "House2",
                    "House3",
                    "Shop",
                    "Office",
                    "Metro",
                    "Signal",
                    "Bridge",
                    "Oak",
                    "Pine",
                    "Pickup",
                }
            )
            {
                var model = UnityEngine.Object.Instantiate(
                    Resources.Load<GameObject>("Realism/Prepared/" + name)
                );
                var b = Bounds(model);
                float size = Mathf.Max(b.size.x, b.size.y, b.size.z);
                camera.transform.position =
                    b.center + new Vector3(1, .65f, 1.15f).normalized * size * 1.9f;
                camera.transform.LookAt(b.center);
                for (int i = 0; i < 3; i++)
                    camera.Render();
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                var image = new Texture2D(960, 640, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 960, 640), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(model);
            }
            target.Release();
        }

        public static void PreviewTown()
        {
            Prepare();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Art.Initialize();
            var player = new GameObject("Preview player").AddComponent<Vehicle>();
            player.transform.position = new Vector3(2.3f, .1f, -85);
            player.Initialize();
            var town = new GameObject("Town").AddComponent<Town>();
            town.Build(player.transform);
            var camera = new GameObject("Preview camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = 65;
            camera.farClipPlane = 900;
            var target = new RenderTexture(1280, 720, 24);
            target.Create();
            camera.targetTexture = target;
            string folder = Path.GetFullPath("../outputs/qualification/realism-previews");
            var views = new[]
            {
                ("Town", new Vector3(-35, 19, -130), new Vector3(15, 3, -50)),
                ("Street", new Vector3(2.3f, 1.5f, -85), new Vector3(2.3f, 1.5f, 50)),
                ("BridgeRoad", new Vector3(2.3f, 6, 269), new Vector3(2.3f, 6, 330)),
                (
                    "Train",
                    town.Train.transform.position + new Vector3(12, 5, -16),
                    town.Train.transform.position + Vector3.up
                ),
                (
                    "ParkingDoor",
                    town.ParkingCar.transform.position + new Vector3(6, 3, -5),
                    town.ParkingCar.transform.position + Vector3.up
                ),
                ("SignalInTown", new Vector3(1, 3.5f, -1), new Vector3(5, 4.5f, 10)),
            };
            foreach (var view in views)
            {
                camera.transform.position = view.Item2;
                camera.transform.LookAt(view.Item3);
                for (int i = 0; i < 4; i++)
                    camera.Render();
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                var picture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                picture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                picture.Apply();
                File.WriteAllBytes(
                    Path.Combine(folder, view.Item1 + ".png"),
                    picture.EncodeToPNG()
                );
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(picture);
            }
            target.Release();
        }
    }
}
