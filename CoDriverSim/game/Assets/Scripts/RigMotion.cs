using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoDriver
{
    public sealed class RigMotion : MonoBehaviour
    {
        Animator animator;
        readonly Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();
        string current = "";
        Transform head;
        bool looking;
        float lookAngle;
        int lookVariation;
        ActorLocomotion locomotion;

        public void Initialize()
        {
            animator = GetComponent<Animator>();
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
                clips[clip.name.Replace("Armature|", "")] = clip;
        }

        public void Animate(float speed, string behavior, int variation)
        {
            if (!animator)
                return;
            if (!locomotion)
                locomotion = GetComponentInParent<ActorLocomotion>();
            if (locomotion && locomotion.Reacting)
            {
                looking = false;
                float elapsed = locomotion.ReactionElapsed;
                bool fallen = locomotion.ImpactSpeed > 3;
                string reaction =
                    fallen ? (elapsed < 2.2f ? "Death01" : "Ground_Recovery")
                    : elapsed < 1.2f ? "Hit_Chest"
                    : "Idle_Loop";
                if (clips.TryGetValue(reaction, out var clip))
                {
                    if (current != clip.name)
                    {
                        animator.CrossFadeInFixedTime(clip.name, .08f);
                        current = clip.name;
                    }
                    animator.speed =
                        fallen
                            ? (
                                elapsed < 1.4f ? clip.length / 1.4f
                                : elapsed < 2.2f ? 0
                                : clip.length / 2.6f
                            )
                        : elapsed < 1.2f ? clip.length / 1.2f
                        : 1;
                    return;
                }
            }
            looking =
                speed < .2f
                && (behavior == "Looking" || behavior == "Waiting" || behavior == "Looking around");
            lookVariation = variation;
            string requested =
                behavior == "Entering car" ? "Sitting_Enter"
                : behavior == "Leaving car" ? "Sitting_Exit"
                : behavior == "Driving" ? "Driving_Loop"
                : behavior == "Opening car door" ? "Interact"
                : speed > .2f ? (variation % 3 == 1 ? "Walk_Formal_Loop" : "Walk_Loop")
                : behavior == "Conversing" ? "Idle_Talking_Loop"
                : behavior == "Working" ? "Fixing_Kneeling"
                : "Idle_Loop";
            if (
                !clips.TryGetValue(requested, out var chosen)
                && !clips.TryGetValue("Idle_Loop", out chosen)
            )
                return;
            // Activity changes must follow the actual door/seat handshake immediately.
            // Behavior-state timers own variation cooldowns; animation must not delay boarding.
            if (current != chosen.name)
            {
                animator.CrossFadeInFixedTime(chosen.name, .3f);
                current = chosen.name;
            }
            animator.speed = speed > .2f ? Mathf.Clamp(speed / 1.25f, .3f, 2) : 1;
        }

        void LateUpdate()
        {
            if (!head)
                return;
            float target = looking ? Mathf.Sin(Time.time * 1.8f + lookVariation) * 42 : 0;
            lookAngle = Mathf.Lerp(lookAngle, target, Time.deltaTime * 7);
            head.localRotation *= Quaternion.Euler(0, lookAngle, 0);
        }
    }
}
