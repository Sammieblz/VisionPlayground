using UnityEngine;
using UnityEngine.UI;

namespace CoDriver
{
    // Layout follows openpilot v0.9.7 annotated_camera.cc and alerts.cc (MIT).
    // UI state is read-only; accepted authority is supplied separately by the arbiter.
    public sealed class OpenpilotDisplay : MonoBehaviour
    {
        CoDriverApp app;
        RawImage cameraImage;
        ModelOverlayGraphic model;
        Text speed,
            cruise,
            maxLabel,
            status,
            alertMain,
            alertDetail,
            controlNotice,
            observer;
        Image engagement,
            alert,
            frame;
        RectTransform alertRect;
        public bool HasMatchingGeometry => model && model.Geometry != null;
        public int GeometryVertexCount => model ? model.GeometryVertexCount : 0;

        public void Initialize(CoDriverApp owner)
        {
            app = owner;
            frame = UiDrawing.Panel("Engagement border", transform, new Color32(23, 23, 23, 255));
            var inner = UiDrawing.Rect(
                "Camera viewport",
                transform,
                Vector2.one * .5f,
                Vector2.zero,
                Vector2.zero
            );
            UiDrawing.Fill(inner);
            inner.offsetMin = new Vector2(30, 30);
            inner.offsetMax = new Vector2(-30, -30);
            cameraImage = inner.gameObject.AddComponent<RawImage>();
            cameraImage.color = Color.white;
            cameraImage.raycastTarget = false;
            var overlay = UiDrawing.Rect(
                "Actual model geometry",
                inner,
                Vector2.one * .5f,
                Vector2.zero,
                Vector2.zero
            );
            UiDrawing.Fill(overlay);
            model = overlay.gameObject.AddComponent<ModelOverlayGraphic>();
            model.raycastTarget = false;
            var shade = UiDrawing.Rect(
                "Upstream header shading",
                inner,
                new Vector2(.5f, 1),
                new Vector2(0, -210),
                new Vector2(0, 420)
            );
            shade.anchorMin = new Vector2(0, 1);
            shade.anchorMax = Vector2.one;
            shade.gameObject.AddComponent<HeaderShade>().raycastTarget = false;
            var max = UiDrawing.Rect(
                "MAX panel",
                inner,
                new Vector2(0, 1),
                new Vector2(146, -147),
                new Vector2(172, 204)
            );
            var maxPanel = max.gameObject.AddComponent<RoundedPanel>();
            maxPanel.color = new Color(0, 0, 0, 166f / 255);
            maxPanel.BorderWidth = 6;
            maxPanel.Radius = 32;
            maxPanel.BorderColor = new Color(1, 1, 1, 75f / 255);
            maxLabel = UiDrawing.Label(
                "MAX",
                max,
                40,
                new Vector2(.5f, 1),
                new Vector2(0, -50),
                new Vector2(140, 50),
                TextAnchor.MiddleCenter
            );
            cruise = UiDrawing.Label(
                "—",
                max,
                90,
                new Vector2(.5f, 0),
                new Vector2(0, 70),
                new Vector2(170, 110),
                TextAnchor.MiddleCenter
            );
            speed = UiDrawing.Label(
                "0",
                inner,
                176,
                new Vector2(.5f, 1),
                new Vector2(0, -142),
                new Vector2(500, 215),
                TextAnchor.MiddleCenter
            );
            UiDrawing.Label(
                "mph",
                inner,
                66,
                new Vector2(.5f, 1),
                new Vector2(0, -267),
                new Vector2(240, 110),
                TextAnchor.MiddleCenter
            );
            var engage = UiDrawing.Rect(
                "Engagement",
                inner,
                Vector2.one,
                new Vector2(-135, -135),
                new Vector2(192, 192)
            );
            var circle = engage.gameObject.AddComponent<RoundedPanel>();
            circle.Radius = 96;
            engagement = circle;
            engagement.color = UiDrawing.Dark;
            var icon = UiDrawing.Rect(
                "Upstream steering icon",
                engage,
                Vector2.one * .5f,
                Vector2.zero,
                new Vector2(156, 156)
            );
            var wheel = icon.gameObject.AddComponent<RawImage>();
            wheel.texture = Resources.Load<Texture2D>("Comma/engagement");
            wheel.raycastTarget = false;
            status = UiDrawing.Label(
                "HUMAN",
                inner,
                24,
                Vector2.one,
                new Vector2(-135, -265),
                new Vector2(280, 55),
                TextAnchor.MiddleCenter
            );
            observer = UiDrawing.Label(
                "",
                inner,
                30,
                new Vector2(.5f, 0),
                new Vector2(0, 55),
                new Vector2(1550, 60),
                TextAnchor.MiddleCenter
            );
            CompanionFace.Create(
                inner,
                app ? app.Companion : null,
                new Vector2(1, 0),
                new Vector2(-95, 75),
                new Vector2(110, 105)
            );
            controlNotice = UiDrawing.Label(
                "",
                inner,
                30,
                new Vector2(.5f, 0),
                new Vector2(0, 140),
                new Vector2(1550, 100),
                TextAnchor.MiddleCenter
            );
            alertRect = UiDrawing.Rect(
                "Comma control alert",
                inner,
                new Vector2(.5f, 0),
                new Vector2(0, 208),
                new Vector2(1750, 373)
            );
            alert = alertRect.gameObject.AddComponent<RoundedPanel>();
            alert.color = new Color32(218, 111, 37, 255);
            alertMain = UiDrawing.Label(
                "",
                alertRect,
                70,
                new Vector2(.5f, .65f),
                Vector2.zero,
                new Vector2(1650, 150),
                TextAnchor.MiddleCenter
            );
            alertDetail = UiDrawing.Label(
                "",
                alertRect,
                46,
                new Vector2(.5f, .25f),
                Vector2.zero,
                new Vector2(1650, 130),
                TextAnchor.MiddleCenter
            );
            speed.font = cruise.font = alertMain.font = UiDrawing.Bold;
            maxLabel.font = UiDrawing.Semibold;
        }

        public void Refresh()
        {
            if (!app || !app.Sensors)
                return;
            Present(
                app.CommaUi,
                app.Session,
                Time.realtimeSinceStartupAsDouble,
                app.Vehicle.Speed,
                app.AutomationActive,
                app.Arbiter.Requested,
                app.Paused,
                app.Sensors.LatestTexture,
                app.Sensors.LatestFrame,
                app.Sensors.FrameTexture,
                app.Companion.Message,
                app.Companion.Severity,
                app.Arbiter.Notice
            );
        }

        // A pure presentation boundary also used by isolated fixed-state capture tests.
        // No vehicle, input action or actuator callback is accepted here.
        public void Present(
            OpenpilotUiSnapshot snapshot,
            string session,
            double now,
            float measuredSpeed,
            bool accepted,
            bool requested,
            bool paused,
            Texture latest,
            long latestFrame,
            System.Func<long, Texture> lookup,
            string observerMessage,
            string observerSeverity,
            string requestNotice = ""
        )
        {
            bool fresh = snapshot != null && snapshot.Fresh(session, now);
            bool active = fresh && snapshot.active && snapshot.healthy && accepted && !paused;
            frame.color = active ? UiDrawing.Green : new Color32(23, 23, 23, 255);
            engagement.color = active ? UiDrawing.Green : new Color32(35, 35, 35, 255);
            speed.text = (measuredSpeed * 2.236936f).ToString("F0");
            cruise.text =
                fresh
                && snapshot.enabled
                && snapshot.cruise_speed_kph > 0
                && snapshot.cruise_speed_kph < 250
                    ? (snapshot.cruise_speed_kph / 1.609344f).ToString("F0")
                    : "—";
            status.text =
                active ? "COMMA ACTIVE"
                : requested ? "ENGAGING"
                : fresh && snapshot.healthy && snapshot.engageable ? "READY · HUMAN"
                : fresh ? "HUMAN DRIVING"
                : "COMMA UNAVAILABLE";
            bool cruiseSet =
                fresh
                && snapshot.enabled
                && snapshot.cruise_speed_kph > 0
                && snapshot.cruise_speed_kph < 250;
            maxLabel.color = cruiseSet
                ? (active ? new Color32(128, 216, 166, 255) : Color.white)
                : new Color32(166, 166, 166, 255);
            cruise.color = cruiseSet ? Color.white : new Color32(114, 114, 114, 255);
            Texture image = latest;
            long frameId = latestFrame;
            if (fresh && snapshot.road != null)
            {
                var matching = lookup?.Invoke(snapshot.road.frame_id);
                if (matching && now - snapshot.road.timestamp < OpenpilotUiSnapshot.GeometryMaxAge)
                {
                    image = matching;
                    frameId = snapshot.road.frame_id;
                }
            }
            cameraImage.texture = image;
            var rect = cameraImage.rectTransform.rect;
            float visible = (964f / 604) / (rect.width / Mathf.Max(1, rect.height));
            var crop = new Rect(0, (1 - visible) * .5f, 1, visible);
            cameraImage.uvRect = crop;
            model.Geometry =
                fresh && snapshot.GeometryFresh(session, frameId, now) ? snapshot.road : null;
            model.Calibration = snapshot?.calibration;
            model.Crop = crop;
            model.Engaged = active;
            model.SetVerticesDirty();
            bool controlAlert =
                fresh
                && !string.IsNullOrWhiteSpace(snapshot.alert_text_1)
                && snapshot.alert_size != "none";
            alertRect.gameObject.SetActive(controlAlert);
            if (controlAlert)
            {
                if (snapshot.alert_status == "critical" || snapshot.alert_status == "userPrompt")
                    status.text = "TAKEOVER";
                bool full = snapshot.alert_size == "full",
                    small = snapshot.alert_size == "small";
                float height =
                    full ? rect.height
                    : small ? 271 - 80
                    : 420 - 80;
                alertRect.sizeDelta = new Vector2(
                    Mathf.Max(400, rect.width - (full ? 0 : 80)),
                    height
                );
                alertRect.anchoredPosition = new Vector2(0, height * .5f + (full ? 0 : 40));
                ((RoundedPanel)alert).Radius = full ? 0 : 30;
                alertMain.fontSize =
                    full ? (snapshot.alert_text_1.Length > 15 ? 132 : 177)
                    : small ? 74
                    : 88;
                alertDetail.fontSize = full ? 88 : 66;
                alertMain.font = small ? UiDrawing.Semibold : UiDrawing.Bold;
                alertMain.rectTransform.anchorMin = alertMain.rectTransform.anchorMax = new Vector2(
                    .5f,
                    full ? 1 : .5f
                );
                alertMain.rectTransform.sizeDelta = new Vector2(
                    alertRect.sizeDelta.x - 40,
                    full ? 600 : 150
                );
                alertMain.rectTransform.anchoredPosition = new Vector2(
                    0,
                    full ? -(snapshot.alert_text_1.Length > 15 ? 240 : 270) - 300
                        : small ? 0
                        : 50
                );
                alertDetail.rectTransform.anchorMin = alertDetail.rectTransform.anchorMax =
                    new Vector2(.5f, full ? 0 : .5f);
                alertDetail.rectTransform.sizeDelta = new Vector2(
                    alertRect.sizeDelta.x - 40,
                    full ? 300 : 130
                );
                alertDetail.rectTransform.anchoredPosition = new Vector2(
                    0,
                    full ? (snapshot.alert_text_1.Length > 15 ? 361 : 420) - 150 : -75
                );
                alertDetail.gameObject.SetActive(!small);
                alert.color =
                    snapshot.alert_status == "critical" ? new Color32(201, 34, 49, 255)
                    : snapshot.alert_status == "userPrompt" ? new Color32(218, 111, 37, 255)
                    : UiDrawing.Dark;
                alertMain.text = snapshot.alert_text_1;
                alertDetail.text = snapshot.alert_text_2;
            }
            observer.gameObject.SetActive(!controlAlert);
            controlNotice.gameObject.SetActive(!controlAlert && !active);
            controlNotice.text = requestNotice;
            observer.text = "CO-DRIVER  ·  " + observerMessage;
            observer.color = observerSeverity == "urgent" ? new Color(1, .35f, .2f) : Color.white;
        }

        void LateUpdate() => Refresh();
    }
}
