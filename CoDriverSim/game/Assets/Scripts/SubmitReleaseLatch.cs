using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace CoDriver
{
    // Ignore only the Submit/handbrake control already held when the menu starts
    // a drive. Release is required before engagement; every new driving input
    // keeps its normal cancellation behavior.
    public sealed class SubmitReleaseLatch
    {
        ButtonControl launchSubmit;

        public bool WaitingForRelease
        {
            get
            {
                if (
                    launchSubmit != null
                    && (
                        !launchSubmit.device.added
                        || !launchSubmit.device.enabled
                        || !launchSubmit.isPressed
                    )
                )
                    launchSubmit = null;
                return launchSubmit != null;
            }
        }

        public void Clear() => launchSubmit = null;

        public void Capture(InputAction handbrake)
        {
            Clear();
            if (
                handbrake?.activeControl is not ButtonControl button
                || !handbrake.IsPressed()
                || !button.isPressed
            )
                return;
            foreach (var usage in button.usages)
                if (usage == CommonUsages.Submit)
                {
                    launchSubmit = button;
                    return;
                }
        }

        bool HandbrakeCancels(InputAction handbrake)
        {
            if (!WaitingForRelease)
                return handbrake.IsPressed();
            // A second binding (for example Space) must still cancel while A is held.
            foreach (var control in handbrake.controls)
                if (control != launchSubmit && control is ButtonControl button && button.isPressed)
                    return true;
            return false;
        }

        public bool ShouldCancel(DriverInput input) =>
            Mathf.Abs(input.Steer) > .25f
            || input.Gas.ReadValue<float>() > .15f
            || input.Brake.ReadValue<float>() > .08f
            || HandbrakeCancels(input.Handbrake)
            || input.Reverse.WasPressedThisFrame()
            || input.Comma.WasPressedThisFrame();
    }
}
