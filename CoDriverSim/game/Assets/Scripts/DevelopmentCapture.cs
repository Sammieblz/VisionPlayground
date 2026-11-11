#if DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;

namespace CoDriver
{
    // Explicit diagnostic player only. Never compiled into a release application.
    public sealed class DevelopmentCapture : MonoBehaviour
    {
        int stage;
        float began;
        string folder;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Initialize()
        {
            if (!Environment.GetCommandLineArgs().Contains("-profile-window"))
                return;
            Profiler.enabled = false;
            var go = new GameObject("Explicit development profiling window");
            DontDestroyOnLoad(go);
            go.AddComponent<DevelopmentCapture>();
        }

        void Update()
        {
            if (!CoDriverApp.Instance || Time.realtimeSinceStartup < 70)
                return;
            if (stage == 0)
            {
                folder = Path.Combine(
                    CoDriverApp.Instance.Root,
                    "outputs",
                    "sessions",
                    CoDriverApp.Instance.Session
                );
                Directory.CreateDirectory(folder);
                Audit();
                began = Time.realtimeSinceStartup;
                stage = 1;
            }
            if (stage == 1 && Time.realtimeSinceStartup - began > 2)
            {
                foreach (ProfilerArea area in Enum.GetValues(typeof(ProfilerArea)))
                    Profiler.SetAreaEnabled(
                        area,
                        area == ProfilerArea.CPU || area == ProfilerArea.Memory
                    );
                Profiler.logFile = Path.Combine(folder, "cpu-window.raw");
                Profiler.enableBinaryLog = true;
                Profiler.enabled = true;
                began = Time.realtimeSinceStartup;
                stage = 2;
            }
            if (stage == 2 && Time.realtimeSinceStartup - began > 10)
            {
                Profiler.enabled = false;
                Profiler.enableBinaryLog = false;
                Profiler.logFile = "";
                stage = 3;
                Debug.Log("CODRIVER_DIAGNOSTIC_CAPTURE_COMPLETE " + folder);
            }
        }

        void Audit()
        {
            var textures = Resources
                .FindObjectsOfTypeAll<Texture>()
                .Select(t => new Item
                {
                    name = t.name,
                    kind = t.GetType().Name,
                    bytes = Profiler.GetRuntimeMemorySizeLong(t),
                    width = t.width,
                    height = t.height,
                    readable = t is Texture2D texture && texture.isReadable,
                    format = t is Texture2D image ? image.format.ToString() : "",
                })
                .OrderByDescending(t => t.bytes)
                .ToArray();
            var renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                .Where(r => r.enabled && r.gameObject.activeInHierarchy && r.isVisible)
                .GroupBy(r => r.transform.root.name)
                .Select(g => new Item
                {
                    name = g.Key,
                    kind = "visible renderer group",
                    width = g.Count(),
                    height = g.Sum(r => r.sharedMaterials.Length),
                })
                .OrderByDescending(g => g.height)
                .ToArray();
            File.WriteAllText(
                Path.Combine(folder, "memory-render-audit.json"),
                JsonUtility.ToJson(
                    new AuditReport
                    {
                        textures = textures,
                        visible_groups = renderers,
                        texture_total_bytes = textures.Sum(t => t.bytes),
                        managed_used_bytes = Profiler.GetMonoUsedSizeLong(),
                        managed_reserved_bytes = Profiler.GetMonoHeapSizeLong(),
                    },
                    true
                )
            );
        }

        [Serializable]
        class Item
        {
            public string name,
                kind,
                format;
            public long bytes;
            public int width,
                height;
            public bool readable;
        }

        [Serializable]
        class AuditReport
        {
            public long texture_total_bytes,
                managed_used_bytes,
                managed_reserved_bytes;
            public Item[] textures,
                visible_groups;
        }
    }
}
#endif
