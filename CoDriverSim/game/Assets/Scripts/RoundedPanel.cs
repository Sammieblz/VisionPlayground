using UnityEngine;
using UnityEngine.UI;

namespace CoDriver
{
    // Canvas-native rounded panels, with no raster dependency at either UI resolution.
    public sealed class RoundedPanel : Image
    {
        public float Radius = 22;
        public float BorderWidth;
        public Color BorderColor = Color.white;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = rectTransform.rect;
            float r = Mathf.Clamp(Radius, 0, Mathf.Min(rect.width, rect.height) * .5f);
            mesh.AddVert(rect.center, color, Vector2.one * .5f);
            for (int corner = 0; corner < 4; corner++)
            {
                Vector2 centre = new Vector2(
                    corner < 2 ? rect.xMax - r : rect.xMin + r,
                    corner == 0 || corner == 3 ? rect.yMax - r : rect.yMin + r
                );
                for (int i = 0; i <= 8; i++)
                {
                    float angle = (90 - corner * 90 - i * 90f / 8) * Mathf.Deg2Rad;
                    mesh.AddVert(
                        centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r,
                        color,
                        Vector2.zero
                    );
                }
            }
            for (int i = 1; i < 36; i++)
                mesh.AddTriangle(0, i, i + 1);
            mesh.AddTriangle(0, 36, 1);
            if (BorderWidth <= 0)
                return;
            int start = mesh.currentVertCount;
            float inset = Mathf.Min(BorderWidth, r);
            for (int corner = 0; corner < 4; corner++)
            {
                Vector2 centre = new Vector2(
                    corner < 2 ? rect.xMax - r : rect.xMin + r,
                    corner == 0 || corner == 3 ? rect.yMax - r : rect.yMin + r
                );
                for (int i = 0; i <= 8; i++)
                {
                    float angle = (90 - corner * 90 - i * 90f / 8) * Mathf.Deg2Rad;
                    var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    mesh.AddVert(centre + direction * r, BorderColor, Vector2.zero);
                    mesh.AddVert(centre + direction * (r - inset), BorderColor, Vector2.zero);
                }
            }
            for (int i = 0; i < 36; i++)
            {
                int a = start + i * 2,
                    b = start + ((i + 1) % 36) * 2;
                mesh.AddTriangle(a, b, b + 1);
                mesh.AddTriangle(a, b + 1, a + 1);
            }
        }
    }
}
