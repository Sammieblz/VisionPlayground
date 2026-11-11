using UnityEngine;

namespace CoDriver
{
    public static class ConstructionCone
    {
        public static GameObject Create(Transform parent, Vector3 position)
        {
            var cone = Realism.Spawn("Cone", parent, position);
            if (!cone)
            {
                cone = new GameObject("Cone");
                cone.transform.SetParent(parent, false);
                cone.transform.localPosition = position;
                Art.Box(
                    "Rubber cone base",
                    cone.transform,
                    Vector3.up * .035f,
                    new Vector3(.4f, .07f, .4f),
                    Art.Rubber,
                    false
                );
                Surface(
                    cone.transform,
                    "Tapered orange cone",
                    .07f,
                    .8f,
                    .185f,
                    .025f,
                    Art.Material("Construction orange", new Color(1, .3f, .03f))
                );
            }
            // Explicit reflective strip remains visible with imported textures
            // and in the fallback model; it is physical scene appearance only.
            Surface(
                cone.transform,
                "White reflective cone band",
                .34f,
                .49f,
                .133f,
                .1f,
                Art.White
            );
            var collider = cone.AddComponent<BoxCollider>();
            collider.center = Vector3.up * .4f;
            collider.size = new Vector3(.4f, .8f, .4f);
            MovableProp.Add(cone, 3.5f);
            return cone;
        }

        static void Surface(
            Transform parent,
            string name,
            float lower,
            float upper,
            float lowerRadius,
            float upperRadius,
            Material material
        )
        {
            const int sides = 20;
            var vertices = new Vector3[(sides + 1) * 2];
            var uv = new Vector2[vertices.Length];
            var indices = new int[sides * 6];
            for (int i = 0; i <= sides; i++)
            {
                float angle = 2 * Mathf.PI * i / sides;
                var radial = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                vertices[i * 2] = radial * lowerRadius + Vector3.up * lower;
                vertices[i * 2 + 1] = radial * upperRadius + Vector3.up * upper;
                uv[i * 2] = new Vector2((float)i / sides, 0);
                uv[i * 2 + 1] = new Vector2((float)i / sides, 1);
                if (i == sides)
                    continue;
                int at = i * 6;
                indices[at] = i * 2;
                indices[at + 1] = i * 2 + 1;
                indices[at + 2] = i * 2 + 2;
                indices[at + 3] = i * 2 + 2;
                indices[at + 4] = i * 2 + 1;
                indices[at + 5] = i * 2 + 3;
            }
            var mesh = new Mesh
            {
                name = name,
                vertices = vertices,
                uv = uv,
                triangles = indices,
            };
            RuntimeResources.Register(parent.gameObject, mesh);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.AddComponent<MeshRenderer>().sharedMaterial = material;
        }
    }
}
