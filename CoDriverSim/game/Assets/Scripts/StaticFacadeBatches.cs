using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoDriver
{
    // Preserve the architectural meshes/materials, but submit nearby immutable
    // modules together. World collision and activity doors are not collected.
    public sealed class StaticFacadeBatches : MonoBehaviour
    {
        const float CellSize = 32;
        readonly Dictionary<(Material, int, int), List<CombineInstance>> pending =
            new Dictionary<(Material, int, int), List<CombineInstance>>();

        public void Add(Transform source, Material material, IEnumerable<CombineInstance> modules)
        {
            var toLocal = transform.worldToLocalMatrix * source.localToWorldMatrix;
            foreach (var module in modules)
            {
                var part = module;
                part.transform = toLocal * module.transform;
                var centre = part.transform.MultiplyPoint3x4(part.mesh.bounds.center);
                var key = (
                    material,
                    Mathf.FloorToInt(centre.x / CellSize),
                    Mathf.FloorToInt(centre.z / CellSize)
                );
                if (!pending.TryGetValue(key, out var list))
                    pending[key] = list = new List<CombineInstance>();
                list.Add(part);
            }
        }

        public void Bake()
        {
            var cells = new Dictionary<(int, int), (GameObject, List<Renderer>)>();
            foreach (var batch in pending)
            {
                var key = (batch.Key.Item2, batch.Key.Item3);
                if (!cells.TryGetValue(key, out var cell))
                {
                    var root = new GameObject("Architecture cell " + key.Item1 + "," + key.Item2);
                    root.transform.SetParent(transform, false);
                    cells[key] = cell = (root, new List<Renderer>());
                }
                var part = new GameObject(batch.Key.Item1.name);
                part.transform.SetParent(cell.Item1.transform, false);
                part.layer = FacadeArchitecture.DetailLayer;
                var mesh = new Mesh
                {
                    name = part.name + " spatial batch",
                    indexFormat = IndexFormat.UInt32,
                };
                mesh.CombineMeshes(batch.Value.ToArray(), true, true, false);
                RuntimeResources.Register(gameObject, mesh);
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = part.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = batch.Key.Item1;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                cell.Item2.Add(renderer);
            }
            foreach (var cell in cells.Values)
            {
                var lod = cell.Item1.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(.05f, cell.Item2.ToArray()) });
                lod.RecalculateBounds();
                // Same approximately 140 m detail range as the individual
                // buildings; small cells retain useful per-street culling.
                // Match that range under the active quality setting's LOD bias.
                lod.size = 10 / Mathf.Max(.01f, QualitySettings.lodBias);
            }
            pending.Clear();
        }
    }
}
