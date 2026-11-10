using UnityEngine;
using UnityEngine.UI;

namespace CoDriver
{
    public sealed class CockpitInstruments : MonoBehaviour
    {
        Vehicle vehicle;
        Text speed, gear, rpm, indicators;
        Image revs;

        public void Initialize(Vehicle owner, AssetDefinition definition)
        {
            vehicle = owner;
            var go = new GameObject("Integrated digital instrument cluster", typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.localPosition = definition.instrumentCentre;
            rect.localRotation = Quaternion.Euler(definition.instrumentEuler);
            rect.localScale = Vector3.one * .001f;
            rect.sizeDelta = definition.instrumentSize * 1000;
            go.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            UiDrawing.Panel("Cluster glass", rect, new Color(.007f, .012f, .018f, 1));
            speed = UiDrawing.Label("0", rect, 48, new Vector2(.34f, .54f), Vector2.zero,
                new Vector2(190, 80), TextAnchor.MiddleCenter);
            speed.font = UiDrawing.Bold;
            UiDrawing.Label("mph", rect, 14, new Vector2(.34f, .15f), Vector2.zero,
                new Vector2(100, 25), TextAnchor.MiddleCenter);
            gear = UiDrawing.Label("D1", rect, 28, new Vector2(.66f, .60f), Vector2.zero,
                new Vector2(100, 60), TextAnchor.MiddleCenter);
            rpm = UiDrawing.Label("", rect, 14, new Vector2(.66f, .24f), Vector2.zero,
                new Vector2(180, 25), TextAnchor.MiddleCenter);
            indicators = UiDrawing.Label("", rect, 28, new Vector2(.47f, .68f), Vector2.zero,
                new Vector2(90, 40), TextAnchor.MiddleCenter);
            indicators.color = new Color(.2f, 1, .55f);
            var bar = UiDrawing.Rect("Engine revs", rect, new Vector2(.5f, 0),
                new Vector2(0, 8), new Vector2(650, 4));
            revs = bar.gameObject.AddComponent<Image>();
            revs.color = new Color(.2f, .65f, .95f);
            foreach (var child in GetComponentsInChildren<Transform>())
                child.gameObject.layer = 9;
        }

        void LateUpdate()
        {
            if (!vehicle) return;
            speed.text = (vehicle.Speed * 2.236936f).ToString("F0");
            gear.text = vehicle.Reverse ? "R" : "D" + vehicle.Gear;
            rpm.text = Mathf.RoundToInt(vehicle.EngineRpm).ToString() + " rpm";
            indicators.text = Time.time % 1 < .5f
                ? vehicle.Indicator < 0 ? "\u25c0" : vehicle.Indicator > 0 ? "\u25b6" : "" : "";
            revs.rectTransform.sizeDelta = new Vector2(650 * Mathf.Clamp01(vehicle.EngineRpm / 6800), 4);
        }
    }
}
