using System;
using System.Collections.Generic;
using System.IO;
using Unity.Profiling;
using UnityEngine;

namespace CoDriver
{
    // Local qualification measurements, never an observer input.
    public sealed class PerformanceProbe : MonoBehaviour
    {
        readonly List<ProfilerRecorder> counters = new List<ProfilerRecorder>();
        readonly List<string> names = new List<string>();
        readonly FrameTiming[] timing = new FrameTiming[1];
        StreamWriter writer;
        float next;

        public void Initialize(CoDriverApp app)
        {
            foreach (
                var name in new[]
                {
                    "Main Thread",
                    "Render Thread",
                    "Physics.Simulate",
                    "Physics.Processing",
                    "BehaviourUpdate",
                    "GC.Alloc",
                    "Draw Calls Count",
                    "Batches Count",
                    "Triangles Count",
                    "Vertices Count",
                    "System Used Memory",
                }
            )
            {
                var category =
                    name.Contains("Count") ? ProfilerCategory.Render
                    : name.Contains("Memory") || name == "GC.Alloc" ? ProfilerCategory.Memory
                    : ProfilerCategory.Internal;
                var counter = ProfilerRecorder.StartNew(category, name, 1);
                if (counter.Valid)
                {
                    names.Add(name);
                    counters.Add(counter);
                }
                else
                    counter.Dispose();
            }
            try
            {
                writer = new StreamWriter(
                    Path.Combine(
                        app.Root,
                        "outputs",
                        "sessions",
                        app.Session,
                        "frame-profile.jsonl"
                    )
                );
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning(e.Message);
            }
        }

        void LateUpdate()
        {
            FrameTimingManager.CaptureFrameTimings();
            if (Time.unscaledTime < next)
                return;
            next = Time.unscaledTime + 1;
            FrameTimingManager.GetLatestTimings(1, timing);
            var samples = new Sample[counters.Count];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = new Sample { name = names[i], value = counters[i].LastValue };
            try
            {
                writer?.WriteLine(
                    JsonUtility.ToJson(
                        new Row
                        {
                            seconds = Time.unscaledTime,
                            cpu_ms = timing[0].cpuFrameTime,
                            gpu_ms = timing[0].gpuFrameTime,
                            counters = samples,
                            camera_submissions = CameraRenderCost.Drain(),
                        }
                    )
                );
                writer?.Flush();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                writer?.Dispose();
                writer = null;
            }
        }

        [Serializable]
        class Row
        {
            public double seconds,
                cpu_ms,
                gpu_ms;
            public Sample[] counters;
            public CameraRenderCost.Sample[] camera_submissions;
        }

        [Serializable]
        class Sample
        {
            public string name;
            public long value;
        }

        void OnDestroy()
        {
            writer?.Dispose();
            foreach (var counter in counters)
                counter.Dispose();
        }
    }
}
