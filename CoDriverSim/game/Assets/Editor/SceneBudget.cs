using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoDriver.Editor
{
    // Offline rendering inventory. Frustum/LOD estimates are not measured GPU timings.
    public static class SceneBudget
    {
        [Serializable]
        class Group
        {
            public string name;
            public int renderers,
                draws;
            public long triangles;
        }

        [Serializable]
        class Report
        {
            public string scope =
                "Default Davis forward view, approximate frustum/LOD inventory; no occlusion or shadow passes";
            public Group[] groups;
        }

        public static void Capture()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var map = MapDefinition.Load("davis-downtown");
            var town = new GameObject("Scene inventory").AddComponent<Town>();
            town.Map = map;
            town.Build(null);
            var camera = new GameObject("Inventory camera").AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(
                map.Spawn + Vector3.up * 1.4f,
                map.SpawnRotation
            );
            camera.fieldOfView = 70;
            camera.aspect = 16f / 9;
            camera.farClipPlane = 900;
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            var selected = new HashSet<Renderer>();
            foreach (var group in town.GetComponentsInChildren<LODGroup>())
            {
                float distance = Mathf.Max(
                    .1f,
                    Vector3.Distance(
                        camera.transform.position,
                        group.transform.TransformPoint(group.localReferencePoint)
                    )
                );
                var scale = group.transform.lossyScale;
                float height =
                    group.size
                    * Mathf.Max(scale.x, scale.y, scale.z)
                    * QualitySettings.lodBias
                    / (2 * distance * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad / 2));
                foreach (var lod in group.GetLODs())
                    if (height >= lod.screenRelativeTransitionHeight)
                    {
                        foreach (var renderer in lod.renderers)
                            selected.Add(renderer);
                        break;
                    }
            }
            var totals = new Dictionary<string, Group>();
            foreach (var renderer in town.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || !GeometryUtility.TestPlanesAABB(planes, renderer.bounds))
                    continue;
                if (renderer.GetComponentInParent<LODGroup>() && !selected.Contains(renderer))
                    continue;
                var root = renderer.transform;
                while (root.parent && root.parent != town.transform)
                    root = root.parent;
                var lodGroup = renderer.GetComponentInParent<LODGroup>();
                string category =
                    renderer.GetComponent<TextMesh>() ? "Text labels"
                    : renderer.GetComponentInParent<Walker>() ? "Walkers"
                    : renderer.GetComponentInParent<TrafficActor>() ? "Road vehicles"
                    : renderer.GetComponentInParent<RailVehicle>() ? "Rail"
                    : renderer.GetComponentInParent<Signal>() ? "Signals"
                    : lodGroup && lodGroup.name.StartsWith("Architecture") ? "Facades"
                    : root.name.Contains("Oak") || root.name.Contains("Pine") ? "Trees"
                    : root.name;
                if (!totals.TryGetValue(category, out var total))
                    totals[category] = total = new Group { name = category };
                total.renderers++;
                total.draws += renderer.sharedMaterials.Length;
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh =
                    renderer is SkinnedMeshRenderer skin ? skin.sharedMesh
                    : filter ? filter.sharedMesh
                    : null;
                if (mesh)
                    for (int i = 0; i < mesh.subMeshCount; i++)
                        total.triangles += (long)mesh.GetIndexCount(i) / 3;
            }
            string path = Path.GetFullPath(
                "../outputs/qualification/realism/scene-render-inventory.json"
            );
            File.WriteAllText(
                path,
                JsonUtility.ToJson(
                    new Report { groups = totals.Values.OrderByDescending(g => g.draws).ToArray() },
                    true
                )
            );
            Debug.Log("SCENE_RENDER_INVENTORY " + path);
            UnityEngine.Object.DestroyImmediate(camera.gameObject);
            UnityEngine.Object.DestroyImmediate(town.gameObject);
        }
    }
}
