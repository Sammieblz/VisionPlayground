using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace CoDriver.Editor
{
    public static class BuildingArtImporter
    {
        public static void Prepare()
        {
            const string sourcePath =
                "Assets/External/Resources/WorldArt/BuildingSource/Lowrise.glb";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (!source)
                return;
            var instance = Object.Instantiate(source);
            instance.name = "Prepared low-rise commercial building";
            WorldArtImporter.PrepareMaterials(
                instance,
                "Assets/External/Resources/WorldArt/BuildingMaterials"
            );
            var renderers = instance.GetComponentsInChildren<Renderer>();
            var near = renderers.Where(r => r.name.Contains("LOD0")).ToArray();
            var far = renderers.Where(r => r.name.Contains("LOD1")).ToArray();
            if (near.Length == 0 || far.Length == 0)
                throw new System.InvalidOperationException(
                    "Prepared building needs both detail levels"
                );
            var lod = instance.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(.06f, near), new LOD(.008f, far) });
            lod.RecalculateBounds();
            var definition = instance.AddComponent<AssetDefinition>();
            definition.assetId = "shalmon-prop-lowrise-retextured";
            definition.sourceUrl = "https://blendswap.com/blend/28283";
            definition.sourceSha256 =
                "58ebf9a700fe2b89f72983f7ba9c96fe8a7ec7f85e279cacf3ec2529601f7efe";
            using (var sha = SHA256.Create())
                definition.preparedSha256 = System
                    .BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(sourcePath)))
                    .Replace("-", "")
                    .ToLowerInvariant();
            definition.license = "CC0-1.0; original embedded texture images replaced";
            definition.publicRedistribution = true;
            const string folder = "Assets/External/Resources/WorldArt/Buildings";
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            PrefabUtility.SaveAsPrefabAsset(instance, folder + "/Lowrise.prefab");
            Object.DestroyImmediate(instance);
            AssetDatabase.SaveAssets();
            Debug.Log("WORLD_ART_LOWRISE_READY");
        }
    }
}
