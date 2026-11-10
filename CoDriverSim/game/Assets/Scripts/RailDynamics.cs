using UnityEngine;

namespace CoDriver
{
    // Finite-mass rail guidance. Contact impulses are never removed by writing a pose.
    public sealed class RailDynamics : MonoBehaviour
    {
        public TrackPath Path;
        public float TargetSpeed;
        public bool Derailed { get; private set; }
        public Rigidbody Body { get; private set; }
        public float Along => Path.NearestDistance(Body.position);
        public string Contact { get; private set; } = "";

        void OnCollisionStay(Collision collision) => Contact = collision.collider.name;

        void OnCollisionExit(Collision collision)
        {
            if (Contact == collision.collider.name)
                Contact = "";
        }

        public void Initialize(TrackPath path, float mass)
        {
            Path = path;
            Body = GetComponent<Rigidbody>();
            if (!Body)
                Body = gameObject.AddComponent<Rigidbody>();
            Body.mass = mass;
            Body.isKinematic = false;
            Body.useGravity = false;
            Body.centerOfMass = new Vector3(0, 1, 0);
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Body.constraints =
                RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        void FixedUpdate()
        {
            if (!Body || Path == null || Derailed)
                return;
            float along = Along;
            Vector3 tangent = Path.Direction(along);
            Vector3 error = Path.Position(along) - Body.position;
            Vector3 lateralError = Vector3.ProjectOnPlane(error, tangent);
            if (lateralError.magnitude > 2)
            {
                Derailed = true;
                Body.useGravity = true;
                Debug.Log("CODRIVER_RAIL_STOP displaced from guide: " + name);
                return;
            }
            Vector3 lateralVelocity = Vector3.ProjectOnPlane(Body.linearVelocity, tangent);
            Vector3 guidance = Vector3.ClampMagnitude(lateralError * 35 - lateralVelocity * 12, 15);
            float speed = Vector3.Dot(Body.linearVelocity, tangent);
            float traction = Mathf.Clamp((TargetSpeed - speed) * 2, -2, 1);
            Body.AddForce((guidance + tangent * traction) * Body.mass, ForceMode.Force);
            float yaw = Vector3.SignedAngle(transform.forward, tangent, Vector3.up) * Mathf.Deg2Rad;
            Body.AddTorque(
                Vector3.up
                    * Mathf.Clamp(
                        (yaw * 4 - Body.angularVelocity.y) * Body.mass * 8,
                        -Body.mass * 10,
                        Body.mass * 10
                    ),
                ForceMode.Force
            );
        }
    }

    public sealed class ParkedDynamics : MonoBehaviour
    {
        RvpVehicleDynamics dynamics;

        void Awake() =>
            dynamics = new RvpVehicleDynamics(
                gameObject.AddComponent<Rigidbody>(),
                new VehicleProfile()
            );

        void FixedUpdate() =>
            dynamics.Step(new VehicleControls { handbrake = true, brake = 1 }, Time.fixedDeltaTime);
    }
}
