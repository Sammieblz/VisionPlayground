using UnityEngine;
using UnityEngine.AI;

namespace CoDriver
{
    // Physical scenery only. No recovery teleport or control override is used.
    public static class WorldBoundary
    {
        public static Bounds[] Envelopes(MapDefinition map)
        {
            const float thickness = 1;
            // Keep the outside face inside the same bounds used by navigation.
            float west = map.boundsMin.x + thickness * .5f;
            float east = map.boundsMax.x - thickness * .5f;
            float south = map.boundsMin.z + thickness * .5f;
            float north = map.boundsMax.z - thickness * .5f;
            float bottom = -8;
            float top = 2.4f;
            // A boundary road can be above or below the ordinary ground plane.
            // Enclose its full height without interrupting interior underpasses
            // or the authored bridge, whose routes stay inside this perimeter.
            foreach (var lane in map.lanes)
            foreach (var point in lane.points)
                if (
                    point.x < west + 8
                    || point.x > east - 8
                    || point.z < south + 8
                    || point.z > north - 8
                )
                {
                    bottom = Mathf.Min(bottom, point.y - 2);
                    top = Mathf.Max(top, point.y + 2.4f);
                }
            float y = (top + bottom) * .5f;
            return new[]
            {
                new Bounds(
                    new Vector3(west, y, (south + north) * .5f),
                    new Vector3(thickness, top - bottom, north - south + thickness)
                ),
                new Bounds(
                    new Vector3(east, y, (south + north) * .5f),
                    new Vector3(thickness, top - bottom, north - south + thickness)
                ),
                new Bounds(
                    new Vector3((west + east) * .5f, y, south),
                    new Vector3(east - west + thickness, top - bottom, thickness)
                ),
                new Bounds(
                    new Vector3((west + east) * .5f, y, north),
                    new Vector3(east - west + thickness, top - bottom, thickness)
                ),
            };
        }

        public static void Build(Town town)
        {
            var perimeter = new GameObject("District perimeter retaining wall").transform;
            perimeter.SetParent(town.transform, false);
            foreach (var bounds in Envelopes(town.Map))
            {
                Art.Box("Perimeter wall", perimeter, bounds.center, bounds.size, Art.Concrete);
                Art.Box(
                    "Visible perimeter cap",
                    perimeter,
                    new Vector3(bounds.center.x, bounds.max.y, bounds.center.z),
                    new Vector3(bounds.size.x + .12f, .18f, bounds.size.z + .12f),
                    Art.Metal,
                    false
                );
                town.WalkSurfaces.Add(
                    new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Box,
                        transform = Matrix4x4.TRS(bounds.center, Quaternion.identity, Vector3.one),
                        size = bounds.size,
                        area = 1,
                    }
                );
            }
        }
    }
}
