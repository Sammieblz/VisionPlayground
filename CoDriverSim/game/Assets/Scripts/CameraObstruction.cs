using UnityEngine;

namespace CoDriver
{
    // Presentation only. Neither the sensor camera nor an actor's physical pose
    // uses this correction. Move a spectator camera in front of static scenery.
    public static class CameraObstruction
    {
        static readonly RaycastHit[] hits = new RaycastHit[64];

        public static Vector3 Resolve(Vector3 focus, Vector3 desired, float radius = .24f)
        {
            var offset = desired - focus;
            float distance = offset.magnitude;
            if (distance < .001f)
                return desired;
            var direction = offset / distance;
            int count = Physics.SphereCastNonAlloc(
                focus,
                radius,
                direction,
                hits,
                distance,
                ~0,
                QueryTriggerInteraction.Ignore
            );
            float nearest = distance;
            // A saturated query must still inspect every obstruction. Allocation
            // is only for this exceptional dense view, not the normal frame.
            var results =
                count == hits.Length
                    ? Physics.SphereCastAll(
                        focus,
                        radius,
                        direction,
                        distance,
                        ~0,
                        QueryTriggerInteraction.Ignore
                    )
                    : hits;
            if (results != hits)
                count = results.Length;
            for (int i = 0; i < count; i++)
                if (!results[i].rigidbody)
                    nearest = Mathf.Min(nearest, Mathf.Max(0, results[i].distance - .08f));
            return focus + direction * nearest;
        }
    }
}
