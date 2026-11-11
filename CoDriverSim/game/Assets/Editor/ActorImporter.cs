using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CoDriver.Editor
{
    public sealed class ActorImporter : AssetPostprocessor
    {
        static bool TownCharacter(string path) =>
            path.Contains("/Actors/Casual_") || path.Contains("/Actors/Worker_");

        void OnPreprocessModel()
        {
            if (!assetPath.Contains("/External/Resources/"))
                return;
            var importer = (ModelImporter)assetImporter;
            importer.importCameras = false;
            importer.importLights = false;
            importer.animationType =
                assetPath.Contains("/Actors/") && !TownCharacter(assetPath)
                    ? ModelImporterAnimationType.Human
                    : ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = !TownCharacter(assetPath);
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        }

        void OnPreprocessAnimation()
        {
            if (!assetPath.Contains("/External/Resources/"))
                return;
            var importer = (ModelImporter)assetImporter;
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.loopTime =
                    clip.name.Contains("Loop")
                    || clip.name.ToLowerInvariant().Contains("walk")
                    || clip.name.ToLowerInvariant().Contains("run")
                    || clip.name.ToLowerInvariant().Contains("fly")
                    || clip.name.ToLowerInvariant().Contains("idle");
                clip.loopPose = clip.loopTime;
            }
            importer.clipAnimations = clips;
        }

        void OnPostprocessMaterial(Material material)
        {
            if (!assetPath.Contains("/External/Resources/"))
                return;
            var color = material.color;
            var texture = material.mainTexture;
            material.shader = Shader.Find("Universal Render Pipeline/Lit");
            material.color = color;
            material.mainTexture = texture;
            material.SetFloat("_Smoothness", .25f);
            material.enableInstancing = true;
        }

        public static void Prepare()
        {
            AssetDatabase.Refresh();
            PrepareProps();
            string folder = "Assets/External/Resources/Actors/";
            var clips = AssetDatabase
                .LoadAllAssetsAtPath(folder + "UAL1_Standard.fbx")
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__"))
                .ToArray();
            if (clips.Length == 0)
            {
                Debug.LogWarning("No external Quaternius clips imported yet");
                return;
            }
            Debug.Log("CODRIVER_ANIMATIONS " + string.Join(",", clips.Select(c => c.name)));
            string controllerPath = folder + "Locomotion.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (!controller)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                foreach (var clip in clips)
                {
                    var state = controller.layers[0].stateMachine.AddState(clip.name);
                    state.motion = clip;
                }
            }
            PrepareRecovery(controller, clips, folder);
            foreach (string role in new[] { "Casual", "Worker" })
            foreach (string sex in new[] { "Male", "Female" })
            {
                string resource = (role == "Casual" ? "Person" : "Worker") + sex;
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(
                    folder + role + "_" + sex + ".fbx"
                );
                if (!source)
                    throw new System.Exception(
                        "Fetch the licensed character asset: " + role + "_" + sex
                    );
                var model = Object.Instantiate(source);
                model.name = resource;
                var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
                // This older pack has foot IK controls outside the leg hierarchy.
                // Preserve their bind pose while parenting them under the lower legs,
                // then build an explicit humanoid avatar for the newer UAL clips.
                foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                    renderer.name = "Clothing mesh " + renderer.name;
                var bones = model.GetComponentsInChildren<Transform>().ToDictionary(t => t.name);
                foreach (string side in new[] { "L", "R" })
                    bones["Foot." + side].SetParent(bones["LowerLeg." + side], true);
                var mapping = new[]
                {
                    ("Hips", "Body"),
                    ("Spine", "Hips"),
                    ("Chest", "Abdomen"),
                    ("UpperChest", "Torso"),
                    ("Neck", "Neck"),
                    ("Head", "Head"),
                    ("LeftShoulder", "Shoulder.L"),
                    ("RightShoulder", "Shoulder.R"),
                    ("LeftUpperArm", "UpperArm.L"),
                    ("RightUpperArm", "UpperArm.R"),
                    ("LeftLowerArm", "LowerArm.L"),
                    ("RightLowerArm", "LowerArm.R"),
                    ("LeftHand", "Fist.L"),
                    ("RightHand", "Fist.R"),
                    ("LeftUpperLeg", "UpperLeg.L"),
                    ("RightUpperLeg", "UpperLeg.R"),
                    ("LeftLowerLeg", "LowerLeg.L"),
                    ("RightLowerLeg", "LowerLeg.R"),
                    ("LeftFoot", "Foot.L"),
                    ("RightFoot", "Foot.R"),
                };
                var description = new HumanDescription
                {
                    human = mapping
                        .Select(p => new HumanBone
                        {
                            humanName = p.Item1,
                            boneName = p.Item2,
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
                };
                var avatar = AvatarBuilder.BuildHumanAvatar(model, description);
                avatar.name = resource + " avatar";
                string avatarPath = folder + resource + "Avatar.asset";
                var existingAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(avatarPath);
                if (existingAvatar)
                {
                    EditorUtility.CopySerialized(avatar, existingAvatar);
                    Object.DestroyImmediate(avatar);
                    avatar = existingAvatar;
                }
                else
                    AssetDatabase.CreateAsset(avatar, avatarPath);
                animator.avatar = avatar;
                if (!animator || !animator.avatar || !animator.avatar.isHuman)
                {
                    Object.DestroyImmediate(model);
                    throw new System.Exception("Quaternius humanoid avatar import failed");
                }
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>();
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers)
                    bounds.Encapsulate(renderer.bounds);
                model.transform.localScale *= 1.75f / bounds.size.y;
                // Preserve the author's separate clothing, skin and hair materials.
                PrefabUtility.SaveAsPrefabAsset(model, folder + resource + ".prefab");
                Object.DestroyImmediate(model);
            }
            AssetDatabase.SaveAssets();
        }

        static void PrepareRecovery(
            AnimatorController controller,
            AnimationClip[] clips,
            string folder
        )
        {
            var fall = clips.FirstOrDefault(c => c.name.EndsWith("Death01"));
            if (!fall)
                return;
            var recovery = Object.Instantiate(fall);
            recovery.name = "Ground_Recovery";
            foreach (var binding in AnimationUtility.GetCurveBindings(fall))
            {
                var curve = AnimationUtility.GetEditorCurve(fall, binding);
                var keys = curve
                    .keys.Reverse()
                    .Select(k => new Keyframe(
                        fall.length - k.time,
                        k.value,
                        -k.outTangent,
                        -k.inTangent,
                        k.outWeight,
                        k.inWeight
                    )
                    {
                        weightedMode =
                            k.weightedMode == WeightedMode.In ? WeightedMode.Out
                            : k.weightedMode == WeightedMode.Out ? WeightedMode.In
                            : k.weightedMode,
                    })
                    .ToArray();
                AnimationUtility.SetEditorCurve(recovery, binding, new AnimationCurve(keys));
            }
            AnimationUtility.SetAnimationEvents(recovery, new AnimationEvent[0]);
            var settings = AnimationUtility.GetAnimationClipSettings(recovery);
            settings.loopTime = false;
            settings.startTime = 0;
            settings.stopTime = fall.length;
            AnimationUtility.SetAnimationClipSettings(recovery, settings);
            string path = folder + "GroundRecovery.anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing)
            {
                EditorUtility.CopySerialized(recovery, existing);
                Object.DestroyImmediate(recovery);
                recovery = existing;
            }
            else
                AssetDatabase.CreateAsset(recovery, path);
            // CreateAsset can take the file basename as the initial clip name.
            // Keep state/clip identity stable across the first and later imports.
            recovery.name = "Ground_Recovery";
            EditorUtility.SetDirty(recovery);
            var machine = controller.layers[0].stateMachine;
            foreach (var prior in machine.states)
                if (prior.state.motion == recovery && prior.state.name != recovery.name)
                    machine.RemoveState(prior.state);
            var state =
                machine.states.Select(s => s.state).FirstOrDefault(s => s.name == recovery.name)
                ?? machine.AddState(recovery.name);
            state.motion = recovery;
            EditorUtility.SetDirty(controller);
        }

        static void PrepareProps()
        {
            string folder = "Assets/External/Resources/Scenery/";
            if (!AssetDatabase.IsValidFolder(folder + "Prepared"))
                AssetDatabase.CreateFolder(folder.TrimEnd('/'), "Prepared");
            foreach (
                string name in new[]
                {
                    "Tree_1",
                    "Birch_1",
                    "Pine_2",
                    "NormalCar1",
                    "SUV",
                    "Cop",
                    "Pug",
                    "Dog",
                    "Cat",
                    "Eagle",
                }
            )
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(folder + name + ".fbx");
                if (!source)
                    continue;
                var instance = Object.Instantiate(source);
                var root = new GameObject(name);
                // FBX animation curves can write the imported root's scale/position.
                // Normalize on a parent that no animation clip can address.
                var normalization = new GameObject("Asset normalization").transform;
                normalization.SetParent(root.transform, false);
                instance.transform.SetParent(normalization, false);
                var renderers = instance.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                {
                    Object.DestroyImmediate(root);
                    continue;
                }
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers)
                    bounds.Encapsulate(renderer.bounds);
                bool tree =
                    name.Contains("Tree") || name.Contains("Pine") || name.Contains("Birch");
                bool animal = name == "Pug" || name == "Dog" || name == "Cat" || name == "Eagle";
                if (tree)
                    foreach (var renderer in renderers)
                    {
                        var materials = renderer.sharedMaterials;
                        for (int i = 0; i < materials.Length; i++)
                        {
                            string original = materials[i].name;
                            bool leaf =
                                original.ToLowerInvariant().Contains("lea")
                                || original.ToLowerInvariant().Contains("pine");
                            Debug.Log(
                                "CODRIVER_TREE_MATERIAL "
                                    + name
                                    + " / "
                                    + original
                                    + " / "
                                    + renderer.name
                            );
                            string texture = leaf
                                ? (
                                    name.Contains("Birch") ? "Birch_Leaves_Green"
                                    : name.Contains("Pine") ? "Pine_Leaves"
                                    : "Tree_Leaves"
                                )
                                : (name.Contains("Birch") ? "Birch_Bark" : "Tree_Bark");
                            string path =
                                folder
                                + name
                                + "-"
                                + i
                                + "-"
                                + renderer.name.Replace("/", "_")
                                + ".mat";
                            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                            if (!material)
                            {
                                material = new Material(
                                    Shader.Find("Universal Render Pipeline/Lit")
                                );
                                AssetDatabase.CreateAsset(material, path);
                            }
                            material.color = leaf
                                ? new Color(.65f, .8f, .55f)
                                : new Color(.6f, .48f, .32f);
                            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                                folder + texture + ".png"
                            );
                            material.enableInstancing = true;
                            material.SetFloat("_Smoothness", .15f);
                            if (leaf)
                            {
                                material.SetFloat("_AlphaClip", 1);
                                material.SetFloat("_Cutoff", .4f);
                                material.EnableKeyword("_ALPHATEST_ON");
                                material.SetFloat("_Cull", 0);
                                material.renderQueue = 2450;
                            }
                            materials[i] = material;
                        }
                        renderer.sharedMaterials = materials;
                    }
                if (!tree && !animal && bounds.size.x > bounds.size.z)
                    normalization.localRotation = Quaternion.Euler(0, 90, 0);
                if (animal)
                {
                    var animator = instance.GetComponentInChildren<Animator>();
                    if (animator)
                    {
                        var clips = AssetDatabase
                            .LoadAllAssetsAtPath(folder + name + ".fbx")
                            .OfType<AnimationClip>()
                            .Where(c => !c.name.StartsWith("__preview__"))
                            .ToArray();
                        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                            folder + name + "Motion.controller"
                        );
                        if (!controller)
                        {
                            controller = AnimatorController.CreateAnimatorControllerAtPath(
                                folder + name + "Motion.controller"
                            );
                            foreach (var clip in clips)
                            {
                                var state = controller.layers[0].stateMachine.AddState(clip.name);
                                state.motion = clip;
                            }
                        }
                        animator.runtimeAnimatorController = controller;
                        animator.applyRootMotion = false;
                        // Measure an evaluated pose, including the FBX root curves.
                        var reference =
                            clips.FirstOrDefault(c => c.name.Contains("Idle"))
                            ?? clips.FirstOrDefault();
                        if (reference)
                            reference.SampleAnimation(instance, 0);
                        Debug.Log(
                            "CODRIVER_ANIMAL_ANIMATIONS "
                                + name
                                + " "
                                + string.Join(",", clips.Select(c => c.name))
                        );
                    }
                }
                // These pinned Quaternius quadrupeds face -X in their evaluated
                // bind/idle pose (front paws and neck at negative X). Keep the
                // correction on the unanimated parent, never on an animation bone.
                if (name == "Dog" || name == "Cat" || name == "Pug")
                    normalization.localRotation = Quaternion.Euler(0, 90, 0);
                bounds = renderers[0].bounds;
                foreach (var renderer in renderers)
                    bounds.Encapsulate(renderer.bounds);
                float scale =
                    tree ? 7 / bounds.size.y
                    : name == "Eagle" ? 1.2f / Mathf.Max(bounds.size.x, bounds.size.z)
                    : animal ? (name == "Cat" ? .35f : .6f) / bounds.size.y
                    : 4.4f / Mathf.Max(bounds.size.x, bounds.size.z);
                normalization.localScale = Vector3.one * scale;
                normalization.localPosition = new Vector3(
                    -bounds.center.x * scale,
                    -bounds.min.y * scale,
                    -bounds.center.z * scale
                );
                PrefabUtility.SaveAsPrefabAsset(root, folder + "Prepared/" + name + ".prefab");
                Object.DestroyImmediate(root);
            }
        }
    }
}
