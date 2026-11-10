using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace CoDriver.Editor
{
    public static class ConstructionArtImporter
    {
        public static void Prepare()
        {
            const string sourcePath =
                "Assets/External/Resources/WorldArt/ConstructionSource/Excavator.glb";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (!source)
                return;
            var root = Object.Instantiate(source);
            root.name = "Articulated compact excavator";
            WorldArtImporter.PrepareMaterials(
                root,
                "Assets/External/Resources/WorldArt/ConstructionMaterials"
            );
            var renderers = root.GetComponentsInChildren<Renderer>();
            var near = renderers.Where(r => r.name.Contains("LOD0")).ToArray();
            var far = renderers.Where(r => r.name.Contains("LOD1")).ToArray();
            if (near.Length != 5 || far.Length != 5)
                throw new System.InvalidOperationException(
                    "Construction asset requires five articulated parts per LOD"
                );
            var lod = root.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(.08f, near), new LOD(.008f, far) });
            lod.RecalculateBounds();
            var definition = root.AddComponent<AssetDefinition>();
            definition.assetId = "innerscene-compact-tracked-excavator";
            definition.sourceUrl =
                "https://www.innerscene.com/tools/library/3d-parts/compact-tracked-excavator-76c41cd5";
            definition.sourceSha256 =
                "d7ab61533437b330b2107150092d10bf5f51fce66c1fb06ee554669a4eff4f17";
            using (var sha = SHA256.Create())
                definition.preparedSha256 = System
                    .BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(sourcePath)))
                    .Replace("-", "")
                    .ToLowerInvariant();
            definition.license = "CC0-1.0";
            definition.publicRedistribution = true;
            root.AddComponent<ExcavatorVisual>();
            const string folder = "Assets/External/Resources/WorldArt/Construction";
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            PrefabUtility.SaveAsPrefabAsset(root, folder + "/Excavator.prefab");
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log("WORLD_ART_EXCAVATOR_READY");
        }
    }
}
