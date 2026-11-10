using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace CoDriver
{
    // Simulation control interface only. Do not add this to CameraHeader.
    [Serializable]
    public sealed class SimulationVehicleConfiguration
    {
        public int version = 1;
        public string profile_id;
        public string profile_parameters_json;
        public float[] camera_position_ego_m;
        public float mass_kg, wheelbase_m, steering_ratio, camera_height_m;
        public float fx = 455, fy = 455;
        public int width = 964, height = 604;

        public static string Encode(VehicleProfile profile) => JsonUtility.ToJson(
            new SimulationVehicleConfiguration { profile_id = profile.id, mass_kg = profile.mass,
                profile_parameters_json = JsonUtility.ToJson(profile),
                camera_position_ego_m = new[] { profile.sensorMount.z, -profile.sensorMount.x, profile.sensorMount.y },
                wheelbase_m = profile.wheelbase, steering_ratio = profile.steeringRatio,
                camera_height_m = profile.sensorMount.y });

        public static string Hash(string value)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value)))
                    .Replace("-", "").ToLowerInvariant();
        }
    }
}
