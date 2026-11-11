using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoDriver.Tests
{
    public sealed class AnimalAnimationTests
    {
        [UnityTest]
        public IEnumerator PreparedCatStandsOnGroundAndWalksForwardWithoutMovingItsRoot()
        {
            var prefab = Resources.Load<GameObject>("WorldArt/Animals/Cat");
            Assert.IsNotNull(prefab);
            var actor = Object.Instantiate(prefab);
            try
            {
                var animator = actor.GetComponentInChildren<Animator>();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var clips = animator.runtimeAnimatorController.animationClips;
                animator.Play(clips.First(c => c.name.EndsWith("Idle_Breathe")).name);
                yield return new WaitForSeconds(.1f);
                float lowest = float.PositiveInfinity;
                foreach (var renderer in actor.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var mesh = new Mesh();
                    renderer.BakeMesh(mesh, true);
                    foreach (var vertex in mesh.vertices)
                        lowest = Mathf.Min(lowest, renderer.transform.TransformPoint(vertex).y);
                    Object.Destroy(mesh);
                }
                Assert.That(lowest, Is.InRange(-.03f, .03f), "Idle paws must rest on the ground");
                var bones = actor.GetComponentsInChildren<Transform>();
                var head = bones.Single(t => t.name == "Head_controller");
                var hip = bones.Single(t => t.name == "Hip");
                Assert.Greater(
                    Vector3.Dot(
                        Vector3.ProjectOnPlane(head.position - hip.position, Vector3.up).normalized,
                        actor.transform.forward
                    ),
                    .95f,
                    "Prepared cat faces sideways/backwards"
                );
                var foot = bones.Single(t => t.name == "Foot_front_L");
                animator.Play(clips.First(c => c.name.EndsWith("Walk")).name, 0, 0);
                yield return new WaitForSeconds(.1f);
                var first = foot.position;
                yield return new WaitForSeconds(.3f);
                Assert.Greater(
                    Vector3.Distance(first, foot.position),
                    .03f,
                    "Imported walk is frozen"
                );
                Assert.Less(
                    actor.transform.position.magnitude,
                    .001f,
                    "Animation moved physical root"
                );
            }
            finally
            {
                Object.Destroy(actor);
            }
            yield return null;
        }
    }
}
