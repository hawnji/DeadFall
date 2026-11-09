using Deadfall.Core;
using Deadfall.Encounters;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// A wall-mounted ammo refill station: press F to buy a full refill of every gun's reserve for Field Credits during
    /// a run. Built on every peer by <see cref="AmmoStationDirector"/>; the host takes the credits and grants the ammo.
    /// The root sits on the floor in front of the wall, facing into the room (+z).
    /// </summary>
    public sealed class AmmoStation : MonoBehaviour, IInteractable
    {
        public int Index { get; private set; }

        public static AmmoStation Create(Transform parent, int index, Vector3 floor, float yaw)
        {
            var root = new GameObject($"Ammo Station #{index}");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(floor, Quaternion.Euler(0f, yaw, 0f));
            var s = root.AddComponent<AmmoStation>();
            StationLabels.Add(root.transform, "Ammo & Syringes", new Color(1f, 0.8f, 0.3f), 2.5f);
            s.Index = index;
            Color steel = new Color(0.2f, 0.22f, 0.24f), olive = new Color(0.3f, 0.34f, 0.2f), brass = new Color(0.8f, 0.6f, 0.25f);
            Part(root.transform, new Vector3(0f, 1.5f, -0.14f), new Vector3(1.0f, 1.4f, 0.06f), steel, false);                // wall plate
            Part(root.transform, new Vector3(0f, 1.45f, 0f), new Vector3(0.84f, 1.0f, 0.3f), olive, true);                    // cabinet (solid: the use ray hits it)
            Part(root.transform, new Vector3(0f, 2.05f, 0.02f), new Vector3(0.96f, 0.14f, 0.34f), new Color(0.85f, 0.65f, 0.13f), false); // hazard header
            Part(root.transform, new Vector3(0f, 0.9f, 0.02f), new Vector3(0.96f, 0.1f, 0.34f), new Color(0.85f, 0.65f, 0.13f), false);   // hazard footer
            // Window with rounds racked behind it.
            Part(root.transform, new Vector3(0f, 1.55f, 0.151f), new Vector3(0.62f, 0.5f, 0.01f), new Color(0.06f, 0.07f, 0.08f), false);
            for (int i = 0; i < 5; i++)
            {
                GameObject round = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                round.transform.SetParent(root.transform, false);
                round.transform.localPosition = new Vector3(-0.22f + i * 0.11f, 1.55f, 0.14f);
                round.transform.localScale = new Vector3(0.05f, 0.15f, 0.05f);
                round.GetComponent<Renderer>().sharedMaterial = RuntimeMaterials.CreateLit(brass);
                Destroy(round.GetComponent<Collider>());
            }
            // Glowing strip and button.
            Renderer strip = Part(root.transform, new Vector3(0f, 1.22f, 0.152f), new Vector3(0.62f, 0.06f, 0.01f), Color.white, false);
            strip.sharedMaterial = RuntimeMaterials.CreateEmissive(new Color(1f, 0.75f, 0.2f), 1.4f);
            Renderer button = Part(root.transform, new Vector3(0.3f, 1.08f, 0.155f), new Vector3(0.1f, 0.1f, 0.02f), Color.white, false);
            button.sharedMaterial = RuntimeMaterials.CreateEmissive(new Color(0.3f, 1f, 0.4f), 1.6f);
            var lightGo = new GameObject("Glow");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 1.5f, 0.6f);
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 3.5f;
            light.intensity = 0.9f;
            light.color = new Color(1f, 0.8f, 0.45f);
            light.shadows = LightShadows.None;
            InteractGlow.Add(root, InteractGlow.PowerUps, () => Running);
            SyringeDispenser.Create(root.transform, index);
            return s;
        }

        internal static Renderer Part(Transform parent, Vector3 local, Vector3 size, Color color, bool solid)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = RuntimeMaterials.CreateLit(color);
            if (!solid) Destroy(go.GetComponent<Collider>());
            return go.GetComponent<Renderer>();
        }

        internal static bool Running => WaveManager.Current != null && WaveManager.Current.IsRunning;

        public string InteractPrompt(GameObject player) =>
            Running ? $"Refill ammo ({AmmoStationDirector.Price} credits)" : "Ammo station (start a run to use)";

        public bool CanInteract(GameObject player) => Running;

        public void Interact(GameObject player) => AmmoStationDirector.Current?.RequestUse(Index);
    }
}
