using NUnit.Framework;
using UnityEngine;

namespace CoDriver.Tests
{
    public class ScenarioTests
    {
        [Test]
        public void HumanTakeoverIsRecordedWithoutCreditingCommaForTheRestOfTheDrive()
        {
            var control = new ComparisonControlEvidence();
            control.Record(3, Vector3.zero, false); // Stationary startup is not manual driving.
            control.Record(10, Vector3.forward * 8, true);
            Assert.IsTrue(control.Matches("comma"));
            control.Record(1, Vector3.forward * 6, false);
            Assert.IsFalse(control.Matches("comma"));
            Assert.AreEqual(1, control.human_moving_seconds);
            Assert.AreEqual(10, control.comma_moving_seconds);
            Assert.IsFalse(control.Matches("human_ai"));
        }

        [Test]
        public void CommaSelectionWithoutEngagementIsNotACommaComparison()
        {
            var control = new ComparisonControlEvidence();
            control.Record(20, Vector3.forward * 5, false);
            Assert.IsFalse(control.Matches("comma"));
            Assert.IsTrue(control.Matches("human_muted"));
        }

        [Test]
        public void FinishCannotBeReachedByDrivingThroughAnAdjacentBlock()
        {
            Assert.IsTrue(ScenarioRules.InsideCourse(new Vector3(-1.7f, 0, 50), 2.3f));
            Assert.IsFalse(ScenarioRules.InsideCourse(new Vector3(25, 0, 50), 2.3f));
        }

        [Test]
        public void SuspensionSettlingDoesNotInvalidateCommaComparison()
        {
            var control = new ComparisonControlEvidence();
            control.Record(.2f, Vector3.down * 2, false);
            control.Record(5, Vector3.forward * 8, true);
            Assert.IsTrue(control.Matches("comma"));
            Assert.AreEqual(0, control.human_moving_seconds);
        }

        [Test]
        public void BeingInsideJunctionWhenSignalChangesIsNotCrossingOnRed()
        {
            Assert.IsFalse(ScenarioRules.CrossedLine(7, 6));
            Assert.IsFalse(ScenarioRules.CrossedLine(-2, -3));
            Assert.IsTrue(ScenarioRules.CrossedLine(13.2f, 12.9f));
            Assert.IsFalse(ScenarioRules.CrossedLine(12.9f, 13.2f));
        }

        [Test]
        public void UnencounteredScenarioCannotEarnFullScore()
        {
            var c = new[]
            {
                new DrivingCriterion { name = "Collision free", passed = true },
                new DrivingCriterion { name = "Encounter", passed = false },
            };
            Assert.AreEqual(50, ScenarioRules.Percentage(c));
        }

        [Test]
        public void CriticalFailureOverridesHighPercentageAndUnavoidableIsExcluded()
        {
            var c = new[]
            {
                new DrivingCriterion
                {
                    name = "Collision",
                    critical = true,
                    passed = false,
                },
                new DrivingCriterion { name = "Speed", passed = true },
                new DrivingCriterion
                {
                    name = "Scripted unavoidable",
                    unavoidable = true,
                    critical = true,
                    passed = false,
                },
            };
            Assert.AreEqual(50, ScenarioRules.Percentage(c));
            Assert.IsTrue(ScenarioRules.CriticalFailure(c));
            c[0].passed = true;
            Assert.AreEqual(100, ScenarioRules.Percentage(c));
            Assert.IsFalse(ScenarioRules.CriticalFailure(c));
        }
    }
}
