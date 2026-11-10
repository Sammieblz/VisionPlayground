using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoDriver.Tests
{
    public sealed class CivilianAnimationTests
    {
        [UnityTest]
        public IEnumerator RetargetedWalkMovesFeetWithoutMovingThePhysicalRoot()
        {
            var prefab = Resources.Load<GameObject>("WorldArt/Civilians/Civilian");
            Assert.IsNotNull(prefab, "Prepare the licensed civilian before qualification");
            var actor = Object.Instantiate(prefab);
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
                var first = foot.position;
                yield return new WaitForSeconds(.25f);
                Assert.Greater(Vector3.Distance(first, foot.position), .03f, "Retargeted legs remained frozen");
                Assert.Less(actor.transform.position.magnitude, .001f, "Animation moved the physical root");
                Assert.Greater(Vector3.Dot((toe.position-foot.position).normalized, actor.transform.forward), .2f,
                    "Prepared civilian faces sideways/backwards");
                Assert.Less(Mathf.Abs(foot.position.y), .35f, "Walking feet are displaced from the ground");
            }
            finally { Object.Destroy(actor); }
            yield return null;
        }
    }
}
