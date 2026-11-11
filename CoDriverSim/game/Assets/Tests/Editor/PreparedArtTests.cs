using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CoDriver.Tests
{
    public sealed class PreparedArtTests
    {
        [Test]
        public void CoupeDetailLevelsSharePhysicalWheelAnchorsAndFiniteEnvelope()
        {
            var prefab = Resources.Load<GameObject>("WorldArt/Fleet/Concept");
            Assert.IsNotNull(prefab);
            var car = Object.Instantiate(prefab);
            try
            {
                var metadata = car.GetComponent<AssetDefinition>();
                Assert.IsTrue(metadata.publicRedistribution);
                Assert.AreEqual(8, metadata.wheels.Length);
                foreach (var wheel in metadata.wheels)
                {
                    var p = car.transform.InverseTransformPoint(wheel.position);
                    Assert.That(Mathf.Abs(p.x), Is.EqualTo(.78f).Within(.002));
                    Assert.That(p.y, Is.EqualTo(.34f).Within(.002));
                    Assert.That(Mathf.Abs(p.z), Is.EqualTo(1.35f).Within(.002));
                    Assert.AreEqual(wheel.name.Contains("Front"), p.z > 0);
                }
                var levels = car.GetComponent<LODGroup>().GetLODs();
                Assert.AreEqual(2, levels.Length);
                int Triangles(LOD lod) =>
                    lod.renderers.Sum(r =>
                        r.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3
                    );
                Assert.Less(Triangles(levels[1]), Triangles(levels[0]) * .4f);
                var body = levels[0].renderers.First(r => r.name.StartsWith("Body"));
                Assert.Less(body.bounds.size.z, 4.5f);
                Assert.Less(body.bounds.size.x, 2.1f);
            }
            finally
            {
                Object.DestroyImmediate(car);
            }
        }

        [Test]
        public void PedestrianRecoveryReachesStandingPose()
        {
            var source = Resources.Load<GameObject>("WorldArt/Civilians/Civilian");
            var actor = Object.Instantiate(source);
            try
            {
                var animator = actor.GetComponent<Animator>();
                var clips = animator.runtimeAnimatorController.animationClips;
                var recovery = clips.Single(c => c.name == "Ground_Recovery");
                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                recovery.SampleAnimation(actor, 0);
                float fallen = head.position.y;
                recovery.SampleAnimation(actor, recovery.length);
                Assert.Greater(
                    head.position.y,
                    fallen + .7f,
                    "Recovery clip must raise the fallen skeleton"
                );
                Assert.Greater(head.position.y, 1.2f);
            }
            finally
            {
                Object.DestroyImmediate(actor);
            }
        }
    }
}
