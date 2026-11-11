using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoDriver.Tests
{
    public class MakeHumanAnimationTests
    {
        [UnityTest]
        public IEnumerator BothClothedVariantsWalkForwardAndRecoverWithoutMovingTheirRoot()
        {
            foreach (string name in new[] { "Citizen_A", "Citizen_B" })
            {
                var actor = Object.Instantiate(
                    Resources.Load<GameObject>("WorldArt/Civilians/" + name)
                );
                try
                {
                    var animator = actor.GetComponent<Animator>();
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    var motion = actor.AddComponent<RigMotion>();
                    motion.Initialize();
                    motion.Animate(1.25f, "Walking", 0);
                    yield return new WaitForSeconds(.5f);
                    var foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                    var toe = animator.GetBoneTransform(HumanBodyBones.LeftToes);
                    Vector3 first = foot.position;
                    yield return new WaitForSeconds(.25f);
                    Assert.Greater(
                        Vector3.Distance(first, foot.position),
                        .03f,
                        name + " feet frozen"
                    );
                    Assert.Greater(
                        Vector3.Dot(
                            (toe.position - foot.position).normalized,
                            actor.transform.forward
                        ),
                        .2f,
                        name + " forward axis"
                    );
                    Assert.Less(Mathf.Abs(foot.position.y), .35f, name + " ground alignment");
                    foreach (
                        string action in new[]
                        {
                            "Armature|Death01",
                            "Ground_Recovery",
                            "Armature|Idle_Loop",
                        }
                    )
                    {
                        // The controller retains upstream clip names; request the actual matching state.
                        var clip = System.Array.Find(
                            animator.runtimeAnimatorController.animationClips,
                            c => c.name.Replace("Armature|", "") == action.Replace("Armature|", "")
                        );
                        Assert.IsNotNull(clip, action);
                        animator.Play(clip.name, 0, .5f);
                        yield return null;
                        Assert.Less(
                            actor.transform.position.magnitude,
                            .001f,
                            name + " animation moved physical root"
                        );
                    }
                }
                finally
                {
                    Object.Destroy(actor);
                }
                yield return null;
            }
        }
    }
}
