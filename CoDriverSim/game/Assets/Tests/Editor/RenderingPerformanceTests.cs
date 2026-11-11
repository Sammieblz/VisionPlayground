using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CoDriver.Tests
{
    public sealed class RenderingPerformanceTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void FacadeDetailRangeIsIndependentOfQualityLodBias(bool batched)
        {
            float originalBias = QualitySettings.lodBias;
            float previousRange = 0;
            int previousVertices = 0;
            try
            {
                foreach (float bias in new[] { 1f, 2f })
                {
                    QualitySettings.lodBias = bias;
                    var root = new GameObject("Facade distance fixture");
                    try
                    {
                        if (batched)
                            root.AddComponent<Town>();
                        var ring = new[]
                        {
                            new Vector3(5, 0, 5),
                            new Vector3(15, 0, 5),
                            new Vector3(15, 0, 15),
                            new Vector3(5, 0, 15),
                            new Vector3(5, 0, 5),
                        };
                        var building = new MapBuilding
                        {
                            sourceId = "rendering-distance-fixture",
                            usage = "residential",
                            height = 6.4f,
                            rings = new[] { new MapRing { points = ring } },
                            roof = new MapMesh
                            {
                                vertices = ring.Select(p => p + Vector3.up * 6.4f).ToArray(),
                            },
                        };
                        Assert.IsTrue(FacadeArchitecture.Add(root.transform, building));
                        if (batched)
                            root.GetComponent<StaticFacadeBatches>().Bake();
                        var groups = root.GetComponentsInChildren<LODGroup>();
                        Assert.AreEqual(1, groups.Length, "The fixture fits within one 32 m cell");
                        var group = groups[0];
                        var lods = group.GetLODs();
                        Assert.AreEqual(1, lods.Length);
                        Assert.AreEqual(.05f, lods[0].screenRelativeTransitionHeight);
                        Assert.IsNotEmpty(lods[0].renderers);
                        foreach (var renderer in lods[0].renderers)
                        {
                            Assert.AreEqual(
                                FacadeArchitecture.DetailLayer,
                                renderer.gameObject.layer
                            );
                            Assert.AreEqual(ShadowCastingMode.Off, renderer.shadowCastingMode);
                        }
                        Assert.IsEmpty(root.GetComponentsInChildren<Collider>());
                        int vertices = root.GetComponentsInChildren<MeshFilter>()
                            .Sum(filter => filter.sharedMesh.vertexCount);
                        Assert.Greater(vertices, 0);

                        // Perspective LOD transition distance, using the actual generated
                        // group's size/scale/threshold and the active quality bias.
                        // This checks configuration; it is not a GPU performance measurement.
                        float range = TransitionDistance(group, 70);
                        Assert.That(range, Is.InRange(142f, 144f));
                        Assert.Greater(range, 100, "Nearby facade details must remain available");
                        Assert.Less(range, 200, "Far facade details must be culled");
                        float sensorFov = 2 * Mathf.Atan(604f / 910) * Mathf.Rad2Deg;
                        Assert.That(TransitionDistance(group, sensorFov), Is.InRange(150f, 152f));
                        if (previousRange > 0)
                        {
                            Assert.AreEqual(previousRange, range, .001f);
                            Assert.AreEqual(
                                previousVertices,
                                vertices,
                                "Quality must not remove geometry"
                            );
                        }
                        previousRange = range;
                        previousVertices = vertices;
                    }
                    finally
                    {
                        Object.DestroyImmediate(root);
                    }
                }
            }
            finally
            {
                QualitySettings.lodBias = originalBias;
            }
        }

        static float TransitionDistance(LODGroup group, float fov)
        {
            var scale = group.transform.lossyScale;
            return group.size
                * Mathf.Max(scale.x, scale.y, scale.z)
                * QualitySettings.lodBias
                / (
                    2
                    * Mathf.Tan(fov * Mathf.Deg2Rad / 2)
                    * group.GetLODs()[0].screenRelativeTransitionHeight
                );
        }

        [TestCase("PC", true)]
        [TestCase("Mobile", false)]
        public void MirrorRendererHasNoFeaturesAndLeavesDefaultPipelinePolicyUnchanged(
            string quality,
            bool defaultCopies
        )
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(
                "Assets/Settings/" + quality + "_RPAsset.asset"
            );
            Assert.IsNotNull(asset);
            var settings = new SerializedObject(asset);
            Assert.AreEqual(0, settings.FindProperty("m_DefaultRendererIndex").intValue);
            Assert.AreEqual(
                defaultCopies,
                settings.FindProperty("m_RequireDepthTexture").boolValue
            );
            Assert.AreEqual(
                defaultCopies,
                settings.FindProperty("m_RequireOpaqueTexture").boolValue
            );
            var renderers = settings.FindProperty("m_RendererDataList");
            Assert.AreEqual(2, renderers.arraySize);
            var main = (ScriptableRendererData)
                renderers.GetArrayElementAtIndex(0).objectReferenceValue;
            var mirror = (ScriptableRendererData)
                renderers.GetArrayElementAtIndex(1).objectReferenceValue;
            Assert.AreSame(
                AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(
                    "Assets/Settings/" + quality + "_Renderer.asset"
                ),
                main
            );
            Assert.AreSame(
                AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(
                    "Assets/Settings/Mobile_Renderer.asset"
                ),
                mirror
            );
            Assert.IsEmpty(
                mirror.rendererFeatures,
                "Mirror renderer must not request SSAO/depth normals"
            );
            if (quality == "PC")
                Assert.IsTrue(
                    main.rendererFeatures.Any(feature =>
                        feature
                        && feature.isActive
                        && feature.GetType().Name == "ScreenSpaceAmbientOcclusion"
                    ),
                    "Main and forward sensor must retain the existing PC SSAO feature"
                );
        }

        [Test]
        public void CockpitMirrorsOptOutOfCopiesWhileForwardSensorAndDefaultCameraStillInherit()
        {
            var mainObject = new GameObject("Default main camera policy");
            var carObject = new GameObject("Rendering camera fixture");
            SensorRig rig = null;
            AudioClip horn = null;
            try
            {
                var main = mainObject.AddComponent<Camera>().GetUniversalAdditionalCameraData();
                string mainBefore = EditorJsonUtility.ToJson(main);
                var car = carObject.AddComponent<Vehicle>();
                car.Profile = VehicleProfile.GlsSimulation();
                car.Initialize();
                horn = car.Horn.clip;
                rig = carObject.AddComponent<SensorRig>();
                rig.Initialize(car, "rendering-policy-fixture", "unused");
                var sensor = rig.Sensor.GetUniversalAdditionalCameraData();
                var mirrors = carObject
                    .GetComponentsInChildren<Camera>()
                    .Where(camera => camera.name == "Limited mirror camera")
                    .ToArray();
                Assert.AreEqual(3, mirrors.Length, "Exercise the actual prepared GLS cockpit");
                foreach (var camera in mirrors)
                {
                    var data = camera.GetUniversalAdditionalCameraData();
                    Assert.AreEqual(1, RendererIndex(data));
                    Assert.AreEqual(CameraOverrideOption.Off, data.requiresColorOption);
                    Assert.AreEqual(CameraOverrideOption.Off, data.requiresDepthOption);
                    Assert.IsFalse(data.renderShadows);
                    Assert.IsFalse(data.renderPostProcessing);
                    Assert.IsFalse(camera.enabled);
                    Assert.AreEqual(256, camera.targetTexture.width);
                }
                foreach (var inherited in new[] { main, sensor })
                {
                    Assert.AreEqual(-1, RendererIndex(inherited));
                    Assert.AreEqual(
                        CameraOverrideOption.UsePipelineSettings,
                        inherited.requiresColorOption
                    );
                    Assert.AreEqual(
                        CameraOverrideOption.UsePipelineSettings,
                        inherited.requiresDepthOption
                    );
                }
                Assert.AreEqual(mainBefore, EditorJsonUtility.ToJson(main));
                Assert.AreEqual(964, rig.Sensor.targetTexture.width);
                Assert.AreEqual(604, rig.Sensor.targetTexture.height);
            }
            finally
            {
                // SensorRig normally releases its target via deferred Destroy in play
                // mode. Dispose it first here so EditMode teardown has no deferred work.
                if (rig && rig.Sensor && rig.Sensor.targetTexture)
                {
                    var target = rig.Sensor.targetTexture;
                    rig.Sensor.targetTexture = null;
                    target.Release();
                    Object.DestroyImmediate(target);
                }
                Object.DestroyImmediate(carObject);
                Object.DestroyImmediate(mainObject);
                if (horn)
                    Object.DestroyImmediate(horn);
            }
        }

        static int RendererIndex(UniversalAdditionalCameraData camera) =>
            new SerializedObject(camera).FindProperty("m_RendererIndex").intValue;
    }
}
