using System;
using UnityEngine;

namespace CoDriver
{
    // The physics pose uses vehicle +X as its axle. Imported meshes keep their
    // authored basis; cylinder meshes carry their 90-degree correction here only.
    [Serializable]
    public sealed class WheelVisualBinding
    {
        public Transform hub;
        public int suspensionIndex;
        public Vector3 axleLocal;
        public Quaternion restRotation;
        public Vector3 suspensionOffset;

        public static WheelVisualBinding Bind(
            Transform vehicle,
            Transform hub,
            int index,
            VehicleProfile profile
        )
        {
            var local = vehicle.InverseTransformPoint(hub.position);
            var rest = Quaternion.Inverse(vehicle.rotation) * hub.rotation;
            return new WheelVisualBinding
            {
                hub = hub,
                suspensionIndex = index,
                restRotation = rest,
                axleLocal = Quaternion.Inverse(rest) * Vector3.right,
                suspensionOffset = new Vector3(
                    local.x - (index % 2 == 0 ? -1 : 1) * profile.TrackAt(index) * .5f,
                    0,
                    local.z - (index < 2 ? 1 : -1) * profile.wheelbase * .5f
                ),
            };
        }

        public void Apply(Transform vehicle, IVehicleDynamics dynamics)
        {
            if (!hub)
                return;
            dynamics.WheelPose(suspensionIndex, out var position, out var rotation);
            hub.SetPositionAndRotation(
                position + vehicle.rotation * suspensionOffset,
                rotation * restRotation
            );
        }
    }
}
