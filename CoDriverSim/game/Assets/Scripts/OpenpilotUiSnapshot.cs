using System;
using UnityEngine;

namespace CoDriver
{
    [Serializable]
    public sealed class ModelLine
    {
        public float[] x,
            y,
            z;
    }

    [Serializable]
    public sealed class ModelLead
    {
        public float[] x,
            y,
            v;
        public float prob;
    }

    [Serializable]
    public sealed class UiModelGeometry
    {
        public int schema_version;
        public string session_id,
            model;
        public long frame_id;
        public double timestamp;
        public ModelLine[] lanes,
            edges;
        public ModelLine path;
        public ModelLead[] leads;
        public float[] lane_probabilities;
    }

    [Serializable]
    public sealed class StockWarning
    {
        public string kind,
            state,
            source,
            provenance,
            session_id,
            frame_association;
        public bool active,
            frame_known;
        public long frame_id,
            source_log_mono_time,
            model_log_mono_time;
        public double timestamp,
            observed_timestamp;

        public bool Fresh(string session, double now) =>
            frame_known
            && session_id == session
            && state != "unknown"
            && now >= timestamp
            && now - timestamp <= .5;
    }

    // Display data only: deliberately no steering, pedals or control callbacks.
    [Serializable]
    public sealed class OpenpilotUiSnapshot
    {
        // CPU presentation may lag capture; keep its exact camera frame and a
        // bounded half-second display age. Actuator expiry is a separate guard.
        public const double GeometryMaxAge = .5;
        public int schema_version;
        public string session_id,
            alert_text_1,
            alert_text_2,
            alert_status,
            alert_size,
            provenance;
        public long frame_id;
        public double timestamp;
        public bool engageable,
            enabled,
            active,
            healthy;
        public float cruise_speed_kph;
        public Calibration calibration;
        public UiModelGeometry road;
        public StockWarning[] stock_warnings;

        public bool Fresh(string session, double now) =>
            schema_version == 1
            && session_id == session
            && now >= timestamp
            && now - timestamp < .5;

        public bool GeometryFresh(string session, long displayedFrame, double now) =>
            Fresh(session, now)
            && road != null
            && road.schema_version == 2
            && road.session_id == session
            && road.frame_id == displayedFrame
            && now >= road.timestamp
            && now - road.timestamp < GeometryMaxAge;
    }
}
