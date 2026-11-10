using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CoDriver.Tests
{
    public class AssetScaleTests
    {
        [TestCase("PersonMale")]
        [TestCase("PersonFemale")]
        [TestCase("WorkerMale")]
        [TestCase("WorkerFemale")]
        public void ClothedHumanoidsHaveUsableRetargetedRigs(string name)
        {
            var source = Resources.Load<GameObject>("Actors/" + name);
            Assert.IsNotNull(source);
            var instance = Object.Instantiate(source);
            try
            {
                var animator = instance.GetComponent<Animator>();
                Assert.IsTrue(animator.avatar.isValid && animator.avatar.isHuman);
                Assert.IsNotNull(animator.GetBoneTransform(HumanBodyBones.Head));
                Assert.IsTrue(
                    animator.runtimeAnimatorController.animationClips.Any(c =>
                        c.name.EndsWith("Driving_Loop")
                    )
                );
                var renderers = instance.GetComponentsInChildren<SkinnedMeshRenderer>();
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers)
                {
                    bounds.Encapsulate(renderer.bounds);
                    foreach (var material in renderer.sharedMaterials)
                        Assert.AreEqual("Universal Render Pipeline/Lit", material.shader.name);
                }
                Assert.That(
                    bounds.size.y,
                    Is.InRange(1.5f, 2f),
                    "Human scale must match doors and vehicles"
                );
                Assert.Greater(
                    renderers.SelectMany(r => r.sharedMaterials).Distinct().Count(),
                    2,
                    "Clothing, hair and skin must retain their separate authored materials"
                );
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [TestCase("Dog", 1.6f)]
        [TestCase("Cat", 1.2f)]
        [TestCase("Eagle", 3f)]
        public void AnimationDoesNotTurnAnimalsIntoBuildingSizedObjects(
            string name,
            float maximumSize
        )
        {
            var prefab = Resources.Load<GameObject>("Scenery/Prepared/" + name);
            Assert.IsNotNull(prefab, "Fetch and prepare the licensed external actor assets first");
            var instance = Object.Instantiate(prefab);
            try
            {
                var animator = instance.GetComponentInChildren<Animator>();
                Assert.IsNotNull(animator);
                Assert.IsNotNull(
                    animator.runtimeAnimatorController,
                    "Prepared prefab must include its animation controller"
                );
                foreach (var clip in animator.runtimeAnimatorController.animationClips.Distinct())
                foreach (float fraction in new[] { 0f, .25f, .5f, .75f })
                {
                    clip.SampleAnimation(animator.gameObject, clip.length * fraction);
                    foreach (
                        var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>()
                    )
                    {
                        var mesh = new Mesh();
                        // Compensate the renderer scale before applying its world scale.
                        renderer.BakeMesh(mesh, true);
                        var size = Vector3.Scale(mesh.bounds.size, renderer.transform.lossyScale);
                        Object.DestroyImmediate(mesh);
                        Assert.Less(
                            Mathf.Max(size.x, size.y, size.z),
                            maximumSize,
                            name + ": " + clip.name
                        );
                        Assert.Greater(size.magnitude, .1f, "Actor must remain visible");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
