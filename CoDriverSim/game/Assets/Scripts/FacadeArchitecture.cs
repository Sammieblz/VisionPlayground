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
        static readonly Dictionary<int, Material> walls = new Dictionary<int, Material>();
        static Mesh cube;

        public static int Style(MapBuilding building)
        {
            uint identity = 2166136261;
            foreach (char c in building.sourceId)
                identity = (identity ^ c) * 16777619;
            return (int)(identity % 6);
        }

        public static Material Wall(int style)
        {
            if (walls.TryGetValue(style, out var material) && material)
                return material;
            var surface =
                style == 0
                    ? Art.Brick
                    : Resources.Load<Material>(
                        "Realism/Prepared/"
                            + (style == 3 || style == 4 ? "PaintedStucco" : "WhiteStucco")
                    ) ?? Art.Concrete;
            material = new Material(surface)
            {
                name = "Architectural wall " + style,
                enableInstancing = true,
            };
            var palette = new[]
            {
                Color.white,
                new Color(1, .96f, .88f),
                new Color(.87f, .89f, .91f),
                new Color(.70f, .80f, .74f),
                new Color(.94f, .80f, .66f),
                new Color(.98f, .98f, .96f),
            };
            material.color = palette[style % palette.Length];
            if (style == 1 || style == 2 || style == 5)
                material.mainTextureScale = Vector2.one * 1.5f;
            material.SetFloat("_Smoothness", .12f);
            material.SetFloat("_BumpScale", style == 0 ? .5f : .2f);
            walls[style] = material;
            return material;
        }

        public static bool FlatRoof(MapBuilding building) =>
            building.roof.vertices.Max(p => p.y) - building.roof.vertices.Min(p => p.y) < .2f;

        public static Material Roof(MapBuilding building) =>
            FlatRoof(building)
                ? Resources.Load<Material>("Realism/Prepared/FlatRoof") ?? Art.Roof
                : Art.Roof;

        static Material Finish(Material original, int style)
        {
            string name = original.name.ToLowerInvariant();
            if (name.Contains("plaster"))
                return Wall(style);
            if (!name.Contains("trim") && !name.Contains("glass"))
                return original;
            if (finishes.TryGetValue((original, style), out var cached))
                return cached;
            var material = new Material(original);
            material.name = "Architectural " + name + " " + style;
            if (name.Contains("trim"))
            {
                // Painted architectural joinery keeps the licensed modeled
                // profiles, without the same distressed atlas on every facade.
                material.SetTexture("_BaseMap", null);
                material.SetTexture("_MetallicGlossMap", null);
                material.SetTexture("_OcclusionMap", null);
                material.SetTexture("_BumpMap", null);
                material.DisableKeyword("_METALLICSPECGLOSSMAP");
                material.DisableKeyword("_OCCLUSIONMAP");
                material.DisableKeyword("_NORMALMAP");
                material.color =
                    style == 0 || style == 4
                        ? new Color(.21f, .24f, .23f)
                        : new Color(.84f, .84f, .79f);
                material.SetFloat("_Metallic", .05f);
                material.SetFloat("_Smoothness", .35f);
            }
            else
            {
                material.color = new Color(.82f, .92f, 1);
                material.SetFloat("_Smoothness", .86f);
            }
            material.enableInstancing = true;
            finishes[(original, style)] = material;
            return material;
        }

        public static bool Add(
            Transform parent,
            MapBuilding building,
            MappedStorefront entrance = null,
            MapDefinition map = null
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
            int style = Style(building);
            bool retail =
                entrance != null
                || building.usage == "retail"
                || building.usage == "commercial"
                || building.usage == "supermarket";
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
                bool shopfront =
                    retail
                    && (
                        entrance != null
                            ? i == entrance.Edge
                            : StreetFacing(map, start + edge * .5f, outward)
                    );
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
                        if (floor == 0 && shopfront)
                        {
                            AddShopBay(origin, rotation, width, floorHeight, style, bay, batches);
                            continue;
                        }
                        string module =
                            floor == 0 && bay == bays / 2 ? "DoorGlass"
                            : style % 3 == 0 ? "Window"
                            : style % 3 == 1 ? "WindowPanel"
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
                if (FlatRoof(building))
                    AddBox(
                        start + edge * .5f + Vector3.up * (building.height + .16f),
                        rotation,
                        new Vector3(length, .32f, .22f),
                        Wall(style),
                        batches
                    );
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

        static bool StreetFacing(MapDefinition map, Vector3 centre, Vector3 outward)
        {
            if (map == null)
                return false;
            var lane = map.Nearest(centre + outward * 6, routable: false);
            if (lane == null)
                return false;
            var delta = lane.Path.Position(lane.Path.NearestDistance(centre)) - centre;
            return delta.sqrMagnitude < 25 * 25 && Vector3.Dot(delta, outward) > 2;
        }

        static void AddBox(
            Vector3 position,
            Quaternion rotation,
            Vector3 size,
            Material material,
            Dictionary<Material, List<CombineInstance>> batches
        )
        {
            if (!cube)
            {
                var source = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube = source.GetComponent<MeshFilter>().sharedMesh;
                Object.DestroyImmediate(source);
            }
            if (!batches.TryGetValue(material, out var parts))
                batches[material] = parts = new List<CombineInstance>();
            parts.Add(
                new CombineInstance
                {
                    mesh = cube,
                    transform = Matrix4x4.TRS(position, rotation, size),
                }
            );
        }

        static void AddShopBay(
            Vector3 origin,
            Quaternion rotation,
            float width,
            float height,
            int style,
            int bay,
            Dictionary<Material, List<CombineInstance>> batches
        )
        {
            var frame = Art.Material("Store window charcoal", new Color(.13f, .16f, .17f));
            var reveal = Art.Material("Shop window reveal", new Color(.24f, .27f, .26f));
            var glass = Art.Glass;
            void Part(string name, Vector3 p, Vector3 size, Material material) =>
                AddBox(origin + rotation * p, rotation, size, material, batches);
            float top = Mathf.Min(2.6f, height - .25f),
                sill = .5f;
            Part(
                "Lower wall",
                new Vector3(0, sill * .5f, 0),
                new Vector3(width, sill, .18f),
                Wall(style)
            );
            Part(
                "Header",
                new Vector3(0, (height + top) * .5f, 0),
                new Vector3(width, height - top, .18f),
                Wall(style)
            );
            Part(
                "Recess",
                new Vector3(0, (top + sill) * .5f, .02f),
                new Vector3(width - .28f, top - sill, .08f),
                reveal
            );
            Part(
                "Glazing",
                new Vector3(0, (top + sill) * .5f, .08f),
                new Vector3(width - .42f, top - sill - .13f, .025f),
                glass
            );
            foreach (float side in new[] { -1f, 1f })
            {
                Part(
                    "Pier",
                    new Vector3(side * (width - .20f) * .5f, top * .5f, .08f),
                    new Vector3(.2f, top, .34f),
                    Wall(style)
                );
                Part(
                    "Frame",
                    new Vector3(side * (width - .36f) * .5f, (top + sill) * .5f, .13f),
                    new Vector3(.065f, top - sill, .09f),
                    frame
                );
            }
            foreach (float level in new[] { sill, top })
                Part(
                    "Rail",
                    new Vector3(0, level, .13f),
                    new Vector3(width - .30f, .065f, .09f),
                    frame
                );
            Part(
                "Mullion",
                new Vector3(0, (top + sill) * .5f, .13f),
                new Vector3(.04f, top - sill, .07f),
                frame
            );
            if (height < 2.9f || bay % 3 == 0)
                return;
            Color[] colors =
            {
                new Color(.14f, .27f, .23f),
                new Color(.20f, .27f, .35f),
                new Color(.42f, .20f, .16f),
                new Color(.52f, .42f, .26f),
            };
            var canopy = Art.Material("Shop canopy " + (style % 4), colors[style % 4]);
            AddBox(
                origin + rotation * new Vector3(0, top + .22f, .55f),
                rotation * Quaternion.Euler(12, 0, 0),
                new Vector3(width - .05f, .06f, 1.15f),
                canopy,
                batches
            );
            Part(
                "Valance",
                new Vector3(0, top + .07f, 1.10f),
                new Vector3(width - .05f, .20f, .055f),
                canopy
            );
            foreach (float side in new[] { -1f, 1f })
                Part(
                    "Support",
                    new Vector3(side * (width - .42f) * .5f, top + .08f, .55f),
                    new Vector3(.035f, .035f, 1),
                    frame
                );
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
