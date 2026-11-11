using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoDriver.Tests
{
    // Actual menu preparation on built worlds. Deferred Comma mode exercises
    // the start handshake only; this fixture never claims stack engagement.
    public sealed class ScenarioStartTests
    {
        Town town;
        Vehicle vehicle;
        ScenarioEvaluator evaluator;
        string outputRoot;
        float oldScale;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            oldScale = Time.timeScale;
            Time.timeScale = 0;
            outputRoot = Path.Combine(
                Path.GetTempPath(),
                "codriver-scenario-start-" + Guid.NewGuid().ToString("N")
            );
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            evaluator?.Close();
            evaluator = null;
            if (vehicle)
                UnityEngine.Object.Destroy(vehicle.gameObject);
            if (town)
                UnityEngine.Object.Destroy(town.gameObject);
            yield return null;
            if (!string.IsNullOrEmpty(outputRoot) && Directory.Exists(outputRoot))
                Directory.Delete(outputRoot, true);
            Time.timeScale = oldScale;
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator EveryCedarMenuScenarioPreparesHumanAndDeferredCommaStarts()
        {
            return CheckStarts(MapDefinition.Cedar());
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator EveryDavisMenuScenarioPreparesHumanAndDeferredCommaStarts()
        {
            return CheckStarts(MapDefinition.Load("davis-downtown"));
        }

        IEnumerator CheckStarts(MapDefinition map)
        {
            Art.Initialize();
            town = new GameObject("Scenario start world " + map.id).AddComponent<Town>();
            town.Map = map;
            town.Density = TrafficDensity.Normal;
            town.Build(null);
            vehicle = new GameObject("Scenario start vehicle").AddComponent<Vehicle>();
            vehicle.transform.SetPositionAndRotation(map.Spawn, map.SpawnRotation);
            vehicle.Initialize();
            vehicle.gameObject.AddComponent<AudioListener>();
            town.Player = vehicle.transform;
            int index = 0;
            foreach (string scenario in ScenarioRules.Names.Concat(new[] { "Free drive" }))
            foreach (string mode in new[] { "human_ai", "comma" })
            {
                string context = map.id + " / " + scenario + " / " + mode;
                string session = map.id + "-" + index++;
                string sessionRoot = Path.Combine(outputRoot, "outputs", "sessions", session);
                int captures = 0;
                evaluator = new ScenarioEvaluator(outputRoot, session, _ => captures++);
                try
                {
                    Assert.DoesNotThrow(
                        () => town.ResetActivities(),
                        context + " population reset"
                    );
                    Assert.IsNotEmpty(
                        town.Signals,
                        context + " requires actual signal infrastructure"
                    );
                    Assert.IsTrue(
                        town.Train && town.Bus && town.Emergency && town.ParkingCar,
                        context + " requires actual transit and dedicated actors"
                    );
                    bool deferred = mode == "comma";
                    Assert.DoesNotThrow(
                        () =>
                            evaluator.Start(
                                scenario,
                                mode,
                                town.Seed,
                                vehicle,
                                town,
                                deferBegin: deferred
                            ),
                        context + " scenario preparation"
                    );
                    Vector3 position = vehicle.Body.position;
                    Quaternion rotation = vehicle.Body.rotation;
                    Assert.IsTrue(
                        float.IsFinite(position.x)
                            && float.IsFinite(position.y)
                            && float.IsFinite(position.z)
                            && float.IsFinite(rotation.x)
                            && float.IsFinite(rotation.y)
                            && float.IsFinite(rotation.z)
                            && float.IsFinite(rotation.w),
                        context + " finite placement"
                    );
                    Assert.IsTrue(
                        map.Contains(position),
                        context + " starts inside the playable map"
                    );
                    var lane = map.Nearest(position, routable: false);
                    Assert.IsNotNull(lane, context + " has a real road lane");
                    var centre = lane.Path.Position(lane.Path.NearestDistance(position));
                    Assert.LessOrEqual(
                        Vector3.ProjectOnPlane(position - centre, Vector3.up).magnitude,
                        lane.width * .5f + .25f,
                        context + " starts inside road width"
                    );
                    Physics.SyncTransforms();
                    Assert.IsTrue(
                        Physics.Raycast(
                            position + Vector3.up,
                            Vector3.down,
                            out _,
                            2.5f,
                            1 << TrafficClearance.RoadLayer,
                            QueryTriggerInteraction.Ignore
                        ),
                        context + " has an actual physical road beneath the start"
                    );
                    if (deferred)
                    {
                        Assert.IsFalse(evaluator.IsRunning, context + " remains prepared");
                        vehicle.Body.linearVelocity = vehicle.transform.forward * 3;
                        evaluator.Observe(
                            vehicle,
                            Time.time + 30,
                            false,
                            "Waiting for Comma readiness"
                        );
                        Assert.AreEqual(0, evaluator.ControlEvidence.human_seconds, context);
                        Assert.AreEqual(0, evaluator.ControlEvidence.comma_seconds, context);
                        Assert.AreEqual(0, evaluator.ControlEvidence.human_moving_seconds, context);
                        Assert.AreEqual(
                            0,
                            captures,
                            context + " cannot start capture before Begin"
                        );
                        Assert.IsFalse(
                            Directory.Exists(sessionRoot),
                            context + " cannot write a score before Begin"
                        );
                        evaluator.Begin();
                        Assert.AreEqual(
                            position,
                            vehicle.Body.position,
                            context + " Begin cannot teleport the body"
                        );
                        Assert.AreEqual(
                            position,
                            vehicle.transform.position,
                            context + " Begin cannot teleport the transform"
                        );
                        Assert.Less(
                            Quaternion.Angle(rotation, vehicle.Body.rotation),
                            .001f,
                            context + " Begin cannot change vehicle heading"
                        );
                    }
                    Assert.IsTrue(evaluator.IsRunning, context + " begins only when requested");
                    Assert.AreEqual(
                        scenario == "Free drive" ? 0 : 1,
                        captures,
                        context + " starts exactly one scenario capture"
                    );
                    Assert.AreEqual(
                        1,
                        Directory
                            .GetFiles(sessionRoot, "scorecard.json", SearchOption.AllDirectories)
                            .Length,
                        context + " creates its initial score only after Begin"
                    );
                }
                finally
                {
                    evaluator.Close();
                    evaluator = null;
                    vehicle.Body.linearVelocity = Vector3.zero;
                }
                yield return null;
            }
            Assert.AreEqual((ScenarioRules.Names.Length + 1) * 2, index, map.id + " menu coverage");
        }
    }
}
