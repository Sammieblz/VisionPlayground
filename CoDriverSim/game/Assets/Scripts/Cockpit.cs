using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace CoDriver
{
    public sealed class Cockpit : MonoBehaviour
    {
        static void DisposeObject(Object value)
        {
            if (value is Collider collider)
                collider.enabled = false;
            if (Application.isPlaying)
                Destroy(value);
            else
                DestroyImmediate(value);
        }

        Vehicle vehicle;
        Transform wheel,
            gas,
            brake;
        Quaternion gasRest,
            brakeRest;
        Quaternion wheelRest;
        readonly List<Camera> mirrors = new List<Camera>();
        readonly List<Renderer> mirrorSurfaces = new List<Renderer>();
        readonly List<RenderTexture> textures = new List<RenderTexture>();
        GameObject deviceUi;
        float nextMirror;
        int mirrorIndex;

        public void Initialize(Vehicle owner)
        {
            vehicle = owner;
            if (vehicle.VisualDefinition)
            {
                wheel = vehicle.VisualDefinition.steeringWheel;
                wheelRest = wheel.localRotation;
                gameObject
                    .AddComponent<CockpitInstruments>()
                    .Initialize(owner, vehicle.VisualDefinition);
                for (int side = -1; side <= 1; side += 2)
                    Mirror(side);
                Mirror(0);
                foreach (var part in GetComponentsInChildren<Transform>())
                    part.gameObject.layer = 9;
                return;
            }
            var prefab = Resources.Load<GameObject>("Cockpit/Interior");
            if (prefab)
            {
                var model = Instantiate(prefab, transform);
                var parts = model.GetComponentsInChildren<Transform>();
                foreach (var panel in model.GetComponentsInChildren<Renderer>())
                    if (panel.name == "InteriorSteeringDash")
                        panel.enabled = false; // Replaced by the live instrument cluster below.
                var sourceWheel = parts.FirstOrDefault(t => t.name == "InteriorSteeringWheel01");
                wheel = new GameObject("Animated steering assembly").transform;
                wheel.SetParent(transform, false);
                wheel.position =
                    sourceWheel && sourceWheel.GetComponent<Renderer>()
                        ? sourceWheel.GetComponent<Renderer>().bounds.center
                        : transform.TransformPoint(new Vector3(-.35f, .95f, .55f));
                foreach (var part in parts.Where(t => t.name.StartsWith("InteriorSteeringWheel")))
                    part.SetParent(wheel, true);
                gas = parts.FirstOrDefault(t => t.name == "InteriorPedalAccel");
                brake = parts.FirstOrDefault(t => t.name == "InteriorPedalBrake");
                if (gas)
                    gasRest = gas.localRotation;
                if (brake)
                    brakeRest = brake.localRotation;
            }
            else
                Debug.LogWarning(
                    "Modeled cockpit missing; run the pinned asset preparation before packaging"
                );
            // Sit beyond and above the wheel rim at the default driver eye.
            var cluster = new Vector3(-.35f, 1.075f, 1.35f);
            Art.Box(
                "Dashboard instrument housing",
                transform,
                cluster + Vector3.forward * .045f,
                new Vector3(.56f, .21f, .08f),
                Art.Rubber,
                false
            );
            gameObject
                .AddComponent<CockpitInstruments>()
                .Initialize(owner, cluster, Vector3.zero, new Vector2(.52f, .17f));
            for (int side = -1; side <= 1; side += 2)
                Mirror(side);
            foreach (var child in GetComponentsInChildren<Transform>())
                child.gameObject.layer = 9;
        }

        public void AttachDisplay(CoDriverApp app)
        {
            var frame = Art.Box(
                "Comma device housing",
                transform,
                vehicle.VisualDefinition
                    ? vehicle.VisualDefinition.deviceCentre
                    : new Vector3(.15f, 1.13f, .98f),
                new Vector3(.30f, .175f, .025f),
                Art.Rubber,
                false
            );
            deviceUi = new GameObject("Cockpit Comma interface", typeof(RectTransform));
            var rect = deviceUi.GetComponent<RectTransform>();
            rect.SetParent(frame.transform, false);
            rect.localPosition = new Vector3(0, 0, -.55f);
            rect.sizeDelta = new Vector2(1920, 1080);
            rect.localScale = new Vector3(.94f / 1920, .88f / 1080, 1);
            // The same read-only UI draws directly on the physical screen. This
            // avoids an auxiliary camera/texture pass and keeps native text sharp.
            deviceUi.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            deviceUi.AddComponent<OpenpilotDisplay>().Initialize(app);
            foreach (var child in frame.GetComponentsInChildren<Transform>())
                child.gameObject.layer = 9; // Excluded from the independent AI camera.
        }

        void Mirror(int side)
        {
            var camera = new GameObject("Limited mirror camera").AddComponent<Camera>();
            camera.transform.SetParent(vehicle.transform, false);
            var definition = vehicle.VisualDefinition;
            camera.transform.localPosition =
                side == 0 ? new Vector3(0, 1.5f, -2.45f)
                : definition ? new Vector3(side * 1.075f, 1.26f, .60f)
                : new Vector3(side * .88f, 1.18f, .55f);
            camera.transform.localRotation = Quaternion.Euler(2, 180 + side * 12, 0);
            camera.fieldOfView = 55;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 120;
            // Small mirror targets do not need distant window bevels and cornices.
            // Building shells remain visible to the mirror's normal far plane.
            var detailDistances = new float[32];
            detailDistances[FacadeArchitecture.DetailLayer] = 45;
            camera.layerCullDistances = detailDistances;
            camera.cullingMask = ~((1 << 9) | (1 << 16));
            camera.GetUniversalAdditionalCameraData().renderShadows = false;
            camera.enabled = false;
            var target = new RenderTexture(256, 128, 16, RenderTextureFormat.ARGB32);
            target.Create();
            textures.Add(target);
            camera.targetTexture = target;
            mirrors.Add(camera);
            var surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
            DisposeObject(surface.GetComponent<Collider>());
            surface.name = side == 0 ? "Working rearview mirror" : "Working side mirror";
            surface.transform.SetParent(transform, false);
            surface.transform.localPosition = definition
                ? side == 0
                    ? definition.rearMirrorCentre
                    : side < 0
                        ? definition.leftMirrorCentre
                        : definition.rightMirrorCentre
                : new Vector3(side * .86f, 1.14f, .49f);
            var size =
                side == 0 ? new Vector2(.222f, .048f)
                : definition ? definition.sideMirrorSize
                : new Vector2(.21f, .12f);
            surface.transform.localScale = new Vector3(size.x, size.y, 1);
            surface.transform.localRotation = definition
                ? Quaternion.Euler(
                    side == 0 ? Vector3.zero
                    : side < 0 ? definition.leftMirrorEuler
                    : definition.rightMirrorEuler
                )
                : Quaternion.Euler(0, -side * 16, 0);
            var material = new Material(Resources.Load<Material>("RuntimeUnlit"));
            RuntimeResources.Register(gameObject, material);
            material.mainTexture = target;
            material.mainTextureScale = new Vector2(-1, 1);
            material.mainTextureOffset = new Vector2(1, 0);
            var surfaceRenderer = surface.GetComponent<Renderer>();
            surfaceRenderer.sharedMaterial = material;
            surfaceRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            surfaceRenderer.receiveShadows = false;
            mirrorSurfaces.Add(surfaceRenderer);
        }

        void LateUpdate()
        {
            if (wheel)
                wheel.localRotation =
                    (vehicle.VisualDefinition ? wheelRest : Quaternion.identity)
                    * Quaternion.AngleAxis(
                        -vehicle.SteerAngle * vehicle.Profile.steeringRatio,
                        vehicle.VisualDefinition ? new Vector3(0, -.4f, .9165f) : Vector3.forward
                    );
            if (gas)
                gas.localRotation = gasRest * Quaternion.Euler(vehicle.Throttle * 18, 0, 0);
            if (brake)
                brake.localRotation = brakeRest * Quaternion.Euler(vehicle.Brake * 15, 0, 0);
            if (
                Time.unscaledTime >= nextMirror
                && CoDriverApp.Instance
                && CoDriverApp.Instance.ViewMode == 1
            )
            {
                nextMirror = Time.unscaledTime + .05f; // Shared 20 renders/s budget across all mirrors.
                int index = mirrorIndex++ % mirrors.Count;
                // Only the driver camera sees layer 9. Keep each visible mirror's
                // cadence, but spend no render work on an off-screen side mirror.
                if (mirrorSurfaces[index].isVisible)
                    CameraRenderCost.Render("mirror", mirrors[index]);
            }
        }

        void OnDestroy()
        {
            foreach (var target in textures)
            {
                target.Release();
                DisposeObject(target);
            }
            if (deviceUi)
                DisposeObject(deviceUi);
        }
    }
}
