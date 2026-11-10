using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoDriver.Editor
{
    public static class GameBuild
    {
        public static void BuildAndCapture()
        {
            Build();
            PresentationCaptures.Capture();
        }

        static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return System
                    .BitConverter.ToString(sha.ComputeHash(bytes))
                    .Replace("-", "")
                    .ToLowerInvariant();
        }

        public static void Build()
        {
            bool publicAssets =
                System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-public-assets")
                >= 0;
            using (new AssetBuildScope(publicAssets))
                BuildInternal(publicAssets);
        }

        static string AssetFingerprint()
        {
            // Hash the actual imported art, not just downloader URLs. Excluded
            // private assets are outside this Resources tree during public builds.
            using (var sha = SHA256.Create())
            {
                var entries = Directory
                    .GetFiles("Assets/External/Resources", "*", SearchOption.AllDirectories)
                    .OrderBy(p => p)
                    .Select(p =>
                    {
                        using (var stream = File.OpenRead(p))
                            return p.Replace('\\', '/')
                                + ":"
                                + System.BitConverter.ToString(sha.ComputeHash(stream));
                    })
                    .ToArray();
                return Hash(Encoding.UTF8.GetBytes(string.Join("\n", entries)));
            }
        }

        static void BuildInternal(bool publicAssets)
        {
            ActorImporter.Prepare();
            WorldArtImporter.Prepare();
            RealismImporter.Prepare();
            CockpitImporter.Prepare();
            Directory.CreateDirectory("Assets/Resources");
            string source = string.Join(
                "\n",
                Directory
                    .GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories)
                    .Concat(
                        Directory.GetFiles("Assets/Editor", "*.cs", SearchOption.AllDirectories)
                    )
                    .OrderBy(p => p)
                    .Select(p => p.Replace('\\', '/') + ":" + Hash(File.ReadAllBytes(p)))
            );
            File.WriteAllText(
                "Assets/Resources/BuildIdentity.json",
                JsonUtility.ToJson(
                    new BuildIdentity
                    {
                        created_utc = System.DateTime.UtcNow.ToString("O"),
                        code_sha256 = Hash(Encoding.UTF8.GetBytes(source)),
                        map_sha256 = Hash(
                            File.ReadAllBytes("Assets/Resources/Maps/davis-downtown.json")
                        ),
                        routing_sha256 = Hash(
                            File.ReadAllBytes("Assets/Resources/Maps/davis-turn-limits.json")
                        ),
                        unity = Application.unityVersion,
                        physics_step = .01f,
                        asset_profile = publicAssets ? "redistributable" : "local-prototype",
                        scenery_sha256 = Hash(
                            Encoding.UTF8.GetBytes(
                                string.Join(
                                    "\n",
                                    new[]
                                    {
                                        "Assets/Resources/Maps/davis-scenery.json",
                                        "Assets/Resources/Maps/davis-building-art.json",
                                    }.Select(path => path + ":" + Hash(File.ReadAllBytes(path)))
                                )
                            )
                        ),
                        art_sha256 = AssetFingerprint(),
                    },
                    true
                )
            );
            AssetDatabase.ImportAsset("Assets/Resources/BuildIdentity.json");
            const string unlitPath = "Assets/Resources/RuntimeUnlit.mat";
            if (!AssetDatabase.LoadAssetAtPath<Material>(unlitPath))
                AssetDatabase.CreateAsset(
                    new Material(Shader.Find("Universal Render Pipeline/Unlit")),
                    unlitPath
                );
            var materialPath = "Assets/Resources/RuntimeLit.mat";
            if (!AssetDatabase.LoadAssetAtPath<Material>(materialPath))
            {
                var lit = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                lit.EnableKeyword("_EMISSION");
                lit.enableInstancing = true;
                AssetDatabase.CreateAsset(lit, materialPath);
            }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Town.unity");
            PlayerSettings.companyName = "VisionPlayground";
            PlayerSettings.productName = "CoDriverSim";
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.runInBackground = true;
            PlayerSettings.enableFrameTimingStats = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(
                BuildTarget.StandaloneWindows64,
                new[] { GraphicsDeviceType.Direct3D11 }
            );
            var settings = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]
            );
            settings.FindProperty("activeInputHandler").intValue = 1;
            settings.ApplyModifiedProperties();
            var rp = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(
                "Assets/Settings/PC_RPAsset.asset"
            );
            GraphicsSettings.defaultRenderPipeline = rp;
            QualitySettings.renderPipeline = rp;
            QualitySettings.vSyncCount = 0;
            var output = Path.GetFullPath("../outputs/game/CoDriverSim.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(
                new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/Scenes/Town.unity" },
                    locationPathName = output,
                    target = BuildTarget.StandaloneWindows64,
                    // The built-in development profiler opens a network listener. The
                    // application only needs its explicit localhost sensor connections.
                    options = BuildOptions.DetailedBuildReport,
                }
            );
            if (report.summary.result != BuildResult.Succeeded)
                throw new System.Exception(report.summary.result.ToString());
            var packedSources = report
                .packedAssets.SelectMany(p => p.contents)
                .Select(p => p.sourceAssetPath.Replace('\\', '/'))
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .OrderBy(p => p)
                .ToArray();
            var localAssets = packedSources
                .Where(p =>
                    p.StartsWith("Assets/External/Resources/WorldArt/Local/")
                    || p.StartsWith("Assets/ExternalLocalExcluded/")
                )
                .ToArray();
            File.WriteAllText(
                Path.Combine(Path.GetDirectoryName(output), "BuildAssetAudit.json"),
                JsonUtility.ToJson(
                    new AssetAudit { packed_sources = packedSources, local_assets = localAssets },
                    true
                )
            );
            if (publicAssets && (localAssets.Length != 0 || packedSources.Length == 0))
                throw new System.InvalidOperationException(
                    "Public build lacks verified local-asset exclusion"
                );
            Debug.Log("CODRIVER_BUILD_OK " + output);
            File.WriteAllText(
                Path.Combine(Path.GetDirectoryName(output), "BuildIdentity.json"),
                File.ReadAllText("Assets/Resources/BuildIdentity.json")
            );
        }

        [System.Serializable]
        sealed class AssetAudit
        {
            public string[] packed_sources,
                local_assets;
        }
    }
}
