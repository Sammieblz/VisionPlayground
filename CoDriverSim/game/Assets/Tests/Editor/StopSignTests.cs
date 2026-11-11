using NUnit.Framework;
using UnityEngine;

namespace CoDriver.Tests
{
    public sealed class StopSignTests
    {
        [Test]
        public void NearbySignRecordsClearIndependentlyAndRequireAnotherStopOnReturn()
        {
            var memory = new StopSignProgress();
            for (int i = 0; i < 9; i++)
            {
                Assert.IsTrue(memory.RequiresStop("a", Vector3.zero, 0, 2, .1f));
                Assert.IsTrue(memory.RequiresStop("b", Vector3.right, 0, 2, .1f));
            }
            for (int i = 0; i < 15; i++)
            {
                Assert.IsFalse(memory.RequiresStop("a", Vector3.zero, 0, 2, .1f));
                Assert.IsFalse(memory.RequiresStop("b", Vector3.right, 0, 2, .1f));
            }
            memory.UpdatePosition(Vector3.forward * 60);
            Assert.IsTrue(memory.RequiresStop("a", Vector3.zero, 0, 2, .1f));
        }

        [Test]
        public void CreepingAndWaitingFarFromTheLineDoNotCountAsACompletedStop()
        {
            var memory = new StopSignProgress();
            for (int i = 0; i < 30; i++)
            {
                Assert.IsTrue(memory.RequiresStop("rolling", Vector3.zero, 1, 2, .1f));
                Assert.IsTrue(memory.RequiresStop("queue", Vector3.zero, 0, 10, .1f));
            }
            Assert.IsTrue(memory.RequiresStop("rolling", Vector3.zero, 0, 2, .1f));
            Assert.IsTrue(memory.RequiresStop("queue", Vector3.zero, 0, 2, .1f));
        }
    }
}
