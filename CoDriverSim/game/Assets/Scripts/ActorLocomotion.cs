using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace CoDriver
{
    // Navigation proposes velocity. PhysX alone owns the position of a solid actor.
    [DefaultExecutionOrder(150)]
    public sealed class ActorLocomotion : MonoBehaviour
    {
        static readonly float[] walkerDetourAngles = { 55f, -55f, 90f, -90f, 125f, -125f };
        BoxCollider detourVehicle;
        readonly Vector3[] detourCorners = new Vector3[2];
        int detourCorner;
        float detourExpires;
        float nextDetourAttempt;
        Rigidbody body;
        Town town;
        float nextRailLook;
        bool waitingForRail;
        NavMeshAgent agent;
        Collider shape;
        Transform visual;
        Quaternion restRotation;
        float recoverUntil,
            blockedUntil;
        Vector3 vehicleContactNormal;
        float vehicleContactUntil,
            sideContactUntil;
        string sideContact = "";
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
        string vehicleBlocker = "";
        string detourStatus = "None";
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
            + stepLedge.ToString("F2")
            + " contact="
            + (Time.time < sideContactUntil ? sideContact : "None")
            + " avoidance="
            + vehicleBlocker
            + " rail_wait="
            + waitingForRail
            + " detour="
            + detourStatus
            + " steering_corner="
            + (
                agent && agent.enabled && agent.isOnNavMesh
                    ? agent.steeringTarget.ToString("F2")
                    : "None"
            );

        public void Initialize(string species, Transform model = null)
        {
            body = GetComponent<Rigidbody>();
            town = GetComponentInParent<Town>();
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
            {
                desired = agent.desiredVelocity;
                Vector3 toGoal = Vector3.ProjectOnPlane(
                    agent.destination - body.position,
                    Vector3.up
                );
                Vector3 toCorner = Vector3.ProjectOnPlane(
                    agent.steeringTarget - body.position,
                    Vector3.up
                );
                bool finalApproach =
                    toGoal.sqrMagnitude < 4
                    && !NavMesh.Raycast(
                        agent.nextPosition,
                        agent.destination,
                        out _,
                        agent.areaMask
                    );
                bool cornerApproach =
                    !finalApproach
                    && toCorner.sqrMagnitude < 1.5f * 1.5f
                    && Vector3
                        .ProjectOnPlane(agent.steeringTarget - agent.destination, Vector3.up)
                        .sqrMagnitude > .25f;
                if (
                    agent.hasPath
                    && !agent.pathPending
                    && agent.pathStatus == NavMeshPathStatus.PathComplete
                    && (finalApproach || cornerApproach)
                )
                {
                    // NavMesh's simulated velocity can oscillate at the destination
                    // while a separate force-driven body catches up. Brake against
                    // the physical distance on the unobstructed final segment.
                    // A force-driven body must still overcome contact friction
                    // near the destination. Tapering to zero 15 cm early left
                    // walkers stranded outside the far sidewalk indefinitely.
                    // Keep a slow approach until the actual body arrives; the
                    // same bounded acceleration then brakes it to rest.
                    // Apply the same physical arrival control to intermediate
                    // corners. A force-driven body cannot instantaneously turn
                    // at NavMesh speed; overshooting a narrow corner repeatedly
                    // left Person 27 oscillating against the navigation edge.
                    // Follow the existing corridor, never a shortcut to its goal.
                    Vector3 approach = finalApproach ? toGoal : toCorner;
                    float speed =
                        finalApproach && approach.magnitude <= .04f
                            ? 0
                            : Mathf.Min(
                                agent.speed,
                                Mathf.Max(.3f, (approach.magnitude - .04f) * 2)
                            );
                    bool personAhead =
                        speed > .05f
                        && physics.SphereCast(
                            body.position + Vector3.up * .8f,
                            .25f,
                            approach.normalized,
                            out _,
                            Mathf.Min(approach.magnitude, .7f),
                            1 << TrafficClearance.WalkerLayer,
                            QueryTriggerInteraction.Ignore
                        );
                    // Retain NavMesh's avoidance velocity when another person
                    // occupies the final segment; zeroing it makes both agents
                    // wait indefinitely instead of passing one another.
                    if (!personAhead)
                        desired = approach.normalized * speed;
                }
            }
            // A physical contact or rounded corner can leave the body beyond the
            // navigation boundary. The agent's internal position is clamped there;
            // copying its desired velocity alone can strand the solid body in the
            // road. Walk back at bounded speed/force, with the same vehicle checks.
            if (
                !flying
                && agent
                && agent.enabled
                && NavMesh.SamplePosition(body.position, out var refuge, 4, agent.areaMask)
                && Vector3.ProjectOnPlane(refuge.position - body.position, Vector3.up).sqrMagnitude
                    > .08f * .08f
            )
            {
                var correction = Vector3.ProjectOnPlane(
                    refuge.position - body.position,
                    Vector3.up
                );
                // Preserve progress along the boundary while steering back onto
                // it. A tiny correction-only velocity could not overcome static
                // friction and stranded actors just eight centimetres outside.
                float outward = Vector3.Dot(desired, correction.normalized);
                if (outward < 0)
                    desired -= correction.normalized * outward;
                desired = Vector3.ClampMagnitude(
                    desired + correction * 6,
                    Mathf.Min(1, agent.speed)
                );
            }
            StepVelocity(desired, Time.fixedDeltaTime);
        }

        public void StepVelocity(Vector3 desired, float dt)
        {
            lastDesired = desired;
            vehicleBlocker = "";
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
            if (
                !flying
                && agent
                && agent.enabled
                && agent.isOnNavMesh
                && desired.sqrMagnitude > .01f
            )
                desired = AvoidPhysicalWalker(desired);
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
                {
                    vehicleBlocker = blocked ? hit.collider.name : "Impact pause";
                    desired = Vector3.zero;
                }
            }
            if (Time.time < vehicleContactUntil)
            {
                // A continuing side contact must not freeze all locomotion.
                // Remove motion into the vehicle while allowing a bounded step
                // away or along its side after the initial impact reaction.
                float into = Vector3.Dot(desired, vehicleContactNormal);
                if (into < 0)
                    desired -= vehicleContactNormal * into;
            }
            if (!flying && town && desired.sqrMagnitude > .01f)
            {
                if (Time.time >= nextRailLook)
                {
                    nextRailLook = Time.time + .1f;
                    var bounds = shape.bounds;
                    float radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
                    var extent = new Vector3(radius, bounds.extents.y, radius);
                    waitingForRail =
                        town.Tram && town.Tram.ApproachesWalkingPath(bounds.center, extent, desired)
                        || town.Train
                            && town.Train.ApproachesWalkingPath(bounds.center, extent, desired);
                }
                if (waitingForRail)
                    desired = Vector3.zero;
            }
            else
                waitingForRail = false;
            lastDesired = desired;
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

        Vector3 AvoidPhysicalWalker(Vector3 desired)
        {
            // NavMesh clamps agents to its surface. A real body displaced just
            // off that surface can therefore be in front of another walker even
            // when RVO sees its agent behind them. Check the solid bodies too.
            var origin = shape.bounds.center;
            float radius = Mathf.Clamp(
                Mathf.Min(shape.bounds.extents.x, shape.bounds.extents.z) - .02f,
                .1f,
                .23f
            );
            if (
                !physics.SphereCast(
                    origin,
                    radius,
                    desired.normalized,
                    out var hit,
                    .7f,
                    1 << TrafficClearance.WalkerLayer,
                    QueryTriggerInteraction.Ignore
                )
                || hit.collider == shape
            )
                return desired;
            foreach (float angle in walkerDetourAngles)
            {
                var direction = Quaternion.AngleAxis(angle, Vector3.up) * desired.normalized;
                if (
                    !NavMesh.SamplePosition(
                        body.position + direction * .8f,
                        out var point,
                        .2f,
                        agent.areaMask
                    ) || NavMesh.Raycast(agent.nextPosition, point.position, out _, agent.areaMask)
                )
                    continue;
                var travel = Vector3.ProjectOnPlane(point.position - body.position, Vector3.up);
                if (
                    travel.sqrMagnitude < .4f * .4f
                    || physics.SphereCast(
                        origin,
                        radius,
                        travel.normalized,
                        out _,
                        travel.magnitude,
                        ~(1 << TrafficClearance.RoadLayer),
                        QueryTriggerInteraction.Ignore
                    )
                )
                    continue;
                return travel.normalized * Mathf.Min(.8f, desired.magnitude);
            }
            return Vector3.zero;
        }

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
            detourStatus = "No clear navigation route around " + vehicle.name;
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
                // Beyond the nose/tail, the whole footprint can move laterally
                // toward the destination side. Choosing a side from an almost
                // zero local x sent the recorded walker to an unreachable corner
                // and deadlocked it against the truck that was yielding to it.
                float radius = Mathf.Max(shape.bounds.extents.x, shape.bounds.extents.z);
                float firstSide =
                    local.z * end > vehicle.size.z * .5f + radius + .05f ? exitSide : side;
                var first = vehicle.transform.TransformPoint(
                    vehicle.center
                        + new Vector3(
                            firstSide * (vehicle.size.x * .5f + .65f),
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
                    !TryDetourPoint(agent.nextPosition, first, out var a)
                    || !TryDetourPoint(a, second, out var b)
                    || !DetourSegmentClear(body.position, a)
                    || !DetourSegmentClear(a, b)
                )
                    continue;
                float length =
                    Vector3.Distance(body.position, a)
                    + Vector3.Distance(a, b)
                    + Vector3.Distance(b, agent.destination);
                if (length >= best)
                    continue;
                best = length;
                detourCorners[0] = a;
                detourCorners[1] = b;
            }
            if (float.IsPositiveInfinity(best))
                return;
            detourVehicle = vehicle;
            detourCorner = 0;
            detourExpires = Time.time + 12;
            detourStatus = $"{vehicle.name} via {detourCorners[0]:F2}, {detourCorners[1]:F2}";
        }

        bool TryDetourPoint(Vector3 start, Vector3 target, out Vector3 point)
        {
            point = default;
            if (!NavMesh.SamplePosition(target, out var sample, .35f, agent.areaMask))
            {
                detourStatus = $"Corner off navigation at {target:F2}";
                return false;
            }
            point = sample.position;
            if (
                (point - start).sqrMagnitude < .0001f
                || !NavMesh.Raycast(start, point, out var hit, agent.areaMask)
            )
                return true;
            // SamplePosition can return the exact boundary. Only an endpoint
            // hit may be inset; a boundary earlier along the route still rejects
            // the segment. Re-sample and ray-check the interior point as well.
            if (Vector3.Distance(hit.position, point) < .05f)
            {
                var interior = Vector3.MoveTowards(
                    point,
                    start,
                    Mathf.Min(.1f, Vector3.Distance(point, start) * .5f)
                );
                if (
                    NavMesh.SamplePosition(interior, out sample, .025f, agent.areaMask)
                    && !NavMesh.Raycast(start, sample.position, out _, agent.areaMask)
                )
                {
                    point = sample.position;
                    return true;
                }
            }
            detourStatus = $"Route crosses navigation boundary at {hit.position:F2}";
            return false;
        }

        bool DetourSegmentClear(Vector3 start, Vector3 finish)
        {
            var delta = Vector3.ProjectOnPlane(finish - start, Vector3.up);
            if (delta.sqrMagnitude < .0001f)
                return true;
            float radius = Mathf.Max(shape.bounds.extents.x, shape.bounds.extents.z) + .02f;
            if (
                !physics.SphereCast(
                    start + Vector3.up * .35f,
                    radius,
                    delta.normalized,
                    out var hit,
                    delta.magnitude,
                    (1 << TrafficClearance.PlayerLayer)
                        | (1 << TrafficClearance.TrafficLayer)
                        | (1 << TrafficClearance.ParkedLayer)
                        | (1 << TrafficClearance.RailLayer),
                    QueryTriggerInteraction.Ignore
                )
            )
                return true;
            detourStatus = "Physical detour blocked by " + hit.collider.name;
            return false;
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
                    .55f,
                    groundMask,
                    QueryTriggerInteraction.Ignore
                ) || support.rigidbody
            )
                return;
            if (TryStepLift(foot + forward * .45f, foot.y, support.point.y, groundMask))
                return;
            if (Speed > .25f)
                return;
            // At an oblique sidewalk corner, the capsule's side can touch a
            // raised surface while the centre-line probe looks past its tip.
            // Only a stalled/starting walker needs these extra footprint probes.
            // The same static-support, ledge-height and bounded-force limits apply.
            float width = Mathf.Clamp(
                Mathf.Min(shape.bounds.extents.x, shape.bounds.extents.z) * .8f,
                .06f,
                .22f
            );
            var right = Vector3.Cross(Vector3.up, forward);
            for (int ahead = 1; ahead <= 2; ahead++)
            for (int side = -1; side <= 1; side++)
                if (
                    TryStepLift(
                        foot + forward * (ahead * .18f) + right * (side * width),
                        foot.y,
                        support.point.y,
                        groundMask
                    )
                )
                    return;
        }

        bool TryStepLift(Vector3 probe, float footHeight, float supportHeight, int groundMask)
        {
            if (
                physics.Raycast(
                    probe + Vector3.up * .42f,
                    Vector3.down,
                    out var step,
                    .44f,
                    groundMask,
                    QueryTriggerInteraction.Ignore
                )
                && !step.rigidbody
                && step.normal.y > .7f
            )
            {
                float rise = step.point.y - footHeight;
                float ledge = step.point.y - supportHeight;
                stepRise = rise;
                stepLedge = ledge;
                // The back of Cedar's raised sidewalk is 33 cm above the
                // landscaped ground. A displaced walker must also recover from
                // that side, using the same bounded leg force as the road curb.
                if (ledge > .03f && ledge < .38f && rise > -.04f && rise < .38f)
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
                    return true;
                }
            }
            return false;
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
                    + " at="
                    + body.position.ToString("F2")
                    + " other_at="
                    + other.position.ToString("F2")
                    + " other_velocity="
                    + other.linearVelocity.ToString("F2")
                    + " actor="
                    + (GetComponent<Walker>()?.NavigationDetail ?? "Physical fixture")
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
            for (int i = 0; i < contact.contactCount; i++)
            {
                var normal = contact.GetContact(i).normal;
                if (Mathf.Abs(normal.y) > .5f)
                    continue;
                sideContact = contact.collider.name;
                sideContactUntil = Time.time + .15f;
                if (contact.rigidbody && contact.rigidbody.mass > 150)
                {
                    vehicleContactNormal = Vector3.ProjectOnPlane(normal, Vector3.up).normalized;
                    vehicleContactUntil = Time.time + .15f;
                }
            }
        }
    }
}
