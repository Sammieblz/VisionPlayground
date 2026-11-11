using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace CoDriver.Editor
{
    public static class MapRouteAudit
    {
        [Serializable]
        class Turn
        {
            public string vehicle;
            public string[] internalLanes;
            public bool reachedExit;
            public int footprintOutsideSamples;
        }

        [Serializable]
        class Audit
        {
            public Turn[] turns;
        }

        [Serializable]
        class Result
        {
            public bool scheduledBusRouteUnchanged;
            public string[] addedBusExclusions,
                before,
                after;
            public string mapSha256,
                priorRoutingSha256,
                physicalReportSha256;
        }

        static string Hash(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter
                    .ToString(sha.ComputeHash(File.ReadAllBytes(path)))
                    .Replace("-", "")
                    .ToLowerInvariant();
        }

        public static void VerifyAdditionalBusExclusions()
        {
            string report = Path.GetFullPath(
                "../outputs/qualification/realism/map-physical-current.json"
            );
            var audit = JsonUtility.FromJson<Audit>(File.ReadAllText(report));
            var blocked = audit
                .turns.Where(t =>
                    t.vehicle == "bus" && (!t.reachedExit || t.footprintOutsideSamples > 0)
                )
                .SelectMany(t => t.internalLanes)
                .ToHashSet();
            var map = MapDefinition.Load("davis-downtown");
            var first = map.Nearest(
                map.landmarks.Single(l => l.sourceId == "node/13095780017").position
            );
            var second = map.Nearest(
                map.landmarks.Single(l => l.sourceId == "node/13095780019").position
            );
            var before = map.Route(first, second, true)
                .Concat(map.Route(second, first, true))
                .Select(l => l.id)
                .ToArray();
            var after = map.Route(first, second, true, blocked)
                .Concat(map.Route(second, first, true, blocked))
                .Select(l => l.id)
                .ToArray();
            var result = new Result
            {
                scheduledBusRouteUnchanged = before.SequenceEqual(after) && before.Length > 4,
                addedBusExclusions = blocked.OrderBy(s => s).ToArray(),
                before = before,
                after = after,
                mapSha256 = Hash("Assets/Resources/Maps/davis-downtown.json"),
                priorRoutingSha256 = Hash("Assets/Resources/Maps/davis-turn-limits.json"),
                physicalReportSha256 = Hash(report),
            };
            File.WriteAllText(
                Path.GetFullPath(
                    "../outputs/qualification/realism/bus-route-restriction-review.json"
                ),
                JsonUtility.ToJson(result, true)
            );
            if (!result.scheduledBusRouteUnchanged)
                throw new InvalidOperationException(
                    "Bus route changes; repeat populated transit/ambulance qualification after applying restrictions"
                );
            Debug.Log("SCHEDULED_BUS_ROUTE_UNCHANGED additions=" + blocked.Count);
        }
    }
}
