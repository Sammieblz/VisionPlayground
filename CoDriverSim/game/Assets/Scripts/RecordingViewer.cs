using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CoDriver
{
    public sealed class RecordingViewer : IDisposable
    {
        [Serializable]
        class Manifest
        {
            public int schema_version;
            public bool complete;
            public string frames_file,
                purpose;
            public Frame[] frames;
        }

        [Serializable]
        class Frame
        {
            public string image;
            public CameraHeader camera;
            public ObserverResult observation;
        }

        Manifest manifest;
        string directory;
        Texture2D texture;
        int index = -1;
        double cursor,
            last;
        bool playing;
        public string Status = "Select a saved camera clip";
        public bool Loaded => manifest != null;
        public Texture CurrentTexture => texture;
        public bool Playing => playing;
        public float Position => (float)cursor;
        public float Duration =>
            manifest == null
                ? 0
                : (float)(
                    manifest.frames[manifest.frames.Length - 1].camera.timestamp
                    - manifest.frames[0].camera.timestamp
                );

        public void Toggle()
        {
            if (cursor >= Duration)
                cursor = 0;
            playing = !playing;
        }

        public void Seek(float seconds) => cursor = Mathf.Clamp(seconds, 0, Duration);

        public void Open(string path)
        {
            Dispose();
            try
            {
                var parsed = JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
                directory =
                    Path.GetFullPath(Path.GetDirectoryName(path)) + Path.DirectorySeparatorChar;
                if (!string.IsNullOrEmpty(parsed.frames_file))
                {
                    string journal = Path.GetFullPath(Path.Combine(directory, parsed.frames_file));
                    if (!journal.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Invalid frame journal path");
                    var rows = new List<Frame>();
                    foreach (string line in File.ReadLines(journal))
                    {
                        try
                        {
                            var frame = JsonUtility.FromJson<Frame>(line);
                            if (frame?.camera == null || string.IsNullOrEmpty(frame.image))
                                break;
                            rows.Add(frame);
                        }
                        catch (ArgumentException)
                        {
                            break;
                        } // Interrupted final append.
                    }
                    parsed.frames = rows.ToArray();
                }
                if (
                    parsed.schema_version != 2
                    || parsed.frames == null
                    || parsed.frames.Length == 0
                )
                    throw new IOException("Clip has no playable camera frames");
                manifest = parsed;
                cursor = 0;
                index = -1;
                last = Time.realtimeSinceStartupAsDouble;
                playing = true;
                string kind = parsed.purpose == "scenario" ? "scenario capture" : "event clip";
                Status = parsed.complete
                    ? "Complete " + kind
                    : "Partial " + kind + " — see recorded coverage";
                texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            }
            catch (Exception e)
            {
                Status = "Recording unavailable: " + e.Message;
                manifest = null;
            }
        }

        public void Tick()
        {
            if (manifest == null)
            {
                return;
            }
            double now = Time.realtimeSinceStartupAsDouble;
            if (playing)
                cursor += now - last;
            last = now;
            var frames = manifest.frames;
            double first = frames[0].camera.timestamp,
                duration = frames[frames.Length - 1].camera.timestamp - first;
            if (cursor >= duration)
            {
                cursor = duration;
                playing = false;
            }
            int selected = 0;
            while (
                selected < frames.Length - 1
                && frames[selected + 1].camera.timestamp - first <= cursor
            )
                selected++;
            if (selected != index)
            {
                try
                {
                    string file = Path.GetFullPath(Path.Combine(directory, frames[selected].image));
                    if (!file.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Invalid frame path");
                    if (!texture.LoadImage(File.ReadAllBytes(file), false))
                        throw new IOException("Invalid recorded image");
                    index = selected;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    Status = e.Message;
                    playing = false;
                }
            }
        }

        public void Draw(Rect area, GUIStyle style)
        {
            Tick();
            if (manifest == null)
            {
                GUI.Label(area, Status, style);
                return;
            }
            var frames = manifest.frames;
            int selected = Mathf.Max(0, index);
            double duration = Duration;
            GUI.DrawTexture(area, texture, ScaleMode.ScaleToFit);
            if (
                GUI.Button(
                    new Rect(area.x, area.yMax + 8, 140, 35),
                    playing ? "Pause replay" : "Play replay"
                )
            )
            {
                if (cursor >= duration)
                    cursor = 0;
                playing = !playing;
            }
            cursor = GUI.HorizontalSlider(
                new Rect(area.x + 160, area.yMax + 20, area.width - 280, 25),
                (float)cursor,
                0,
                (float)duration
            );
            GUI.Label(
                new Rect(area.xMax - 110, area.yMax + 12, 110, 30),
                cursor.ToString("F1") + " / " + duration.ToString("F1") + " s",
                style
            );
            GUI.Label(
                new Rect(area.x, area.yMax + 48, area.width, 45),
                Status + " · Frame " + frames[selected].camera.frame_id,
                style
            );
            var observation = frames[selected].observation;
            if (observation?.risks != null && observation.risks.Length > 0)
                GUI.Label(
                    new Rect(area.x, area.yMax + 86, area.width, 45),
                    observation.risks[0].message,
                    style
                );
        }

        public void Dispose()
        {
            manifest = null;
            if (texture)
                UnityEngine.Object.Destroy(texture);
            texture = null;
            playing = false;
        }
    }
}
