using UnityEngine;

namespace CoDriver
{
    // Visual motion follows measured displacement, including reversing and stops.
    // Player wheels are posed separately by the shared raycast suspension.
    public sealed class RollingWheel : MonoBehaviour
    {
        public float Radius = .34f;
        Transform direction;
        Vector3 previous;
        TrafficActor traffic;
        Vector3 localRest;
        Quaternion rotationRest;
        int wheelIndex;
        float turn;

        void Start()
        {
            if (GetComponentInParent<Vehicle>())
            {
                enabled = false;
                return;
            }
            traffic = GetComponentInParent<TrafficActor>();
            var rail = GetComponentInParent<RailVehicle>();
            if (!traffic && !rail)
            {
                enabled = false;
                return;
            }
            direction = traffic ? traffic.transform : transform.parent;
            previous = transform.position;
            if (traffic)
            {
                localRest = traffic.transform.InverseTransformPoint(transform.position);
                rotationRest = Quaternion.Inverse(traffic.transform.rotation) * transform.rotation;
                wheelIndex = (localRest.z >= 0 ? 0 : 2) + (localRest.x >= 0 ? 1 : 0);
            }
        }

        void LateUpdate()
        {
            if (traffic && traffic.Dynamics != null)
            {
                traffic.Dynamics.WheelPose(wheelIndex, out var position, out var rotation);
                var local = traffic.transform.InverseTransformPoint(position);
                transform.position = traffic.transform.TransformPoint(
                    new Vector3(localRest.x, local.y, localRest.z)
                );
                turn = Mathf.Repeat(
                    turn + traffic.Dynamics.Rpm[wheelIndex] * 6 * Time.deltaTime,
                    360
                );
                float steering = wheelIndex < 2 ? traffic.Dynamics.LastSteering : 0;
                transform.rotation =
                    traffic.transform.rotation
                    * Quaternion.Euler(0, steering, 0)
                    * Quaternion.AngleAxis(turn, Vector3.right)
                    * rotationRest;
                return;
            }
            Vector3 displacement = transform.position - previous;
            previous = transform.position;
            // Scenario placement is not a rolling movement.
            if (displacement.sqrMagnitude < 16)
                transform.Rotate(
                    direction.right,
                    Vector3.Dot(displacement, direction.forward) / Radius * Mathf.Rad2Deg,
                    Space.World
                );
        }

        public static void PrepareImported(GameObject model)
        {
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>())
            {
                if (
                    !renderer.name.ToLowerInvariant().Contains("wheel")
                    || renderer.name.ToLowerInvariant().Contains("steering")
                )
                    continue;
                var pivot = new GameObject("Wheel rotation pivot").transform;
                pivot.SetParent(renderer.transform.parent, false);
                pivot.position = renderer.bounds.center;
                float radius = renderer.bounds.size.y / 2;
                renderer.transform.SetParent(pivot, true);
                pivot.gameObject.AddComponent<RollingWheel>().Radius = Mathf.Max(.1f, radius);
            }
        }
    }
}
