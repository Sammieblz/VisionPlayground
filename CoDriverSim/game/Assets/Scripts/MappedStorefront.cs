using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace CoDriver
{
    // Authored simulation scenery is identified separately from OSM source facts.
    // Its geometry and activity doors are only used by the world, never perception.
    public sealed class MappedStorefront
    {
        [Serializable]
        public sealed class Entry
        {
            public string sourceId,
                label;
            public int palette;
        }

        [Serializable]
        sealed class Catalog
        {
            public int schemaVersion;
            public Entry[] entries;
        }

        static Dictionary<string, Entry> catalog;
        public int Edge,
            Bay;
        public float CentreDistance,
            Width,
            WalkDistance;
        public Vector3 Centre,
            Outward,
            SidewalkPoint;
        public Entry Definition;
        public const float Opening = 1.4f,
            Height = 2.45f;

        public static MappedStorefront Find(MapDefinition map, MapBuilding building)
        {
            if (catalog == null)
            {
                var json = Resources.Load<TextAsset>("Maps/davis-scenery");
                catalog = json
                    ? JsonUtility.FromJson<Catalog>(json.text).entries.ToDictionary(e => e.sourceId)
                    : new Dictionary<string, Entry>();
            }
            if (!catalog.TryGetValue(building.sourceId, out var definition))
                return null;
            var ring = building.rings[0].points;
            float area = 0;
            for (int i = 1; i < ring.Length; i++)
                area += ring[i - 1].x * ring[i].z - ring[i].x * ring[i - 1].z;
            MappedStorefront best = null;
            float score = float.MaxValue;
            for (int i = 1; i < ring.Length; i++)
            {
                var edge = ring[i] - ring[i - 1];
                if (edge.magnitude < 4 || edge.magnitude > 90)
                    continue;
                var outward = Vector3.Cross(Vector3.up, edge.normalized) * (area > 0 ? 1 : -1);
                int bays = Mathf.Max(1, Mathf.RoundToInt(edge.magnitude / 3.2f));
                int bay = bays / 2;
                float width = edge.magnitude / bays;
                var centre = ring[i - 1] + edge.normalized * ((bay + .5f) * width);
                // A real sidewalk connection is required. Never send a visitor to
                // a decorative door on an inaccessible rear wall.
                var surface = map.sidewalkSurface;
                for (int triangle = 0; triangle < surface.triangles.Length; triangle += 3)
                {
                    var a = surface.vertices[surface.triangles[triangle]];
                    var b = surface.vertices[surface.triangles[triangle + 1]];
                    var c = surface.vertices[surface.triangles[triangle + 2]];
                    var point = ClosestPoint(centre + outward * 2, a, b, c);
                    var delta = point - centre;
                    float distance = Vector3.Dot(delta, outward);
                    float lateral = Mathf.Abs(Vector3.Dot(delta, edge.normalized));
                    if (distance < 1 || distance > 8 || lateral > 2 || Mathf.Abs(delta.y) > .3f)
                        continue;
                    float candidate = distance + lateral * 2;
                    if (candidate >= score)
                        continue;
                    score = candidate;
                    best = new MappedStorefront
                    {
                        Edge = i,
                        Bay = bay,
                        Width = width,
                        CentreDistance = (bay + .5f) * width,
                        Centre = centre,
                        Outward = outward,
                        WalkDistance = distance + .75f,
                        Definition = definition,
                        SidewalkPoint = Vector3.MoveTowards(point, (a + b + c) / 3, .65f),
                    };
                }
            }
            return best;
        }

        static Vector3 ClosestPoint(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            var ab = b - a;
            var ac = c - a;
            var ap = p - a;
            float d00 = Vector3.Dot(ab, ab),
                d01 = Vector3.Dot(ab, ac),
                d11 = Vector3.Dot(ac, ac);
            float d20 = Vector3.Dot(ap, ab),
                d21 = Vector3.Dot(ap, ac);
            float denominator = d00 * d11 - d01 * d01;
            if (Mathf.Abs(denominator) > .00001f)
            {
                float u = (d11 * d20 - d01 * d21) / denominator,
                    v = (d00 * d21 - d01 * d20) / denominator;
                if (u >= 0 && v >= 0 && u + v <= 1)
                    return a + ab * u + ac * v;
            }
            Vector3 Segment(Vector3 start, Vector3 end) =>
                start
                + (end - start)
                    * Mathf.Clamp01(
                        Vector3.Dot(p - start, end - start)
                            / Mathf.Max(.000001f, (end - start).sqrMagnitude)
                    );
            var candidates = new[] { Segment(a, b), Segment(b, c), Segment(c, a) };
            return candidates.OrderBy(q => (q - p).sqrMagnitude).First();
        }

        public void Build(Town town)
        {
            var root = new GameObject("Simulated storefront " + Definition.sourceId).transform;
            root.SetParent(town.transform, false);
            root.SetPositionAndRotation(Centre + Outward * .06f, Quaternion.LookRotation(-Outward));
            var colors = new[]
            {
                new Color(.12f, .23f, .21f),
                new Color(.30f, .15f, .10f),
                new Color(.15f, .22f, .30f),
                new Color(.45f, .32f, .19f),
            };
            var trim = Art.Material(
                "Storefront trim " + Definition.palette,
                colors[Definition.palette % colors.Length]
            );
            var hinge = new GameObject("Operable shop door").transform;
            hinge.SetParent(root, false);
            hinge.localPosition = new Vector3(-.6f, .12f, 0);
            Art.Box(
                "Door glazing",
                hinge,
                new Vector3(.6f, 1.12f, 0),
                new Vector3(1.17f, 2.24f, .04f),
                Art.Glass,
                true
            );
            foreach (float x in new[] { .02f, 1.18f })
                Art.Box(
                    "Door stile",
                    hinge,
                    new Vector3(x, 1.12f, -.025f),
                    new Vector3(.045f, 2.3f, .08f),
                    trim,
                    false
                );
            foreach (float y in new[] { .02f, 2.24f })
                Art.Box(
                    "Door rail",
                    hinge,
                    new Vector3(.6f, y, -.025f),
                    new Vector3(1.2f, .045f, .08f),
                    trim,
                    false
                );
            Art.Box(
                "Door handle",
                hinge,
                new Vector3(1.05f, 1.1f, -.08f),
                new Vector3(.025f, .28f, .035f),
                Art.White,
                false
            );
            town.Doors.Add(hinge.gameObject.AddComponent<ActivityDoor>());
            foreach (float x in new[] { -.69f, .69f })
                Art.Box(
                    "Door reveal",
                    root,
                    new Vector3(x, 1.23f, 0),
                    new Vector3(.08f, 2.46f, .22f),
                    trim,
                    false
                );
            var inside = Centre - Outward * 3;
            var path = SidewalkPoint - inside;
            path.y = 0;
            var floor = Art.Box(
                "Connected shop threshold",
                town.transform,
                (inside + SidewalkPoint) * .5f + Vector3.up * .01f,
                new Vector3(1.8f, .12f, path.magnitude + 1),
                Art.Concrete
            );
            floor.transform.rotation = Quaternion.LookRotation(path);
            town.WalkSurfaces.Add(
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    transform = floor.transform.localToWorldMatrix,
                    size = Vector3.one,
                    area = 0,
                }
            );
            // A shallow vestibule gives the physical visitor somewhere to enter.
            Art.Box(
                "Shop interior back",
                root,
                new Vector3(0, 1.5f, 3.3f),
                new Vector3(3, 3, .15f),
                Art.Concrete
            );
            Art.Box(
                "Shop canopy",
                root,
                new Vector3(0, 2.65f, -.5f),
                new Vector3(Width - .1f, .12f, 1.15f),
                trim,
                false
            );
            Art.Box(
                "Storefront fascia",
                root,
                new Vector3(0, 2.65f, -1.05f),
                new Vector3(Width - .1f, .34f, .08f),
                trim,
                false
            );
            var text = new GameObject("Fictional shop sign").AddComponent<TextMesh>();
            text.transform.SetParent(root, false);
            text.transform.localPosition = new Vector3(0, 2.65f, -1.10f);
            text.text = Definition.label;
            text.fontSize = 48;
            text.characterSize = Mathf.Min(
                .045f,
                (Width - .25f) / Mathf.Max(1, Definition.label.Length)
            );
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = new Color(.94f, .92f, .84f);
            var renderer = text.GetComponent<Renderer>();
            var lettering = new Material(Shader.Find("CoDriver/WorldText"));
            RuntimeResources.Register(text.gameObject, lettering);
            lettering.mainTexture = renderer.sharedMaterial.mainTexture;
            renderer.sharedMaterial = lettering;
        }
    }
}
