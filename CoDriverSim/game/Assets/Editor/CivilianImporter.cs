using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CoDriver.Editor
{
    public static class CivilianImporter
    {
        public static void Prepare()
        {
            const string sourcePath = "Assets/External/Resources/WorldArt/CivilianSource/Civilian.fbx";
            const string folder = "Assets/External/Resources/WorldArt/Civilians";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/External/Resources/Actors/Locomotion.controller");
            if (!source || !controller) return;
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            var model = Object.Instantiate(source);
            var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
            var pairs = new[] { ("Hips","hips"), ("Spine","spine"), ("Chest","ribs"),
                ("Neck","neck"), ("Head","head"), ("LeftShoulder","shoulder.L"),
                ("RightShoulder","shoulder.R"), ("LeftUpperArm","upper_arm.L"),
                ("RightUpperArm","upper_arm.R"), ("LeftLowerArm","forearm.L"),
                ("RightLowerArm","forearm.R"), ("LeftHand","hand.L"), ("RightHand","hand.R"),
                ("LeftUpperLeg","thigh.L"), ("RightUpperLeg","thigh.R"),
                ("LeftLowerLeg","shin.L"), ("RightLowerLeg","shin.R"),
                ("LeftFoot","foot.L"), ("RightFoot","foot.R"),
                ("LeftToes","toe.L"), ("RightToes","toe.R") };
            var avatar = AvatarBuilder.BuildHumanAvatar(model, new HumanDescription {
                human = pairs.Select(p => new HumanBone { humanName=p.Item1, boneName=p.Item2,
                    limit=new HumanLimit { useDefaultValues=true } }).ToArray(),
                skeleton = model.GetComponentsInChildren<Transform>().Select(t => new SkeletonBone {
                    name=t.name, position=t.localPosition, rotation=t.localRotation, scale=t.localScale }).ToArray(),
                upperArmTwist=.5f, lowerArmTwist=.5f, upperLegTwist=.5f, lowerLegTwist=.5f,
                armStretch=.05f, legStretch=.05f });
            if (!avatar.isValid || !avatar.isHuman)
                throw new System.InvalidOperationException("Supplied civilian humanoid binding is invalid");
            string avatarPath = folder + "/Civilian.avatar";
            var existing = AssetDatabase.LoadAssetAtPath<Avatar>(avatarPath);
            if (existing) { EditorUtility.CopySerialized(avatar, existing); Object.DestroyImmediate(avatar); avatar=existing; }
            else AssetDatabase.CreateAsset(avatar, avatarPath);
            animator.avatar = avatar;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            var metadata = model.AddComponent<AssetDefinition>();
            metadata.assetId = "brightonpiers-walking-civilian";
            metadata.sourceUrl = "https://blendswap.com/blend/20564";
            metadata.license = "CC-BY-3.0";
            metadata.publicRedistribution = true;
            metadata.sourceSha256 = "b93b59c5bdff4eea175873fa53f37ed7cc1534ed4ea96e7f58d7d192d76f4aab";
            using (var sha = System.Security.Cryptography.SHA256.Create())
                metadata.preparedSha256 = System.BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(sourcePath))).Replace("-", "").ToLowerInvariant();
            var materials = model.GetComponentsInChildren<SkinnedMeshRenderer>()
                .SelectMany(r => r.sharedMaterials).Distinct().ToArray();
            var converted = new System.Collections.Generic.Dictionary<Material, Material>();
            foreach (var original in materials)
            {
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.color = original.color;
                material.SetFloat("_Smoothness", .18f);
                material.enableInstancing = true;
                string name = original.name.Replace(" (Instance)", "");
                if (name == "shirt")
                {
                    material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                        "Assets/External/Resources/WorldArt/Fabric/Fabric034_2K-JPG_Color.jpg");
                    material.color = new Color(.24f, .36f, .43f);
                }
                else if (name == "jeans")
                {
                    material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                        "Assets/External/Resources/WorldArt/CivilianSource/" + name + ".png");
                    material.color = Color.white;
                }
                else if (name == "head" || name == "arms")
                    material.color = new Color(.66f, .43f, .30f);
                string path = folder + "/" + name + ".mat";
                var old = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (old) { EditorUtility.CopySerialized(material, old); Object.DestroyImmediate(material); material=old; }
                else AssetDatabase.CreateAsset(material, path);
                converted[original] = material;
            }
            foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                renderer.sharedMaterials = renderer.sharedMaterials.Select(m => converted[m]).ToArray();
                renderer.updateWhenOffscreen = false;
            }
            PrefabUtility.SaveAsPrefabAsset(model, folder + "/Civilian.prefab");
            Object.DestroyImmediate(model);
            Debug.Log("WORLD_ART_CIVILIAN_READY explicit humanoid rig + existing UAL clips");
        }
    }
}
