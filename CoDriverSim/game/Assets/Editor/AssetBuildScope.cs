using System;
using System.IO;
using UnityEditor;

namespace CoDriver.Editor
{
    // Unity includes every Resources asset, even if no scene references it.
    // Move optional local assets outside Resources for the duration of a public
    // build, preserving their GUIDs and restoring them even when the build fails.
    public sealed class AssetBuildScope : IDisposable
    {
        const string Source = "Assets/External/Resources/WorldArt/Local";
        const string Excluded = "Assets/ExternalLocalExcluded/Mercedes";
        bool moved;
        public AssetBuildScope(bool publicAssets)
        {
            if (!publicAssets || !AssetDatabase.IsValidFolder(Source)) return;
            if (AssetDatabase.IsValidFolder(Excluded))
                throw new InvalidOperationException("A previous build exclusion needs restoration: " + Excluded);
            Directory.CreateDirectory("Assets/ExternalLocalExcluded");
            AssetDatabase.Refresh();
            string error = AssetDatabase.MoveAsset(Source, Excluded);
            if (error.Length > 0) throw new IOException(error);
            moved = true;
            AssetDatabase.SaveAssets();
        }
        public void Dispose()
        {
            if (!moved) return;
            string error = AssetDatabase.MoveAsset(Excluded, Source);
            if (error.Length > 0) throw new IOException("Restore local assets: " + error);
            AssetDatabase.SaveAssets();
        }
    }
}
