// Adapted from Randomation Vehicle Physics, Justin Couch, MIT.
// Revision 192ba860c3c92a16363ffe87fb943dc282e62398: Suspension, Wheel,
// DriveForce, GasMotor, GearboxTransmission. See RVP-MIT.txt and dynamics-provenance.json.
// CoDriver adaptation: explicit stepping, SI forces, load-bounded combined tire forces,
// scene-local casts, independent controls, no stunts, damage or global input dependencies.
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoDriver
{
    [Serializable]
    public sealed class VehicleProfile
    {
        public string id = "civic-comparison-v1";
        public string visualResource = "";
        public Vector3 driverEye = new Vector3(-.35f, 1.23f, .15f),
            sensorMount = new Vector3(0, 1.22f, 1.7f);
        public Vector3 collisionSize = new Vector3(1.82f, 1.15f, 4.4f),
            collisionCentre = new Vector3(0, .85f, 0);

        // openpilot v0.9.7 Honda Civic Bosch specs + its standard 136 kg cargo allowance.
        public float mass = 1462,
            wheelbase = 2.7f,
            track = 1.68f,
            steeringRatio = 15.38f;
        public float radius = .34f,
            suspensionLength = .32f,
            spring = 35000,
            damper = 4500;
        public float wheelMountHeight = .58f,
            maxSteering = 34,
            grip = .9f;
        public float engineTorque = 175,
            finalDrive = 4.1f,
            efficiency = .9f;
        public float brakeForce = 14500,
            drag = .42f,
            rollingResistance = .012f;
        public Vector3 centreOfMass = new Vector3(0, .55f, .27f);
        public float[] gears = { 3.6f, 2.1f, 1.45f, 1.05f, .82f, .66f };
        public float frontTrack, rearTrack, frontDriveFraction = 1, reverseRatio = 3.3f;
        public float TrackAt(int wheel) => wheel < 2
            ? (frontTrack > 0 ? frontTrack : track) : (rearTrack > 0 ? rearTrack : track);
        public bool abs = true,
            tractionControl = true;

        public static VehicleProfile GlsSimulation() => new VehicleProfile
        {
            id = "gls-simulation-v1", visualResource = "WorldArt/Local/MercedesVehicle",
            mass = 2660, wheelbase = 3.134f, track = 1.68f,
            frontTrack = 1.669f, rearTrack = 1.692f, steeringRatio = 18.7f,
            collisionSize = new Vector3(1.956f, 1.30f, 5.207f),
            collisionCentre = new Vector3(0, .99f, -.10f),
            radius = .39f, wheelMountHeight = .60f,
            spring = 60000, damper = 7000, suspensionLength = .32f,
            centreOfMass = new Vector3(0, .70f, .12f),
            engineTorque = 700, finalDrive = 3.27f, efficiency = .80f,
            frontDriveFraction = .45f, reverseRatio = 4.8f,
            gears = new[] { 5.35f, 3.24f, 2.25f, 1.64f, 1.21f, 1, .86f, .72f, .60f },
            brakeForce = 26000, drag = .60f, maxSteering = 35,
                driverEye = new Vector3(-.387f, 1.43f, 0),
            sensorMount = new Vector3(0, 1.54f, .78f),
        };

        public static VehicleProfile ForTraffic(string kind)
        {
            var p = new VehicleProfile();
            if (kind == "Bus")
            {
                p.id = "coach-game-v1";
                p.collisionSize = new Vector3(2.35f, 2.55f, 10);
                p.collisionCentre = new Vector3(0, 1.525f, 0);
                p.mass = 12000;
                p.wheelbase = 5.5f;
                p.track = 2;
                p.engineTorque = 1450;
                p.spring = 220000;
                p.damper = 25000;
                p.brakeForce = 90000;
                p.centreOfMass = new Vector3(0, 1.15f, 0);
                p.maxSteering = 38;
            }
            else if (kind == "Emergency" || kind == "Truck")
            {
                p.id = kind.ToLowerInvariant() + "-game-v1";
                p.collisionSize =
                    kind == "Emergency"
                        ? new Vector3(1.74f, 1.45f, 4.4f)
                        : new Vector3(1.93f, 1.2f, 4.4f);
                p.collisionCentre = new Vector3(0, .25f + p.collisionSize.y / 2, 0);
                p.mass = kind == "Emergency" ? 3200 : 2400;
                p.engineTorque = 330;
                p.spring = 65000;
                p.damper = 7000;
                p.brakeForce = 26000;
                p.centreOfMass = new Vector3(0, .7f, .15f);
                // Utility shell bottoms at the asset origin. Suspension support
                // must hold that origin above the road under full static load.
                p.wheelMountHeight = .45f;
            }
            return p;
        }
    }

    public struct VehicleControls
    {
        public float throttle,
            brake,
            roadWheelAngle;
        public bool reverse,
            handbrake;
    }

    public interface IVehicleDynamics
    {
        Rigidbody Body { get; }
        VehicleProfile Profile { get; }
        float EngineRpm { get; }
        int Gear { get; }
        int GroundedWheels { get; }
        void Step(VehicleControls controls, float dt);
        void WheelPose(int index, out Vector3 position, out Quaternion rotation);
    }

    public sealed class RvpVehicleDynamics : IVehicleDynamics
    {
        public Rigidbody Body { get; }
        public VehicleProfile Profile { get; }
        public float EngineRpm { get; private set; } = 900;
        public int Gear { get; private set; } = 1;
        public int GroundedWheels { get; private set; }
        public bool AbsActive { get; private set; }
        public bool TractionActive { get; private set; }
        public float LastSteering { get; private set; }
        public readonly float[] Rpm = new float[4];
        readonly Vector3[] centres = new Vector3[4];
        readonly Quaternion[] rotations = new Quaternion[4];
        readonly float[] angles = new float[4],
            travel = new float[4],
            loads = new float[4];
        readonly RaycastHit[] contact = new RaycastHit[4];
        readonly bool[] grounded = new bool[4];
        readonly PhysicsScene physics;
        float shiftRemaining,
            shiftCooldown;
        static readonly AnimationCurve torqueCurve = new AnimationCurve(
            new Keyframe(.9f, .55f),
            new Keyframe(2.5f, .9f),
            new Keyframe(4.5f, 1),
            new Keyframe(5.7f, .96f),
            new Keyframe(6.8f, .7f)
        );

        public RvpVehicleDynamics(Rigidbody body, VehicleProfile profile)
        {
            Body = body;
            Profile = profile;
            physics = body.gameObject.scene.GetPhysicsScene();
            body.mass = profile.mass;
            body.centerOfMass = profile.centreOfMass;
            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearDamping = 0;
            body.angularDamping = .08f;
            body.solverIterations = 12;
            body.solverVelocityIterations = 4;
            for (int i = 0; i < 4; i++)
                travel[i] = profile.suspensionLength;
        }

        public Vector3 Mount(int i) =>
            new Vector3(
                (i % 2 == 0 ? -1 : 1) * Profile.TrackAt(i) / 2,
                Profile.wheelMountHeight,
                (i < 2 ? 1 : -1) * Profile.wheelbase / 2
            );

        public static float Ackermann(float centreAngle, float wheelbase, float track, bool right)
        {
            if (Mathf.Abs(centreAngle) < .01f)
                return 0;
            float radius = wheelbase / Mathf.Tan(Mathf.Abs(centreAngle) * Mathf.Deg2Rad);
            bool inside = right == (centreAngle > 0);
            return Mathf.Sign(centreAngle)
                * Mathf.Atan(wheelbase / Mathf.Max(.5f, radius + (inside ? -1 : 1) * track / 2))
                * Mathf.Rad2Deg;
        }

        public void Step(VehicleControls c, float dt)
        {
            if (dt <= 0 || !float.IsFinite(dt))
                return;
            c.throttle = Mathf.Clamp01(c.throttle);
            c.brake = Mathf.Clamp01(c.brake);
            c.roadWheelAngle = Mathf.Clamp(
                c.roadWheelAngle,
                -Profile.maxSteering,
                Profile.maxSteering
            );
            LastSteering = c.roadWheelAngle;
            var tr = Body.transform;
            float speed = Vector3.Dot(Body.linearVelocity, tr.forward);
            shiftRemaining = Mathf.Max(0, shiftRemaining - dt);
            shiftCooldown = Mathf.Max(0, shiftCooldown - dt);
            if (c.reverse)
                Gear = -1;
            else if (Gear < 1)
                Gear = 1;
            float ratio = Gear < 0 ? -Profile.reverseRatio : Profile.gears[Gear - 1];
            float coupledRpm = Mathf.Abs(
                speed / Profile.radius * 60 / (2 * Mathf.PI) * ratio * Profile.finalDrive
            );
            EngineRpm = Mathf.Lerp(
                EngineRpm,
                Mathf.Clamp(coupledRpm, 900, 6800),
                1 - Mathf.Exp(-dt * 12)
            );
            if (Gear > 0 && shiftCooldown == 0)
            {
                int next = Gear;
                if (coupledRpm > 5700 && Gear < Profile.gears.Length)
                    next++;
                else if (Gear > 1 && coupledRpm < (c.throttle > .7f ? 2300 : 1500))
                    next--;
                if (next != Gear)
                {
                    Gear = next;
                    shiftRemaining = .28f;
                    shiftCooldown = .8f;
                }
            }
            float driveTorque =
                torqueCurve.Evaluate(EngineRpm * .001f)
                * Profile.engineTorque
                * c.throttle
                * ratio
                * Profile.finalDrive
                * Profile.efficiency;
            if (shiftRemaining > 0)
                driveTorque *= .15f;
            if (Mathf.Abs(speed) < 1 && c.throttle == 0 && c.brake == 0 && !c.handbrake)
                driveTorque = (c.reverse ? -1 : 1) * 50;
            AbsActive = TractionActive = false;
            GroundedWheels = 0;
            for (int i = 0; i < 4; i++)
            {
                Vector3 origin = tr.TransformPoint(Mount(i));
                // A finite tire footprint detects a driveway lip before the
                // chassis reaches it. A centre ray can leave driven wheels
                // unsupported against a curb even with throttle applied.
                grounded[i] =
                    physics.SphereCast(
                        origin,
                        Profile.radius,
                        -tr.up,
                        out contact[i],
                        Profile.suspensionLength,
                        1 | (1 << TrafficClearance.RoadLayer),
                        QueryTriggerInteraction.Ignore
                    )
                    && Vector3.Dot(contact[i].normal, tr.up) > .25f;
                float steer =
                    i < 2
                        ? Ackermann(c.roadWheelAngle, Profile.wheelbase, Profile.TrackAt(i), i % 2 == 1)
                        : 0;
                Quaternion rotation = tr.rotation * Quaternion.Euler(0, steer, 0);
                loads[i] = 0;
                if (grounded[i])
                {
                    GroundedWheels++;
                    var hit = contact[i];
                    travel[i] = Mathf.Clamp(hit.distance, 0, Profile.suspensionLength);
                    Vector3 groundVelocity = hit.rigidbody
                        ? hit.rigidbody.GetPointVelocity(hit.point)
                        : Vector3.zero;
                    Vector3 velocity = Body.GetPointVelocity(hit.point) - groundVelocity;
                    float compression = Profile.suspensionLength - travel[i];
                    // Damper speed is suspension travel relative to the contact
                    // plane. Chassis-up velocity alone treats climbing a grade
                    // as extension and can remove support at a bridge approach.
                    float extensionSpeed =
                        Vector3.Dot(velocity, hit.normal)
                        / Mathf.Max(.25f, Vector3.Dot(tr.up, hit.normal));
                    float load = Mathf.Clamp(
                        Profile.spring * compression - Profile.damper * extensionSpeed,
                        0,
                        Profile.mass * 9.81f
                    );
                    loads[i] = load;
                    Vector3 support = hit.normal * load;
                    Body.AddForceAtPosition(support, origin, ForceMode.Force);
                    Vector3 forward = Vector3
                        .ProjectOnPlane(rotation * Vector3.forward, hit.normal)
                        .normalized;
                    Vector3 right = Vector3.Cross(hit.normal, forward).normalized;
                    float longitudinal = Vector3.Dot(velocity, forward);
                    float lateral = Vector3.Dot(velocity, right);
                    float cornerMass = Profile.mass * .25f;
                    float limit = load * Profile.grip;
                    float axleFraction = i < 2 ? Profile.frontDriveFraction : 1 - Profile.frontDriveFraction;
                    float propulsion = driveTorque * axleFraction / (2 * Profile.radius);
                    if (Profile.tractionControl && Mathf.Abs(propulsion) > limit * .98f)
                    {
                        propulsion = Mathf.Clamp(propulsion, -limit * .98f, limit * .98f);
                        TractionActive = true;
                    }
                    float brake =
                        Profile.brakeForce * c.brake * (i < 2 ? .32f : .18f)
                        + (c.handbrake && i >= 2 ? Profile.brakeForce * .5f : 0);
                    if (
                        Profile.abs
                        && c.brake > 0
                        && Mathf.Abs(longitudinal) > .5f
                        && brake > limit * .96f
                    )
                    {
                        brake = limit * .96f;
                        AbsActive = true;
                    }
                    float gravity = Vector3.Dot(Physics.gravity, forward) * cornerMass;
                    float hold = -longitudinal * cornerMass / dt - propulsion - gravity;
                    float brakeForce = Mathf.Clamp(hold, -brake, brake);
                    float rolling = Mathf.Clamp(
                        -longitudinal * cornerMass / dt,
                        -load * Profile.rollingResistance,
                        load * Profile.rollingResistance
                    );
                    float fx = propulsion + brakeForce + rolling;
                    // RVP's separate slip curves are load-scaled and coupled here by a friction circle.
                    float slip = Mathf.Atan2(lateral, Mathf.Max(2, Mathf.Abs(longitudinal)));
                    float fy = -Mathf.Sign(slip) * Mathf.Min(Mathf.Abs(slip) / .10f, 1) * limit;
                    if (Mathf.Abs(longitudinal) < 2)
                        fy = Mathf.Clamp(-lateral * cornerMass / dt, -limit, limit);
                    Vector2 tire = Vector2.ClampMagnitude(new Vector2(fx, fy), limit);
                    Vector3 force = forward * tire.x + right * tire.y;
                    Body.AddForceAtPosition(force, hit.point, ForceMode.Force);
                    if (hit.rigidbody && !hit.rigidbody.isKinematic)
                        hit.rigidbody.AddForceAtPosition(
                            -support - force,
                            hit.point,
                            ForceMode.Force
                        );
                    float slipSpeed =
                        Mathf.Max(0, Mathf.Abs(propulsion) - limit) / Mathf.Max(100, cornerMass);
                    Rpm[i] =
                        (longitudinal + Mathf.Sign(propulsion) * slipSpeed)
                        / Profile.radius
                        * 60
                        / (2 * Mathf.PI);
                }
                else
                {
                    travel[i] = Profile.suspensionLength;
                    Rpm[i] *= Mathf.Exp(-dt * .5f);
                }
                angles[i] = Mathf.Repeat(angles[i] + Rpm[i] * 6 * dt, 360);
                centres[i] = origin - tr.up * travel[i];
                rotations[i] = rotation * Quaternion.Euler(angles[i], 0, 90);
            }
            for (int axle = 0; axle < 2; axle++)
            {
                int left = axle * 2,
                    right = left + 1;
                if (!grounded[left] || !grounded[right])
                    continue;
                float antiRoll = Mathf.Clamp(
                    (travel[left] - travel[right]) * Profile.spring * .35f,
                    -Mathf.Min(loads[left], loads[right]) * .5f,
                    Mathf.Min(loads[left], loads[right]) * .5f
                );
                Body.AddForceAtPosition(
                    -tr.up * antiRoll,
                    tr.TransformPoint(Mount(left)),
                    ForceMode.Force
                );
                Body.AddForceAtPosition(
                    tr.up * antiRoll,
                    tr.TransformPoint(Mount(right)),
                    ForceMode.Force
                );
            }
            Body.AddForce(
                -Body.linearVelocity * Body.linearVelocity.magnitude * Profile.drag,
                ForceMode.Force
            );
        }

        public void WheelPose(int index, out Vector3 position, out Quaternion rotation)
        {
            position = centres[index];
            rotation = rotations[index];
        }
    }
}
