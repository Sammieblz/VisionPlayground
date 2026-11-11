using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoDriver
{
    // Presentation only. These assets never supply data to perception or control.
    public static class Realism
    {
        public static GameObject Spawn(
            string name,
            Transform parent,
            Vector3 position = default,
            float yaw = 0
        )
        {
            var prefab = Resources.Load<GameObject>("Realism/Prepared/" + name);
            if (!prefab)
                return null;
            var instance = Object.Instantiate(prefab, parent);
            instance.name = "Modeled " + name;
            instance.transform.localPosition = position;
            instance.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            return instance;
        }

        public static bool Car(
            Transform parent,
            string name,
            bool wheels = true,
            Material paint = null
        )
        {
            var source =
                name == "Concept" ? Resources.Load<GameObject>("WorldArt/Fleet/Concept") : null;
            var model = source ? Object.Instantiate(source, parent) : Spawn(name, parent);
            if (!model)
                return false;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                if (paint)
                    for (int i = 0; i < renderer.sharedMaterials.Length; i++)
                        if (renderer.sharedMaterials[i].name.StartsWith("Paint 1 Carmine"))
                        {
                            var tint = new MaterialPropertyBlock();
                            tint.SetColor("_BaseColor", paint.color);
                            renderer.SetPropertyBlock(tint, i);
                        }
                string part = renderer.name.ToLowerInvariant();
                if (part.Contains("steering") || part.Contains("interior"))
                    renderer.enabled = false;
                if (!wheels && part.Contains("wheel"))
                    renderer.gameObject.SetActive(false);
                // The cockpit has its own modeled dashboard. Hide only the exterior
                // shell in cockpit view, without altering the independent AI camera.
                if (!wheels && renderer.name.StartsWith("Upper cabin"))
                    renderer.gameObject.layer = 8;
            }
            if (wheels)
            {
                var definition = model.GetComponent<AssetDefinition>();
                if (definition && definition.wheels != null && definition.wheels.Length > 0)
                    foreach (var wheel in definition.wheels)
                        wheel.gameObject.AddComponent<RollingWheel>().Radius = .34f;
                else
                    RollingWheel.PrepareImported(model);
            }
            return true;
        }

        public static void AttachPanels(GameObject model, Transform hinge)
        {
            foreach (
                var part in model
                    .GetComponentsInChildren<Transform>()
                    .Where(t => t.name == "DoorPanel")
                    .ToArray()
            )
                part.SetParent(hinge, true);
        }

        public static bool Building(
            Transform parent,
            Vector3 position,
            bool commercial,
            float yaw,
            int variant
        )
        {
            string name = commercial
                ? new[] { "Shop", "Apartment", "Office" }[variant % 3]
                : "House" + (1 + variant % 3);
            var root = new GameObject("Detailed " + name).transform;
            root.SetParent(parent, false);
            root.localPosition = position;
            root.localRotation = Quaternion.Euler(0, yaw, 0);
            var model = Spawn(name, root, Vector3.zero, 180);
            if (!model)
            {
                Object.Destroy(root.gameObject);
                return false;
            }
            // Decorative façades are not registered as activity doors. Visitors use
            // the accessible buildings; no actor walks through an imported wall.
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
                filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
            Art.Box(
                "Driveway",
                root,
                new Vector3(4, .015f, -11),
                new Vector3(3.5f, .03f, 15),
                Art.Concrete
            );
            Art.Box(
                "Front walk",
                root,
                new Vector3(-2, .025f, -9),
                new Vector3(1.2f, .05f, 16),
                Art.Concrete
            );
            if (!commercial)
                Spawn("Mailbox", root, new Vector3(6, 0, -15));
            return true;
        }

        public static void Lighting()
        {
            var sky = Resources.Load<Material>("Realism/Prepared/Sky");
            var cube = Resources.Load<Cubemap>("Realism/Daylight");
            if (!sky || !cube)
                return;
            RenderSettings.skybox = sky;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = cube;
            RenderSettings.reflectionIntensity = .7f;
            DynamicGI.UpdateEnvironment();
        }

        public static bool Rail(Transform carriage, RailVehicle owner, bool boardingCar)
        {
            var model = Spawn("Metro", carriage);
            if (!model)
                return false;
            if (boardingCar)
                model.AddComponent<RailDoorVisual>().Initialize(owner);
            RollingWheel.PrepareImported(model);
            return true;
        }
    }

    public sealed class RailDoorVisual : MonoBehaviour
    {
        RailVehicle owner;
        Transform[] leaves;
        Vector3[] closed;
        float centre;

        public void Initialize(RailVehicle vehicle)
        {
            owner = vehicle;
            // Select the right-side pair nearest the existing platform stop.
            var candidates = GetComponentsInChildren<MeshRenderer>()
                .Where(r =>
                    r.name.Contains("sliding door")
                    && transform.InverseTransformPoint(r.bounds.center).x > 0
                )
                .ToArray();
            var nearest = candidates
                .OrderBy(r => Mathf.Abs(transform.InverseTransformPoint(r.bounds.center).z - 4))
                .FirstOrDefault();
            if (!nearest)
            {
                enabled = false;
                return;
            }
            float z = transform.InverseTransformPoint(nearest.bounds.center).z;
            var selected = candidates
                .OrderBy(r => Mathf.Abs(transform.InverseTransformPoint(r.bounds.center).z - z))
                .Take(2)
                .ToArray();
            centre = selected.Average(r => transform.InverseTransformPoint(r.bounds.center).z);
            owner.BoardingPoint = new Vector3(1.34f, 0, centre);
            leaves = selected.Select(r => r.transform).ToArray();
            closed = leaves.Select(t => t.localPosition).ToArray();
        }

        void LateUpdate()
        {
            if (leaves == null)
                return;
            for (int i = 0; i < leaves.Length; i++)
            {
                float z =
                    transform
                        .InverseTransformPoint(leaves[i].GetComponent<Renderer>().bounds.center)
                        .z - (leaves[i].localPosition.z - closed[i].z);
                var offset = owner.DoorOpen
                    ? Vector3.forward * Mathf.Sign(z - centre) * .65f
                    : Vector3.zero;
                leaves[i].localPosition = Vector3.Lerp(
                    leaves[i].localPosition,
                    closed[i] + offset,
                    Time.deltaTime * 5
                );
            }
        }
    }
}
