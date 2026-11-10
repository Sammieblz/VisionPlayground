using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CoDriver
{
    [Serializable]
    public class VirtualCarFrame : CameraHeader
    {
        public string configuration_json, configuration_sha256;
        public bool request_comma;
        public float steer,
            brake,
            throttle;
        public int indicator,
            cruise_delta;
    }

    public sealed class CommaBridge : IDisposable
    {
        public string Status = "Starting stock Comma";
        public CommaCommand Command;
        public OpenpilotUiSnapshot UiSnapshot;
        public long Revision;
        public bool Requested;
        public float Steer,
            Brake,
            Throttle;
        public int Indicator,
            CruiseDelta;
        public string RoadFile => Path.ChangeExtension(readyFile, ".road.json");
        public bool Running => process != null && !process.HasExited;
        readonly string root,
            token,
            readyFile;
        readonly string configurationJson, configurationHash;
        readonly object sync = new object();
        byte[] pending;
        string header,
            lastStartupError = "No startup error was reported";
        TcpClient client;
        Process process;
        bool stopped;
        Task worker;

        public CommaBridge(string root, string token, VehicleProfile profile = null)
        {
            this.root = root;
            this.token = token;
            configurationJson = SimulationVehicleConfiguration.Encode(profile ?? new VehicleProfile());
            configurationHash = SimulationVehicleConfiguration.Hash(configurationJson);
            readyFile = Path.Combine(root, "outputs", "services", "comma-" + token + ".json");
        }

        static string LinuxPath(string path)
        {
            path = Path.GetFullPath(path);
            return "/mnt/" + char.ToLowerInvariant(path[0]) + path.Substring(2).Replace('\\', '/');
        }

        public void Start()
        {
            try
            {
                StartProcess();
            }
            catch (Exception e)
                when (e is IOException
                    || e is UnauthorizedAccessException
                    || e is System.ComponentModel.Win32Exception
                )
            {
                Status = "Comma unavailable: " + e.Message;
            }
        }

        void StartProcess()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(readyFile));
            if (File.Exists(readyFile))
                File.Delete(readyFile);
            string stopFile = Path.ChangeExtension(readyFile, ".stop");
            if (File.Exists(stopFile))
                File.Delete(stopFile);
            string script = LinuxPath(Path.Combine(root, "scripts", "unity-comma.sh"));
            var installation = Installation.Read(root);
            string environment = string.IsNullOrEmpty(installation.openpilot_directory)
                ? ""
                : " --openpilot " + Installation.Argument(installation.openpilot_directory);
            var info = new ProcessStartInfo(
                Path.GetFullPath(Path.Combine(root, installation.python)),
                Installation.Argument(Path.Combine(root, "scripts", "launch-wsl-bridge.py"))
                    + " --distribution "
                    + Installation.Argument(installation.wsl_distribution)
                    + " --script "
                    + Installation.Argument(script)
                    + " --ready "
                    + Installation.Argument(readyFile)
                    + " --token "
                    + token
                    + " --parent "
                    + Process.GetCurrentProcess().Id
                    + environment
            )
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = root,
                // A GUI-launched player has no inherited console input handle.
                // Give WSL a real pipe and EOF; it communicates through localhost.
                RedirectStandardInput = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            process = Process.Start(info);
            process.StandardInput.Close();
            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                    Status = lastStartupError = e.Data;
            };
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
            worker = Task.Run(Run);
        }

        public void Submit(byte[] jpeg, CameraHeader camera)
        {
            var frame = new VirtualCarFrame
            {
                configuration_json = configurationJson,
                configuration_sha256 = configurationHash,
                session_id = camera.session_id,
                frame_id = camera.frame_id,
                timestamp = camera.timestamp,
                calibration = camera.calibration,
                pose = camera.pose,
                ego = camera.ego,
                route = camera.route,
                token = token,
                request_comma = Requested,
                steer = Steer,
                brake = Brake,
                throttle = Throttle,
                indicator = Indicator,
                cruise_delta = CruiseDelta,
            };
            string json = JsonUtility.ToJson(frame);
            lock (sync)
            {
                pending = jpeg;
                header = json;
            }
        }

        void Run()
        {
            try
            {
                var clock = Stopwatch.StartNew();
                while (!stopped && !File.Exists(readyFile) && clock.Elapsed.TotalSeconds < 60)
                {
                    if (process.HasExited)
                    {
                        process.WaitForExit();
                        throw new IOException(
                            "Comma bridge failed to start (exit "
                                + process.ExitCode
                                + "): "
                                + lastStartupError
                        );
                    }
                    Thread.Sleep(100);
                }
                if (stopped)
                    return;
                if (!File.Exists(readyFile))
                    throw new IOException("Comma startup timeout");
                var ready = JsonUtility.FromJson<Ready>(File.ReadAllText(readyFile));
                client = new TcpClient("127.0.0.1", ready.port)
                {
                    NoDelay = true,
                    ReceiveTimeout = 10000,
                    SendTimeout = 3000,
                };
                using (var stream = client.GetStream())
                    while (!stopped)
                    {
                        byte[] image;
                        string json;
                        lock (sync)
                        {
                            image = pending;
                            json = header;
                            pending = null;
                            header = null;
                        }
                        if (image == null)
                        {
                            Thread.Sleep(5);
                            continue;
                        }
                        byte[] bytes = Encoding.UTF8.GetBytes(json);
                        stream.Write(BitConverter.GetBytes(bytes.Length), 0, 4);
                        stream.Write(BitConverter.GetBytes(image.Length), 0, 4);
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Write(image, 0, image.Length);
                        int length = BitConverter.ToInt32(SensorLink.Read(stream, 4), 0);
                        if (length < 1 || length > 65536)
                            throw new IOException("Invalid Comma response");
                        var cmd = JsonUtility.FromJson<CommaCommand>(
                            Encoding.UTF8.GetString(SensorLink.Read(stream, length))
                        );
                        if (cmd.configuration_sha256 != configurationHash)
                        {
                            cmd.active = cmd.healthy = cmd.engageable = false;
                            cmd.status = "Vehicle configuration not acknowledged";
                        }
                        Command = cmd;
                        Status = cmd.status;
                        Interlocked.Increment(ref Revision);
                        string uiFile = Path.ChangeExtension(readyFile, ".ui.json");
                        try
                        {
                            if (File.Exists(uiFile))
                            {
                                var info = new FileInfo(uiFile);
                                if (info.Length <= 262144)
                                    UiSnapshot = JsonUtility.FromJson<OpenpilotUiSnapshot>(
                                        File.ReadAllText(uiFile)
                                    );
                            }
                        }
                        catch (Exception e)
                            when (e is IOException
                                || e is UnauthorizedAccessException
                                || e is ArgumentException
                            )
                        {
                            UiSnapshot = null;
                        }
                    }
            }
            catch (Exception e)
            {
                if (!stopped)
                {
                    Command = null;
                    UiSnapshot = null;
                    Interlocked.Increment(ref Revision);
                    Status = "Comma disconnected: " + e.Message;
                    UnityEngine.Debug.LogWarning(Status);
                }
            }
            finally
            {
                client?.Close();
            }
        }

        public void Dispose()
        {
            stopped = true;
            Requested = false;
            try
            {
                File.WriteAllText(Path.ChangeExtension(readyFile, ".stop"), token);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                UnityEngine.Debug.LogWarning("Comma stop signal unavailable: " + e.Message);
            }
            client?.Close();
            if (process != null)
            {
                try
                {
                    if (!process.WaitForExit(12000))
                    {
                        Status = "Previous Comma instance is still closing; retry shortly.";
                        return;
                    }
                }
                catch (InvalidOperationException) { }
                process.Dispose();
                process = null;
            }
        }
    }
}
