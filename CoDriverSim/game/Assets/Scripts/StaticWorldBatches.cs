using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoDriver
{
    // Combine immutable presentation meshes by material and spatial cell. Original
    // colliders and source IDs remain in place; moving/animated geometry is excluded.
    public sealed class StaticWorldBatches : MonoBehaviour
    {
        readonly List<Mesh> owned = new List<Mesh>();

        public void Build(IEnumerable<GameObject> objects)
        {
            var groups = new Dictionary<(Material, int, int, int), List<CombineInstance>>();
            foreach (var go in objects)
            {
                var filter = go.GetComponent<MeshFilter>();
                var renderer = go.GetComponent<MeshRenderer>();
                if (
                    !renderer
                    || go.GetComponentInParent<LODGroup>()
                    || !renderer.enabled
                    || !filter
                    || !filter.sharedMesh
                    || !filter.sharedMesh.isReadable
                )
                    continue;
                var mesh = filter.sharedMesh;
                var materials = renderer.sharedMaterials;
                if (materials.Length < mesh.subMeshCount)
                    continue;
                Vector3 position = renderer.bounds.center;
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    if (!materials[i])
                        continue;
                    var key = (
                        materials[i],
                        Mathf.FloorToInt(position.x / 80),
                        Mathf.FloorToInt(position.z / 80),
                        go.layer
                    );
                    if (!groups.TryGetValue(key, out var list))
                    {
                        list = new List<CombineInstance>();
                        groups[key] = list;
                    }
                    list.Add(
                        new CombineInstance
                        {
                            mesh = mesh,
                            subMeshIndex = i,
                            transform = go.transform.localToWorldMatrix,
                        }
                    );
                }
                renderer.enabled = false;
            }
            foreach (var group in groups)
            {
                var mesh = new Mesh
                {
                    name = "Spatial world batch",
                    indexFormat = IndexFormat.UInt32,
                };
                mesh.CombineMeshes(group.Value.ToArray(), true, true, false);
                owned.Add(mesh);
                var go = new GameObject(
                    "Static geometry "
                        + group.Key.Item2
                        + ","
                        + group.Key.Item3
                        + " "
                        + group.Key.Item1.name
                );
                go.transform.SetParent(transform, false);
                go.layer = group.Key.Item4;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = group.Key.Item1;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
        }

        void OnDestroy()
        {
            foreach (var mesh in owned)
                Destroy(mesh);
        }
    }
}
