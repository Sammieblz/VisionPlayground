using UnityEngine;

namespace CoDriver
{
    public static class MovableProp
    {
        public static void Add(GameObject target, float mass)
        {
            target.layer = TrafficClearance.ParkedLayer;
            var body = target.AddComponent<Rigidbody>();
            body.mass = mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearDamping = .05f;
            body.angularDamping = .2f;
        }
    }
}
