using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoDriver
{
    [Serializable]
    public class Calibration
    {
        public float fx = 455,
            fy = 455,
            cx = 482,
            cy = 302,
            height_m = 1.20f,
            pitch_deg = 0;
        public string source = "pinhole";
    }

    [Serializable]
    public class EgoTelemetry
    {
        public float speed_mps,
            steering_deg,
            acceleration_mps2,
            yaw_rate_rps;
        public double latitude = 37,
            longitude = -122;
        public float bearing_deg;
        public string source = "simulated_sensors";

        // IMU device axes are forward, right, down; these are measurements,
        // not map/actor truth. Specific force includes the gravity response.
        public float[] specific_force_device_mps2,
            angular_velocity_device_rps;
    }

    public static class SensorAxes
    {
        public static Vector3 SpecificForce(
            Quaternion orientation,
            Vector3 acceleration,
            Vector3 gravity
        )
        {
            var local = Quaternion.Inverse(orientation) * (acceleration - gravity);
            return new Vector3(local.z, local.x, -local.y);
        }

        public static Vector3 AngularVelocity(Quaternion orientation, Vector3 angularVelocity)
        {
            var local = Quaternion.Inverse(orientation) * angularVelocity;
            // Angular velocity is an axial vector: changing handedness adds
            // a sign, unlike the polar acceleration vector above.
            return new Vector3(-local.z, -local.x, local.y);
        }
    }

    [Serializable]
    public class CameraPose
    {
        public float[] position_ego_m =  { 1.7f, 0, 1.22f },
            rotation =  { 0, 0, 1, -1, 0, 0, 0, -1, 0 };
        public string source = "simulated_mount";
    }

    [Serializable]
    public class RouteContext
    {
        public string instruction = "Explore Cedar Grove",
            source = "navigation_map";
    }

    [Serializable]
    public class CameraHeader
    {
        public int schema_version = 2,
            width = 964,
            height = 604;
        public long frame_id;
        public double timestamp;
        public string session_id,
            encoding = "jpeg",
            token;
        public Calibration calibration = new Calibration();
        public EgoTelemetry ego = new EgoTelemetry();
        public RouteContext route = new RouteContext();
        public CameraPose pose = new CameraPose();
    }

    [Serializable]
    public class Risk
    {
        public string severity,
            kind,
            message;
        public bool record;
        public double timestamp;
    }

    [Serializable]
    public class Detection
    {
        public int track_id;
        public string label,
            signal_state;
        public float[] box;
        public float score;
    }

    [Serializable]
    public class ObserverResult
    {
        public string status,
            model,
            recording_error,
            session_id;
        public long frame_id;
        public double timestamp;
        public float latency_ms;
        public float camera_hz,
            detection_hz;
        public int received_frames,
            processed_frames,
            dropped_frames,
            saved_clips,
            image_bytes;
        public Detection[] tracks;
        public Risk[] risks;
    }

    [Serializable]
    public class Ready
    {
        public int port,
            pid;
        public string status;
    }

    // Latest-frame slots are deliberately single element. A slow model cannot queue old images.
    public sealed class SensorLink : IDisposable
    {
        public string Status = "Starting observer";
        public ObserverResult Result;
        public double ReceivedAt;
        public int Port;
        public Action<byte[], CameraHeader> Encoded;
        public Action<string> Failure;
        readonly CameraDeliveryQueue delivery = new CameraDeliveryQueue();
        public int Dropped => delivery.Dropped;
        volatile bool stop;
        TcpClient client;
        Task task;

        public void Start(int port)
        {
            Port = port;
            task = Task.Run(Run);
        }

        public void Submit(byte[] image, string header)
        {
            var owned = new CameraFrameBuffer(image.Length);
            System.Buffer.BlockCopy(image, 0, owned.Buffer, 0, image.Length);
            Submit(owned, header);
        }

        // Transfers ownership, including when a stopped link discards the frame.
        public void Submit(CameraFrameBuffer image, string header)
        {
            delivery.Submit(image, header, Clock());
        }

        static double Clock() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        void Run()
        {
            try
            {
                client = new TcpClient("127.0.0.1", Port);
                client.NoDelay = true;
                client.SendBufferSize = 8 * 1024 * 1024;
                client.ReceiveBufferSize = 1024 * 1024;
                client.ReceiveTimeout = 10000;
                client.SendTimeout = 3000;
                using (var stream = client.GetStream())
                    while (!stop)
                    {
                        var packet = delivery.Take(Clock());
                        if (packet == null)
                        {
                            delivery.WaitForFrame();
                            continue;
                        }
                        string header = packet.Header;
                        using (packet)
                        {
                            var image = packet.Image;
                            byte[] json = Encoding.UTF8.GetBytes(header);
                            stream.Write(BitConverter.GetBytes(json.Length), 0, 4);
                            stream.Write(BitConverter.GetBytes(image.Length), 0, 4);
                            stream.Write(json, 0, json.Length);
                            stream.Write(image.Buffer, 0, image.Length);
                        }
                        int size = BitConverter.ToInt32(Read(stream, 4), 0);
                        if (size < 1 || size > 1000000)
                            throw new IOException("Invalid observer response length");
                        string response = Encoding.UTF8.GetString(Read(stream, size));
                        Result = JsonUtility.FromJson<ObserverResult>(response);
                        ReceivedAt = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
                        Status = Result.status;
                        if (Result.image_bytes < 1 || Result.image_bytes > 8000000)
                            throw new IOException("Invalid encoded camera size");
                        byte[] jpeg = Read(stream, Result.image_bytes);
                        var camera = JsonUtility.FromJson<CameraHeader>(header);
                        camera.encoding = "jpeg";
                        Encoded?.Invoke(jpeg, camera);
                    }
            }
            catch (Exception e)
            {
                if (!stop)
                {
                    Status = "Disconnected: " + e.Message;
                    Failure?.Invoke(e.ToString());
                }
            }
            finally
            {
                stop = true;
                delivery.Dispose();
                client?.Close();
            }
        }

        public static byte[] Read(Stream stream, int count)
        {
            var data = new byte[count];
            int n = 0;
            while (n < count)
            {
                int got = stream.Read(data, n, count - n);
                if (got == 0)
                    throw new EndOfStreamException();
                n += got;
            }
            return data;
        }

        public void Dispose()
        {
            stop = true;
            delivery.Dispose();
            client?.Close();
        }
    }

    public sealed class SensorRig : MonoBehaviour
    {
        public Camera Sensor;
        public SensorLink Observer;
        public string Session,
            Token;
        public Vehicle Vehicle;
        public string RouteInstruction = "Explore Cedar Grove";
        public MapDefinition Map;
        public Action<byte[], CameraHeader> Captured;
        RenderTexture target;
        readonly RenderTexture[] displayFrames = new RenderTexture[12];
        readonly long[] displayIds = new long[12];
        public Texture LatestTexture => target;
        public long LatestFrame { get; private set; } = -1;

        public Texture FrameTexture(long id)
        {
            for (int i = 0; i < displayIds.Length; i++)
                if (displayFrames[i] && displayIds[i] == id)
                    return displayFrames[i];
            return null;
        }

        int inFlight;
        readonly FrameDeliveryOrder deliveryOrder = new FrameDeliveryOrder();
        readonly CameraCadence cadence = new CameraCadence();
        public int ReorderedDropped => deliveryOrder.Dropped;
        public long MissedCaptureDeadlines => cadence.SkippedDeadlines;
        float previousSpeed;
        long frame;
        double previousCaptureTime;
        Vector3 previousPhysicsVelocity,
            specificForce,
            angularVelocity;
        public int Captures,
            Dropped;
        public string Error = "";

        public void Initialize(Vehicle vehicle, string session, string token)
        {
            Vehicle = vehicle;
            Session = session;
            Token = token;
            previousPhysicsVelocity = vehicle.Body.linearVelocity;
            specificForce = SensorAxes.SpecificForce(
                vehicle.Body.rotation,
                Vector3.zero,
                Physics.gravity
            );
            angularVelocity = SensorAxes.AngularVelocity(
                vehicle.Body.rotation,
                vehicle.Body.angularVelocity
            );
            var g = new GameObject("Independent forward camera");
            g.transform.SetParent(vehicle.transform, false);
            g.transform.localPosition = vehicle.Profile.sensorMount;
            Sensor = g.AddComponent<Camera>();
            Sensor.nearClipPlane = .1f;
            Sensor.farClipPlane = 700;
            Sensor.fieldOfView = 2 * Mathf.Atan(604f / 910) * Mathf.Rad2Deg;
            Sensor.clearFlags = CameraClearFlags.Skybox;
            Sensor.backgroundColor = new Color(.63f, .76f, .85f);
            Sensor.cullingMask = ~((1 << 9) | (1 << 16));
            target = new RenderTexture(964, 604, 16, RenderTextureFormat.ARGB32);
            target.Create();
            Sensor.targetTexture = target;
            Sensor.enabled = false;
        }

        void FixedUpdate()
        {
            if (!Vehicle || !Vehicle.Body)
                return;
            var velocity = Vehicle.Body.linearVelocity;
            var acceleration = (velocity - previousPhysicsVelocity) / Time.fixedDeltaTime;
            previousPhysicsVelocity = velocity;
            specificForce = SensorAxes.SpecificForce(
                Vehicle.Body.rotation,
                acceleration,
                Physics.gravity
            );
            angularVelocity = SensorAxes.AngularVelocity(
                Vehicle.Body.rotation,
                Vehicle.Body.angularVelocity
            );
        }

        void LateUpdate()
        {
            if (!cadence.Due(Time.realtimeSinceStartupAsDouble))
                return;
            if (inFlight >= 3)
            {
                Dropped++;
                return;
            }
            inFlight++;
            CameraRenderCost.Render("sensor", Sensor);
            var header = new CameraHeader
            {
                session_id = Session,
                token = Token,
                frame_id = frame++,
                timestamp = Time.realtimeSinceStartupAsDouble,
                encoding = "rgb24-bottom-up",
            };
            header.calibration.height_m = Vehicle.Profile.sensorMount.y;
            header.pose.position_ego_m = new[]
            {
                Vehicle.Profile.sensorMount.z,
                -Vehicle.Profile.sensorMount.x,
                Vehicle.Profile.sensorMount.y,
            };
            LatestFrame = header.frame_id;
            int slot = (int)(LatestFrame % displayFrames.Length);
            if (!displayFrames[slot])
            {
                displayFrames[slot] = new RenderTexture(964, 604, 0, RenderTextureFormat.ARGB32);
                displayFrames[slot].Create();
            }
            Graphics.CopyTexture(target, displayFrames[slot]);
            displayIds[slot] = LatestFrame;
            header.ego.speed_mps = Vehicle.Speed;
            header.ego.steering_deg = -Vehicle.SteerAngle * Vehicle.Profile.steeringRatio;
            header.ego.acceleration_mps2 =
                previousCaptureTime > 0
                    ? (Vehicle.Speed - previousSpeed)
                        / (float)Math.Max(.001, header.timestamp - previousCaptureTime)
                    : 0;
            previousSpeed = Vehicle.Speed;
            previousCaptureTime = header.timestamp;
            header.ego.yaw_rate_rps = -Vehicle.Body.angularVelocity.y;
            header.ego.specific_force_device_mps2 = new[]
            {
                specificForce.x,
                specificForce.y,
                specificForce.z,
            };
            header.ego.angular_velocity_device_rps = new[]
            {
                angularVelocity.x,
                angularVelocity.y,
                angularVelocity.z,
            };
            if (Map != null)
                Map.Gps(
                    Vehicle.transform.position,
                    out header.ego.latitude,
                    out header.ego.longitude
                );
            header.ego.bearing_deg = Vehicle.transform.eulerAngles.y;
            header.route.instruction = RouteInstruction;
            string json = JsonUtility.ToJson(header);
            AsyncGPUReadback.Request(
                target,
                0,
                TextureFormat.RGB24,
                request =>
                {
                    if (!this)
                    {
                        return;
                    }
                    inFlight--;
                    if (request.hasError)
                    {
                        Error = "GPU camera readback failed";
                        return;
                    }
                    // D3D11 URP is bottom-up. The explicit wire format is normalized to
                    // top-left pixels by OpenCV in the local camera service.
                    var observer = Observer;
                    if (observer != null)
                    {
                        if (!deliveryOrder.TryAdvance(header.frame_id, header.timestamp))
                            return;
                        var pixels = request.GetData<byte>();
                        CameraFrameBuffer raw = new CameraFrameBuffer(pixels.Length);
                        try
                        {
                            Unity.Collections.NativeArray<byte>.Copy(
                                pixels,
                                0,
                                raw.Buffer,
                                0,
                                pixels.Length
                            );
                            observer.Encoded = Captured;
                            observer.Submit(raw, json);
                            raw = null;
                            Interlocked.Increment(ref Captures);
                        }
                        finally
                        {
                            raw?.Dispose();
                        }
                    }
                }
            );
        }

        void OnDestroy()
        {
            AsyncGPUReadback.WaitAllRequests();
            if (target)
            {
                target.Release();
                Destroy(target);
            }
            foreach (var frameTexture in displayFrames)
                if (frameTexture)
                {
                    frameTexture.Release();
                    Destroy(frameTexture);
                }
            Observer?.Dispose();
        }
    }

    public sealed class ServiceSupervisor : IDisposable
    {
        public SensorLink Link;
        public string Status = "Not started",
            Root,
            Token = Guid.NewGuid().ToString("N"),
            ReadyFile;
        public int Generation { get; private set; }
        int completedTransportDrops;
        public int TransportDropped => completedTransportDrops + (Link?.Dropped ?? 0);
        public string DiagnosticLog { get; private set; }
        string lastOutput = "";
        readonly object logLock = new object();
        Process process;
        float started;
        bool disposed;

        public ServiceSupervisor(string root)
        {
            Root = root;
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
                Status = "Observer unavailable: " + e.Message;
                DisposeProcess();
            }
        }

        void StartProcess()
        {
            DisposeProcess();
            disposed = false;
            string python = Path.GetFullPath(Path.Combine(Root, Installation.Read(Root).python));
            string model = Path.Combine(Root, "models", "object_detection_nanodet_2022nov.onnx");
            if (!File.Exists(python) || !File.Exists(model))
            {
                Status = "Missing Python environment or NanoDet weights. Run setup-game.ps1.";
                return;
            }
            ReadyFile = Path.Combine(Root, "outputs", "services", "observer-" + Token + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(ReadyFile));
            Generation++;
            int generation = Generation;
            string log = DiagnosticLog = Path.Combine(
                Root,
                "outputs",
                "services",
                "observer-" + Token + "-" + generation + ".log"
            );
            lastOutput = "";
            if (File.Exists(ReadyFile))
                File.Delete(ReadyFile);
            var info = new ProcessStartInfo(
                python,
                "-m codriversim.service --model \""
                    + model
                    + "\" --ready \""
                    + ReadyFile
                    + "\" --output \""
                    + Path.Combine(Root, "outputs", "recordings")
                    + "\" --token "
                    + Token
            )
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Root,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            process = Process.Start(info);
            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                    Trace(log, generation, "stderr", e.Data);
            };
            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                    Trace(log, generation, "stdout", e.Data);
            };
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
            Status = "Loading camera model";
            started = Time.unscaledTime;
        }

        void Trace(string log, int generation, string channel, string message)
        {
            lock (logLock)
            {
                if (generation == Generation)
                {
                    lastOutput = message.Split('\n')[0].Trim();
                    if (lastOutput.Length > 180)
                        lastOutput = lastOutput.Substring(0, 180) + "…";
                }
                try
                {
                    File.AppendAllText(
                        log,
                        DateTime.UtcNow.ToString("O") + " " + channel + " " + message + "\n"
                    );
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            }
        }

        public void Poll()
        {
            if (disposed || process == null)
                return;
            if (process.HasExited)
            {
                Status =
                    "Observer stopped (exit "
                    + process.ExitCode
                    + "). "
                    + lastOutput
                    + " Use Restart camera service.";
                return;
            }
            if (Link == null && File.Exists(ReadyFile))
            {
                try
                {
                    var ready = JsonUtility.FromJson<Ready>(File.ReadAllText(ReadyFile));
                    Link = new SensorLink();
                    string log = DiagnosticLog;
                    int generation = Generation;
                    Link.Failure = message => Trace(log, generation, "transport", message);
                    Link.Start(ready.port);
                    Status = "Observer ready";
                }
                catch (Exception e) when (e is IOException || e is ArgumentException)
                {
                    Status = "Observer readiness unavailable: " + e.Message;
                }
            }
            if (Link == null && Time.unscaledTime - started > 45)
                Status = "Observer startup timed out. Use Restart services.";
        }

        [Serializable]
        class CaptureRequest
        {
            public string action,
                id;
        }

        public void StartCapture(string id) => Capture("start", id);

        public void StopCapture() => Capture("stop", "");

        void Capture(string action, string id)
        {
            try
            {
                string path = Path.Combine(
                    Root,
                    "outputs",
                    "services",
                    "observer-" + Token + ".capture"
                );
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(
                    path + ".tmp",
                    JsonUtility.ToJson(new CaptureRequest { action = action, id = id })
                );
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(path + ".tmp", path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Status = "Scenario capture unavailable: " + e.Message;
            }
        }

        void DisposeProcess()
        {
            Link?.Dispose();
            completedTransportDrops += Link?.Dropped ?? 0;
            Link = null;
            if (process != null)
            {
                try
                {
                    if (!process.WaitForExit(2000))
                        process.Kill();
                }
                catch (InvalidOperationException) { }
                process.Dispose();
                process = null;
            }
        }

        public void Dispose()
        {
            disposed = true;
            DisposeProcess();
        }
    }
}
