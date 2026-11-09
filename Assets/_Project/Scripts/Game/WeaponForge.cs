using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Player;
using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// The Weapon Forge: a workbench against a wall that raises the gun in your hands one tier (Standard to Refined to
    /// Prototype) for Field Credits, keeping its attachments and adding one. One per map, built on every peer by
    /// <see cref="PerkMachineDirector"/>. The root sits on the floor in front of the wall, facing into the room (+z).
    /// </summary>
    public sealed class WeaponForge : MonoBehaviour, IInteractable
    {
        public static WeaponForge Create(Transform parent, Vector3 floor, float yaw)
        {
            var root = new GameObject("Weapon Forge");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(floor, Quaternion.Euler(0f, yaw, 0f));
            var f = root.AddComponent<WeaponForge>();
            StationLabels.Add(root.transform, "Weapon Forge", new Color(1f, 0.6f, 0.3f), 2.3f);
            Color steel = new Color(0.22f, 0.23f, 0.25f), dark = new Color(0.1f, 0.1f, 0.11f), copper = new Color(0.72f, 0.42f, 0.2f);
            PerkMachine.Part(root.transform, new Vector3(0f, 0.45f, 0f), new Vector3(1.5f, 0.9f, 0.7f), steel, true);              // bench (solid)
            PerkMachine.Part(root.transform, new Vector3(0f, 0.93f, 0.02f), new Vector3(1.56f, 0.06f, 0.76f), dark, false);         // top
            PerkMachine.Part(root.transform, new Vector3(0f, 1.45f, -0.3f), new Vector3(1.5f, 1.0f, 0.08f), steel, false);          // back board
            PerkMachine.Part(root.transform, new Vector3(-0.45f, 1.06f, 0f), new Vector3(0.36f, 0.2f, 0.3f), dark, false);          // anvil base
            PerkMachine.Part(root.transform, new Vector3(-0.45f, 1.2f, 0f), new Vector3(0.5f, 0.08f, 0.22f), dark, false);          // anvil face
            PerkMachine.Part(root.transform, new Vector3(0.45f, 1.5f, -0.24f), new Vector3(0.5f, 0.06f, 0.04f), copper, false);     // tool rail
            // Glowing crucible and a lit sign on the back board.
            GameObject crucible = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            crucible.transform.SetParent(root.transform, false);
            crucible.transform.localPosition = new Vector3(0.38f, 1.06f, 0.05f);
            crucible.transform.localScale = new Vector3(0.3f, 0.09f, 0.3f);
            crucible.GetComponent<Renderer>().sharedMaterial = RuntimeMaterials.CreateEmissive(new Color(1f, 0.45f, 0.1f), 2.4f);
            Destroy(crucible.GetComponent<Collider>());
            Renderer sign = PerkMachine.Part(root.transform, new Vector3(0f, 1.8f, -0.255f), new Vector3(0.9f, 0.16f, 0.01f), Color.white, false);
            sign.sharedMaterial = RuntimeMaterials.CreateEmissive(new Color(0.85f, 0.4f, 1f), 1.8f);
            var lightGo = new GameObject("Glow");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0.3f, 1.4f, 0.6f);
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 4f;
            light.intensity = 1.1f;
            light.color = new Color(1f, 0.6f, 0.35f);
            light.shadows = LightShadows.None;
            InteractGlow.Add(root, InteractGlow.PowerUps, () => PerkMachine.Running);
            return f;
        }

        private static PlayerWeapons Weapons(GameObject player) => player != null ? player.GetComponent<PlayerWeapons>() : null;

        public string InteractPrompt(GameObject player)
        {
            if (!PerkMachine.Running) return "Weapon Forge (start a run to use)";
            PlayerWeapons w = Weapons(player);
            WeaponDefinition def = w != null ? w.CurrentDefinition : null;
            if (def == null) return "Weapon Forge";
            WeaponTier tier = w.CurrentMods != null ? w.CurrentMods.Tier : WeaponTier.Standard;
            int price = LootRules.ForgePrice(tier);
            if (price <= 0) return $"{def.DisplayName} is already Prototype";
            return $"Forge {def.DisplayName} to {tier + 1} ({price:N0} credits)";
        }

        public bool CanInteract(GameObject player)
        {
            if (!PerkMachine.Running) return false;
            PlayerWeapons w = Weapons(player);
            if (w == null || w.CurrentDefinition == null) return false;
            return LootRules.ForgePrice(w.CurrentMods != null ? w.CurrentMods.Tier : WeaponTier.Standard) > 0;
        }

        public void Interact(GameObject player) => PerkMachineDirector.Current?.RequestForge(Weapons(player));
    }
}
