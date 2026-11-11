using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoDriver.Editor
{
    // glTF uses metallic=B, roughness=G. URP Lit uses metallic=R,
    // smoothness=A. Conversion is an offline preparation step only.
    public static class WorldMaterialMaps
    {
        public static Texture2D SavePacked(Texture2D pixels, string path)
        {
            // Windows/D3D11 supports BC7. Keep the linear channel data and mip
            // chain, but discard the CPU copy after offline compression.
            EditorUtility.CompressTexture(
                pixels,
                TextureFormat.BC7,
                TextureCompressionQuality.Normal
            );
            pixels.Apply(false, true);
            var previous = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (previous)
            {
                EditorUtility.CopySerialized(pixels, previous);
                UnityEngine.Object.DestroyImmediate(pixels);
                EditorUtility.SetDirty(previous);
                return previous;
            }
            AssetDatabase.CreateAsset(pixels, path);
            return pixels;
        }

        public static void Copy(Material source, Material target, string directory, string name)
        {
            Texture Map(string property) =>
                source.HasProperty(property) ? source.GetTexture(property) : null;
            float Factor(string property, float fallback) =>
                source.HasProperty(property) ? source.GetFloat(property) : fallback;
            var normal = Map("normalTexture");
            if (normal)
            {
                var pixels = Read(normal);
                string path = directory + "/" + name + "-normal.png";
                File.WriteAllBytes(path, pixels.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(pixels);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.NormalMap;
                importer.sRGBTexture = false;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
                target.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
                target.SetFloat("_BumpScale", Factor("normalTexture_scale", 1));
                target.EnableKeyword("_NORMALMAP");
            }
            var packed = Map("metallicRoughnessTexture");
            if (packed)
            {
                var pixels = Read(packed);
                var colors = pixels.GetPixels();
                float metal = Factor("metallicFactor", 1),
                    rough = Factor("roughnessFactor", 1);
                for (int i = 0; i < colors.Length; i++)
                    colors[i] = new Color(colors[i].b * metal, 0, 0, 1 - colors[i].g * rough);
                pixels.SetPixels(colors);
                pixels.Apply(true, false);
                string path = directory + "/" + name + "-metal-smooth.asset";
                target.SetTexture("_MetallicGlossMap", SavePacked(pixels, path));
                target.SetFloat("_Smoothness", 1);
                target.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            // URP samples occlusion from G; glTF supplies R. These are often the
            // same ORM texture, so using the imported image directly is wrong.
            var ao = Map("occlusionTexture");
            if (ao)
            {
                var pixels = Read(ao);
                var colors = pixels.GetPixels();
                for (int i = 0; i < colors.Length; i++)
                    colors[i] = new Color(1, colors[i].r, 1, 1);
                pixels.SetPixels(colors);
                pixels.Apply(true, false);
                string path = directory + "/" + name + "-occlusion.asset";
                target.SetTexture("_OcclusionMap", SavePacked(pixels, path));
                target.SetFloat("_OcclusionStrength", Factor("occlusionTexture_strength", 1));
                target.EnableKeyword("_OCCLUSIONMAP");
            }
            if (source.HasProperty("emissiveFactor"))
            {
                var color = source.GetColor("emissiveFactor");
                target.SetColor("_EmissionColor", color);
                target.SetTexture("_EmissionMap", Map("emissiveTexture"));
                if (color.maxColorComponent > 0)
                    target.EnableKeyword("_EMISSION");
            }
        }

        static Texture2D Read(Texture source)
        {
            var pixels = new Texture2D(
                source.width,
                source.height,
                TextureFormat.RGBA32,
                true,
                true
            );
            pixels.wrapMode = source.wrapMode;
            if (source is Texture2D readable && readable.isReadable)
            {
                pixels.SetPixels(readable.GetPixels());
                pixels.Apply();
                return pixels;
            }
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException(
                    "Preparing glTF textures requires -force-d3d11, without -nographics"
                );
            var prior = RenderTexture.active;
            var temporary = RenderTexture.GetTemporary(
                source.width,
                source.height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear
            );
            try
            {
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                pixels.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                pixels.Apply();
                return pixels;
            }
            finally
            {
                RenderTexture.active = prior;
                RenderTexture.ReleaseTemporary(temporary);
            }
        }
    }
}
