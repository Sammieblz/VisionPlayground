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
        TextMesh instruments;
        readonly List<Camera> mirrors = new List<Camera>();
        readonly List<Renderer> mirrorSurfaces = new List<Renderer>();
        readonly List<RenderTexture> textures = new List<RenderTexture>();
        GameObject deviceUi;
        float nextMirror;
        int mirrorIndex;
        Camera displayCamera;
        float nextDisplay;

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
            var display = new GameObject("Live instruments");
            display.transform.SetParent(transform, false);
            display.transform.localPosition = new Vector3(-.35f, 1.075f, 1.0f);
            instruments = display.AddComponent<TextMesh>();
            instruments.fontSize = 48;
            instruments.characterSize = .006f;
            instruments.anchor = TextAnchor.MiddleCenter;
            instruments.alignment = TextAlignment.Center;
            instruments.color = new Color(.7f, .95f, .9f);
            var renderer = display.GetComponent<MeshRenderer>();
            var material = new Material(Shader.Find("CoDriver/WorldText"));
            RuntimeResources.Register(gameObject, material);
            material.mainTexture = renderer.sharedMaterial.mainTexture;
            renderer.sharedMaterial = material;
            for (int side = -1; side <= 1; side += 2)
                Mirror(side);
            foreach (var child in GetComponentsInChildren<Transform>())
                child.gameObject.layer = 9;
        }

        public void AttachDisplay(CoDriverApp app)
        {
            deviceUi = new GameObject("Cockpit Comma interface");
            var camera = new GameObject("Cockpit display renderer").AddComponent<Camera>();
            camera.transform.SetParent(deviceUi.transform, false);
            camera.transform.position = new Vector3(0, -5000, 0);
            camera.orthographic = true;
            camera.orthographicSize = 540;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 1 << 16;
            var target = new RenderTexture(960, 540, 16, RenderTextureFormat.ARGB32);
            target.Create();
            textures.Add(target);
            camera.targetTexture = target;
            displayCamera = camera;
            camera.enabled = false;
            var mirrorSettings = camera.GetUniversalAdditionalCameraData();
            mirrorSettings.renderShadows = false;
            mirrorSettings.requiresColorOption = CameraOverrideOption.Off;
            mirrorSettings.requiresDepthOption = CameraOverrideOption.Off;
            var canvas = new GameObject(
                "Comma display Canvas",
                typeof(RectTransform)
            ).AddComponent<Canvas>();
            canvas.transform.SetParent(deviceUi.transform, false);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1;
            var scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            canvas.gameObject.AddComponent<OpenpilotDisplay>().Initialize(app);
            foreach (var child in canvas.GetComponentsInChildren<Transform>())
                child.gameObject.layer = 16;
            var frame = Art.Box(
                "Comma device housing",
                transform,
                vehicle.VisualDefinition
                    ? vehicle.VisualDefinition.deviceCentre
                    : new Vector3(-.1f, 1.13f, .98f),
                new Vector3(.30f, .175f, .025f),
                Art.Rubber,
                false
            );
            var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            DisposeObject(screen.GetComponent<Collider>());
            screen.name = "Comma device display";
            screen.transform.SetParent(frame.transform, false);
            screen.transform.localPosition = new Vector3(0, 0, -.55f);
            screen.transform.localScale = new Vector3(.94f, .88f, 1);
            var screenMaterial = new Material(Resources.Load<Material>("RuntimeUnlit"));
            RuntimeResources.Register(gameObject, screenMaterial);
            screenMaterial.mainTexture = target;
            screen.GetComponent<Renderer>().sharedMaterial = screenMaterial;
            screen.layer = frame.layer = 9;
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
            if (instruments)
                instruments.text =
                    (vehicle.Indicator < 0 ? "◀  " : "   ")
                    + (vehicle.Speed * 2.23694f).ToString("F0")
                    + " mph  "
                    + (vehicle.Reverse ? "R" : vehicle.Gear.ToString())
                    + (vehicle.Indicator > 0 ? "  ▶" : "")
                    + "\n"
                    + (vehicle.EngineRpm / 1000).ToString("F1")
                    + " ×1000 rpm";
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
            if (
                displayCamera
                && Time.unscaledTime >= nextDisplay
                && CoDriverApp.Instance
                && CoDriverApp.Instance.ViewMode == 1
            )
            {
                nextDisplay = Time.unscaledTime + .05f;
                CameraRenderCost.Render("device", displayCamera);
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
