using Deadfall.Core;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// The self-revive syringe dispenser hung beside each <see cref="AmmoStation"/>: press F to buy one more
    /// self-revive kit for the squad's shared pool (host takes the credits through <see cref="AmmoStationDirector"/>).
    /// </summary>
    public sealed class SyringeDispenser : MonoBehaviour, IInteractable
    {
        private int _station;

        public static SyringeDispenser Create(Transform station, int stationIndex)
        {
            var root = new GameObject("Syringe Dispenser");
            root.transform.SetParent(station, false);
            root.transform.localPosition = new Vector3(0.62f, 0f, 0f);
            var d = root.AddComponent<SyringeDispenser>();
            d._station = stationIndex;
            Color white = new Color(0.86f, 0.88f, 0.9f), red = new Color(0.85f, 0.15f, 0.15f);
            AmmoStation.Part(root.transform, new Vector3(0f, 1.45f, -0.02f), new Vector3(0.28f, 0.62f, 0.24f), white, true); // box (solid: the use ray hits it)
            Renderer cross1 = AmmoStation.Part(root.transform, new Vector3(0f, 1.6f, 0.101f), new Vector3(0.14f, 0.04f, 0.01f), Color.white, false);
            Renderer cross2 = AmmoStation.Part(root.transform, new Vector3(0f, 1.6f, 0.101f), new Vector3(0.04f, 0.14f, 0.01f), Color.white, false);
            cross1.sharedMaterial = cross2.sharedMaterial = RuntimeMaterials.CreateEmissive(red, 1.6f);
            // Window with a syringe inside.
            AmmoStation.Part(root.transform, new Vector3(0f, 1.32f, 0.101f), new Vector3(0.2f, 0.16f, 0.01f), new Color(0.06f, 0.07f, 0.08f), false);
            GameObject barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            barrel.transform.SetParent(root.transform, false);
            barrel.transform.localPosition = new Vector3(0f, 1.32f, 0.09f);
            barrel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            barrel.transform.localScale = new Vector3(0.035f, 0.07f, 0.035f);
            barrel.GetComponent<Renderer>().sharedMaterial = RuntimeMaterials.CreateEmissive(new Color(0.3f, 1f, 0.5f), 1.2f);
            Destroy(barrel.GetComponent<Collider>());
            InteractGlow.Add(root, InteractGlow.PowerUps, () => AmmoStation.Running);
            return d;
        }

        public string InteractPrompt(GameObject player)
        {
            if (!AmmoStation.Running) return "Self-revive syringes (start a run to use)";
            SquadLife squad = SquadLife.Current;
            if (squad != null && squad.Kits >= SquadLife.MaxKits) return $"Self-revive syringe (squad is full: {SquadLife.MaxKits} kits)";
            return $"Buy self-revive syringe: +1 kit for the squad ({AmmoStationDirector.SyringePrice:N0} credits)";
        }

        public bool CanInteract(GameObject player) => AmmoStation.Running;

        public void Interact(GameObject player) => AmmoStationDirector.Current?.RequestSyringe(_station);
    }
}
