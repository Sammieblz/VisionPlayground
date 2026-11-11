using System.Linq;
using UnityEngine;

namespace CoDriver
{
    public sealed class AnimalMotion : MonoBehaviour
    {
        Animator animator;
        AnimationClip[] clips;
        string current = "";
        float until;
        float walkingSpeed = 1.3f,
            runningSpeed = 3;

        public void Initialize()
        {
            var definition = GetComponent<AssetDefinition>();
            if (definition && definition.walkingMetresPerSecond > 0)
            {
                walkingSpeed = definition.walkingMetresPerSecond;
                runningSpeed = definition.runningMetresPerSecond;
            }
            animator = GetComponentInChildren<Animator>();
            clips =
                animator && animator.runtimeAnimatorController
                    ? animator.runtimeAnimatorController.animationClips
                    : new AnimationClip[0];
        }

        public void Animate(float speed, string state, int variation)
        {
            if (!animator || clips.Length == 0)
                return;
            string key =
                state == "Flying" || state == "Taking off" || state == "Landing" ? "fly"
                : speed > walkingSpeed * 1.8f ? "run"
                : speed > .15f ? "walk"
                : "idle";
            var candidates = clips.Where(c => c.name.ToLowerInvariant().Contains(key)).ToArray();
            if (candidates.Length == 0 && key == "run")
                candidates = clips.Where(c => c.name.ToLowerInvariant().Contains("walk")).ToArray();
            if (candidates.Length == 0)
                candidates = clips.Where(c => c.name.ToLowerInvariant().Contains("idle")).ToArray();
            if (candidates.Length == 0)
                return;
            var chosen = candidates[Mathf.Abs(variation) % candidates.Length];
            if (current != chosen.name && (speed > .15f || Time.time >= until))
            {
                animator.CrossFadeInFixedTime(chosen.name, .25f);
                current = chosen.name;
                until = Time.time + Mathf.Max(2, chosen.length);
            }
            animator.speed =
                key == "walk" ? Mathf.Clamp(speed / walkingSpeed, .1f, 2)
                : key == "run" ? Mathf.Clamp(speed / runningSpeed, .3f, 2.2f)
                : 1;
        }
    }
}
