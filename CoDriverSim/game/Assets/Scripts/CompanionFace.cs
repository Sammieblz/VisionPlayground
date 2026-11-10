using UnityEngine;
using UnityEngine.UI;

namespace CoDriver
{
    // Screen presentation only: no actor queries, world truth or actuator references.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CompanionFace : MaskableGraphic
    {
        public Companion Observer;
        float gaze;
        Color tint = new Color(.27f, .91f, .76f);

        public static CompanionFace Create(Transform parent, Companion observer,
            Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect = UiDrawing.Rect("Animated camera companion", parent, anchor, position, size);
            var face = rect.gameObject.AddComponent<CompanionFace>();
            face.Observer = observer;
            face.raycastTarget = false;
            return face;
        }

        void Update()
        {
            string state = Observer ? Observer.State : "disconnected";
            bool urgent = state == "urgent";
            Color target = urgent ? new Color(1, .30f, .22f)
                : state == "caution" ? new Color(1, .73f, .23f)
                : state == "advisory" ? new Color(.40f, .77f, 1)
                : state == "muted" || state == "disconnected" ? new Color(.45f, .52f, .55f)
                : new Color(.27f, .91f, .76f);
            tint = urgent ? target : Color.Lerp(tint, target, Time.unscaledDeltaTime * 9);
            float targetGaze = urgent ? 0 : Mathf.Sin(Time.unscaledTime * .36f) * .035f;
            gaze = Mathf.Lerp(gaze, targetGaze, Time.unscaledDeltaTime * 5);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            string state = Observer ? Observer.State : "disconnected";
            float t = Time.unscaledTime;
            bool urgent = state == "urgent";
            float breath = urgent ? 0 : Mathf.Sin(t * 1.6f) * .008f;
            Rounded(vh, .05f, .17f + breath, .90f, .67f, .19f, new Color(.21f, .27f, .29f));
            Rounded(vh, .075f, .19f + breath, .85f, .62f, .17f, new Color(.79f, .84f, .84f));
            Rounded(vh, .12f, .245f + breath, .76f, .51f, .145f, new Color(.025f, .05f, .065f));
            Rounded(vh, .0f, .40f + breath, .065f, .20f, .03f, tint);
            Rounded(vh, .935f, .40f + breath, .065f, .20f, .03f, tint);
            Rounded(vh, .48f, .83f + breath, .04f, .09f, .02f, new Color(.66f, .74f, .75f));
            Rounded(vh, .455f, .91f + breath, .09f, .07f, .035f, tint);
            float cycle = t % 5.3f;
            float blink = urgent ? 1 : state == "muted" ? .15f
                : cycle < .16f ? Mathf.Max(.08f, Mathf.Abs(cycle - .08f) / .08f) : 1;
            float eyeH = (state == "caution" ? .18f : .22f) * blink;
            for (int side = -1; side <= 1; side += 2)
            {
                float x = .5f + side * .18f + gaze;
                if (state == "disconnected")
                {
                    Line(vh, new Vector2(x - .055f, .51f), new Vector2(x + .055f, .62f), .025f, tint);
                    Line(vh, new Vector2(x - .055f, .62f), new Vector2(x + .055f, .51f), .025f, tint);
                }
                else
                    Rounded(vh, x - .055f, .57f - eyeH / 2 + breath, .11f, eyeH, .05f, tint);
            }
            if (urgent || state == "caution")
                Rounded(vh, .46f, .32f + breath, .08f, urgent ? .11f : .06f, .03f, tint);
            else
            {
                Vector2 last = new Vector2(.39f, .36f + breath);
                for (int i = 1; i <= 12; i++)
                {
                    float u = i / 12f;
                    var p = new Vector2(.39f + .22f * u,
                        .36f + breath - (state == "attentive" ? .035f : 0) * Mathf.Sin(u * Mathf.PI));
                    Line(vh, last, p, .018f, tint);
                    last = p;
                }
            }
        }

        Vector2 Point(Vector2 p)
        {
            var r = rectTransform.rect;
            return new Vector2(r.xMin + p.x * r.width, r.yMin + p.y * r.height);
        }

        void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color c) =>
            UiDrawing.Line(vh, Point(a), Point(b), width * rectTransform.rect.width, c);

        void Rounded(VertexHelper vh, float x, float y, float w, float h, float radius, Color c)
        {
            radius = Mathf.Min(radius, Mathf.Min(w, h) / 2);
            int start = vh.currentVertCount;
            vh.AddVert(Point(new Vector2(x + w / 2, y + h / 2)), c, Vector2.zero);
            for (int corner = 0; corner < 4; corner++)
            {
                var centre = new Vector2(corner < 2 ? x + w - radius : x + radius,
                    corner == 0 || corner == 3 ? y + radius : y + h - radius);
                for (int i = 0; i <= 8; i++)
                {
                    float angle = (-90 + corner * 90 + i * 90f / 8) * Mathf.Deg2Rad;
                    vh.AddVert(Point(centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius), c, Vector2.zero);
                }
            }
            for (int i = 0; i < 36; i++)
                vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % 36);
        }
    }
}
