using System;
using System.IO;
using UnityEngine;

namespace CoDriver
{
    [Serializable]
    public sealed class Installation
    {
        public int schema_version = 1;
        public string python = "runtime/python.exe";
        public string wsl_distribution = "Ubuntu-22.04";
        public string openpilot_directory = "";

        public static Installation Read(string root)
        {
            string path = Path.Combine(root, "codriversim-app.json");
            if (!File.Exists(path))
                return new Installation { python = ".venv/Scripts/python.exe" };
            Installation settings;
            try
            {
                settings = JsonUtility.FromJson<Installation>(File.ReadAllText(path));
            }
            catch (ArgumentException e)
            {
                throw new IOException("Invalid application configuration", e);
            }
            if (
                settings == null
                || settings.schema_version != 1
                || string.IsNullOrWhiteSpace(settings.python)
            )
                throw new IOException("Unsupported application configuration: " + path);
            return settings;
        }

        public static string Argument(string value)
        {
            // These arguments go directly to ProcessStartInfo/wsl.exe, never to a shell.
            if (value == null || value.IndexOfAny(new[] { '"', '\r', '\n', '\0' }) >= 0)
                throw new IOException("Invalid application path or distribution name");
            return "\"" + value + "\"";
        }
    }
}
