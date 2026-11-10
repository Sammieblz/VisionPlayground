using System;
using UnityEngine;

namespace CoDriver
{
    [Serializable]
    public sealed class BuildIdentity
    {
        public string created_utc,
            code_sha256,
            map_sha256,
            routing_sha256,
            unity;
        public string asset_profile;
        public string scenery_sha256, art_sha256;
        public float physics_step;
        public static BuildIdentity Current =>
            JsonUtility.FromJson<BuildIdentity>(
                Resources.Load<TextAsset>("BuildIdentity")?.text ?? "{}"
            );
    }
}
