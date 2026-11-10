using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoDriver.Editor
{
    public static class MakeHumanImporter
    {
        const string Source = "Assets/External/Resources/WorldArt/MakeHumanSource/";
        const string Output = "Assets/External/Resources/WorldArt/Civilians/";


        [Serializable]
        class Surface
        {
            public string name,
                albedo,
                normal;
            public float[] color;
            public float smoothness;
            public bool alpha_clip;
        }

        [Serializable]
        class Surfaces
        {
            public Surface[] materials;
        }

        public static void Prepare()
        {
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/External/Resources/Actors/Locomotion.controller"
            );
            if (!controller)
                return;
            Directory.CreateDirectory(Output);
            AssetDatabase.Refresh();
            foreach (string name in new[] { "Citizen_A", "Citizen_B" })
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(Source + name + ".fbx");
                if (!source)
                    continue;
                var model = Object.Instantiate(source);
                try
                {
                    model.name = name;
                    var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
                    var pairs = new List<(string human, string bone)>
                    {
                        ("Hips", "Hips"),
                        ("Spine", "Spine"),
                        ("Chest", "Spine1"),
                        ("UpperChest", "Spine2"),
                        ("Neck", "Neck"),
                        ("Head", "Head"),
                        ("LeftEye", "LeftEye"),
                        ("RightEye", "RightEye"),
                    };
                    foreach (string side in new[] { "Left", "Right" })
                    {
                        foreach (
                            var pair in new[]
                            {
                                ("Shoulder", "Shoulder"),
                                ("UpperArm", "Arm"),
                                ("LowerArm", "ForeArm"),
                                ("Hand", "Hand"),
                                ("UpperLeg", "UpLeg"),
                                ("LowerLeg", "Leg"),
                                ("Foot", "Foot"),
                                ("Toes", "ToeBase"),
                            }
                        )
                            pairs.Add((side + pair.Item1, side + pair.Item2));
                        foreach (
                            string finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" }
                        )
                            for (int i = 0; i < 3; i++)
                                pairs.Add(
                                    (
                                        side
                                            + " "
                                            + finger
                                            + " "
                                            + new[] { "Proximal", "Intermediate", "Distal" }[i],
                                        side
                                            + "Hand"
                                            + (finger == "Little" ? "Pinky" : finger)
                                            + (i + 1)
                                    )
                                );
                    }
                    var avatar = AvatarBuilder.BuildHumanAvatar(
                        model,
                        new HumanDescription
                        {
                            human = pairs
                                .Select(p => new HumanBone
                                {
                                    humanName = p.human,
                                    boneName = "mixamorig:" + p.bone,
                                    limit = new HumanLimit { useDefaultValues = true },
                                })
                                .ToArray(),
                            skeleton = model
                                .GetComponentsInChildren<Transform>()
                                .Select(t => new SkeletonBone
                                {
                                    name = t.name,
                                    position = t.localPosition,
                                    rotation = t.localRotation,
                                    scale = t.localScale,
                                })
                                .ToArray(),
                            upperArmTwist = .5f,
                            lowerArmTwist = .5f,
                            upperLegTwist = .5f,
                            lowerLegTwist = .5f,
                            armStretch = .05f,
                            legStretch = .05f,
                        }
                    );
                    if (!avatar.isValid || !avatar.isHuman)
                        throw new InvalidOperationException(name + " humanoid binding invalid");
                    string avatarPath = Output + name + "Rig.asset";
                    if (
                        AssetDatabase.LoadAssetAtPath<Avatar>(Output + name + ".avatar")
                        && !AssetDatabase.LoadAssetAtPath<Avatar>(avatarPath)
                    )
                        AssetDatabase.MoveAsset(Output + name + ".avatar", avatarPath);
                    animator.avatar = Save(avatar, avatarPath);
                    animator.runtimeAnimatorController = controller;
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                    var definitions = JsonUtility.FromJson<Surfaces>(
                        File.ReadAllText(Source + name + "-materials.json")
                    );
                    var materials = new Dictionary<string, Material>();
                    foreach (var definition in definitions.materials)
                    {
                        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                        material.name = definition.name;
                        var color = definition.color;
                        material.color = new Color(color[0], color[1], color[2], color[3]);
                        material.SetFloat("_Smoothness", definition.smoothness);
                        material.enableInstancing = true;
                        if (!string.IsNullOrEmpty(definition.albedo))
                            material.mainTexture = Texture(definition.albedo, false);
                        if (!string.IsNullOrEmpty(definition.normal))
                        {
                            material.SetTexture("_BumpMap", Texture(definition.normal, true));
                            material.EnableKeyword("_NORMALMAP");
                        }
                        if (definition.alpha_clip)
                        {
                            material.SetFloat("_AlphaClip", 1);
                            material.SetFloat("_Cutoff", .35f);
                            material.SetFloat("_Cull", 0);
                            material.EnableKeyword("_ALPHATEST_ON");
                            material.renderQueue = 2450;
                        }
                        materials.Add(
                            definition.name,
                            Save(material, Output + definition.name + ".mat")
                        );
                    }
                    var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>();
                    foreach (var renderer in renderers)
                    {
                        renderer.sharedMaterials = renderer
                            .sharedMaterials.Select(m =>
                                materials[m.name.Replace(" (Instance)", "")]
                            )
                            .ToArray();
                        renderer.updateWhenOffscreen = false;
                    }
                    var lod = model.GetComponent<LODGroup>() ?? model.AddComponent<LODGroup>();
                    lod.SetLODs(
                        new[]
                        {
                            new LOD(
                                .065f,
                                renderers
                                    .Where(r => r.name.EndsWith("_LOD0"))
                                    .Cast<Renderer>()
                                    .ToArray()
                            ),
                            new LOD(
                                .009f,
                                renderers
                                    .Where(r => r.name.EndsWith("_LOD1"))
                                    .Cast<Renderer>()
                                    .ToArray()
                            ),
                        }
                    );
                    lod.RecalculateBounds();
                    var metadata = model.AddComponent<AssetDefinition>();
                    metadata.assetId = "makehuman-cc0-" + name.ToLowerInvariant();
                    metadata.sourceUrl =
                        "https://static.makehumancommunity.org/assets/assetpacks/makehuman_system_assets.html";
                    metadata.license = "CC0-1.0";
                    metadata.publicRedistribution = true;
                    using (var sha = SHA256.Create())
                    {
                        metadata.sourceSha256 = Hex(
                            sha.ComputeHash(File.ReadAllBytes("makehuman-core-manifest.json"))
                        );
                        metadata.preparedSha256 = Hex(
                            sha.ComputeHash(File.ReadAllBytes(Source + name + ".fbx"))
                        );
                    }
                    PrefabUtility.SaveAsPrefabAsset(model, Output + name + ".prefab");
                }
                finally
                {
                    Object.DestroyImmediate(model);
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log(
                "WORLD_ART_MAKEHUMAN_READY two CC0 clothed variants with retargeted UAL animation and LODs"
            );
        }

        static string Hex(byte[] bytes) =>
            BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();

        static T Save<T>(T asset, string path)
            where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing)
            {
                EditorUtility.CopySerialized(asset, existing);
                Object.DestroyImmediate(asset);
                return existing;
            }
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static Texture2D Texture(string name, bool normal)
        {
            string path = Source + name;
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.textureType != type || importer.maxTextureSize != 2048)
            {
                importer.textureType = type;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
