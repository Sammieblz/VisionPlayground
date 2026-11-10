using UnityEngine;

namespace CoDriver
{
    public sealed class TrafficLights : MonoBehaviour
    {
        TrafficActor actor;
        Renderer[] lamps;
        Material amber,
            dark;

        public void Initialize(TrafficActor owner)
        {
            actor = owner;
            amber = Art.Material("Traffic indicator on", new Color(1, .48f, .025f), true);
            dark = Art.Material("Traffic indicator off", new Color(.18f, .08f, .015f));
            lamps = new Renderer[4];
            float width = actor.Kind == "Bus" ? 1.07f : .78f;
            float length = actor.Kind == "Bus" ? 5.04f : 2.24f;
            for (int i = 0; i < 4; i++)
                lamps[i] = Art.Box(
                        "Turn signal",
                        transform,
                        new Vector3(i % 2 == 0 ? -width : width, .85f, i < 2 ? length : -length),
                        new Vector3(.17f, .11f, .04f),
                        dark,
                        false
                    )
                    .GetComponent<Renderer>();
        }

        void Update()
        {
            for (int i = 0; i < lamps.Length; i++)
                lamps[i].sharedMaterial =
                    actor.Indicator == (i % 2 == 0 ? -1 : 1) && Time.time % .8f < .4f
                        ? amber
                        : dark;
        }
    }
}
