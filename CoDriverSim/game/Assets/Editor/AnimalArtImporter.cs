using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CoDriver.Editor
{
    public static class AnimalArtImporter
    {
        public static void InspectSkinning()
        {
            var root = Object.Instantiate(Resources.Load<GameObject>("WorldArt/CatSource/Cat"));
            var clip = AssetDatabase
                .LoadAllAssetsAtPath("Assets/External/Resources/WorldArt/CatSource/Cat.fbx")
                .OfType<AnimationClip>()
                .First(c => c.name.EndsWith("Idle_Breathe"));
            clip.SampleAnimation(root, 0);
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                foreach (bool scale in new[] { false, true })
                {
                    var mesh = new Mesh();
                    skin.BakeMesh(mesh, scale);
                    var b = new Bounds(
                        skin.transform.TransformPoint(mesh.vertices[0]),
                        Vector3.zero
                    );
                    foreach (var v in mesh.vertices)
                        b.Encapsulate(skin.transform.TransformPoint(v));
                    Debug.Log(
                        "CAT_SKIN "
                            + skin.name
                            + " scale="
                            + scale
                            + " local="
                            + skin.transform.localScale
                            + " lossy="
                            + skin.transform.lossyScale
                            + " skin="
                            + skin.bounds
                            + " baked="
                            + b
                    );
                    Object.DestroyImmediate(mesh);
                }
            }
            Object.DestroyImmediate(root);
        }

        public static void Prepare()
        {
            const string sourcePath = "Assets/External/Resources/WorldArt/CatSource/Cat.fbx";
            const string folder = "Assets/External/Resources/WorldArt/Animals";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (!source)
                return;
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            var root = new GameObject("Prepared calico cat");
            var orientation = new GameObject("Measured forward and ground normalization").transform;
            orientation.SetParent(root.transform, false);
            var model = Object.Instantiate(source, orientation);
            var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
            var clips = AssetDatabase
                .LoadAllAssetsAtPath(sourcePath)
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__"))
                .ToArray();
            if (clips.Length < 4)
                throw new System.InvalidOperationException(
                    "Cat requires walk, run and two idle clips"
                );
            string controllerPath = folder + "/Cat.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (!controller)
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var machine = controller.layers[0].stateMachine;
            foreach (var clip in clips)
            {
                var state =
                    machine.states.Select(s => s.state).FirstOrDefault(s => s.name == clip.name)
                    ?? machine.AddState(clip.name);
                state.motion = clip;
                if (clip.name.EndsWith("Idle_Breathe"))
                    machine.defaultState = state;
            }
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            clips.First(c => c.name.EndsWith("Idle_Breathe")).SampleAnimation(model, 0);
            var bones = model.GetComponentsInChildren<Transform>();
            var head = bones.Single(t => t.name == "Head_controller");
            var hip = bones.Single(t => t.name == "Hip");
            var forward = Vector3
                .ProjectOnPlane(head.position - hip.position, Vector3.up)
                .normalized;
            orientation.rotation = Quaternion.FromToRotation(forward, Vector3.forward);
            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>();
            // SkinnedRenderer.bounds covers every animation, including raised
            // paws/tail. Ground the evaluated standing mesh, not that envelope.
            var bounds = new Bounds();
            bool firstVertex = true;
            foreach (var renderer in renderers)
            {
                var posed = new Mesh();
                renderer.BakeMesh(posed, true);
                foreach (var vertex in posed.vertices)
                {
                    var point = renderer.transform.TransformPoint(vertex);
                    if (firstVertex)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        firstVertex = false;
                    }
                    else
                        bounds.Encapsulate(point);
                }
                Object.DestroyImmediate(posed);
            }
            var bodyCentre = (head.position + hip.position) * .5f;
            orientation.position = new Vector3(-bodyCentre.x, -bounds.min.y, -bodyCentre.z);
            var converted = new System.Collections.Generic.Dictionary<Material, Material>();
            foreach (var renderer in renderers)
            {
                renderer.sharedMaterials = renderer
                    .sharedMaterials.Select(original =>
                    {
                        if (converted.TryGetValue(original, out var material))
                            return material;
                        material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                        material.enableInstancing = true;
                        material.color = Color.white;
                        string role = original.name.Replace(" (Instance)", "");
                        var image = AssetDatabase.LoadAssetAtPath<Texture2D>(
                            "Assets/External/Resources/WorldArt/CatSource/" + role + ".png"
                        );
                        material.mainTexture = image ? image : original.mainTexture;
                        material.SetFloat("_Smoothness", .18f);
                        if (role == "Cornea")
                        {
                            material.color = new Color(.2f, .3f, .15f, .06f);
                            material.SetFloat("_Smoothness", .85f);
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
                        }
                        string path = folder + "/" + role + ".mat";
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
                renderer.updateWhenOffscreen = false;
            }
            var definition = root.AddComponent<AssetDefinition>();
            definition.assetId = "jonas-dichelle-calico-cat";
            definition.sourceUrl = "https://blendswap.com/blend/18519";
            definition.license = "CC-BY-3.0";
            definition.publicRedistribution = true;
            definition.sourceSha256 =
                "fda4b80f1a06e67a1cc04396b858085faa5cd9742f28d9bde9837aaeab2727fa";
            using (var sha = System.Security.Cryptography.SHA256.Create())
                definition.preparedSha256 = System
                    .BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(sourcePath)))
                    .Replace("-", "")
                    .ToLowerInvariant();
            definition.walkingMetresPerSecond = .5f;
            definition.runningMetresPerSecond = 1.4f;
            var lod = root.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(.005f, renderers) });
            lod.RecalculateBounds();
            PrefabUtility.SaveAsPrefabAsset(root, folder + "/Cat.prefab");
            Debug.Log(
                "WORLD_ART_CAT_READY forward="
                    + root.transform.InverseTransformDirection(head.position - hip.position)
                    + " bounds="
                    + bounds
            );
            Object.DestroyImmediate(root);
        }
    }
}
