using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CoDriver
{
    public static class UiDrawing
    {
        public static readonly Color Green = new Color32(23, 134, 68, 255),
            Dark = new Color32(22, 22, 22, 255);
        public static Font Regular =>
            Resources.Load<Font>("Comma/Inter-Regular")
            ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        public static Font Bold => Resources.Load<Font>("Comma/Inter-Bold") ?? Regular;
        public static Font Semibold => Resources.Load<Font>("Comma/Inter-SemiBold") ?? Regular;

        public static RectTransform Rect(
            string name,
            Transform parent,
            Vector2 anchor,
            Vector2 position,
            Vector2 size
        )
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        public static void Fill(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        public static Image Panel(string name, Transform parent, Color color)
        {
            var r = Rect(name, parent, Vector2.one * .5f, Vector2.zero, Vector2.zero);
            Fill(r);
            var i = r.gameObject.AddComponent<Image>();
            i.color = color;
            i.raycastTarget = false;
            return i;
        }

        public static Text Label(
            string value,
            Transform parent,
            int size,
            Vector2 anchor,
            Vector2 position,
            Vector2 dimensions,
            TextAnchor align = TextAnchor.MiddleLeft
        )
        {
            var r = Rect(value, parent, anchor, position, dimensions);
            var t = r.gameObject.AddComponent<Text>();
            t.font = Regular;
            t.fontSize = size;
            t.text = value;
            t.color = Color.white;
            t.alignment = align;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        public static Button Button(
            string text,
            Transform parent,
            Vector2 anchor,
            Vector2 position,
            Vector2 dimensions,
            Action action
        )
        {
            var r = Rect(text, parent, anchor, position, dimensions);
            var i = r.gameObject.AddComponent<RoundedPanel>();
            i.color = Color.white;
            var b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = i;
            var c = b.colors;
            c.normalColor = Dark;
            c.highlightedColor = new Color(.18f, .32f, .26f);
            c.selectedColor = Green;
            c.pressedColor = new Color(.2f, .55f, .35f);
            b.colors = c;
            Label(text, r, 30, Vector2.one * .5f, Vector2.zero, dimensions - new Vector2(35, 10));
            b.onClick.AddListener(() => action());
            return b;
        }

        public static void Quad(
            VertexHelper vh,
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 d,
            Color color
        )
        {
            int n = vh.currentVertCount;
            vh.AddVert(a, color, Vector2.zero);
            vh.AddVert(b, color, Vector2.zero);
            vh.AddVert(c, color, Vector2.zero);
            vh.AddVert(d, color, Vector2.zero);
            vh.AddTriangle(n, n + 1, n + 2);
            vh.AddTriangle(n, n + 2, n + 3);
        }

        public static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
        {
            var side = new Vector2(-(b - a).y, (b - a).x).normalized * width * .5f;
            Quad(vh, a + side, b + side, b - side, a - side, color);
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class HeaderShade : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = rectTransform.rect;
            for (int row = 0; row < 3; row++)
            {
                float y =
                    row == 0 ? rect.yMin
                    : row == 1 ? rect.yMin + rect.height * .4f
                    : rect.yMax;
                Color tint = new Color(0, 0, 0, row == 0 ? 0 : .45f);
                mesh.AddVert(new Vector3(rect.xMin, y), tint, Vector2.zero);
                mesh.AddVert(new Vector3(rect.xMax, y), tint, Vector2.zero);
            }
            mesh.AddTriangle(0, 2, 1);
            mesh.AddTriangle(1, 2, 3);
            mesh.AddTriangle(2, 4, 3);
            mesh.AddTriangle(3, 4, 5);
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MapGraphic : MaskableGraphic, IPointerClickHandler
    {
        public MapDefinition Map;
        public Navigation Navigation;
        public Transform Player;
        public bool Full;
        public float Range = 130,
            Heading;
        public Vector3 Centre;
        public Action<Vector3> Selected;
        MapDefinition drawnMap;
        Texture2D raster;
        public override Texture mainTexture => raster ? raster : Texture2D.whiteTexture;
        readonly Dictionary<MapLane, Vector3[]> roadLines = new Dictionary<MapLane, Vector3[]>();
        float nextMesh;
        MapOverlay overlay;

        protected override void OnEnable()
        {
            base.OnEnable();
            if (!overlay)
            {
                var rect = UiDrawing.Rect(
                    "Map markers",
                    transform,
                    Vector2.one * .5f,
                    Vector2.zero,
                    Vector2.zero
                );
                UiDrawing.Fill(rect);
                overlay = rect.gameObject.AddComponent<MapOverlay>();
                overlay.Map = this;
                overlay.raycastTarget = false;
            }
        }

        public Vector2 Project(Vector3 point)
        {
            var delta = point - Centre;
            float angle = -Heading * Mathf.Deg2Rad;
            return new Vector2(
                    delta.x * Mathf.Cos(angle) + delta.z * Mathf.Sin(angle),
                    -delta.x * Mathf.Sin(angle) + delta.z * Mathf.Cos(angle)
                )
                * rectTransform.rect.width
                / (Range * 2);
        }

        public Vector3 Unproject(Vector2 point)
        {
            point *= Range * 2 / rectTransform.rect.width;
            float angle = Heading * Mathf.Deg2Rad;
            return Centre
                + new Vector3(
                    point.x * Mathf.Cos(angle) + point.y * Mathf.Sin(angle),
                    0,
                    -point.x * Mathf.Sin(angle) + point.y * Mathf.Cos(angle)
                );
        }

        void Update()
        {
            if (drawnMap != Map)
            {
                drawnMap = Map;
                raster =
                    Map == null ? null : Resources.Load<Texture2D>("Maps/" + Map.id + "-minimap");
                roadLines.Clear();
                if (Map != null)
                    foreach (var lane in Map.lanes)
                    {
                        if (
                            raster
                            && !Array.Exists(
                                lane.points,
                                p => Map.OutsideRaster(p) && Map.Contains(p)
                            )
                        )
                            continue;
                        var simplified = new List<Vector3>();
                        LineUtility.Simplify(new List<Vector3>(lane.points), .12f, simplified);
                        roadLines[lane] = simplified.ToArray();
                    }
                SetMaterialDirty();
                SetVerticesDirty();
                overlay?.SetVerticesDirty();
            }
            if (!Player || Map == null)
                return;
            if (Full)
            {
                Centre = (Map.boundsMin + Map.boundsMax) * .5f;
                Range =
                    Mathf.Max(Map.boundsMax.x - Map.boundsMin.x, Map.boundsMax.z - Map.boundsMin.z)
                    * .56f;
                Heading = 0;
            }
            else
            {
                Centre = Player.position;
                Heading = Mathf.LerpAngle(
                    Heading,
                    Player.eulerAngles.y,
                    1 - Mathf.Exp(-Time.unscaledDeltaTime * 10)
                );
                float speed = Player.GetComponent<Vehicle>().Speed;
                Range = Mathf.Lerp(
                    Range,
                    90 + speed * 3.5f,
                    1 - Mathf.Exp(-Time.unscaledDeltaTime * 2)
                );
            }
            if (Time.unscaledTime >= nextMesh)
            {
                nextMesh = Time.unscaledTime + .05f;
                SetVerticesDirty();
                overlay?.SetVerticesDirty();
            }
        }

        bool Visible(Vector3 a, Vector3 b) =>
            Mathf.Min(a.x, b.x) < Centre.x + Range * 1.5f
            && Mathf.Max(a.x, b.x) > Centre.x - Range * 1.5f
            && Mathf.Min(a.z, b.z) < Centre.z + Range * 1.5f
            && Mathf.Max(a.z, b.z) > Centre.z - Range * 1.5f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (Map == null)
                return;
            if (drawnMap != Map)
                return;
            float scale = rectTransform.rect.width / (Range * 2);
            if (raster)
            {
                var lo = Map.RasterBoundsMin;
                var hi = Map.RasterBoundsMax;
                vh.AddVert(Project(lo), Color.white, new Vector2(0, 0));
                vh.AddVert(Project(new Vector3(lo.x, 0, hi.z)), Color.white, new Vector2(0, 1));
                vh.AddVert(Project(hi), Color.white, new Vector2(1, 1));
                vh.AddVert(Project(new Vector3(hi.x, 0, lo.z)), Color.white, new Vector2(1, 0));
                vh.AddTriangle(0, 1, 2);
                vh.AddTriangle(0, 2, 3);
            }
            else
            {
                foreach (var building in Map.buildings)
                {
                    if (
                        building.roof.vertices.Length == 0
                        || !Visible(building.roof.vertices[0], building.roof.vertices[0])
                    )
                        continue;
                    var v = building.roof.vertices;
                    var t = building.roof.triangles;
                    for (int i = 0; i < t.Length; i += 3)
                    {
                        if (vh.currentVertCount > 57000)
                            break;
                        int n = vh.currentVertCount;
                        for (int j = 0; j < 3; j++)
                            vh.AddVert(
                                Project(v[t[i + j]]),
                                new Color32(70, 75, 76, 255),
                                Vector2.zero
                            );
                        vh.AddTriangle(n, n + 1, n + 2);
                    }
                }
                foreach (var lane in Map.lanes)
                {
                    var points = roadLines[lane];
                    for (int i = 1; i < points.Length; i++)
                        if (Visible(points[i - 1], points[i]) && vh.currentVertCount < 62000)
                            UiDrawing.Line(
                                vh,
                                Project(points[i - 1]),
                                Project(points[i]),
                                Mathf.Max(1, lane.width * scale),
                                new Color32(169, 175, 175, 255)
                            );
                }
            }
        }

        public void PopulateOverlay(VertexHelper vh)
        {
            vh.Clear();
            if (Map == null)
                return;
            if (raster)
                foreach (var entry in roadLines)
                    for (int i = 1; i < entry.Value.Length; i++)
                    {
                        var a = entry.Value[i - 1];
                        var b = entry.Value[i];
                        if (ClipRasterExtension(Map, ref a, ref b) && Visible(a, b))
                            UiDrawing.Line(
                                vh,
                                Project(a),
                                Project(b),
                                Mathf.Max(
                                    1,
                                    entry.Key.width * rectTransform.rect.width / (Range * 2)
                                ),
                                new Color32(169, 175, 175, 255)
                            );
                    }
            foreach (var closure in Map.Closures)
            {
                Vector3 side = Vector3.Cross(Vector3.up, closure.forward) * closure.width * .5f;
                if (Visible(closure.position, closure.position))
                    UiDrawing.Line(
                        vh,
                        Project(closure.position - side),
                        Project(closure.position + side),
                        3,
                        new Color32(245, 137, 45, 255)
                    );
            }
            if (Navigation != null)
                for (int i = 1; i < Navigation.Route.Count; i++)
                    if (Visible(Navigation.Route[i - 1], Navigation.Route[i]))
                        UiDrawing.Line(
                            vh,
                            Project(Navigation.Route[i - 1]),
                            Project(Navigation.Route[i]),
                            Full ? 3 : 5,
                            new Color32(52, 199, 154, 255)
                        );
            if (Player)
            {
                Vector2 centre = Project(Player.position);
                float angle = (Player.eulerAngles.y - Heading) * Mathf.Deg2Rad;
                Vector2 forward = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)),
                    right = new Vector2(forward.y, -forward.x);
                int n = vh.currentVertCount;
                vh.AddVert(centre + forward * 12, Color.white, Vector2.zero);
                vh.AddVert(centre - forward * 8 - right * 8, Color.white, Vector2.zero);
                vh.AddVert(centre - forward * 4, Color.white, Vector2.zero);
                vh.AddVert(centre - forward * 8 + right * 8, Color.white, Vector2.zero);
                vh.AddTriangle(n, n + 1, n + 2);
                vh.AddTriangle(n, n + 2, n + 3);
            }
            if (Navigation != null && Navigation.Route.Count > 0)
            {
                Vector2 p = Project(Navigation.Route[Navigation.Route.Count - 1]);
                UiDrawing.Quad(
                    vh,
                    p + new Vector2(0, 9),
                    p + new Vector2(7, 0),
                    p + new Vector2(0, -9),
                    p + new Vector2(-7, 0),
                    new Color32(255, 206, 75, 255)
                );
            }
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (
                Full
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rectTransform,
                    e.position,
                    e.pressEventCamera,
                    out var point
                )
            )
                Selected?.Invoke(Unproject(point));
        }

        public static bool ClipRasterExtension(MapDefinition map, ref Vector3 a, ref Vector3 b)
        {
            bool outsideA = map.OutsideRaster(a),
                outsideB = map.OutsideRaster(b);
            if (!outsideA && !outsideB)
                return false;
            if (outsideA && outsideB)
                return true;
            Vector3 inside = outsideA ? b : a,
                outside = outsideA ? a : b;
            Vector3 delta = outside - inside;
            float t = 1;
            if (delta.x > 0)
                t = Mathf.Min(t, (map.RasterBoundsMax.x - inside.x) / delta.x);
            if (delta.x < 0)
                t = Mathf.Min(t, (map.RasterBoundsMin.x - inside.x) / delta.x);
            if (delta.z > 0)
                t = Mathf.Min(t, (map.RasterBoundsMax.z - inside.z) / delta.z);
            if (delta.z < 0)
                t = Mathf.Min(t, (map.RasterBoundsMin.z - inside.z) / delta.z);
            Vector3 edge = Vector3.Lerp(inside, outside, t);
            if (outsideA)
                b = edge;
            else
                a = edge;
            return true;
        }
    }

    // Solid markers use the white UI texture independently of the map raster.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MapOverlay : MaskableGraphic
    {
        public MapGraphic Map;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (Map)
                Map.PopulateOverlay(vh);
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ModelOverlayGraphic : MaskableGraphic
    {
        public int GeometryVertexCount { get; private set; }
        public UiModelGeometry Geometry;
        public Calibration Calibration;
        public Rect Crop = new Rect(0, 0, 1, 1);
        public bool Engaged;

        Vector2 Project(float x, float y, float z)
        {
            var r = rectTransform.rect;
            float pitch = Calibration.pitch_deg * Mathf.Deg2Rad;
            float depth = x * Mathf.Cos(pitch) + z * Mathf.Sin(pitch);
            z = -x * Mathf.Sin(pitch) + z * Mathf.Cos(pitch);
            x = Mathf.Max(.5f, depth);
            float u = (Calibration.cx + Calibration.fx * y / x) / 964f;
            float v = 1 - (Calibration.cy + Calibration.fy * z / x) / 604f;
            return new Vector2(
                ((u - Crop.x) / Crop.width - .5f) * r.width,
                ((v - Crop.y) / Crop.height - .5f) * r.height
            );
        }

        void Strip(VertexHelper vh, ModelLine line, float halfWidth, float height, Color color)
        {
            if (line?.x == null || line.y == null || line.z == null)
                return;
            int count = Math.Min(line.x.Length, Math.Min(line.y.Length, line.z.Length));
            for (int i = 1; i < count; i++)
            {
                if (
                    line.x[i - 1] < 1
                    || line.x[i] < 1
                    || line.x[i] > 100
                    || !float.IsFinite(line.x[i - 1])
                    || !float.IsFinite(line.x[i])
                    || !float.IsFinite(line.y[i - 1])
                    || !float.IsFinite(line.y[i])
                    || !float.IsFinite(line.z[i - 1])
                    || !float.IsFinite(line.z[i])
                )
                    continue;
                UiDrawing.Quad(
                    vh,
                    Project(line.x[i - 1], line.y[i - 1] - halfWidth, line.z[i - 1] + height),
                    Project(line.x[i], line.y[i] - halfWidth, line.z[i] + height),
                    Project(line.x[i], line.y[i] + halfWidth, line.z[i] + height),
                    Project(line.x[i - 1], line.y[i - 1] + halfWidth, line.z[i - 1] + height),
                    color
                );
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            GeometryVertexCount = 0;
            if (Geometry == null || Calibration == null)
                return;
            Strip(
                vh,
                Geometry.path,
                .9f,
                Calibration.height_m,
                Engaged ? new Color(.15f, .85f, .35f, .45f) : new Color(.9f, .9f, .9f, .35f)
            );
            if (Geometry.lanes != null)
                for (int i = 0; i < Geometry.lanes.Length; i++)
                    if (
                        Geometry.lane_probabilities != null
                        && i < Geometry.lane_probabilities.Length
                        && Geometry.lane_probabilities[i] > .5f
                    )
                        Strip(vh, Geometry.lanes[i], .035f, 0, new Color(1, 1, 1, .7f));
            if (Geometry.edges != null)
                foreach (var edge in Geometry.edges)
                    Strip(vh, edge, .035f, 0, new Color(1, .25f, .2f, .55f));
            if (Geometry.leads != null)
                foreach (var lead in Geometry.leads)
                {
                    if (
                        lead == null
                        || lead.x == null
                        || lead.y == null
                        || lead.prob < .5f
                        || lead.x.Length == 0
                        || lead.y.Length == 0
                        || lead.x[0] < 1
                        || !float.IsFinite(lead.x[0])
                        || !float.IsFinite(lead.y[0])
                    )
                        continue;
                    Vector2 p = Project(lead.x[0], lead.y[0], 0);
                    float size = Mathf.Clamp(800 / lead.x[0], 15, 55);
                    UiDrawing.Line(
                        vh,
                        p + new Vector2(-size, -size * .5f),
                        p,
                        size * .3f,
                        Color.red
                    );
                    UiDrawing.Line(
                        vh,
                        p,
                        p + new Vector2(size, -size * .5f),
                        size * .3f,
                        Color.red
                    );
                }
            GeometryVertexCount = vh.currentVertCount;
        }
    }
}
