using System.Collections.Generic;
using UnityEngine;

namespace CoDriver
{
    public static class RailArchitecture
    {
        public static void Build(Town town, MapRail track)
        {
            var path = new TrackPath(track.points, false);
            var rail = new[]
            {
                new Vector2(-.07f, -.09f),
                new Vector2(.07f, -.09f),
                new Vector2(.07f, -.075f),
                new Vector2(.012f, -.06f),
                new Vector2(.012f, .035f),
                new Vector2(.034f, .045f),
                new Vector2(.034f, .075f),
                new Vector2(-.034f, .075f),
                new Vector2(-.034f, .045f),
                new Vector2(-.012f, .035f),
                new Vector2(-.012f, -.06f),
                new Vector2(-.07f, -.075f),
            };
            for (int side = -1; side <= 1; side += 2)
                Section(
                    town,
                    path,
                    "Continuous rail " + track.id,
                    rail,
                    side * .7515f,
                    Art.Metal,
                    false
                );
            var ballast = Resources.Load<Material>("Realism/Prepared/Ballast") ?? Art.Concrete;
            Section(
                town,
                path,
                "Continuous gravel ballast " + track.id,
                new[]
                {
                    new Vector2(-2.1f, -.30f),
                    new Vector2(-1.4f, -.17f),
                    new Vector2(1.4f, -.17f),
                    new Vector2(2.1f, -.30f),
                },
                0,
                ballast,
                true
            );
            var timber = Resources.Load<Material>("Realism/Prepared/RailTimber") ?? Art.Wood;
            Mesh sleeperMesh = null;
            var sleeperSize = new Vector3(2.6f, .16f, .23f);
            for (float distance = 0; distance < path.Length; distance += .65f)
            {
                var point = path.Position(distance);
                if (!town.Map.Contains(point) || AtLevelCrossing(town, point))
                    continue;
                var forward = path.Direction(distance);
                var rotation = Quaternion.LookRotation(forward);
                var right = Vector3.Cross(Vector3.up, forward);
                var sleeper = Art.Box(
                    "Timber sleeper",
                    town.transform,
                    point - Vector3.up * .11f,
                    sleeperSize,
                    Art.Wood,
                    false
                );
                sleeper.transform.rotation = rotation;
                if (timber.mainTexture)
                {
                    var filter = sleeper.GetComponent<MeshFilter>();
                    if (!sleeperMesh)
                        sleeperMesh = RuntimeResources.Register(
                            town.gameObject,
                            TimberMesh(filter.sharedMesh, sleeperSize)
                        );
                    filter.sharedMesh = sleeperMesh;
                    sleeper.GetComponent<Renderer>().sharedMaterial = timber;
                }
                // Low-profile plates bind the running rail to each sleeper. They
                // are visual only; rail contact remains the finite-mass rail system.
                foreach (int side in new[] { -1, 1 })
                {
                    var plate = Art.Box(
                        "Rail tie plate",
                        town.transform,
                        point + right * (side * .7515f) - Vector3.up * .023f,
                        new Vector3(.24f, .025f, .25f),
                        Art.Metal,
                        false
                    );
                    plate.transform.rotation = rotation;
                }
            }
        }

        static Mesh TimberMesh(Mesh source, Vector3 size)
        {
            // One shared mesh per track, with the source's vertical grain along
            // the sleeper's long axis. Preserve 0.5 m scan scale on every face;
            // a stretched cube UV would turn the wood into broad painted bands.
            var mesh = Object.Instantiate(source);
            mesh.name = "Metric weathered timber sleeper";
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var uv = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var p = Vector3.Scale(vertices[i], size);
                uv[i] =
                    Mathf.Abs(normals[i].y) > .5f ? new Vector2(p.z, p.x) * 2
                    : Mathf.Abs(normals[i].z) > .5f ? new Vector2(p.y, p.x) * 2
                    : new Vector2(p.z, p.y) * 2;
            }
            mesh.uv = uv;
            mesh.RecalculateTangents();
            return mesh;
        }

        static bool AtLevelCrossing(Town town, Vector3 point)
        {
            var lane = town.Map.Nearest(point, routable: false);
            if (lane == null)
                return false;
            var road = lane.Path.Position(lane.Path.NearestDistance(point));
            // Richards passes below the rails, so its road must not cut a hole
            // in the railway bed above it.
            return Mathf.Abs(road.y - point.y) < .6f
                && Vector3.ProjectOnPlane(road - point, Vector3.up).sqrMagnitude
                    < Mathf.Pow(lane.width * .5f + 1, 2);
        }

        static void Section(
            Town town,
            TrackPath path,
            string name,
            Vector2[] profile,
            float offset,
            Material material,
            bool crossingGaps
        )
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            int count = Mathf.CeilToInt(path.Length / 1.3f);
            for (int i = 0; i <= count; i++)
            {
                float along = path.Length * i / count;
                var point = path.Position(along);
                var right = Vector3.Cross(Vector3.up, path.Direction(along)).normalized;
                foreach (var v in profile)
                    vertices.Add(point + right * (offset + v.x) + Vector3.up * v.y);
                if (i == 0 || !town.Map.Contains(point))
                    continue;
                if (
                    crossingGaps
                    && AtLevelCrossing(town, path.Position(along - path.Length / count * .5f))
                )
                    continue;
                int row = (i - 1) * profile.Length;
                int edges = crossingGaps ? profile.Length - 1 : profile.Length;
                for (int j = 0; j < edges; j++)
                {
                    int a = row + j,
                        b = row + (j + 1) % profile.Length;
                    triangles.AddRange(
                        crossingGaps
                            ? new[]
                            {
                                a,
                                a + profile.Length,
                                b,
                                b,
                                a + profile.Length,
                                b + profile.Length,
                            }
                            : new[]
                            {
                                a,
                                b,
                                a + profile.Length,
                                b,
                                b + profile.Length,
                                a + profile.Length,
                            }
                    );
                }
            }
            DavisWorld.Surface(
                name,
                town.transform,
                new MapMesh { vertices = vertices.ToArray(), triangles = triangles.ToArray() },
                material,
                false
            );
        }
    }
}
