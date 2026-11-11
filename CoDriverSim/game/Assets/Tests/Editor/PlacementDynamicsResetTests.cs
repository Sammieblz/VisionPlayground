using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace CoDriver.Tests
{
    public sealed class PlacementDynamicsResetTests
    {
        static FieldInfo Field(object owner, string name) =>
            owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

        static void Property(object owner, string name, object value) =>
            owner.GetType().GetProperty(name).SetValue(owner, value);

        [TestCase(false)]
        [TestCase(true)]
        public void PausedPlacementClearsTurnHighGearAndWheelStateWithoutAStep(bool gls)
        {
            float previousScale = Time.timeScale;
            var car = new GameObject("Placement reset car").AddComponent<Vehicle>();
            var sensor = new GameObject("Placement reset sensor").AddComponent<SensorRig>();
            try
            {
                Time.timeScale = 0;
                car.Profile = gls ? VehicleProfile.GlsSimulation() : new VehicleProfile();
                var body = car.gameObject.AddComponent<Rigidbody>();
                var dynamics = new RvpVehicleDynamics(body, car.Profile);
                Property(car, "Body", body);
                Property(car, "Dynamics", dynamics);
                sensor.Vehicle = car;

                // A previous turning/high-gear drive. Seeding retained state makes
                // the regression independent of a running physics simulation.
                Property(car, "SteerAngle", 25f);
                car.CommandedRoadAngle = -12;
                car.Throttle = .8f;
                car.Brake = .1f;
                car.Steer = .7f;
                car.Handbrake = car.Reverse = true;
                car.Indicator = 1;
                Property(dynamics, "LastSteering", 25f);
                Property(dynamics, "Gear", car.Profile.gears.Length);
                Property(dynamics, "EngineRpm", 6000f);
                Property(dynamics, "GroundedWheels", 4);
                Property(dynamics, "AbsActive", true);
                Property(dynamics, "TractionActive", true);
                Field(dynamics, "shiftRemaining").SetValue(dynamics, .2f);
                Field(dynamics, "shiftCooldown").SetValue(dynamics, .7f);
                for (int i = 0; i < 4; i++)
                {
                    dynamics.Rpm[i] = 1200;
                    ((float[])Field(dynamics, "angles").GetValue(dynamics))[i] = 80;
                    ((float[])Field(dynamics, "travel").GetValue(dynamics))[i] = .08f;
                    ((float[])Field(dynamics, "loads").GetValue(dynamics))[i] = 4000;
                    ((bool[])Field(dynamics, "grounded").GetValue(dynamics))[i] = true;
                    ((RaycastHit[])Field(dynamics, "contact").GetValue(dynamics))[i] =
                        new RaycastHit { distance = .08f, normal = Vector3.up };
                }
                body.position = new Vector3(120, 4, -65);
                body.rotation = Quaternion.Euler(0, 113, 0);
                body.linearVelocity = body.angularVelocity = Vector3.zero;
                car.transform.SetPositionAndRotation(body.position, body.rotation);
                sensor.ResetMotion();
                Assert.AreEqual(
                    25,
                    car.SteerAngle,
                    "IMU reset alone must not be mistaken for resetting steering"
                );

                var placedPosition = body.position;
                car.ResetForPlacement();

                // SensorRig derives ego.steering_deg from SteerAngle and the profile ratio.
                // This reset takes effect before the next capture, without a physics step.
                Assert.AreEqual(0, car.SteerAngle);
                Assert.IsTrue(float.IsNaN(car.CommandedRoadAngle));
                Assert.AreEqual(0, car.Throttle + car.Brake + car.Steer);
                Assert.IsFalse(car.Handbrake || car.Reverse);
                Assert.AreEqual(0, car.Indicator);
                Assert.AreEqual(1, car.Gear);
                Assert.AreEqual(900, car.EngineRpm);
                Assert.AreEqual(0, dynamics.LastSteering);
                Assert.AreEqual(0, dynamics.GroundedWheels);
                Assert.IsFalse(dynamics.AbsActive || dynamics.TractionActive);
                Assert.AreEqual(0f, Field(dynamics, "shiftRemaining").GetValue(dynamics));
                Assert.AreEqual(0f, Field(dynamics, "shiftCooldown").GetValue(dynamics));
                for (int i = 0; i < 4; i++)
                {
                    Assert.AreEqual(0, car.WheelRPM[i]);
                    Assert.AreEqual(0, ((float[])Field(dynamics, "loads").GetValue(dynamics))[i]);
                    Assert.IsFalse(((bool[])Field(dynamics, "grounded").GetValue(dynamics))[i]);
                    Assert.AreEqual(
                        0,
                        ((RaycastHit[])Field(dynamics, "contact").GetValue(dynamics))[i].distance
                    );
                    dynamics.WheelPose(i, out var position, out var rotation);
                    Assert.Less(
                        Vector3.Distance(
                            position,
                            car.transform.TransformPoint(
                                dynamics.Mount(i) - Vector3.up * car.Profile.suspensionLength
                            )
                        ),
                        .0001f
                    );
                    Assert.Less(Quaternion.Angle(rotation, body.rotation), .01f);
                }
                Assert.AreEqual(placedPosition, body.position);
                Assert.Less(Quaternion.Angle(Quaternion.Euler(0, 113, 0), body.rotation), .01f);
                Assert.AreEqual(Vector3.zero, body.linearVelocity);
                Assert.AreEqual(Vector3.zero, body.angularVelocity);
                Assert.AreEqual(0, Time.timeScale);
            }
            finally
            {
                Time.timeScale = previousScale;
                Object.DestroyImmediate(sensor.gameObject);
                Object.DestroyImmediate(car.gameObject);
            }
        }
    }
}
