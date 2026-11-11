using UnityEngine;

namespace CoDriver
{
    // Presentation consumes observer decisions only. It has no vehicle reference.
    public sealed class Companion : MonoBehaviour
    {
        AudioSource audioSource;
        Texture2D disc;
        string kind = "";
        float lastSound = -10;
        Transform robotHead,
            leftArm,
            rightArm;
        readonly Transform[] eyes = new Transform[2];
        public string State { get; private set; } = "disconnected";

        public void Mount(Transform dashboard)
        {
            var root = new GameObject("Camera co-driver robot").transform;
            root.SetParent(dashboard, false);
            root.localPosition = new Vector3(.22f, 1.02f, 1.1f);
            root.localScale = Vector3.one * .65f;
            var shell = Art.Material("Robot pearl shell", new Color(.82f, .86f, .86f));
            Art.Shape(
                "Robot body",
                root,
                new Vector3(0, .07f, 0),
                new Vector3(.11f, .14f, .09f),
                shell,
                PrimitiveType.Capsule
            );
            robotHead = new GameObject("Expressive robot head").transform;
            robotHead.SetParent(root, false);
            robotHead.localPosition = new Vector3(0, .19f, 0);
            Art.Shape(
                "Head shell",
                robotHead,
                Vector3.zero,
                new Vector3(.16f, .11f, .10f),
                shell,
                PrimitiveType.Sphere
            );
            Art.Box(
                "Face visor",
                robotHead,
                new Vector3(0, 0, -.045f),
                new Vector3(.12f, .065f, .015f),
                Art.Rubber,
                false
            );
            for (int i = 0; i < 2; i++)
                eyes[i] = Art.Shape(
                    "Observer eye",
                    robotHead,
                    new Vector3((i == 0 ? -1 : 1) * .03f, 0, -.056f),
                    new Vector3(.022f, .026f, .008f),
                    Art.Material("Robot eye mint", new Color(.2f, .9f, .65f), true),
                    PrimitiveType.Sphere
                ).transform;
            leftArm = Art.Shape(
                "Left arm",
                root,
                new Vector3(-.08f, .08f, 0),
                new Vector3(.035f, .09f, .035f),
                shell,
                PrimitiveType.Capsule
            ).transform;
            rightArm = Art.Shape(
                "Right arm",
                root,
                new Vector3(.08f, .08f, 0),
                new Vector3(.035f, .09f, .035f),
                shell,
                PrimitiveType.Capsule
            ).transform;
            foreach (var part in root.GetComponentsInChildren<Transform>())
                part.gameObject.layer = 9;
        }

        public string Message = "Co-driver is connecting",
            Severity = "unknown";

        public void Initialize()
        {
            disc = new Texture2D(48, 48, TextureFormat.RGBA32, false);
            var pixels = new Color[48 * 48];
            for (int y = 0; y < 48; y++)
            for (int x = 0; x < 48; x++)
            {
                float r = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(24, 24));
                pixels[y * 48 + x] = new Color(1, 1, 1, Mathf.Clamp01(24 - r));
            }
            disc.SetPixels(pixels);
            disc.Apply();
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.volume = .12f;
            audioSource.spatialBlend = 0;
            var data = new float[8820];
            for (int i = 0; i < data.Length; i++)
                data[i] =
                    Mathf.Sin(2 * Mathf.PI * 740 * i / 44100)
                    * Mathf.Sin(Mathf.PI * i / data.Length);
            var clip = AudioClip.Create("Co-driver warning", data.Length, 1, 44100, false);
            clip.SetData(data, 0);
            audioSource.clip = clip;
        }

        public void Observe(ObserverResult result, bool fresh, bool muted)
        {
            Severity = fresh ? "calm" : "unknown";
            State =
                muted ? "muted"
                : fresh ? "attentive"
                : "disconnected";
            Message =
                muted ? "Drive paused"
                : fresh ? "Watching the road ahead"
                : "Co-driver is reconnecting";
            if (muted || !fresh || result.risks == null || result.risks.Length == 0)
            {
                kind = "";
                return;
            }
            Risk risk = result.risks[0];
            foreach (var candidate in result.risks)
                if (candidate.severity == "urgent" || risk.severity == "advisory")
                    risk = candidate;
            Severity = risk.severity;
            State = Severity;
            Message = risk.message?.Replace('\u2014', '-');
            if (
                (Severity == "urgent" || Severity == "caution")
                && (kind != risk.kind || Time.unscaledTime - lastSound > 6)
                && Time.unscaledTime - lastSound > 2
            )
            {
                audioSource.pitch = Severity == "urgent" ? 1.2f : 1;
                audioSource.Play();
                lastSound = Time.unscaledTime;
            }
            kind = risk.kind;
        }

        void LateUpdate()
        {
            if (!robotHead)
                return;
            bool urgent = State == "urgent";
            float time = Time.unscaledTime;
            var rotation = Quaternion.Euler(
                State == "muted" ? 15
                    : urgent ? -8
                    : Mathf.Sin(time * .7f) * 3,
                urgent ? 0 : Mathf.Sin(time * .4f) * 9,
                State == "caution" ? 12 : 0
            );
            robotHead.localRotation = urgent
                ? rotation
                : Quaternion.Slerp(robotHead.localRotation, rotation, Time.unscaledDeltaTime * 5);
            Color colour =
                urgent ? new Color(1, .22f, .1f)
                : State == "caution" ? new Color(1, .65f, .08f)
                : State == "disconnected" || State == "muted" ? Color.gray
                : new Color(.2f, .9f, .65f);
            float blink =
                urgent ? 1
                : State == "muted" ? .15f
                : time % 5 < .13f ? .08f
                : 1;
            foreach (var eye in eyes)
            {
                eye.localScale = new Vector3(.022f, .026f * blink, .008f);
                eye.GetComponent<Renderer>().sharedMaterial = Art.Material(
                    "Robot state " + State,
                    colour,
                    true
                );
            }
            leftArm.localRotation = Quaternion.Euler(
                urgent ? -35 : 0,
                0,
                -(State == "advisory" ? 35 : 8)
            );
            rightArm.localRotation = Quaternion.Euler(urgent ? -35 : 0, 0, 8);
        }

        public void Draw(Rect area)
        {
            Color previous = GUI.color;
            GUI.color =
                Severity == "urgent" ? new Color(1, .3f, .1f)
                : Severity == "caution" ? new Color(1, .7f, .2f)
                : Severity == "unknown" ? Color.gray
                : new Color(.2f, .75f, .65f);
            GUI.DrawTexture(area, disc);
            GUI.color = new Color(.02f, .07f, .08f);
            float blink =
                Severity == "urgent" ? 1
                : Time.unscaledTime % 4.7f < .12f ? .2f
                : 1;
            for (int side = -1; side <= 1; side += 2)
                GUI.DrawTexture(
                    new Rect(
                        area.center.x + side * area.width * .15f - area.width * .045f,
                        area.y + area.height * .4f,
                        area.width * .09f,
                        area.height * .16f * blink
                    ),
                    disc
                );
            GUI.color = previous;
        }

        void OnDestroy()
        {
            if (disc)
                Destroy(disc);
            if (audioSource && audioSource.clip)
                Destroy(audioSource.clip);
        }
    }
}
