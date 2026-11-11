using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CoDriver
{
    // NPC traffic-rule memory; never an input to camera perception or Comma.
    public sealed class StopSignProgress
    {
        sealed class Visit
        {
            public Vector3 position;
            public float stoppedSeconds;
            public bool cleared;
        }

        readonly Dictionary<string, Visit> visits = new Dictionary<string, Visit>();

        public void UpdatePosition(Vector3 position)
        {
            foreach (
                string id in visits
                    .Where(p => (p.Value.position - position).sqrMagnitude > 2500)
                    .Select(p => p.Key)
                    .ToArray()
            )
                visits.Remove(id);
        }

        public bool RequiresStop(
            string id,
            Vector3 sign,
            float speed,
            float frontDistance,
            float delta
        )
        {
            if (frontDistance <= -.5f || frontDistance >= 40)
                return false;
            if (!visits.TryGetValue(id, out var visit))
                visits[id] = visit = new Visit { position = sign };
            if (visit.cleared)
                return false;
            visit.stoppedSeconds =
                speed < .2f && frontDistance < 4 ? visit.stoppedSeconds + delta : 0;
            visit.cleared = visit.stoppedSeconds >= 1;
            return !visit.cleared;
        }
    }
}
