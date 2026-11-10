using System;
using System.Linq;
using UnityEngine;

namespace CoDriver
{
    // Presentation of the construction activity, not a vehicle controller.
    // The machine's finite-mass root is moved only by PhysX.
    public sealed class ExcavatorVisual : MonoBehaviour
    {
        sealed class Hydraulic
        {
            public Transform start,
                end,
                barrel,
                piston;
            public float barrelLength,
                radius;
        }

        Transform upper,
            boom,
            dipper,
            bucket;
        Quaternion upperRest,
            boomRest,
            dipperRest,
            bucketRest;
        Hydraulic[] hydraulics;
        Animator seatedOperator;
        string seatedClip;
        public Transform Bucket => bucket;
        public float HydraulicEndpointError { get; private set; }

        public void Initialize()
        {
            if (upper)
                return;
            var parts = GetComponentsInChildren<Transform>(true);
            Transform Find(string name) => parts.Single(p => p.name == name);
            upper = Find("Upper");
            boom = Find("Boom");
            dipper = Find("Dipper");
            bucket = Find("Bucket");
            upperRest = upper.localRotation;
            boomRest = boom.localRotation;
            dipperRest = dipper.localRotation;
            bucketRest = bucket.localRotation;
            string[] names = { "Boom", "Dipper", "Bucket" };
            float[] lengths = { .55f, .45f, .48f };
            float[] radii = { .047f, .038f, .031f };
            hydraulics = names
                .Select(
                    (name, i) =>
                        new Hydraulic
                        {
                            start = Find(name + "CylinderBase"),
                            end = Find(name + "CylinderEnd"),
                            barrel = Cylinder(name + " hydraulic barrel", Art.Yellow),
                            piston = Cylinder(name + " polished piston", Art.Metal),
                            barrelLength = lengths[i],
                            radius = radii[i],
                        }
                )
                .ToArray();
            var operatorPrefab = Resources.Load<GameObject>("WorldArt/Civilians/Citizen_A");
            if (operatorPrefab)
            {
                var person = Instantiate(operatorPrefab, upper);
                person.name = "Seated construction operator";
                seatedOperator = person.GetComponent<Animator>();
                seatedOperator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                seatedOperator.applyRootMotion = false;
                seatedClip = seatedOperator
                    .runtimeAnimatorController.animationClips.Single(c =>
                        c.name.EndsWith("Driving_Loop")
                    )
                    .name;
                // The visual is attached to the seat, never a navigation/physics actor.
                // Sample explicitly with the machinery cycle so capture and runtime agree.
                seatedOperator.enabled = false;
            }
        }

        Transform Cylinder(string name, Material material) =>
            Art.Shape(
                name,
                transform,
                Vector3.zero,
                Vector3.one,
                material,
                PrimitiveType.Cylinder,
                false
            ).transform;

        public void Pose(float time)
        {
            Initialize();
            float t = Mathf.Repeat(time, 16);
            Vector4 dig = new Vector4(0, 10, 0, -10);
            Vector4 lifted = new Vector4(0, -14, 12, -25);
            Vector4 swung = new Vector4(-55, -14, 12, -25);
            Vector4 dumped = new Vector4(-55, -14, 12, 45);
            Vector4 pose;
            if (t < 5)
            {
                pose = dig;
                float scoop = Mathf.Pow(Mathf.Sin(t / 5 * Mathf.PI), 2);
                pose.y += 2 * scoop;
                pose.w += 8 * scoop;
            }
            else if (t < 9)
                pose = Blend(dig, lifted, (t - 5) / 4);
            else if (t < 12)
                pose = Blend(lifted, swung, (t - 9) / 3);
            else if (t < 14)
                pose = Blend(swung, dumped, (t - 12) / 2);
            else
                pose = Blend(dumped, dig, (t - 14) / 2);
            upper.localRotation = upperRest * Quaternion.AngleAxis(pose.x, Vector3.up);
            boom.localRotation = boomRest * Quaternion.AngleAxis(pose.y, Vector3.right);
            dipper.localRotation = dipperRest * Quaternion.AngleAxis(pose.z, Vector3.right);
            bucket.localRotation = bucketRest * Quaternion.AngleAxis(pose.w, Vector3.right);
            HydraulicEndpointError = 0;
            foreach (var hydraulic in hydraulics)
            {
                Vector3 delta = hydraulic.end.position - hydraulic.start.position;
                float length = delta.magnitude;
                if (length < hydraulic.barrelLength + .02f)
                    throw new InvalidOperationException(
                        "Construction pose exceeds hydraulic stroke"
                    );
                Vector3 direction = delta / length;
                Segment(
                    hydraulic.barrel,
                    hydraulic.start.position,
                    hydraulic.start.position + direction * hydraulic.barrelLength,
                    hydraulic.radius
                );
                // The rod overlaps the gland; its far end always meets the moving pin.
                Vector3 rodStart =
                    hydraulic.start.position + direction * (hydraulic.barrelLength - .035f);
                Segment(
                    hydraulic.piston,
                    rodStart,
                    hydraulic.end.position,
                    hydraulic.radius * .48f
                );
                Vector3 actualEnd = hydraulic.piston.TransformPoint(Vector3.up);
                HydraulicEndpointError = Mathf.Max(
                    HydraulicEndpointError,
                    Vector3.Distance(actualEnd, hydraulic.end.position)
                );
            }
            PoseOperator(t);
        }

        void PoseOperator(float time)
        {
            if (!seatedOperator)
                return;
            seatedOperator.Play(seatedClip, 0, Mathf.Repeat(time / 4, 1));
            seatedOperator.Update(0);
            var hips = seatedOperator.GetBoneTransform(HumanBodyBones.Hips);
            // Measured from the source seat cushion, relative to the upper slew pivot.
            seatedOperator.transform.position +=
                upper.TransformPoint(new Vector3(-.25f, .66f, .16f)) - hips.position;
            PlaceHand(
                HumanBodyBones.LeftUpperArm,
                HumanBodyBones.LeftLowerArm,
                HumanBodyBones.LeftHand,
                new Vector3(-.495f, .84f, .4f),
                new Vector3(-.48f, .75f, .15f)
            );
            PlaceHand(
                HumanBodyBones.RightUpperArm,
                HumanBodyBones.RightLowerArm,
                HumanBodyBones.RightHand,
                new Vector3(-.005f, .84f, .4f),
                new Vector3(.01f, .75f, .15f)
            );
        }

        void PlaceHand(
            HumanBodyBones shoulderBone,
            HumanBodyBones elbowBone,
            HumanBodyBones handBone,
            Vector3 localTarget,
            Vector3 localBend
        )
        {
            var shoulder = seatedOperator.GetBoneTransform(shoulderBone);
            var elbow = seatedOperator.GetBoneTransform(elbowBone);
            var hand = seatedOperator.GetBoneTransform(handBone);
            Vector3 target = upper.TransformPoint(localTarget);
            Vector3 delta = target - shoulder.position;
            float a = Vector3.Distance(shoulder.position, elbow.position);
            float b = Vector3.Distance(elbow.position, hand.position);
            float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(a - b) + .001f, a + b - .001f);
            Vector3 direction = delta.normalized;
            Vector3 bend = Vector3
                .ProjectOnPlane(upper.TransformPoint(localBend) - shoulder.position, direction)
                .normalized;
            float cosine = Mathf.Clamp(
                (a * a + distance * distance - b * b) / (2 * a * distance),
                -1,
                1
            );
            Vector3 elbowTarget =
                shoulder.position
                + a * (cosine * direction + Mathf.Sqrt(1 - cosine * cosine) * bend);
            shoulder.rotation =
                Quaternion.FromToRotation(
                    elbow.position - shoulder.position,
                    elbowTarget - shoulder.position
                ) * shoulder.rotation;
            elbow.rotation =
                Quaternion.FromToRotation(hand.position - elbow.position, target - elbow.position)
                * elbow.rotation;
        }

        static Vector4 Blend(Vector4 from, Vector4 to, float t) =>
            Vector4.Lerp(from, to, Mathf.SmoothStep(0, 1, t));

        static void Segment(Transform part, Vector3 from, Vector3 to, float radius)
        {
            Vector3 delta = to - from;
            part.position = (from + to) * .5f;
            part.rotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
            part.localScale = new Vector3(radius * 2, delta.magnitude * .5f, radius * 2);
        }
    }
}
