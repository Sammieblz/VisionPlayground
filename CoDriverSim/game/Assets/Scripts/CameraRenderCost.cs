using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace CoDriver
{
    // CPU submission/wait cost of auxiliary cameras in release builds. These
    // diagnostics are never sensor inputs and do not claim to measure GPU time.
    public static class CameraRenderCost
    {
        [Serializable]
        public sealed class Sample
        {
            public string camera;
            public int calls;
            public double cpu_ms,
                peak_cpu_ms;
        }

        static readonly Dictionary<string, Sample> samples = new Dictionary<string, Sample>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => samples.Clear();

        public static void Render(string name, Camera camera)
        {
            long start = Stopwatch.GetTimestamp();
            try
            {
                camera.Render();
            }
            finally
            {
                double duration = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                if (!samples.TryGetValue(name, out var sample))
                    samples[name] = sample = new Sample { camera = name };
                sample.calls++;
                sample.cpu_ms += duration;
                sample.peak_cpu_ms = Math.Max(sample.peak_cpu_ms, duration);
            }
        }

        public static Sample[] Drain()
        {
            var result = new Sample[samples.Count];
            int index = 0;
            foreach (var sample in samples.Values)
            {
                result[index++] = new Sample
                {
                    camera = sample.camera,
                    calls = sample.calls,
                    cpu_ms = sample.cpu_ms,
                    peak_cpu_ms = sample.peak_cpu_ms,
                };
                sample.calls = 0;
                sample.cpu_ms = sample.peak_cpu_ms = 0;
            }
            return result;
        }
    }
}
