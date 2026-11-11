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
    }
}
