using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoDriver.Tests
{
    public sealed class PreparedTextureTests
    {
        [Test]
        public void PackedMapsKeepLinearChannelsAfterCompressionSaveReloadAndReplacement()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore(
                    "Requires a real graphics device for serialized GPU texture readback."
                );
            // Importers live in Unity's predefined editor assembly, which cannot
            // be referenced by an asmdef. Exercise its public preparation entry.
            var type = AppDomain
                .CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("CoDriver.Editor.WorldMaterialMaps"))
                .First(t => t != null);
            var save = type.GetMethod("SavePacked");
            string path = "Assets/Tests/packed-map-" + Guid.NewGuid().ToString("N") + ".asset";
            var prior = RenderTexture.active;
            var target = RenderTexture.GetTemporary(
                32,
                32,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear
            );
            var readback = new Texture2D(32, 32, TextureFormat.RGBA32, false, true);
            try
            {
                string guid = null;
                for (int pass = 0; pass < 2; pass++)
                {
                    var pixels = new Texture2D(32, 32, TextureFormat.RGBA32, true, true);
                    var values = new Color[32 * 32];
                    for (int y = 0; y < 32; y++)
                    for (int x = 0; x < 32; x++)
                        values[y * 32 + x] = new Color(x / 40f, y / 40f, pass * .6f, .2f + x / 64f);
                    pixels.SetPixels(values);
                    pixels.Apply();
                    var saved = (Texture2D)save.Invoke(null, new object[] { pixels, path });
                    AssetDatabase.SaveAssets();
                    guid ??= AssetDatabase.AssetPathToGUID(path);
                    Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(path));
                    Resources.UnloadAsset(saved);
                    AssetDatabase.ImportAsset(
                        path,
                        ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate
                    );
                    var loaded = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    Assert.AreEqual(TextureFormat.BC7, loaded.format);
                    Assert.IsFalse(loaded.isReadable, "Runtime must not retain a CPU pixel copy.");
                    Assert.AreEqual(6, loaded.mipmapCount);
                    Graphics.Blit(loaded, target);
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, 32, 32), 0, 0);
                    readback.Apply();
                    foreach (int y in new[] { 5, 16, 26 })
                    foreach (int x in new[] { 5, 16, 26 })
                    {
                        var actual = readback.GetPixel(x, y);
                        var expected = values[y * 32 + x];
                        for (int channel = 0; channel < 4; channel++)
                            Assert.AreEqual(
                                expected[channel],
                                actual[channel],
                                .035f,
                                $"pass={pass}, pixel={x},{y}, channel={channel}"
                            );
                    }
                }
            }
            finally
            {
                RenderTexture.active = prior;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(readback);
                AssetDatabase.DeleteAsset(path);
            }
        }
    }
}
