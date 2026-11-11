using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CoDriver.Editor
{
    public static class MirrorArtImporter
    {
        // Extract the actual planar back faces of the supplied housings. The
        // source has these surfaces in the body mesh, not named mirror objects.
        public static MirrorSurface[] Prepare(GameObject root, string directory)
        {
            var result = new List<MirrorSurface>();
            foreach (int side in new[] { -1, 1, 0 })
            {
                var vertices = new List<Vector3>();
                Vector3 normal = Vector3.zero;
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
                {
                    var mesh = filter.sharedMesh;
                    var transform =
                        root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                    var points = mesh.vertices;
                    var triangles = mesh.triangles;
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        var a = transform.MultiplyPoint3x4(points[triangles[i]]);
                        var b = transform.MultiplyPoint3x4(points[triangles[i + 1]]);
                        var c = transform.MultiplyPoint3x4(points[triangles[i + 2]]);
                        var centre = (a + b + c) / 3;
                        var cross = Vector3.Cross(b - a, c - a);
                        var n = cross.normalized;
                        bool region =
                            side == 0
                                ? Mathf.Abs(centre.x) < .12f
                                    && centre.y > 1.46f
                                    && centre.y < 1.54f
                                    && centre.z > .52f
                                    && centre.z < .58f
                                : centre.x * side > .85f
                                    && centre.x * side < 1.08f
                                    && centre.y > 1.18f
                                    && centre.y < 1.35f
                                    && centre.z > .57f
                                    && centre.z < .70f;
                        bool plane =
                            side == 0
                                ? n.z < -.97f && Mathf.Abs(n.y) < .08f && n.x < -.12f && n.x > -.18f
                                : n.z < -.94f
                                    && Mathf.Abs(n.y) < .02f
                                    && n.x * side < -.30f
                                    && n.x * side > -.34f;
                        if (!region || !plane || cross.magnitude < .00015f)
                            continue;
                        vertices.AddRange(new[] { a, b, c });
                        normal += cross;
                    }
                }
                if (vertices.Count < 6)
                    throw new System.InvalidOperationException(
                        "Missing measured mirror surface " + side
                    );
                normal.Normalize();
                var bounds = new Bounds(vertices[0], Vector3.zero);
                foreach (var p in vertices)
                    bounds.Encapsulate(p);
                var centrePoint = bounds.center;
                var right = Vector3.Cross(Vector3.up, -normal).normalized;
                var up = Vector3.Cross(-normal, right).normalized;
                var uv = vertices
                    .Select(p => new Vector2(
                        Vector3.Dot(p - centrePoint, right),
                        Vector3.Dot(p - centrePoint, up)
                    ))
                    .ToArray();
                float width = uv.Max(v => v.x) - uv.Min(v => v.x);
                float height = uv.Max(v => v.y) - uv.Min(v => v.y);
                float minU = uv.Min(v => v.x),
                    minV = uv.Min(v => v.y);
                var prepared = new Mesh { name = "Fitted mirror " + side };
                prepared.vertices = vertices
                    .Select(p => centrePoint + (p - centrePoint) * .96f + normal * .0015f)
                    .ToArray();
                prepared.triangles = Enumerable.Range(0, vertices.Count).ToArray();
                prepared.uv = uv.Select(v => new Vector2(
                        (v.x - minU) / width,
                        (v.y - minV) / height
                    ))
                    .ToArray();
                prepared.RecalculateNormals();
                prepared.RecalculateBounds();
                string path = directory + "/Mirror-" + side + ".asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing)
                {
                    EditorUtility.CopySerialized(prepared, existing);
                    Object.DestroyImmediate(prepared);
                    prepared = existing;
                    EditorUtility.SetDirty(prepared);
                }
                else
                    AssetDatabase.CreateAsset(prepared, path);
                result.Add(
                    new MirrorSurface
                    {
                        side = side,
                        mesh = prepared,
                        centre = centrePoint,
                        normal = normal,
                        size = new Vector2(width, height),
                    }
                );
            }
            return result.ToArray();
        }
    }
}
