using System;
using System.Linq;
using UnityEngine;

namespace CoDriver
{
    // Authored appearances refer to stable source IDs. The original footprint
    // remains the collision, navigation and minimap authority.
    public static class MappedBuildingArt
    {
        [Serializable]
        sealed class Entry
        {
            public string sourceId,
                resource;
        }

        [Serializable]
        sealed class Catalog
        {
            public int schemaVersion;
            public Entry[] entries;
        }

        static Catalog catalog;

        public static bool TryAdd(Transform parent, MapBuilding building)
        {
            if (catalog == null)
            {
                var text = Resources.Load<TextAsset>("Maps/davis-building-art");
                catalog = text ? JsonUtility.FromJson<Catalog>(text.text) : new Catalog();
            }
            var entry = catalog.entries?.FirstOrDefault(e => e.sourceId == building.sourceId);
            if (entry == null || building.rings.Length != 1)
                return false;
            var prefab = Resources.Load<GameObject>(entry.resource);
            if (!prefab)
                return false;
            var ring = building.rings[0].points;
            var right = (ring[1] - ring[0]).normalized;
            var forward = Vector3.Cross(right, Vector3.up);
            float x0 = ring.Min(p => Vector3.Dot(p, right)),
                x1 = ring.Max(p => Vector3.Dot(p, right)),
                z0 = ring.Min(p => Vector3.Dot(p, forward)),
                z1 = ring.Max(p => Vector3.Dot(p, forward));
            // This prepared rectangular model cannot replace a concave footprint.
            if (
                ring.Any(p =>
                    Mathf.Min(
                        Mathf.Abs(Vector3.Dot(p, right) - x0),
                        Mathf.Abs(Vector3.Dot(p, right) - x1)
                    ) > .15f
                    && Mathf.Min(
                        Mathf.Abs(Vector3.Dot(p, forward) - z0),
                        Mathf.Abs(Vector3.Dot(p, forward) - z1)
                    ) > .15f
                )
            )
                return false;
            var instance = UnityEngine.Object.Instantiate(prefab, parent);
            var renderers = instance.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1))
                bounds.Encapsulate(renderer.bounds);
            instance.transform.localScale = new Vector3(
                (x1 - x0) / bounds.size.x,
                building.height / bounds.size.y,
                (z1 - z0) / bounds.size.z
            );
            instance.transform.localRotation = Quaternion.LookRotation(forward);
            instance.transform.localPosition =
                right * ((x0 + x1) * .5f) + forward * ((z0 + z1) * .5f);
            instance.name = "Adapted low-rise " + building.sourceId;
            return true;
        }
    }
}
