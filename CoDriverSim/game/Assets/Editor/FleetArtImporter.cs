using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CoDriver.Editor
{
    public static class FleetArtImporter
    {
        public static void Prepare()
        {
            const string sourcePath =
                "Assets/External/Resources/WorldArt/FleetSource/ConceptFleet.glb";
            const string folder = "Assets/External/Resources/WorldArt/Fleet";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (!source)
                return;
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            var model = Object.Instantiate(source);
            WorldArtImporter.PrepareMaterials(model, folder + "/Materials");
            foreach (
                var material in model
                    .GetComponentsInChildren<Renderer>()
                    .SelectMany(r => r.sharedMaterials)
                    .Distinct()
            )
            {
                if (material.name == "Glass")
                {
                    // The source uses glTF transmission. NPC interiors are removed,
                    // so use a dark exterior glazing surface instead of white opaque transmission fallback.
                    material.color = new Color(.025f, .045f, .06f);
                    material.SetFloat("_Smoothness", .85f);
                    material.SetFloat("_Metallic", .15f);
                }
                if (material.name.StartsWith("Paint 1 Carmine"))
                {
                    // Source normal-only microflakes use KHR_texture_transform 30x.
                    // URP Lit shares base UVs for normals; no base-color image is present here.
                    material.mainTextureScale = Vector2.one * 30;
                    material.SetFloat("_BumpScale", .3f);
                }
                EditorUtility.SetDirty(material);
            }
            var nodes = model.GetComponentsInChildren<Transform>();
            var group = model.AddComponent<LODGroup>();
            group.SetLODs(
                new[]
                {
                    new LOD(
                        .09f,
                        nodes.Single(t => t.name == "LOD0").GetComponentsInChildren<Renderer>()
                    ),
                    new LOD(
                        .008f,
                        nodes.Single(t => t.name == "LOD1").GetComponentsInChildren<Renderer>()
                    ),
                }
            );
            group.RecalculateBounds();
            var metadata = model.AddComponent<AssetDefinition>();
            metadata.assetId = "khronos-carconcept-npc";
            metadata.sourceUrl =
                "https://github.com/KhronosGroup/glTF-Sample-Assets/tree/7d4ba189827916452eeadc82d4b712dbc6280a6f/Models/CarConcept";
            metadata.license = "CC-BY-4.0";
            metadata.publicRedistribution = true;
            metadata.wheels = nodes
                .Where(t => t.name.StartsWith("Wheel") && t.name.Contains("_LOD"))
                .ToArray();
            if (metadata.wheels.Length != 8)
                throw new System.InvalidOperationException(
                    "Fleet requires four independent wheels per detail level"
                );
            foreach (var wheel in metadata.wheels)
            {
                var position = model.transform.InverseTransformPoint(wheel.position);
                if (
                    Mathf.Abs(Mathf.Abs(position.x) - .78f) > .01f
                    || Mathf.Abs(Mathf.Abs(position.z) - 1.35f) > .01f
                )
                    throw new System.InvalidOperationException(
                        "Fleet wheel axes do not match the road-car profile"
                    );
            }
            using (var sha = System.Security.Cryptography.SHA256.Create())
                metadata.preparedSha256 = System
                    .BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(sourcePath)))
                    .Replace("-", "")
                    .ToLowerInvariant();
            PrefabUtility.SaveAsPrefabAsset(model, folder + "/Concept.prefab");
            Object.DestroyImmediate(model);
            Debug.Log("WORLD_ART_FLEET_READY measured 2.7 m wheelbase; two detail levels");
        }
    }
}
