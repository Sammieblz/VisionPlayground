using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoDriver
{
    public static class Art
    {
        public static Material Asphalt,
            Concrete,
            Grass,
            Brick,
            Roof,
            White,
            Yellow,
            Glass,
            Rubber,
            Metal,
            Paint,
            Wood,
            Foliage;
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

        public static Material Material(string name, Color color, bool glow = false)
        {
            if (cache.TryGetValue(name, out var m))
                return m;
            var template = Resources.Load<Material>("RuntimeLit");
            m = template
                ? new Material(template)
                : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.name = name;
            m.color = color;
            m.enableInstancing = true;
            m.SetFloat("_Smoothness", .25f);
            if (glow)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color * 2);
            }
            cache[name] = m;
            return m;
        }

        static Material Surface(string name, Color tint, float scale)
        {
            var prepared = Resources.Load<Material>("Realism/Prepared/" + name);
            if (prepared)
                return prepared;
            var m = Material(name, tint);
            var tex = Resources.Load<Texture2D>("Materials/" + name);
            if (tex)
            {
                m.mainTexture = tex;
                m.mainTextureScale = Vector2.one * scale;
            }
            return m;
        }

        public static void Initialize()
        {
            Asphalt = Surface("Asphalt", new Color(.62f, .62f, .62f), 1);
            Concrete = Surface("Concrete", new Color(.7f, .69f, .65f), 1);
            Grass = Surface("Grass", new Color(.56f, .65f, .4f), 1);
            Brick = Surface("Brick", new Color(.72f, .63f, .53f), 1);
            Roof = Surface("Roof", new Color(.3f, .32f, .34f), 1);
            White = Material("Road paint", new Color(.9f, .88f, .8f));
            Yellow = Material("Yellow", new Color(.95f, .65f, .05f));
            Glass = Material("Window glass", new Color(.12f, .22f, .29f));
            Glass.SetFloat("_Smoothness", .85f);
            Rubber = Material("Rubber", new Color(.035f, .04f, .045f));
            Metal = Material("Steel", new Color(.36f, .39f, .42f));
            Metal.SetFloat("_Metallic", .65f);
            Paint = Material("Car teal", new Color(.13f, .32f, .37f));
            Paint.SetFloat("_Metallic", .55f);
            Paint.SetFloat("_Smoothness", .7f);
            Wood = Material("Bark", new Color(.26f, .21f, .14f));
            Foliage = Material("Leaves", new Color(.19f, .32f, .13f));
        }

        public static GameObject Box(
            string name,
            Transform parent,
            Vector3 p,
            Vector3 size,
            Material mat,
            bool collision = true
        )
        {
            var g = Shape(name, parent, p, size, mat, PrimitiveType.Cube, collision);
            if (mat && mat.mainTexture)
            {
                var filter = g.GetComponent<MeshFilter>();
                var mesh = Object.Instantiate(filter.sharedMesh);
                var uv = mesh.uv;
                for (int i = 0; i < uv.Length; i++)
                {
                    var normal = mesh.normals[i];
                    var tile =
                        Mathf.Abs(normal.y) > .5f ? new Vector2(size.x, size.z)
                        : Mathf.Abs(normal.x) > .5f ? new Vector2(size.z, size.y)
                        : new Vector2(size.x, size.y);
                    uv[i] = Vector2.Scale(uv[i], tile / 4);
                }
                mesh.uv = uv;
                filter.sharedMesh = mesh;
            }
            return g;
        }

        public static GameObject Marking(
            string name,
            Transform parent,
            Vector3 position,
            Vector2 size,
            Material material
        )
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            float x = size.x * .5f,
                z = size.y * .5f;
            var mesh = new Mesh { name = "Flush road paint" };
            RuntimeResources.Register(go, mesh);
            mesh.vertices = new[]
            {
                new Vector3(-x, 0, -z),
                new Vector3(x, 0, -z),
                new Vector3(x, 0, z),
                new Vector3(-x, 0, z),
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            return go;
        }

        public static void Octagon(
            Transform parent,
            Vector3 position,
            float radius,
            Material material
        )
        {
            var sign = new GameObject("Octagonal sign face");
            sign.transform.SetParent(parent, false);
            sign.transform.localPosition = position;
            var vertices = new Vector3[18];
            var triangles = new List<int>();
            for (int i = 0; i < 8; i++)
            {
                float a = (22.5f + i * 45) * Mathf.Deg2Rad;
                vertices[i + 1] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * radius;
                vertices[i + 10] = vertices[i + 1];
                int next = (i + 1) % 8 + 1;
                triangles.AddRange(new[] { 0, i + 1, next, 9, next + 9, i + 10 });
            }
            var mesh = new Mesh { name = "Eight-sided sign" };
            RuntimeResources.Register(sign, mesh);
            mesh.vertices = vertices;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            sign.AddComponent<MeshFilter>().sharedMesh = mesh;
            sign.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        public static void Gable(
            Transform parent,
            float width,
            float depth,
            float height,
            float rise
        )
        {
            var g = new GameObject("Closed gable attic");
            g.transform.SetParent(parent, false);
            g.transform.localPosition = Vector3.up * height;
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            var uv = new List<Vector2>();
            for (int side = -1; side <= 1; side += 2)
            {
                int offset = vertices.Count;
                vertices.Add(new Vector3(-width / 2, 0, side * depth / 2));
                vertices.Add(new Vector3(width / 2, 0, side * depth / 2));
                vertices.Add(new Vector3(0, rise, side * depth / 2));
                uv.Add(Vector2.zero);
                uv.Add(new Vector2(width / 4, 0));
                uv.Add(new Vector2(width / 8, rise / 4));
                indices.Add(offset);
                indices.Add(offset + (side < 0 ? 2 : 1));
                indices.Add(offset + (side < 0 ? 1 : 2));
            }
            var mesh = new Mesh { name = "Gable end walls" };
            RuntimeResources.Register(g, mesh);
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            g.AddComponent<MeshRenderer>().sharedMaterial = Brick;
            g.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        public static GameObject Shape(
            string name,
            Transform parent,
            Vector3 p,
            Vector3 size,
            Material mat,
            PrimitiveType type,
            bool collision = false
        )
        {
            var g = GameObject.CreatePrimitive(type);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = p;
            g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collision)
            {
                var collider = g.GetComponent<Collider>();
                collider.enabled = false;
                if (Application.isPlaying)
                    Object.Destroy(collider);
                else
                    Object.DestroyImmediate(collider);
            }
            return g;
        }

        public static Transform Wheel(Transform parent, Vector3 p)
        {
            var modeled = Realism.Spawn(p.x < 0 ? "WheelLeft" : "WheelRight", parent, p);
            if (modeled)
            {
                modeled.transform.localRotation = Quaternion.Euler(0, 0, 90);
                modeled.AddComponent<RollingWheel>();
                return modeled.transform;
            }
            var w = Shape(
                "Tire",
                parent,
                p,
                new Vector3(.68f, .12f, .68f),
                Rubber,
                PrimitiveType.Cylinder
            ).transform;
            w.localRotation = Quaternion.Euler(0, 0, 90);
            w.gameObject.AddComponent<RollingWheel>();
            Shape(
                "Alloy",
                w,
                new Vector3(0, 1.02f, 0),
                new Vector3(.68f, .06f, .68f),
                Metal,
                PrimitiveType.Cylinder
            );
            for (int i = 0; i < 5; i++)
            {
                var spoke = Box(
                    "Wheel spoke",
                    w,
                    new Vector3(0, 1.1f, 0),
                    new Vector3(.055f, .05f, .6f),
                    White,
                    false
                );
                spoke.transform.localRotation = Quaternion.Euler(0, i * 36, 0);
            }
            return w;
        }

        public static void Car(Transform p, Material paint, bool wheels = true, float length = 4.4f)
        {
            if (length < 5 && Realism.Car(p, wheels ? "Sedan" : "PlayerSedan", wheels))
                return;
            var prefab =
                length < 5 ? Resources.Load<GameObject>("Scenery/Prepared/NormalCar1") : null;
            if (prefab && wheels)
            {
                var model = Object.Instantiate(prefab, p);
                RollingWheel.PrepareImported(model);
                return;
            }
            Box("Body", p, new Vector3(0, .7f, 0), new Vector3(1.82f, .55f, length), paint, false);
            Box(
                "Cabin",
                p,
                new Vector3(0, 1.18f, -.2f),
                new Vector3(1.62f, .55f, length * .48f),
                Glass,
                false
            );
            Box(
                "Roof",
                p,
                new Vector3(0, 1.48f, -.25f),
                new Vector3(1.65f, .08f, length * .43f),
                paint,
                false
            );
            for (int s = -1; s <= 1; s += 2)
            {
                Box(
                    "B pillar",
                    p,
                    new Vector3(s * .83f, 1.19f, -.28f),
                    new Vector3(.08f, .58f, .13f),
                    paint,
                    false
                );
                Box(
                    "Headlight",
                    p,
                    new Vector3(s * .65f, .77f, length / 2 + .02f),
                    new Vector3(.45f, .16f, .04f),
                    White,
                    false
                );
                Box(
                    "Tail lamp",
                    p,
                    new Vector3(s * .65f, .79f, -length / 2 - .02f),
                    new Vector3(.42f, .16f, .04f),
                    Material("Tail red", new Color(.65f, .05f, .04f), true),
                    false
                );
                Box(
                    "Mirror",
                    p,
                    new Vector3(s * .97f, 1.15f, .65f),
                    new Vector3(.22f, .15f, .24f),
                    paint,
                    false
                );
                if (wheels)
                {
                    Wheel(p, new Vector3(s * .87f, .34f, 1.38f));
                    Wheel(p, new Vector3(s * .87f, .34f, -1.32f));
                }
            }
            Box(
                "Grille",
                p,
                new Vector3(0, .55f, length / 2 + .025f),
                new Vector3(.9f, .2f, .04f),
                Rubber,
                false
            );
            Box(
                "Plate",
                p,
                new Vector3(0, .58f, -length / 2 - .03f),
                new Vector3(.45f, .2f, .02f),
                White,
                false
            );
        }

        public static Transform ParkingCar(Transform parent, Material paint)
        {
            var imported = Realism.Spawn("ParkingSedan", parent);
            if (imported)
            {
                var pivot = new GameObject("Driver door hinge").transform;
                pivot.SetParent(parent, false);
                pivot.localPosition = new Vector3(-.92f, .55f, .6f);
                Realism.AttachPanels(imported, pivot);
                RollingWheel.PrepareImported(imported);
                return pivot;
            }
            Box(
                "Car floor",
                parent,
                new Vector3(0, .47f, 0),
                new Vector3(1.8f, .16f, 4.4f),
                paint,
                false
            );
            Box(
                "Hood",
                parent,
                new Vector3(0, .72f, 1.35f),
                new Vector3(1.8f, .4f, 1.7f),
                paint,
                false
            );
            Box(
                "Trunk",
                parent,
                new Vector3(0, .75f, -1.6f),
                new Vector3(1.8f, .45f, 1.2f),
                paint,
                false
            );
            Box(
                "Passenger side",
                parent,
                new Vector3(.88f, .85f, -.2f),
                new Vector3(.08f, .65f, 1.6f),
                paint,
                false
            );
            Box(
                "Car roof",
                parent,
                new Vector3(0, 1.55f, -.2f),
                new Vector3(1.7f, .08f, 1.7f),
                paint,
                false
            );
            Box(
                "Front windshield",
                parent,
                new Vector3(0, 1.22f, .65f),
                new Vector3(1.7f, .6f, .06f),
                Glass,
                false
            );
            Box(
                "Rear windshield",
                parent,
                new Vector3(0, 1.22f, -1.05f),
                new Vector3(1.7f, .6f, .06f),
                Glass,
                false
            );
            Box(
                "Passenger window",
                parent,
                new Vector3(.86f, 1.25f, -.2f),
                new Vector3(.04f, .5f, 1.6f),
                Glass,
                false
            );
            var hinge = new GameObject("Driver door hinge").transform;
            hinge.SetParent(parent, false);
            hinge.localPosition = new Vector3(-.92f, .55f, .6f);
            Box(
                "Driver door",
                hinge,
                new Vector3(0, .3f, -.7f),
                new Vector3(.08f, .6f, 1.4f),
                paint,
                false
            );
            Box(
                "Driver door window",
                hinge,
                new Vector3(0, .7f, -.7f),
                new Vector3(.04f, .45f, 1.4f),
                Glass,
                false
            );
            for (int side = -1; side <= 1; side += 2)
            {
                Wheel(parent, new Vector3(side * .87f, .34f, 1.38f));
                Wheel(parent, new Vector3(side * .87f, .34f, -1.32f));
                Box(
                    "Headlamp",
                    parent,
                    new Vector3(side * .65f, .75f, 2.21f),
                    new Vector3(.4f, .18f, .03f),
                    White,
                    false
                );
                Box(
                    "Tail lamp",
                    parent,
                    new Vector3(side * .65f, .75f, -2.21f),
                    new Vector3(.4f, .18f, .03f),
                    Material("Tail red", Color.red, true),
                    false
                );
            }
            return hinge;
        }

        public static void Text(
            string text,
            Transform parent,
            Vector3 p,
            float scale,
            Color color,
            Quaternion rotation
        )
        {
            var g = new GameObject(text);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = p;
            g.transform.localRotation = rotation;
            var tm = g.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = 48;
            tm.characterSize = scale;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            var renderer = g.GetComponent<MeshRenderer>();
            var material = new Material(Shader.Find("CoDriver/WorldText"));
            RuntimeResources.Register(g, material);
            material.mainTexture = renderer.sharedMaterial.mainTexture;
            renderer.sharedMaterial = material;
        }
    }
}
