using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace CoDriver.Tests
{
    public sealed class CameraExposureTests
    {
        static FieldInfo Field(object owner, string name) =>
            owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

        static ScenarioEvaluator Evaluator(string scenario, Vehicle car, string criterion)
        {
            var result = new ScenarioEvaluator("", "test");
            Field(result, "name").SetValue(result, scenario);
            Field(result, "vehicle").SetValue(result, car);
            Field(result, "running").SetValue(result, true);
            Field(result, "cameraWindowStart").SetValue(result, double.PositiveInfinity);
            ((List<DrivingCriterion>)Field(result, "criteria").GetValue(result)).Add(
                new DrivingCriterion { name = criterion }
            );
            return result;
        }

        [Test]
        public void AnimalGradeAcceptsDetectionCapturedBeforeCrossingEnded()
        {
            var car = new GameObject("Exposure car").AddComponent<Vehicle>();
            var animal = new GameObject("Crossing animal").AddComponent<Walker>();
            try
            {
                animal.Species = "Cat";
                animal.Crossing = true;
                animal.transform.position = new Vector3(0, 0, 10);
                var evaluator = Evaluator(
                    "Animal recognition",
                    car,
                    "Co-driver notices the animal"
                );
                Field(evaluator, "crossing").SetValue(evaluator, animal);
                evaluator.RecordCameraExposure(10, 1);
                animal.Crossing = false;
                animal.transform.position = new Vector3(0, 0, -10);
                evaluator.RecordCameraExposure(11, 1.05);
                evaluator.ObserveCamera(
                    new ObserverResult
                    {
                        session_id = "test",
                        frame_id = 10,
                        timestamp = 1,
                        tracks = new[]
                        {
                            new Detection
                            {
                                label = "cat",
                                score = .8f,
                                source = "camera",
                                provenance = "test detector",
                            },
                        },
                    },
                    true
                );
                Assert.IsTrue((bool)Field(evaluator, "cameraObserved").GetValue(evaluator));
            }
            finally
            {
                Object.DestroyImmediate(animal.gameObject);
                Object.DestroyImmediate(car.gameObject);
            }
        }

        [Test]
        public void WorkzoneGradeRejectsLaterUnrelatedCueAfterLeavingExposure()
        {
            var car = new GameObject("Exposure car").AddComponent<Vehicle>();
            try
            {
                var evaluator = Evaluator(
                    "Construction recognition",
                    car,
                    "Co-driver notices roadwork cues"
                );
                car.transform.position = new Vector3(0, 0, -100);
                evaluator.RecordCameraExposure(10, 1);
                car.transform.position = new Vector3(0, 0, -50);
                evaluator.RecordCameraExposure(11, 1.05);
                evaluator.ObserveCamera(
                    new ObserverResult
                    {
                        session_id = "test",
                        frame_id = 11,
                        timestamp = 1.05,
                        risks = new[]
                        {
                            new Risk
                            {
                                kind = "workzone",
                                frame_id = 11,
                                source = "camera",
                                provenance = "test cue",
                            },
                        },
                    },
                    true
                );
                Assert.IsFalse((bool)Field(evaluator, "cameraObserved").GetValue(evaluator));
            }
            finally
            {
                Object.DestroyImmediate(car.gameObject);
            }
        }

        [Test]
        public void MotionResetClearsPrePlacementAccelerationAndYawWithoutPhysicsStepping()
        {
            var car = new GameObject("Reset car").AddComponent<Vehicle>();
            var sensors = new GameObject("Reset sensor").AddComponent<SensorRig>();
            try
            {
                var body = car.gameObject.AddComponent<Rigidbody>();
                typeof(Vehicle).GetProperty("Body").SetValue(car, body);
                sensors.Vehicle = car;
                Field(sensors, "previousPhysicsVelocity").SetValue(sensors, Vector3.forward * 12);
                Field(sensors, "specificForce").SetValue(sensors, new Vector3(-4, 2, -9));
                Field(sensors, "angularVelocity").SetValue(sensors, new Vector3(0, 0, 1));
                Field(sensors, "previousSpeed").SetValue(sensors, 12f);
                Field(sensors, "previousCaptureTime").SetValue(sensors, 1d);
                sensors.ResetMotion();
                Assert.AreEqual(Vector3.zero, Field(sensors, "angularVelocity").GetValue(sensors));
                Assert.AreEqual(0d, Field(sensors, "previousCaptureTime").GetValue(sensors));
                var expected = new Vector3(0, 0, -Physics.gravity.magnitude);
                Assert.Less(
                    (
                        (Vector3)Field(sensors, "specificForce").GetValue(sensors) - expected
                    ).magnitude,
                    .0001f
                );
                typeof(SensorRig)
                    .GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(sensors, null);
                Assert.Less(
                    (
                        (Vector3)Field(sensors, "specificForce").GetValue(sensors) - expected
                    ).magnitude,
                    .0001f
                );
            }
            finally
            {
                Object.DestroyImmediate(sensors.gameObject);
                Object.DestroyImmediate(car.gameObject);
            }
        }

        [Test]
        public void DelayedAnimalDetectionKeepsItsCaptureTimeEligibility()
        {
            var history = new CameraExposureHistory();
            history.Record(100, 10, true);
            history.Record(101, 10.05, false); // Actor finished crossing before inference returned.
            Assert.IsTrue(history.Contains(100, 10));
            Assert.IsFalse(history.Contains(101, 10.05));
        }

        [Test]
        public void WorkzoneCueAfterExitCannotReuseEarlierExposure()
        {
            var history = new CameraExposureHistory();
            history.Record(200, 20, true);
            history.Record(201, 20.05, false);
            history.Record(202, 20.10, false); // Unrelated orange cue beyond the closure.
            Assert.IsTrue(history.Contains(200, 20));
            Assert.IsFalse(history.Contains(202, 20.10));
            Assert.IsFalse(history.Contains(200, 20.10));
        }

        [Test]
        public void ExposureCannotCrossScenarioResetOrOutliveBoundedHistory()
        {
            var history = new CameraExposureHistory();
            history.Record(0, 0, true);
            for (int frame = 1; frame <= 128; frame++)
                history.Record(frame, frame * .05, false);
            Assert.IsFalse(history.Contains(0, 0));
            history.Record(129, 6.45, true);
            history.Clear();
            Assert.IsFalse(history.Contains(129, 6.45));
        }

        [Test]
        public void InvalidOrReorderedExposureCannotRewriteACapture()
        {
            var history = new CameraExposureHistory();
            history.Record(5, 1, false);
            history.Record(5, 1, true);
            history.Record(4, .9, true);
            history.Record(6, double.NaN, true);
            Assert.IsFalse(history.Contains(5, 1));
            Assert.IsFalse(history.Contains(4, .9));
            Assert.IsFalse(history.Contains(6, double.NaN));
        }
    }
}
