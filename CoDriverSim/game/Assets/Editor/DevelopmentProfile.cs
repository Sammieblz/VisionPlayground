using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace CoDriver.Editor
{
    public static class DevelopmentProfile
    {
        // Uses the already prepared assets in an isolated diagnostic output.
        // Not a package/release builder; all release qualification remains separate.
        public static void Build()
        {
            var output = Path.GetFullPath("../outputs/profile-game/CoDriverSim.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(
                new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/Scenes/Town.unity" },
                    locationPathName = output,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development | BuildOptions.DetailedBuildReport,
                }
            );
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception(report.summary.result.ToString());
            File.WriteAllText(
                Path.Combine(Path.GetDirectoryName(output), "DIAGNOSTIC-ONLY.txt"),
                "Development profiler build. Not eligible for distribution or FPS qualification.\n"
                    + File.ReadAllText("Assets/Resources/BuildIdentity.json")
            );
            Debug.Log("CODRIVER_PROFILE_BUILD_OK " + output);
        }

        public static void Review()
        {
            var args = Environment.GetCommandLineArgs();
            int option = Array.IndexOf(args, "-profile-source");
            if (option < 0 || option + 1 >= args.Length)
                throw new ArgumentException("Expected -profile-source path.raw");
            string path = args[option + 1];
            ProfilerDriver.ClearAllFrames();
            if (!ProfilerDriver.LoadProfile(path, false))
                throw new IOException("Profile could not be loaded");
            var totals = new Dictionary<string, Row>();
            int frames = 0;
            for (
                int frame = ProfilerDriver.firstFrameIndex;
                frame <= ProfilerDriver.lastFrameIndex;
                frame++
            )
            {
                using (
                    var data = ProfilerDriver.GetHierarchyFrameDataView(
                        frame,
                        0,
                        HierarchyFrameDataView.ViewModes.Default,
                        HierarchyFrameDataView.columnDontSort,
                        false
                    )
                )
                {
                    if (!data.valid)
                        continue;
                    frames++;
                    var pending = new Stack<int>();
                    var children = new List<int>();
                    pending.Push(data.GetRootItemID());
                    while (pending.Count > 0)
                    {
                        int id = pending.Pop();
                        string name = data.GetItemPath(id);
                        if (!totals.TryGetValue(name, out var row))
                            totals[name] = row = new Row { path = name };
                        row.inclusive_ms += data.GetItemColumnDataAsDouble(
                            id,
                            HierarchyFrameDataView.columnTotalTime
                        );
                        row.self_ms += data.GetItemColumnDataAsDouble(
                            id,
                            HierarchyFrameDataView.columnSelfTime
                        );
                        row.calls += data.GetItemColumnDataAsDouble(
                            id,
                            HierarchyFrameDataView.columnCalls
                        );
                        children.Clear();
                        data.GetItemChildren(id, children);
                        foreach (int child in children)
                            pending.Push(child);
                    }
                }
            }
            if (frames < 2)
                throw new IOException("No usable profile frames");
            foreach (var row in totals.Values)
            {
                row.inclusive_ms /= frames;
                row.self_ms /= frames;
                row.calls /= frames;
            }
            File.WriteAllText(
                Path.ChangeExtension(path, ".review.json"),
                JsonUtility.ToJson(
                    new ReviewReport
                    {
                        frames = frames,
                        source = path,
                        rows = totals.Values.OrderByDescending(r => r.self_ms).ToArray(),
                    },
                    true
                )
            );
            Debug.Log("CODRIVER_PROFILE_REVIEW_OK " + frames);
        }

        [Serializable]
        class Row
        {
            public string path;
            public double inclusive_ms,
                self_ms,
                calls;
        }

        [Serializable]
        class ReviewReport
        {
            public int frames;
            public string source;
            public Row[] rows;
        }
    }
}
