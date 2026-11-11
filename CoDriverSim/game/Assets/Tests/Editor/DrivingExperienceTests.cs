using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace CoDriver.Tests
{
    public sealed class DrivingExperienceTests
    {
        [Test]
        public void AcceptedFreeDriveClearsPendingNoticeAndTakeoverRestoresManualNotice()
        {
            var root = new GameObject("Inactive control notice fixture");
            root.SetActive(false);
            try
            {
                var app = root.AddComponent<CoDriverApp>();
                app.Paused = false;
                app.Mode = "comma";
                app.Scenario = "Free drive";
                var command = new CommaCommand
                {
                    session_id = app.Session,
                    healthy = true,
                    engageable = true,
                    active = true,
                };
                Assert.IsTrue(app.Arbiter.TryRequest(command, app.Session, .05, 10));
                StringAssert.Contains("Waiting for self-driving", app.ControlNotice);
                var acceptedField = typeof(CoDriverApp).GetField(
                    "automationAccepted",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                bool accepted = app.Arbiter.Accept(
                    command,
                    app.Session,
                    .05,
                    0,
                    0,
                    false,
                    false,
                    now: 10.1
                );
                acceptedField.SetValue(app, accepted);
                Assert.IsTrue(app.AutomationActive);
                Assert.AreEqual("SELF-DRIVING", app.ControlStatus);
                Assert.AreEqual("Free drive", app.ScenarioStatus);
                Assert.IsEmpty(
                    app.ControlNotice,
                    "An accepted drive is no longer waiting to engage"
                );
                accepted = app.Arbiter.Accept(
                    command,
                    app.Session,
                    .05,
                    0,
                    1,
                    false,
                    false,
                    now: 10.2
                );
                acceptedField.SetValue(app, accepted);
                Assert.IsFalse(app.AutomationActive);
                Assert.AreEqual("MANUAL DRIVING", app.ControlStatus);
                StringAssert.Contains("You are driving", app.ControlNotice);
                StringAssert.DoesNotContain("Waiting", app.ControlNotice);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SelfDrivingStartRejectsFramesFromBeforePlacement()
        {
            var command = new CommaCommand
            {
                session_id = "drive",
                frame_id = 101,
                healthy = true,
                engageable = true,
            };
            var snapshot = new OpenpilotUiSnapshot
            {
                schema_version = 1,
                session_id = "drive",
                timestamp = 10.2,
                road = new UiModelGeometry
                {
                    schema_version = 2,
                    session_id = "drive",
                    frame_id = 101,
                    timestamp = 10.1,
                },
            };
            Assert.IsTrue(
                CoDriverApp.StartViewReady(command, snapshot, "drive", .1, 10.2, 100, 10, 10.3)
            );
            command.frame_id = 100;
            Assert.IsFalse(
                CoDriverApp.StartViewReady(command, snapshot, "drive", .1, 10.2, 100, 10, 10.3)
            );
            command.frame_id = 101;
            snapshot.road.frame_id = 100;
            Assert.IsFalse(
                CoDriverApp.StartViewReady(command, snapshot, "drive", .1, 10.2, 100, 10, 10.3)
            );
            snapshot.road.frame_id = 101;
            snapshot.road.timestamp = 9.99;
            Assert.IsFalse(
                CoDriverApp.StartViewReady(command, snapshot, "drive", .1, 10.2, 100, 10, 10.3)
            );
            snapshot.road.timestamp = 10.1;
            Assert.IsFalse(
                CoDriverApp.StartViewReady(command, snapshot, "drive", .1, 9.99, 100, 10, 10.3)
            );
        }

        [Test]
        public void PendingScenarioDoesNotGradeManualTimeAndBeginDoesNotTeleport()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "codriver-start-" + Guid.NewGuid().ToString("N")
            );
            var world = new GameObject("Prepared scenario");
            var car = new GameObject("Prepared car");
            ScenarioEvaluator evaluator = null;
            try
            {
                var town = world.AddComponent<Town>();
                town.Map = MapDefinition.Load("cedar-grove");
                town.Signals.Add(world.AddComponent<Signal>());
                var vehicle = car.AddComponent<Vehicle>();
                var body = car.AddComponent<Rigidbody>();
                typeof(Vehicle).GetProperty("Body").SetValue(vehicle, body);
                int captures = 0;
                evaluator = new ScenarioEvaluator(directory, "drive", _ => captures++);
                evaluator.Start("Normal driving", "comma", 42, vehicle, town, deferBegin: true);
                body.linearVelocity = Vector3.forward * 3;
                evaluator.Observe(vehicle, Time.time + 20, false, "Waiting");
                Assert.IsFalse(evaluator.IsRunning);
                Assert.AreEqual(0, captures);
                Assert.AreEqual(0, evaluator.ControlEvidence.human_seconds);
                Assert.AreEqual(0, evaluator.ControlEvidence.human_moving_seconds);
                Vector3 preparedPosition = vehicle.transform.position;
                evaluator.Begin();
                Assert.IsTrue(evaluator.IsRunning);
                Assert.AreEqual(preparedPosition, vehicle.transform.position);
                Assert.AreEqual(1, captures);
                evaluator.Observe(vehicle, Time.time + .1f, true, "Accepted");
                Assert.AreEqual(0, evaluator.ControlEvidence.human_seconds);
                Assert.Greater(evaluator.ControlEvidence.comma_seconds, 0);
            }
            finally
            {
                evaluator?.Close();
                UnityEngine.Object.DestroyImmediate(car);
                UnityEngine.Object.DestroyImmediate(world);
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        [Test]
        public void RuntimeBridgeDoesNotStretchTheBakedDistrictAndIsDrawnOutsideIt()
        {
            var map = MapDefinition.Load("davis-downtown");
            var source = JsonUtility.FromJson<MapDefinition>(
                Resources.Load<TextAsset>("Maps/davis-downtown").text
            );
            Assert.AreEqual(source.boundsMax, map.RasterBoundsMax);
            Assert.Greater(map.boundsMax.x, map.RasterBoundsMax.x);
            var go = new GameObject("Raster registration", typeof(RectTransform));
            try
            {
                var graphic = go.AddComponent<MapGraphic>();
                graphic.Map = map;
                graphic.rectTransform.sizeDelta = new Vector2(400, 400);
                graphic.Centre = map.Spawn;
                graphic.Range = 2000;
                typeof(MapGraphic)
                    .GetMethod(
                        "Update",
                        BindingFlags.Instance | BindingFlags.NonPublic,
                        null,
                        Type.EmptyTypes,
                        null
                    )
                    .Invoke(graphic, null);
                using (var vh = new UnityEngine.UI.VertexHelper())
                {
                    typeof(MapGraphic)
                        .GetMethod(
                            "OnPopulateMesh",
                            BindingFlags.Instance | BindingFlags.NonPublic,
                            null,
                            new[] { typeof(UnityEngine.UI.VertexHelper) },
                            null
                        )
                        .Invoke(graphic, new object[] { vh });
                    var lo = new UnityEngine.UIVertex();
                    var hi = new UnityEngine.UIVertex();
                    vh.PopulateUIVertex(ref lo, 0);
                    vh.PopulateUIVertex(ref hi, 2);
                    Vector3 landmark = source.landmarks.First(l => l.kind == "stop_sign").position;
                    float u = Mathf.InverseLerp(source.boundsMin.x, source.boundsMax.x, landmark.x);
                    float v = Mathf.InverseLerp(source.boundsMin.z, source.boundsMax.z, landmark.z);
                    Vector2 rasterPosition = new Vector2(
                        Mathf.Lerp(lo.position.x, hi.position.x, u),
                        Mathf.Lerp(lo.position.y, hi.position.y, v)
                    );
                    Assert.Less(Vector2.Distance(graphic.Project(landmark), rasterPosition), .001f);
                    graphic.PopulateOverlay(vh);
                    bool bridgeVisible = false;
                    for (int i = 0; i < vh.currentVertCount; i++)
                    {
                        var vertex = new UnityEngine.UIVertex();
                        vh.PopulateUIVertex(ref vertex, i);
                        if (graphic.Unproject(vertex.position).x > map.RasterBoundsMax.x + 10)
                            bridgeVisible = true;
                    }
                    Assert.IsTrue(
                        bridgeVisible,
                        "The extension must have visible vector road geometry"
                    );
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SelfDrivingStartRequiresFreshHealthyEngageableFiniteCommands()
        {
            var command = new CommaCommand
            {
                session_id = "drive",
                healthy = true,
                engageable = true,
            };
            Assert.IsTrue(CoDriverApp.CanStartSelfDriving(command, "drive", .1));
            Assert.IsFalse(CoDriverApp.CanStartSelfDriving(command, "drive", .3));
            Assert.IsFalse(CoDriverApp.CanStartSelfDriving(command, "other", .1));
            command.engageable = false;
            Assert.IsFalse(CoDriverApp.CanStartSelfDriving(command, "drive", .1));
            command.engageable = true;
            command.acceleration_mps2 = float.NaN;
            Assert.IsFalse(CoDriverApp.CanStartSelfDriving(command, "drive", .1));
        }

        [TestCase(0)]
        [TestCase(90)]
        [TestCase(180)]
        [TestCase(270)]
        public void MapKeepsPlayerCentredAndProjectsHeadingForward(float heading)
        {
            var go = new GameObject("Map projection", typeof(RectTransform));
            try
            {
                var map = go.AddComponent<MapGraphic>();
                map.rectTransform.sizeDelta = new Vector2(370, 285);
                map.Range = 130;
                map.Centre = new Vector3(923, 0, -356);
                map.Heading = heading;
                Assert.Less(map.Project(map.Centre).magnitude, .001f);
                Vector3 ahead = map.Centre + Quaternion.Euler(0, heading, 0) * Vector3.forward * 40;
                Assert.Less(Mathf.Abs(map.Project(ahead).x), .001f);
                Assert.Greater(map.Project(ahead).y, 0);
                Assert.Less(Vector3.Distance(map.Unproject(map.Project(ahead)), ahead), .001f);
                var overlay = go.GetComponentInChildren<MapOverlay>();
                Assert.IsNotNull(overlay);
                Assert.AreSame(Texture2D.whiteTexture, overlay.mainTexture);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ComfortAndDirectionRulesDistinguishUnsafeAndSafeMotion()
        {
            Assert.IsFalse(ScenarioRules.ComfortableAcceleration(4, .6f));
            Assert.IsTrue(ScenarioRules.ComfortableAcceleration(4, .1f));
            Assert.IsTrue(ScenarioRules.ComfortableAcceleration(2, 2));
            Assert.IsFalse(ScenarioRules.TimelyBrake(5, 10, 0));
            Assert.IsTrue(ScenarioRules.TimelyBrake(30, 10, 0));
            Assert.IsTrue(ScenarioRules.TimelyBrake(5, 10, 4));
            Assert.IsTrue(ScenarioRules.TravellingWrongWay(Vector3.back * 3, Vector3.forward));
            Assert.IsFalse(ScenarioRules.TravellingWrongWay(Vector3.forward * 3, Vector3.forward));
            Assert.IsFalse(ScenarioRules.TravellingWrongWay(Vector3.zero, Vector3.forward));
        }

        [Test]
        public void RecognitionRejectsStaleForeignAndBeforeEncounterFrames()
        {
            var result = new ObserverResult
            {
                session_id = "drive",
                frame_id = 12,
                timestamp = 20,
            };
            Assert.IsTrue(ScenarioRules.CameraEvidenceCurrent(result, true, "drive", 19, 11));
            Assert.IsFalse(ScenarioRules.CameraEvidenceCurrent(result, false, "drive", 19, 11));
            Assert.IsFalse(ScenarioRules.CameraEvidenceCurrent(result, true, "other", 19, 11));
            Assert.IsFalse(ScenarioRules.CameraEvidenceCurrent(result, true, "drive", 21, 11));
            Assert.IsFalse(ScenarioRules.CameraEvidenceCurrent(result, true, "drive", 19, 12));
        }

        [Test]
        public void RecognitionNeedsSupportedEvidenceAndMatchingFrame()
        {
            var detection = new Detection
            {
                label = "dog",
                score = .9f,
                source = "camera",
                provenance = "detector",
            };
            var risk = new Risk
            {
                kind = "workzone",
                frame_id = 12,
                source = "camera",
                provenance = "heuristic-unvalidated",
            };
            var result = new ObserverResult
            {
                frame_id = 12,
                tracks = new[] { detection },
                risks = new[] { risk },
            };
            Assert.IsTrue(ScenarioRules.RecognizesAnimal(result, "Dog"));
            Assert.IsFalse(ScenarioRules.RecognizesAnimal(result, "Cat"));
            detection.score = .1f;
            Assert.IsFalse(ScenarioRules.RecognizesAnimal(result));
            detection.score = .9f;
            detection.provenance = "";
            Assert.IsFalse(ScenarioRules.RecognizesAnimal(result));
            Assert.IsTrue(ScenarioRules.RecognizesWorkzone(result));
            risk.frame_id = 11;
            Assert.IsFalse(ScenarioRules.RecognizesWorkzone(result));
            risk.frame_id = 12;
            risk.source = "";
            Assert.IsFalse(ScenarioRules.RecognizesWorkzone(result));
        }

        [Test]
        public void DavisStopLineIsBetweenStartAndFinish()
        {
            var go = new GameObject("Mapped stop course");
            try
            {
                var town = go.AddComponent<Town>();
                town.Map = MapDefinition.Load("davis-downtown");
                var course = new MappedScenarioCourse(town, "Stop compliance", -197.7f);
                course.Validate(-260, -170);
                float stopZ = course.Local(course.StopLine).z;
                Assert.That(stopZ, Is.GreaterThan(-260).And.LessThan(-170));
                Assert.That(stopZ, Is.EqualTo(-213).Within(.5f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
