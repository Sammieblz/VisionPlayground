using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoDriver.Editor
{
    // Split textured triangles at exact panel boundaries. Merely hiding triangles
    // by their centroid leaves ragged holes in a low-polygon vehicle shell.
    internal static class MeshPanels
    {
        struct Vertex
        {
            public Vector3 p,
                n;
            public Vector2 uv;

            public static Vertex Lerp(Vertex a, Vertex b, float t) =>
                new Vertex
                {
                    p = Vector3.Lerp(a.p, b.p, t),
                    n = Vector3.Lerp(a.n, b.n, t).normalized,
                    uv = Vector2.Lerp(a.uv, b.uv, t),
                };
        }

        static List<Vertex> Clip(List<Vertex> polygon, int axis, float edge, bool greater)
        {
            var result = new List<Vertex>();
            if (polygon.Count == 0)
                return result;
            var previous = polygon[polygon.Count - 1];
            float pd = (previous.p[axis] - edge) * (greater ? 1 : -1);
            foreach (var current in polygon)
            {
                float cd = (current.p[axis] - edge) * (greater ? 1 : -1);
                if ((pd >= 0) != (cd >= 0))
                    result.Add(Vertex.Lerp(previous, current, pd / (pd - cd)));
                if (cd >= 0)
                    result.Add(current);
                previous = current;
                pd = cd;
            }
            return result;
        }

        static void Add(List<Vertex> polygon, List<Vertex> output)
        {
            for (int i = 1; i + 1 < polygon.Count; i++)
            {
                output.Add(polygon[0]);
                output.Add(polygon[i]);
                output.Add(polygon[i + 1]);
            }
        }

        static Mesh Build(List<Vertex> vertices, string name)
        {
            var p = new Vector3[vertices.Count];
            var n = new Vector3[p.Length];
            var uv = new Vector2[p.Length];
            var indices = new int[p.Length];
            for (int i = 0; i < p.Length; i++)
            {
                p[i] = vertices[i].p;
                n[i] = vertices[i].n;
                uv[i] = vertices[i].uv;
                indices[i] = i;
            }
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.vertices = p;
            mesh.normals = n;
            mesh.uv = uv;
            mesh.triangles = indices;
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        public static (Mesh outside, Mesh inside) Cut(Mesh source, Bounds box)
        {
            var outside = new List<Vertex>();
            var inside = new List<Vertex>();
            var positions = source.vertices;
            var normals = source.normals;
            var uv = source.uv;
            var triangles = source.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var polygon = new List<Vertex>();
                for (int j = 0; j < 3; j++)
                {
                    int k = triangles[i + j];
                    polygon.Add(
                        new Vertex
                        {
                            p = positions[k],
                            n = normals[k],
                            uv = uv.Length > k ? uv[k] : Vector2.zero,
                        }
                    );
                }
                for (int axis = 0; axis < 3 && polygon.Count > 0; axis++)
                {
                    Add(Clip(polygon, axis, box.min[axis], false), outside);
                    polygon = Clip(polygon, axis, box.min[axis], true);
                    Add(Clip(polygon, axis, box.max[axis], true), outside);
                    polygon = Clip(polygon, axis, box.max[axis], false);
                }
                Add(polygon, inside);
            }
            return (Build(outside, source.name + " shell"), Build(inside, source.name + " panel"));
        }
    }
}
