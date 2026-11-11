using System.Collections.Generic;
using UnityEngine;

namespace CoDriver
{
    // Destroying a GameObject does not release meshes/materials created with new.
    // Register only newly allocated resources, never imported/shared assets.
    public sealed class RuntimeResources : MonoBehaviour
    {
        readonly List<Object> owned = new List<Object>();
        public IReadOnlyList<Object> Owned => owned;

        public static T Register<T>(GameObject location, T resource)
            where T : Object
        {
            var town = location.GetComponentInParent<Town>();
            var root = town ? town.gameObject : location;
            var owner =
                root.GetComponent<RuntimeResources>() ?? root.AddComponent<RuntimeResources>();
            owner.owned.Add(resource);
            return resource;
        }

        void OnDestroy()
        {
            foreach (var resource in owned)
                if (resource)
                {
                    if (Application.isPlaying)
                        Destroy(resource);
                    else
                        DestroyImmediate(resource);
                }
            owned.Clear();
        }
    }
}
