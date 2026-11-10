using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CoDriver.Tests
{
    public class InputTests : InputTestFixture
    {
        [Test]
        public void DisabledGamepadIsUnavailableAndReenablingDoesNotPressHeldGearButton()
        {
            var pad = InputSystem.AddDevice<Gamepad>();
            using (var input = new DriverInput(false))
            {
                Assert.IsTrue(DriverInput.HasAvailableGamepad);
                Press(pad.buttonEast);
                Assert.AreEqual(1, input.ReversePressCount);
                Assert.IsTrue(input.Reverse.WasPressedThisFrame());
                InputSystem.DisableDevice(pad);
                Assert.IsFalse(DriverInput.HasAvailableGamepad);
                InputSystem.EnableDevice(pad);
                InputSystem.Update();
                Assert.IsTrue(DriverInput.HasAvailableGamepad);
                Assert.AreEqual(
                    1,
                    input.ReversePressCount,
                    "Reconnect must not replay the prior gear request"
                );
                InputSystem.RemoveDevice(pad);
            }
        }

        [TestCase(InputDeviceChange.Disabled)]
        [TestCase(InputDeviceChange.Disconnected)]
        [TestCase(InputDeviceChange.Removed)]
        public void ControllerLossPausesAndCancelsAuthorityUntilExplicitResume(
            InputDeviceChange change
        )
        {
            var root = new GameObject("Inactive application fixture");
            root.SetActive(false); // Exercise the handler without starting world/services.
            var app = root.AddComponent<CoDriverApp>();
            var car = new GameObject("Input safety vehicle").AddComponent<Vehicle>();
            var pad = InputSystem.AddDevice<Gamepad>();
            float oldScale = Time.timeScale;
            try
            {
                app.Vehicle = car;
                app.Paused = false;
                app.Page = "Drive";
                car.Throttle = 1;
                car.Steer = .5f;
                car.CommandedRoadAngle = 12;
                app.Arbiter.Request();
                var handler = typeof(CoDriverApp).GetMethod(
                    "DeviceChanged",
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic
                );
                handler.Invoke(app, new object[] { pad, change });
                Assert.IsTrue(app.Paused);
                Assert.AreEqual(0, Time.timeScale);
                Assert.AreEqual("Pause", app.Page);
                Assert.IsFalse(app.Arbiter.Requested);
                Assert.IsFalse(app.AutomationActive);
                Assert.AreEqual(0, car.Throttle);
                Assert.AreEqual(0, car.Steer);
                Assert.AreEqual(1, car.Brake);
                Assert.IsTrue(float.IsNaN(car.CommandedRoadAngle));
                handler.Invoke(app, new object[] { pad, InputDeviceChange.Enabled });
                Assert.IsTrue(app.Paused, "Reconnect must never resume vehicle motion by itself");
                app.ResumeDrive();
                Assert.IsFalse(app.Paused);
                Assert.IsFalse(app.Arbiter.Requested);
            }
            finally
            {
                Time.timeScale = oldScale;
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(car.gameObject);
                InputSystem.RemoveDevice(pad);
            }
        }

        [Test]
        public void RemappingSurvivesRecreatingRuntimeInputActions()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            string saved;
            using (var first = new DriverInput(false))
            {
                first.Gas.ApplyBindingOverride(1, "<Keyboard>/upArrow");
                first.Steering.ApplyBindingOverride(3, "<Keyboard>/rightArrow");
                saved = first.ExportBindings();
            }
            using (var second = new DriverInput(false))
            {
                second.RestoreBindings(saved);
                Press(keyboard.upArrowKey);
                Press(keyboard.rightArrowKey);
                Assert.AreEqual(1, second.Gas.ReadValue<float>());
                Assert.AreEqual(1, second.Steer);
                Release(keyboard.upArrowKey);
                Press(keyboard.wKey);
                Assert.AreEqual(0, second.Gas.ReadValue<float>());
            }
        }

        [Test]
        public void XboxTriggersSteeringAndDeadZone()
        {
            var pad = InputSystem.AddDevice<Gamepad>();
            using (var input = new DriverInput(false))
            {
                try
                {
                    InputSystem.QueueStateEvent(
                        pad,
                        new GamepadState
                        {
                            leftStick = new Vector2(.7f, 0),
                            rightTrigger = .8f,
                            leftTrigger = .4f,
                        }
                    );
                    InputSystem.Update();
                    Assert.Greater(input.Steer, .5f);
                    Assert.AreEqual(.8f, input.Gas.ReadValue<float>(), .01f);
                    Assert.AreEqual(.4f, input.Brake.ReadValue<float>(), .01f);
                    InputSystem.QueueStateEvent(
                        pad,
                        new GamepadState { leftStick = new Vector2(.03f, 0) }
                    );
                    InputSystem.Update();
                    Assert.AreEqual(0, input.Steer);
                }
                finally
                {
                    InputSystem.RemoveDevice(pad);
                }
            }
        }

        [TestCase(GamepadButton.South, "Handbrake")]
        [TestCase(GamepadButton.East, "Forward / reverse")]
        [TestCase(GamepadButton.North, "Camera")]
        [TestCase(GamepadButton.West, "Comma toggle")]
        [TestCase(GamepadButton.Start, "Pause")]
        [TestCase(GamepadButton.LeftShoulder, "Left indicator")]
        [TestCase(GamepadButton.RightShoulder, "Right indicator")]
        [TestCase(GamepadButton.LeftStick, "Horn")]
        [TestCase(GamepadButton.DpadUp, "Cruise up")]
        [TestCase(GamepadButton.DpadDown, "Cruise down")]
        public void XboxButtonsHaveExpectedActions(GamepadButton button, string action)
        {
            var pad = InputSystem.AddDevice<Gamepad>();
            using (var input = new DriverInput(false))
            {
                try
                {
                    InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(button));
                    InputSystem.Update();
                    Assert.IsTrue(input.Map.FindAction(action, true).IsPressed());
                }
                finally
                {
                    InputSystem.RemoveDevice(pad);
                }
            }
        }
    }
}
