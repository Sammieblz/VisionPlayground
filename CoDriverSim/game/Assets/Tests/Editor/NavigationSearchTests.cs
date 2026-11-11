using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace CoDriver.Tests
{
    public sealed class NavigationSearchTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void HintedProjectionMatchesExhaustiveSegmentsAcrossLoopsAndEndpoints(bool closed)
        {
            var points = new List<Vector3>();
            for (int i = 0; i < 240; i++)
            {
                float angle = i * Mathf.PI * 2 / 240;
                points.Add(
                    new Vector3(
                        Mathf.Sin(angle) * 240,
                        Mathf.Sin(angle * 3) * 2,
                        Mathf.Cos(angle) * 160
                    )
                );
            }
            if (closed)
                points.Add(points[0]);
            var path = new TrackPath(points, closed);
            var random = new System.Random(19031);
            for (int i = 0; i < 2400; i++)
            {
                float hint = (float)random.NextDouble() * path.Length * 3 - path.Length;
                if (i % 13 == 0)
                    hint = float.NaN;
                if (i % 17 == 0)
                    hint = 0;
                if (i % 19 == 0)
                    hint = path.Length;
                var point = new Vector3(
                    (float)random.NextDouble() * 600 - 300,
                    (float)random.NextDouble() * 8 - 4,
                    (float)random.NextDouble() * 400 - 200
                );
                float expected = Exhaustive(points, path.Length, closed, point, hint);
                Assert.AreEqual(
                    expected,
                    path.NearestDistance(point, hint),
                    .001f,
                    $"closed={closed}, query={i}, hint={hint}"
                );
            }
            // A long segment's projected point, not merely its endpoint or
            // overlap with the hint window, decides whether it is admissible.
            var line = new List<Vector3>
            {
                Vector3.zero,
                Vector3.forward * 1000,
                new Vector3(10, 0, 1000),
                new Vector3(10, 0, 0),
            };
            var longPath = new TrackPath(line, closed);
            if (closed)
                line.Add(line[0]);
            foreach (float hint in new[] { 0f, 45, 500, 955, 1000, 1055, 1900, 5000 })
                Assert.AreEqual(
                    Exhaustive(line, longPath.Length, closed, new Vector3(5, 0, 500), hint),
                    longPath.NearestDistance(new Vector3(5, 0, 500), hint),
                    .001f
                );
        }

        static float Exhaustive(
            List<Vector3> points,
            float length,
            bool closed,
            Vector3 point,
            float hint
        )
        {
            float offset = 0,
                error = float.PositiveInfinity,
                best = 0;
            for (int i = 1; i < points.Count; i++)
            {
                var delta = points[i] - points[i - 1];
                float span = delta.magnitude;
                float t = Mathf.Clamp01(
                    Vector3.Dot(point - points[i - 1], delta) / delta.sqrMagnitude
                );
                float at = Mathf.Lerp(offset, offset + span, t);
                float distance = (points[i - 1] + delta * t - point).sqrMagnitude;
                float window = closed
                    ? Mathf.Repeat(at - hint + length * .5f, length) - length * .5f
                    : at - hint;
                if ((!float.IsFinite(hint) || Mathf.Abs(window) <= 45) && distance < error)
                {
                    error = distance;
                    best = at;
                }
                offset += span;
            }
            return float.IsPositiveInfinity(error) && float.IsFinite(hint)
                ? Exhaustive(points, length, closed, point, float.NaN)
                : best;
        }

        [Test]
        public void IndexedDavisLookupMatchesAllLaneDistancesNearRoads()
        {
            var map = MapDefinition.Load("davis-downtown");
            for (int i = 0; i < map.lanes.Length; i += 3)
                foreach (float fraction in new[] { .1f, .5f, .9f })
                foreach (float direction in new[] { -1f, 0f, 1f })
                {
                    var lane = map.lanes[i];
                    float at = lane.Path.Length * fraction;
                    var forward = lane.Path.Direction(at) * direction;
                    var point =
                        lane.Path.Position(at)
                        + Vector3.Cross(Vector3.up, lane.Path.Direction(at)) * 3;
                    float expected = float.PositiveInfinity;
                    foreach (var candidate in map.lanes)
                        expected = Mathf.Min(expected, Score(candidate, point, forward));
                    var actual = map.Nearest(point, forward, false);
                    Assert.IsNotNull(actual);
                    Assert.AreEqual(expected, Score(actual, point, forward), .001f, lane.id);
                }
        }

        static float Score(MapLane lane, Vector3 point, Vector3 forward)
        {
            float at = lane.Path.NearestDistance(point);
            float score = (lane.Path.Position(at) - point).sqrMagnitude;
            return score
                + (
                    forward.sqrMagnitude > .1f && Vector3.Dot(forward, lane.Path.Direction(at)) < 0
                        ? 64
                        : 0
                );
        }
    }
}
