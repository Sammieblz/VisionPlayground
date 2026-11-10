using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoDriver
{
    // Architectural presentation follows the source footprint. The shell remains
    // the collision authority; facade details never create invisible street obstacles.
    public static class FacadeArchitecture
    {
        public const int DetailLayer = 17;
        static readonly Dictionary<(Material, int), Material> finishes =
            new Dictionary<(Material, int), Material>();

        static Material Finish(Material original, int style)
        {
            if (!original.name.ToLowerInvariant().Contains("plaster"))
                return original;
            if (finishes.TryGetValue((original, style), out var cached))
                return cached;
            var surface =
                style == 0
                    ? Art.Brick
                    : Resources.Load<Material>("Realism/Prepared/Plaster") ?? Art.Concrete;
            var material = new Material(surface);
            material.name = "Architectural finish " + style;
            material.color =
                style == 0 ? new Color(.95f, .87f, .77f)
                : style == 1 ? new Color(.93f, .91f, .84f)
                : new Color(.65f, .74f, .72f);
            material.SetFloat("_Smoothness", .12f);
            material.mainTextureScale = Vector2.one * .7f;
            material.enableInstancing = true;
            finishes[(original, style)] = material;
            return material;
        }

        public static bool Add(
            Transform parent,
            MapBuilding building,
            MappedStorefront entrance = null
        )
        {
            var window = Resources.Load<GameObject>("WorldArt/Facades/Window");
            if (!window)
                return false;
            var root = new GameObject("Architecture " + building.sourceId);
            root.transform.SetParent(parent, false);
            var batches = new Dictionary<Material, List<CombineInstance>>();
            int floors = Mathf.Max(1, Mathf.RoundToInt(building.height / 3.2f));
            float floorHeight = Mathf.Min(3.5f, building.height / floors);
            uint identity = 2166136261;
            foreach (char c in building.sourceId)
                identity = (identity ^ c) * 16777619;
            int style = (int)(identity % 3);
            var ring = building.rings[0].points;
            // Source rings can have either winding. Choose the outward normal
            // from signed area, rather than relying on an arbitrary import rotation.
            float area = 0;
            for (int i = 1; i < ring.Length; i++)
                area += ring[i - 1].x * ring[i].z - ring[i].x * ring[i - 1].z;
            for (int i = 1; i < ring.Length; i++)
            {
                Vector3 start = ring[i - 1],
                    edge = ring[i] - start;
                float length = edge.magnitude;
                if (length < 2.4f || length > 120)
                    continue;
                var along = edge.normalized;
                var outward = Vector3.Cross(Vector3.up, along) * (area > 0 ? 1 : -1);
                var rotation = Quaternion.LookRotation(outward);
                int bays = Mathf.Max(1, Mathf.RoundToInt(length / 3.2f));
                float width = length / bays;
                for (int bay = 0; bay < bays; bay++)
                {
                    Vector3 basePoint = start + along * ((bay + .5f) * width) + outward * .26f;
                    for (int floor = 0; floor < floors; floor++)
                    {
                        var origin = basePoint + Vector3.up * (floor * floorHeight);
                        if (
                            floor == 0
                            && entrance != null
                            && i == entrance.Edge
                            && bay == entrance.Bay
                        )
                            continue; // The operable storefront replaces this bay, including its aperture.
                        string module =
                            floor == 0 && bay == bays / 2 ? "DoorGlass"
                            : style == 0 ? "Window"
                            : style == 1 ? "WindowPanel"
                            : "WindowSimple";
                        AddModule(
                            module,
                            origin,
                            rotation,
                            new Vector3(width / 3, floorHeight / 3, 1),
                            batches,
                            style
                        );
                        if (floor > 0 && style == 0)
                            AddModule(
                                "Cornice",
                                origin,
                                rotation,
                                new Vector3(width / 3, 1, 1),
                                batches,
                                style
                            );
                    }
                    if (entrance == null || i != entrance.Edge || bay != entrance.Bay)
                        AddModule(
                            "Plinth",
                            basePoint,
                            rotation,
                            new Vector3(width / 3, 1, 1),
                            batches,
                            style
                        );
                    AddModule(
                        "Crown",
                        basePoint + Vector3.up * (floors * floorHeight - .15f),
                        rotation,
                        new Vector3(width / 3, style == 0 ? .6f : .25f, 1),
                        batches,
                        style
                    );
                }
            }
            var renderers = new List<Renderer>();
            foreach (var batch in batches)
            {
                var mesh = new Mesh
                {
                    indexFormat = IndexFormat.UInt32,
                    name = "Facade " + building.sourceId,
                };
                mesh.CombineMeshes(batch.Value.ToArray(), true, true);
                RuntimeResources.Register(root, mesh);
                var part = new GameObject(batch.Key.name);
                part.layer = DetailLayer;
                part.transform.SetParent(root.transform, false);
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = part.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = batch.Key;
                // The footprint shell casts the building shadow. Repeating every
                // window's tiny bevels in cascades overwhelmed the target GPU.
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderers.Add(renderer);
            }
            var lod = root.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(.05f, renderers.ToArray()) });
            lod.RecalculateBounds();
            // A fixed reference size gives approximately 140 m detail range at
            // the driving FOV, independent of the full building footprint size.
            lod.size = 10;
            return true;
        }

        static void AddModule(
            string name,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            Dictionary<Material, List<CombineInstance>> batches,
            int style
        )
        {
            var source = Resources.Load<GameObject>("WorldArt/Facades/" + name);
            if (!source)
                return;
            var transform = Matrix4x4.TRS(position, rotation, scale);
            foreach (var filter in source.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                var materials = filter.GetComponent<Renderer>().sharedMaterials;
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    var material = Finish(materials[i], style);
                    if (!batches.TryGetValue(material, out var list))
                        batches[material] = list = new List<CombineInstance>();
                    list.Add(
                        new CombineInstance
                        {
                            mesh = mesh,
                            subMeshIndex = i,
                            transform =
                                transform
                                * source.transform.worldToLocalMatrix
                                * filter.transform.localToWorldMatrix,
                        }
                    );
                }
            }
        }
    }
}
