using NUnit.Framework;
using UnityEngine;

namespace CoDriver.Tests
{
    public sealed class SensorAxesTests
    {
        static void Near(Vector3 actual, Vector3 expected) =>
            Assert.Less(Vector3.Distance(actual, expected), .0001f);

        [Test]
        public void StationaryImuMeasuresGravityAndForwardAccelerationRetainsSign()
        {
            var gravity = new Vector3(0, -9.81f, 0);
            Near(
                SensorAxes.SpecificForce(Quaternion.identity, Vector3.zero, gravity),
                new Vector3(0, 0, -9.81f)
            );
            Near(
                SensorAxes.SpecificForce(Quaternion.identity, new Vector3(1, 0, 2), gravity),
                new Vector3(2, 1, -9.81f)
            );
            var heading = Quaternion.Euler(0, 90, 0);
            Near(
                SensorAxes.SpecificForce(heading, new Vector3(2, 0, 0), gravity),
                new Vector3(2, 0, -9.81f)
            );
        }

        [Test]
        public void RightTurnIsDeviceDownAxisRotationAndRollPitchRetainHandedness()
        {
            Near(
                SensorAxes.AngularVelocity(Quaternion.identity, Vector3.up * .2f),
                new Vector3(0, 0, .2f)
            );
            Near(
                SensorAxes.AngularVelocity(Quaternion.identity, new Vector3(1, 2, 3)),
                new Vector3(-3, -1, 2)
            );
            var heading = Quaternion.Euler(0, 90, 0);
            Near(
                SensorAxes.AngularVelocity(heading, heading * new Vector3(1, 2, 3)),
                new Vector3(-3, -1, 2)
            );
        }
    }
}
