using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Player;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// The Mod Bench: a gunsmith's bench against a wall where you buy attachments for the gun in your hands with Field
    /// Credits (<see cref="ModBenchMenu"/>). One per map, in the first sector, built on every peer by
    /// <see cref="PerkMachineDirector"/>. The root sits on the floor in front of the wall, facing into the room (+z).
    /// </summary>
    public sealed class ModBench : MonoBehaviour, IInteractable
    {
        public static ModBench Create(Transform parent, Vector3 floor, float yaw)
        {
            var root = new GameObject("Mod Bench");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(floor, Quaternion.Euler(0f, yaw, 0f));
            var b = root.AddComponent<ModBench>();
            StationLabels.Add(root.transform, "Mod Bench", new Color(0.45f, 0.85f, 0.6f), 2.3f);
            Color steel = new Color(0.2f, 0.24f, 0.27f), dark = new Color(0.1f, 0.1f, 0.11f), board = new Color(0.46f, 0.38f, 0.26f), mat = new Color(0.12f, 0.3f, 0.22f);
            PerkMachine.Part(root.transform, new Vector3(0f, 0.45f, 0f), new Vector3(1.5f, 0.9f, 0.7f), steel, true);              // bench (solid)
            PerkMachine.Part(root.transform, new Vector3(0f, 0.93f, 0.02f), new Vector3(1.56f, 0.06f, 0.76f), dark, false);         // top
            PerkMachine.Part(root.transform, new Vector3(0.1f, 0.965f, 0.06f), new Vector3(0.9f, 0.01f, 0.42f), mat, false);        // cutting mat
            PerkMachine.Part(root.transform, new Vector3(0f, 1.45f, -0.3f), new Vector3(1.5f, 1.0f, 0.06f), board, false);          // pegboard
            for (int i = 0; i < 4; i++)
                PerkMachine.Part(root.transform, new Vector3(-0.6f + i * 0.4f, 1.2f, -0.255f), new Vector3(0.03f, 0.03f, 0.04f), dark, false); // pegs
            PerkMachine.Part(root.transform, new Vector3(-0.55f, 1.03f, 0.1f), new Vector3(0.22f, 0.1f, 0.16f), new Color(0.65f, 0.2f, 0.15f), false); // parts tray
            PerkMachine.Part(root.transform, new Vector3(0.55f, 1.0f, -0.12f), new Vector3(0.2f, 0.06f, 0.08f), new Color(0.75f, 0.6f, 0.2f), false);  // vice

            // Attachments hanging on the pegboard, shown big enough to read from across the room.
            Hang(root.transform, "att_sniper_scope", new Vector3(-0.35f, 1.62f, -0.24f), 2.2f);
            Hang(root.transform, "att_red_dot", new Vector3(0.25f, 1.62f, -0.24f), 2.4f);
            Hang(root.transform, "att_foregrip", new Vector3(0.55f, 1.68f, -0.24f), 2.2f);
            Hang(root.transform, "att_compensator", new Vector3(-0.6f, 1.3f, -0.24f), 2.4f);
            Hang(root.transform, "att_ext_mag_stick", new Vector3(0.05f, 1.68f, -0.24f), 1.4f);
            Hang(root.transform, "att_light_stock", new Vector3(0.3f, 1.25f, -0.24f), 1.6f);

            Renderer sign = PerkMachine.Part(root.transform, new Vector3(0f, 2.02f, -0.29f), new Vector3(0.9f, 0.16f, 0.01f), Color.white, false);
            sign.sharedMaterial = RuntimeMaterials.CreateEmissive(new Color(0.35f, 0.75f, 1f), 1.8f);
            var lightGo = new GameObject("Glow");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 1.5f, 0.6f);
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 4f;
            light.intensity = 1.1f;
            light.color = new Color(0.6f, 0.8f, 1f);
            light.shadows = LightShadows.None;
            InteractGlow.Add(root, InteractGlow.Guns, () => PerkMachine.Running);
            return b;
        }

        private static void Hang(Transform root, string id, Vector3 at, float scale)
        {
            GameObject go = ModelLibrary.Spawn(id, root);
            if (go == null) return;
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(0f, 90f, 0f); // side on, so you see the profile
            go.transform.localScale = Vector3.one * scale;
        }

        private static PlayerWeapons Weapons(GameObject player) => player != null ? player.GetComponent<PlayerWeapons>() : null;

        public string InteractPrompt(GameObject player)
        {
            if (!PerkMachine.Running) return "Mod Bench (start a run to use)";
            WeaponDefinition def = Weapons(player)?.CurrentDefinition;
            if (def == null || def.Kind != WeaponKind.Firearm) return "Mod Bench (hold a gun)";
            return $"Mod Bench: fit attachments to the {def.DisplayName}";
        }

        public bool CanInteract(GameObject player)
        {
            if (!PerkMachine.Running) return false;
            WeaponDefinition def = Weapons(player)?.CurrentDefinition;
            return def != null && def.Kind == WeaponKind.Firearm;
        }

        public void Interact(GameObject player) => ModBenchMenu.Open(this);
    }
}
