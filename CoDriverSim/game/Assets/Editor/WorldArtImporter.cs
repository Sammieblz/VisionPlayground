using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace CoDriver.Editor
{
    public static class WorldArtImporter
    {
        public static void Prepare()
        {
            const string sourcePath = "Assets/External/Resources/WorldArt/Local/MercedesSource.glb";
            AssetDatabase.Refresh();
            PrepareFacades();
            PrepareSigns();
            CivilianImporter.Prepare();
            MakeHumanImporter.Prepare();
            AnimalArtImporter.Prepare();
            FleetArtImporter.Prepare();
            BuildingArtImporter.Prepare();
            ConstructionArtImporter.Prepare();
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (!source)
                return; // Public builds and existing checkouts need no private asset.
            var root = Object.Instantiate(source);
            root.name = "Mercedes GLS local prototype";
            PrepareMaterials(root, "Assets/External/Resources/WorldArt/Local/Materials");
            var definition = root.AddComponent<AssetDefinition>();
            definition.assetId = "supplied-mercedes-gls-580-2020";
            definition.sourceUrl =
                "https://free3d.com/3d-model/mercedes-benz-gls-580-2020-83444.html";
            using (var sha = SHA256.Create())
                definition.preparedSha256 = System
                    .BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(sourcePath)))
                    .Replace("-", "")
                    .ToLowerInvariant();
            definition.sourceSha256 =
                "8f694684cbb44c26b93d7872885870a0e6fe9f65da85aaf2888524d5eb3c81bf";
            definition.license = "Personal Use; public redistribution unverified";
            definition.publicRedistribution = false;
            var nodes = root.GetComponentsInChildren<Transform>();
            definition.wheels = new[] { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" }
                .Select(name => nodes.Single(t => t.name == name))
                .ToArray();
            definition.steeringWheel = nodes.Single(t => t.name == "SteeringWheel");
            var frontLeft = root.transform.InverseTransformPoint(definition.wheels[0].position);
            if (frontLeft.x >= 0 || frontLeft.z <= 0)
                throw new System.InvalidOperationException(
                    "GLS axis contract requires left=-X, forward=+Z"
                );
            definition.driverEye = VehicleProfile.GlsSimulation().driverEye;
            definition.sensorMount = VehicleProfile.GlsSimulation().sensorMount;
            // Measured from the supplied dashboard face and mirror housings.
            definition.instrumentCentre = new Vector3(-.25f, 1.184f, .641f);
            definition.instrumentEuler = new Vector3(24, 0, 0);
            definition.instrumentSize = new Vector2(.88f, .105f);
            definition.leftMirrorCentre = new Vector3(-.965f, 1.26f, .603f);
            definition.rightMirrorCentre = new Vector3(.965f, 1.26f, .603f);
            definition.leftMirrorEuler = new Vector3(0, -12, 0);
            definition.rightMirrorEuler = new Vector3(0, 12, 0);
            definition.rearMirrorCentre = new Vector3(0, 1.497f, .526f);
            definition.deviceCentre = new Vector3(.14f, 1.38f, .71f);
            PrefabUtility.SaveAsPrefabAsset(
                root,
                "Assets/External/Resources/WorldArt/Local/MercedesVehicle.prefab"
            );
            Debug.Log(
                "WORLD_ART_GLS_READY wheels="
                    + string.Join(
                        ";",
                        definition.wheels.Select(w => w.localPosition.ToString("F3"))
                    )
            );
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
        }

        static void PrepareFacades()
        {
            const string sourcePath =
                "Assets/External/Resources/WorldArt/FacadePrepared/FacadePrepared.glb";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (!source)
                return;
            var instance = Object.Instantiate(source);
            PrepareMaterials(instance, "Assets/External/Resources/WorldArt/FacadeMaterials");
            var nodes = instance.GetComponentsInChildren<Transform>();
            const string folder = "Assets/External/Resources/WorldArt/Facades";
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            var names = new[]
            {
                "Window",
                "Door",
                "Cornice",
                "Crown",
                "Plinth",
                "WindowPanel",
                "WindowSimple",
                "DoorGlass",
            };
            var groups = new[]
            {
                new[] { "wall_window_centered_large_01", "window_centered_large_01" },
                new[] { "wall_door_centered_large_01", "door_centered_large_01" },
                new[] { "cornice_standard_standard_01" },
                new[] { "crown_standard_standard_01" },
                new[] { "dado_standard_standard_01" },
                new[] { "wall_window_centered_large_02", "window_centered_large_02" },
                new[] { "wall_window_centered_large_03", "window_centered_large_03" },
                new[] { "wall_door_window_small_01", "door_window_small_01" },
            };
            for (int i = 0; i < names.Length; i++)
            {
                var module = new GameObject(names[i]);
                foreach (var name in groups[i])
                {
                    var part = Object.Instantiate(
                        nodes.Single(t => t.name == name).gameObject,
                        module.transform
                    );
                    part.transform.localPosition = new Vector3(-1.5f, 0, 0);
                    part.transform.localRotation = Quaternion.identity;
                    part.transform.localScale = Vector3.one;
                }
                var metadata = module.AddComponent<AssetDefinition>();
                metadata.assetId = "polyhaven-facade-" + names[i].ToLowerInvariant();
                metadata.sourceUrl = "https://polyhaven.com/a/modular_urban_apartments_facade";
                metadata.license = "CC0-1.0";
                metadata.publicRedistribution = true;
                PrefabUtility.SaveAsPrefabAsset(module, folder + "/" + names[i] + ".prefab");
                Object.DestroyImmediate(module);
            }
            Object.DestroyImmediate(instance);
            Debug.Log("WORLD_ART_FACADES_READY");
        }

        public static void PrepareMaterials(GameObject root, string directory)
        {
            // Use the application's URP material family, including its instancing
            // layout, instead of mixing imported ShaderGraph constant buffers.
            Directory.CreateDirectory(directory);
            AssetDatabase.Refresh();
            var converted = new System.Collections.Generic.Dictionary<Material, Material>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterials = renderer
                    .sharedMaterials.Select(original =>
                    {
                        if (converted.TryGetValue(original, out var material))
                            return material;
                        material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                        material.name = original.name;
                        material.enableInstancing = true;
                        material.SetFloat("_Cull", 0);
                        material.color = original.HasProperty("baseColorFactor")
                            ? original.GetColor("baseColorFactor")
                            : original.color;
                        if (original.HasProperty("baseColorTexture"))
                        {
                            material.mainTexture = original.GetTexture("baseColorTexture");
                            material.mainTextureScale = original.GetTextureScale(
                                "baseColorTexture"
                            );
                            material.mainTextureOffset = original.GetTextureOffset(
                                "baseColorTexture"
                            );
                        }
                        else
                            material.mainTexture = original.mainTexture;
                        float roughness = original.HasProperty("roughnessFactor")
                            ? original.GetFloat("roughnessFactor")
                            : .7f;
                        material.SetFloat("_Smoothness", Mathf.Clamp01(1 - roughness));
                        if (original.HasProperty("metallicFactor"))
                            material.SetFloat("_Metallic", original.GetFloat("metallicFactor"));
                        if (material.color.a < .99f)
                        {
                            material.SetFloat("_Surface", 1);
                            material.SetFloat(
                                "_SrcBlend",
                                (float)UnityEngine.Rendering.BlendMode.SrcAlpha
                            );
                            material.SetFloat(
                                "_DstBlend",
                                (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha
                            );
                            material.SetFloat("_ZWrite", 0);
                            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                            material.renderQueue = 3000;
                            material.SetOverrideTag("RenderType", "Transparent");
                        }
                        else if (
                            original.renderQueue == (int)UnityEngine.Rendering.RenderQueue.AlphaTest
                        )
                        {
                            material.SetFloat("_AlphaClip", 1);
                            material.SetFloat(
                                "_Cutoff",
                                original.HasProperty("alphaCutoff")
                                    ? original.GetFloat("alphaCutoff")
                                    : .5f
                            );
                            material.EnableKeyword("_ALPHATEST_ON");
                            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                            material.SetOverrideTag("RenderType", "TransparentCutout");
                        }
                        string file = string.Concat(
                            original.name.Select(c =>
                                Path.GetInvalidFileNameChars().Contains(c) ? '_' : c
                            )
                        );
                        if (string.IsNullOrWhiteSpace(file))
                        {
                            // glTF permits unnamed materials. Preserve a stable
                            // identity instead of trying to create an invalid .mat.
                            file = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                                original,
                                out string sourceGuid,
                                out long localId
                            )
                                ? "Material-" + sourceGuid + "-" + localId
                                : "Material-" + converted.Count;
                        }
                        WorldMaterialMaps.Copy(original, material, directory, file);
                        string path = directory + "/" + file + ".mat";
                        var previous = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (previous)
                        {
                            EditorUtility.CopySerialized(material, previous);
                            Object.DestroyImmediate(material);
                            material = previous;
                        }
                        else
                            AssetDatabase.CreateAsset(material, path);
                        converted[original] = material;
                        return material;
                    })
                    .ToArray();
            }
            AssetDatabase.SaveAssets();
        }

        static void PrepareSigns()
        {
            const string path = "Assets/External/Resources/WorldArt/SignsSource/StreetSigns.glb";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!source)
                return;
            var instance = Object.Instantiate(source);
            PrepareMaterials(instance, "Assets/External/Resources/WorldArt/SignMaterials");
            const string directory = "Assets/External/Resources/WorldArt/Signs";
            Directory.CreateDirectory(directory);
            AssetDatabase.Refresh();
            foreach (var part in instance.GetComponentsInChildren<MeshFilter>())
            {
                var sign = new GameObject(part.name);
                var model = Object.Instantiate(part.gameObject, sign.transform);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                var metadata = sign.AddComponent<AssetDefinition>();
                metadata.assetId = "screamingorange-" + part.name;
                metadata.sourceUrl = "https://blendswap.com/blend/31350";
                metadata.license = "CC0-1.0";
                metadata.publicRedistribution = true;
                PrefabUtility.SaveAsPrefabAsset(sign, directory + "/" + part.name + ".prefab");
                Object.DestroyImmediate(sign);
            }
            Object.DestroyImmediate(instance);
        }
    }
}
