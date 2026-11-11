using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoDriver.Tests
{
    public sealed class SubmitReleaseLatchTests : InputTestFixture
    {
        [Test]
        public void HeldLaunchSubmitWaitsForReleaseAndANewPressCancels()
        {
            var pad = InputSystem.AddDevice<Gamepad>();
            using var input = new DriverInput(false);
            var latch = new SubmitReleaseLatch();
            Press(pad.buttonSouth);
            latch.Capture(input.Handbrake);
            Assert.IsTrue(latch.WaitingForRelease);
            Assert.IsFalse(latch.ShouldCancel(input));
            InputSystem.Update();
            Assert.IsTrue(latch.WaitingForRelease);
            Assert.IsFalse(latch.ShouldCancel(input));
            Release(pad.buttonSouth);
            Assert.IsFalse(latch.WaitingForRelease);
            Assert.IsFalse(latch.ShouldCancel(input));
            Press(pad.buttonSouth);
            Assert.IsTrue(latch.ShouldCancel(input));
        }

        [Test]
        public void APressAfterLaunchIsNotConsumed()
        {
            var pad = InputSystem.AddDevice<Gamepad>();
            using var input = new DriverInput(false);
            var latch = new SubmitReleaseLatch();
            latch.Capture(input.Handbrake);
            Press(pad.buttonSouth);
            Assert.IsFalse(latch.WaitingForRelease);
            Assert.IsTrue(latch.ShouldCancel(input));
        }

        [Test]
        public void HeldNonSubmitHandbrakeIsNotConsumed()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            using var input = new DriverInput(false);
            var latch = new SubmitReleaseLatch();
            Press(keyboard.spaceKey);
            latch.Capture(input.Handbrake);
            Assert.IsFalse(latch.WaitingForRelease);
            Assert.IsTrue(latch.ShouldCancel(input));
        }

        [Test]
        public void AnotherHandbrakeBindingCancelsWhileLaunchSubmitIsHeld()
        {
            var pad = InputSystem.AddDevice<Gamepad>();
            var keyboard = InputSystem.AddDevice<Keyboard>();
            using var input = new DriverInput(false);
            var latch = new SubmitReleaseLatch();
            Press(pad.buttonSouth);
            latch.Capture(input.Handbrake);
            Press(keyboard.spaceKey);
            Assert.IsTrue(latch.WaitingForRelease);
            Assert.IsTrue(latch.ShouldCancel(input));
        }

        [TestCase("brake")]
        [TestCase("steer")]
        [TestCase("throttle")]
        [TestCase("reverse")]
        [TestCase("comma")]
        public void OtherDrivingInputStillCancelsWhileSubmitIsHeld(string control)
        {
            var pad = InputSystem.AddDevice<Gamepad>();
            using var input = new DriverInput(false);
            var latch = new SubmitReleaseLatch();
            Press(pad.buttonSouth);
            latch.Capture(input.Handbrake);
            if (control == "brake")
                Set(pad.leftTrigger, .5f);
            if (control == "steer")
                Set(pad.leftStick, new Vector2(.8f, 0));
            if (control == "throttle")
                Set(pad.rightTrigger, .6f);
            if (control == "reverse")
                Press(pad.buttonEast);
            if (control == "comma")
                Press(pad.buttonWest);
            Assert.IsTrue(latch.WaitingForRelease);
            Assert.IsTrue(latch.ShouldCancel(input));
        }

        [Test]
        public void HeldSubmitCannotBypassThePendingStartTimeout()
        {
            var pad = InputSystem.AddDevice<Gamepad>();
            using var input = new DriverInput(false);
            var root = new GameObject("Inactive pending start fixture");
            root.SetActive(false);
            var car = new GameObject("Held start car");
            float oldScale = Time.timeScale;
            try
            {
                var app = root.AddComponent<CoDriverApp>();
                var vehicle = car.AddComponent<Vehicle>();
                typeof(Vehicle)
                    .GetProperty("Body")
                    .SetValue(vehicle, car.AddComponent<Rigidbody>());
                app.Vehicle = vehicle;
                app.Input = input;
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(CoDriverApp)
                    .GetField("evaluator", flags)
                    .SetValue(app, new ScenarioEvaluator("", "fixture"));
                typeof(CoDriverApp).GetField("awaitingStart", flags).SetValue(app, true);
                typeof(CoDriverApp)
                    .GetField("startPlacementAt", flags)
                    .SetValue(app, Time.realtimeSinceStartupAsDouble - 11);
                var latch = (SubmitReleaseLatch)
                    typeof(CoDriverApp).GetField("startSubmitLatch", flags).GetValue(app);
                Press(pad.buttonSouth);
                latch.Capture(input.Handbrake);
                typeof(CoDriverApp)
                    .GetMethod("UpdatePendingStart", flags, null, System.Type.EmptyTypes, null)
                    .Invoke(app, null);
                Assert.AreEqual("Start drive", app.Page);
                StringAssert.Contains("Self-driving is unavailable", app.StartMessage);
                Assert.IsFalse(app.StartPending);
                Assert.IsFalse(app.Arbiter.Requested);
            }
            finally
            {
                // This inactive fixture never owns service processes or a running scenario.
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(car);
                Time.timeScale = oldScale;
            }
        }
    }
}
