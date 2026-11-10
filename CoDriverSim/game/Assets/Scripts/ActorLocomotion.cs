using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace CoDriver
{
    // Navigation proposes velocity. PhysX alone owns the position of a solid actor.
    [DefaultExecutionOrder(150)]
    public sealed class ActorLocomotion : MonoBehaviour
    {
        BoxCollider detourVehicle;
        readonly Vector3[] detourCorners = new Vector3[2];
        int detourCorner;
        float detourExpires;
        float nextDetourAttempt;
        Rigidbody body;
        NavMeshAgent agent;
        Collider shape;
        Transform visual;
        Quaternion restRotation;
        float recoverUntil,
            blockedUntil;
        bool flying;
        bool skeletalReaction;
        float reactionStarted;
        public float ImpactSpeed { get; private set; }
        public float ReactionElapsed => Time.time - reactionStarted;
        Vector3 flightTarget;
        PhysicsScene physics;
        public float Speed =>
            body ? Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude : 0;
        public bool Reacting => Time.time < recoverUntil;
        Vector3 lastDesired;
        float stepRise = float.NaN,
            stepLedge = float.NaN;
        public string Diagnostic =>
            "intent="
            + lastDesired.ToString("F2")
            + " velocity="
            + body.linearVelocity.ToString("F2")
            + " foot="
            + shape.bounds.min.y.ToString("F2")
            + " rise="
            + stepRise.ToString("F2")
            + " ledge="
            + stepLedge.ToString("F2");

        public void Initialize(string species, Transform model = null)
        {
            body = GetComponent<Rigidbody>();
            physics = gameObject.scene.GetPhysicsScene();
            agent = GetComponent<NavMeshAgent>();
            shape = GetComponent<Collider>();
            visual = model;
            if (visual)
                restRotation = visual.localRotation;
            skeletalReaction = visual && visual.GetComponentInChildren<RigMotion>();
            body.isKinematic = false;
            body.mass =
                species == "Dog" ? 22
                : species == "Cat" ? 4.5f
                : species == "Bird" ? 1.4f
                : 78;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearDamping = 0;
            shape.sharedMaterial = new PhysicsMaterial("Actor contact")
            {
                dynamicFriction = .1f,
                staticFriction = .15f,
                bounciness = 0,
                frictionCombine = PhysicsMaterialCombine.Minimum,
            };
            if (agent)
            {
                agent.updatePosition = false;
                agent.updateRotation = false;
            }
        }

        public void FlyToward(Vector3 target)
        {
            flying = true;
            flightTarget = target;
        }

        public void Land()
        {
            flying = false;
        }

        void Update()
        {
            if (agent && agent.enabled && agent.isOnNavMesh && !body.isKinematic)
                agent.nextPosition = body.position;
            if (visual)
                visual.localRotation = Quaternion.Slerp(
                    visual.localRotation,
                    restRotation * Quaternion.Euler(Reacting && !skeletalReaction ? 35 : 0, 0, 0),
                    Time.deltaTime * 6
                );
        }

        void FixedUpdate()
        {
            if (!body || !shape)
                return;
            // Boarding animation has no world collider; it cannot impart a contact impulse.
            bool seated = !shape.enabled;
            if (body.isKinematic != seated)
            {
                if (seated)
                    body.linearVelocity = body.angularVelocity = Vector3.zero;
                body.isKinematic = seated;
            }
            if (seated)
                return;
            body.useGravity = !flying || Reacting;
            if (Reacting)
                return;
            Vector3 desired = Vector3.zero;
            if (flying)
                desired = Vector3.ClampMagnitude((flightTarget - body.position) * 3, 5);
            else if (agent && agent.enabled && agent.isOnNavMesh && !agent.isStopped)
                desired = agent.desiredVelocity;
            // A physical contact or rounded corner can leave the body beyond the
            // navigation boundary. The agent's internal position is clamped there;
            // copying its desired velocity alone can strand the solid body in the
            // road. Walk back at bounded speed/force, with the same vehicle checks.
            if (
                !flying
                && agent
                && agent.enabled
                && !NavMesh.SamplePosition(body.position, out _, .25f, agent.areaMask)
                && NavMesh.SamplePosition(body.position, out var refuge, 4, agent.areaMask)
            )
                desired = Vector3.ClampMagnitude(
                    Vector3.ProjectOnPlane(refuge.position - body.position, Vector3.up) * 2,
                    Mathf.Min(1, agent.speed)
                );
            StepVelocity(desired, Time.fixedDeltaTime);
        }

        public void StepVelocity(Vector3 desired, float dt)
        {
            lastDesired = desired;
            if (!body || body.isKinematic || Reacting)
                return;
            if (!flying)
                desired.y = 0;
            float requestedSpeed = desired.magnitude;
            if (detourVehicle)
            {
                if (requestedSpeed < .1f || Time.time > detourExpires || !Stationary(detourVehicle))
                {
                    detourVehicle = null;
                    desired = Vector3.zero;
                }
                else
                {
                    while (
                        detourCorner < 2
                        && Vector3
                            .ProjectOnPlane(detourCorners[detourCorner] - body.position, Vector3.up)
                            .magnitude < .2f
                    )
                        detourCorner++;
                    if (detourCorner == 2)
                        detourVehicle = null;
                    else
                        desired =
                            Vector3
                                .ProjectOnPlane(
                                    detourCorners[detourCorner] - body.position,
                                    Vector3.up
                                )
                                .normalized * Mathf.Min(1.2f, requestedSpeed);
                }
            }
            float distance = Mathf.Max(.65f, desired.magnitude * .6f);
            if (desired.sqrMagnitude > .01f)
            {
                bool blocked = physics.SphereCast(
                    body.position + Vector3.up * .35f,
                    .22f,
                    desired.normalized,
                    out var hit,
                    distance,
                    (1 << TrafficClearance.PlayerLayer)
                        | (1 << TrafficClearance.TrafficLayer)
                        | (1 << TrafficClearance.ParkedLayer)
                        | (1 << TrafficClearance.RailLayer),
                    QueryTriggerInteraction.Ignore
                );
                if (blocked && !detourVehicle && hit.collider is BoxCollider vehicle)
                    PlanVehicleDetour(vehicle);
                if (blocked || Time.time < blockedUntil)
                    desired = Vector3.zero;
            }
            Vector3 current = flying
                ? body.linearVelocity
                : Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            Vector3 acceleration = Vector3.ClampMagnitude((desired - current) * 7, flying ? 8 : 3);
            body.AddForce(acceleration * body.mass, ForceMode.Force);
            if (!flying && desired.sqrMagnitude > .03f)
                StepCurb(desired.normalized);
            if (desired.sqrMagnitude > .03f)
            {
                var flat = Vector3.ProjectOnPlane(desired, Vector3.up);
                if (flat.sqrMagnitude > .001f)
                    body.MoveRotation(
                        Quaternion.RotateTowards(
                            body.rotation,
                            Quaternion.LookRotation(flat),
                            240 * dt
                        )
                    );
            }
        }

        static bool Stationary(BoxCollider vehicle) =>
            vehicle
            && vehicle.attachedRigidbody
            && vehicle.attachedRigidbody.linearVelocity.sqrMagnitude < .09f
            && vehicle.attachedRigidbody.angularVelocity.sqrMagnitude < .04f;

        void PlanVehicleDetour(BoxCollider vehicle)
        {
            if (
                Time.time < nextDetourAttempt
                || !agent
                || !agent.enabled
                || !agent.isOnNavMesh
                || flying
                || vehicle.gameObject.layer == TrafficClearance.RailLayer
                || !Stationary(vehicle)
            )
                return;
            nextDetourAttempt = Time.time + .75f;
            // A stopped car may occupy the pedestrian's straight NavMesh route.
            // Walk around its end using bounded locomotion, within the already
            // permitted navigation area. Never move the vehicle or snap the person.
            var local = vehicle.transform.InverseTransformPoint(body.position) - vehicle.center;
            var goal = vehicle.transform.InverseTransformPoint(agent.destination) - vehicle.center;
            float side = local.x >= 0 ? 1 : -1;
            // A person already beyond the nose can be heading toward the same
            // side of the vehicle. Do not send them back across the entire car.
            float exitSide = goal.x >= 0 ? 1 : -1;
            float best = float.PositiveInfinity;
            for (int end = -1; end <= 1; end += 2)
            {
                var first = vehicle.transform.TransformPoint(
                    vehicle.center
                        + new Vector3(
                            side * (vehicle.size.x * .5f + .65f),
                            -vehicle.center.y,
                            end * (vehicle.size.z * .5f + .7f)
                        )
                );
                var second = vehicle.transform.TransformPoint(
                    vehicle.center
                        + new Vector3(
                            exitSide * (vehicle.size.x * .5f + .65f),
                            -vehicle.center.y,
                            end * (vehicle.size.z * .5f + .7f)
                        )
                );
                if (
                    !NavMesh.SamplePosition(first, out var a, .35f, agent.areaMask)
                    || !NavMesh.SamplePosition(second, out var b, .35f, agent.areaMask)
                    || NavMesh.Raycast(agent.nextPosition, a.position, out _, agent.areaMask)
                    || NavMesh.Raycast(a.position, b.position, out _, agent.areaMask)
                )
                    continue;
                float length =
                    Vector3.Distance(body.position, a.position)
                    + Vector3.Distance(a.position, b.position)
                    + Vector3.Distance(b.position, agent.destination);
                if (length >= best)
                    continue;
                best = length;
                detourCorners[0] = a.position;
                detourCorners[1] = b.position;
            }
            if (float.IsPositiveInfinity(best))
                return;
            detourVehicle = vehicle;
            detourCorner = 0;
            detourExpires = Time.time + 12;
        }

        void StepCurb(Vector3 forward)
        {
            stepRise = stepLedge = float.NaN;
            // A bounded leg force lifts over small static curbs. Never step onto
            // a vehicle or change a solid actor's position to match navigation.
            Vector3 foot = body.position;
            foot.y = shape.bounds.min.y;
            int groundMask = (1 << TrafficClearance.RoadLayer) | 1;
            if (
                !physics.Raycast(
                    foot + Vector3.up * .12f,
                    Vector3.down,
                    out var support,
                    .4f,
                    groundMask,
                    QueryTriggerInteraction.Ignore
                ) || support.rigidbody
            )
                return;
            if (
                physics.Raycast(
                    foot + forward * .45f + Vector3.up * .32f,
                    Vector3.down,
                    out var step,
                    .34f,
                    groundMask,
                    QueryTriggerInteraction.Ignore
                )
                && !step.rigidbody
                && step.normal.y > .7f
            )
            {
                float rise = step.point.y - foot.y;
                float ledge = step.point.y - support.point.y;
                stepRise = rise;
                stepLedge = ledge;
                if (ledge > .03f && ledge < .29f && rise > -.04f && rise < .29f)
                {
                    float lift = Mathf.Max(
                        0,
                        -Physics.gravity.y
                            + Mathf.Clamp(
                                (rise + .04f) * 40 - body.linearVelocity.y * 10,
                                Physics.gravity.y,
                                8
                            )
                    );
                    body.AddForce(Vector3.up * (lift * body.mass), ForceMode.Force);
                }
            }
        }

        void OnCollisionEnter(Collision contact)
        {
            var other = contact.rigidbody;
            if (!other || other.mass < 150)
                return;
            blockedUntil = Time.time + .5f;
            Debug.Log(
                "CODRIVER_ACTOR_CONTACT "
                    + name
                    + " with "
                    + contact.collider.name
                    + " relative_speed="
                    + contact.relativeVelocity.magnitude.ToString("F2")
                    + " impulse="
                    + contact.impulse.magnitude.ToString("F2")
            );
            if (contact.relativeVelocity.magnitude > 1)
            {
                ImpactSpeed = contact.relativeVelocity.magnitude;
                reactionStarted = Time.time;
                recoverUntil = Time.time + (skeletalReaction && ImpactSpeed > 3 ? 4.8f : 2.5f);
            }
        }

        void OnCollisionStay(Collision contact)
        {
            if (contact.rigidbody && contact.rigidbody.mass > 150)
                blockedUntil = Time.time + .15f;
        }
    }
}
