using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
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
            var requested = new HashSet<string>(
                new[]
                {
                    "Main Thread",
                    "Render Thread",
                    "CPU Main Thread Frame Time",
                    "CPU Render Thread Frame Time",
                    "Gfx.WaitForRenderThread",
                    "Physics.Simulate",
                    "Physics.Processing",
                    "BehaviourUpdate",
                    "BehaviourFixedUpdate",
                    "Physics.UpdateBodies",
                    "Animator.Update",
                    "GC.Alloc",
                    "Draw Calls Count",
                    "Batches Count",
                    "Triangles Count",
                    "Vertices Count",
                    "System Used Memory",
                }
            );
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var descriptions = new List<Description>();
            foreach (var handle in handles)
            {
                var description = ProfilerRecorderHandle.GetDescription(handle);
                descriptions.Add(
                    new Description
                    {
                        name = description.Name,
                        category = description.Category.ToString(),
                        unit = description.UnitType.ToString(),
                    }
                );
                if (!requested.Contains(description.Name))
                    continue;
                // Categories are supplied by the runtime, not guessed from the
                // marker's spelling. Some timings do not exist in release players.
                var counter = ProfilerRecorder.StartNew(description.Category, description.Name, 1);
                if (counter.Valid)
                {
                    names.Add(description.Name);
                    counters.Add(counter);
                }
                else
                    counter.Dispose();
            }
            try
            {
                var folder = Path.Combine(app.Root, "outputs", "sessions", app.Session);
                Directory.CreateDirectory(folder);
                File.WriteAllText(
                    Path.Combine(folder, "profiler-catalog.json"),
                    JsonUtility.ToJson(
                        new Catalog
                        {
                            available = descriptions.ToArray(),
                            requested_unavailable = requested.Except(names).ToArray(),
                        },
                        true
                    )
                );
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
        class Description
        {
            public string name,
                category,
                unit;
        }

        [Serializable]
        class Catalog
        {
            public Description[] available;
            public string[] requested_unavailable;
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
