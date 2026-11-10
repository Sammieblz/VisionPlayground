using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CoDriver.Editor
{
    public static class CockpitImporter
    {
        public static void Prepare()
        {
            const string folder = "Assets/External/Resources/Cockpit/";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(
                folder + "Source/CarConcept.glb"
            );
            if (!source)
                throw new System.InvalidOperationException(
                    "Fetch the pinned CarConcept asset first"
                );
            var model = Object.Instantiate(source);
            model.name = "Unbranded road-car interior";
            var graphite = AssetDatabase
                .LoadAllAssetsAtPath(folder + "Source/CarConcept.glb")
                .OfType<Material>()
                .FirstOrDefault(m => m.name == "Interior 3 Graphite");
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                string name = renderer.name;
                if (graphite)
                    renderer.sharedMaterials = renderer
                        .sharedMaterials.Select(m =>
                            m && m.name == "Interior 3 Carmine" ? graphite : m
                        )
                        .ToArray();
                bool keep =
                    name.StartsWith("Interior")
                    || name.Contains("Window")
                    || name.Contains("WindshieldGasket")
                    || name == "BodyPillars"
                    || name.Contains("Mirror")
                    || name == "BodyRoofPanel";
                if (!keep || name.Contains("Emblem"))
                {
                    Object.DestroyImmediate(renderer.gameObject.GetComponent<MeshFilter>());
                    Object.DestroyImmediate(renderer);
                }
            }
            // Controls were authored for a central driving position. Seat meshes
            // contain several seats/material sections, so do not translate one
            // material section independently and tear the seat assembly apart.
            foreach (var part in model.GetComponentsInChildren<Transform>())
            {
                bool steering = part.name.StartsWith("InteriorSteering");
                bool pedal = part.name.StartsWith("InteriorPedal");
                if (!steering && !pedal)
                    continue;
                // Children inherit the assembly offset. Moving the cylinder and
                // its child wheel separately would tear the controls apart.
                bool nested = false;
                for (var ancestor = part.parent; ancestor != null; ancestor = ancestor.parent)
                    if (
                        ancestor.name.StartsWith("InteriorSteering")
                        || ancestor.name.StartsWith("InteriorPedal")
                    )
                        nested = true;
                if (nested)
                    continue;
                part.position += Vector3.left * .42f;
                if (steering)
                    part.position += Vector3.forward * .25f + Vector3.up * .05f;
            }
            var root = new GameObject("Road car cockpit");
            model.transform.SetParent(root.transform, false);
            model.transform.localScale = new Vector3(.82f, .95f, .85f);
            model.transform.localPosition = new Vector3(0, .32f, -.22f);
            foreach (var part in root.GetComponentsInChildren<Transform>())
                part.gameObject.layer = 9;
            PrefabUtility.SaveAsPrefabAsset(root, folder + "Interior.prefab");
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
        }
    }
}
