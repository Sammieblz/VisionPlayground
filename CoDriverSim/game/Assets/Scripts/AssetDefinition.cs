using UnityEngine;

namespace CoDriver
{
    // Import/packaging metadata. It is never serialized into camera observations.
    public sealed class AssetDefinition : MonoBehaviour
    {
        public string assetId,
            sourceUrl,
            sourceSha256,
            license;
        public string preparedSha256;
        public bool publicRedistribution;
        public Vector3 authoredForward = Vector3.forward;
        public float metresPerUnit = 1;
        public float walkingMetresPerSecond,
            runningMetresPerSecond;
        public Transform[] wheels;
        public WheelVisualBinding[] wheelBindings;
        public Transform steeringWheel;
        public Vector3 driverEye,
            sensorMount,
            instrumentCentre,
            deviceCentre;
        public Vector3 instrumentEuler;
        public Vector2 instrumentSize = new Vector2(.7f, .125f);
        public Vector3 leftMirrorCentre,
            rightMirrorCentre,
            rearMirrorCentre;
        public Vector3 leftMirrorEuler,
            rightMirrorEuler;
        public Vector2 sideMirrorSize = new Vector2(.16f, .095f);
        public MirrorSurface[] mirrors;
    }

    [System.Serializable]
    public sealed class MirrorSurface
    {
        public int side;
        public Mesh mesh;
        public Vector3 centre,
            normal;
        public Vector2 size;

        public static Vector3 ViewDirection(Vector3 eye, Vector3 centre, Vector3 normal) =>
            Vector3.Reflect((centre - eye).normalized, normal.normalized).normalized;

        public Vector3 DrivingViewDirection(
            Vector3 eye,
            Vector3 defaultEye,
            Vector3 rearWindowCentre
        )
        {
            // The supplied housing plane is not an optically adjusted mirror.
            // Set the glass normal for the default seat, then retain that fixed
            // adjustment while reflecting the ray from the driver's actual eye.
            float outward = 27 * Mathf.Deg2Rad;
            Vector3 target =
                side == 0
                    ? (rearWindowCentre - centre).normalized
                    : new Vector3(side * Mathf.Sin(outward), -.04f, -Mathf.Cos(outward)).normalized;
            Vector3 incident = (centre - defaultEye).normalized;
            Vector3 opticalNormal = (incident - target).normalized;
            if (Vector3.Dot(opticalNormal, normal) < 0)
                opticalNormal = -opticalNormal;
            Quaternion adjustment = Quaternion.FromToRotation(normal.normalized, opticalNormal);
            return ViewDirection(eye, centre, adjustment * normal.normalized);
        }
    }
}
