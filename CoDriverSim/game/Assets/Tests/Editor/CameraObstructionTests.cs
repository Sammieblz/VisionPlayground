using NUnit.Framework;
using UnityEngine;

namespace CoDriver.Tests
{
    public sealed class CameraObstructionTests
    {
        [Test]
        public void CameraStaysInFrontOfWallWithoutMovingAnyPhysicalBody()
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var actor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var focus = new Vector3(3000, 1, 3000);
                wall.transform.position = focus + Vector3.back * 5;
                wall.transform.localScale = new Vector3(10, 10, 1);
                actor.transform.position = focus + Vector3.back * 2;
                var body = actor.AddComponent<Rigidbody>();
                body.isKinematic = true;
                Physics.SyncTransforms();
                var desired = focus + Vector3.back * 8;
                var corrected = CameraObstruction.Resolve(focus, desired);
                Assert.That(Vector3.Distance(corrected, focus), Is.InRange(4.0f, 4.3f));
                Assert.AreEqual(focus + Vector3.back * 2, body.position);
                wall.GetComponent<Collider>().enabled = false;
                Physics.SyncTransforms();
                Assert.AreEqual(desired, CameraObstruction.Resolve(focus, desired));
                Assert.AreEqual(focus, CameraObstruction.Resolve(focus, focus));
            }
            finally
            {
                Object.DestroyImmediate(wall);
                Object.DestroyImmediate(actor);
            }
        }
    }
}
