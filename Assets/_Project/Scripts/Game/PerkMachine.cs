using Deadfall.Core;
using Deadfall.Encounters;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>The four perks sold by perk machines. Each lasts until its owner goes down.</summary>
    public enum PerkKind : byte { SwiftHands, IronHide, FieldMedic, FleetFoot }

    /// <summary>
    /// A perk machine standing against a wall: press F to buy its perk for Field Credits during a run. Built on every
    /// peer by <see cref="PerkMachineDirector"/>. The root sits on the floor in front of the wall, facing into the room (+z).
    /// </summary>
    public sealed class PerkMachine : MonoBehaviour, IInteractable
    {
        public const int Count = 4;

        public int Index { get; private set; }
        public PerkKind Perk { get; private set; }

        public static string NameOf(PerkKind p)
        {
            switch (p)
            {
                case PerkKind.SwiftHands: return "Swift Hands";
                case PerkKind.IronHide: return "Iron Hide";
                case PerkKind.FieldMedic: return "Field Medic";
                default: return "Fleet Foot";
            }
        }

        public static string ShortOf(PerkKind p)
        {
            switch (p)
            {
                case PerkKind.SwiftHands: return "SH";
                case PerkKind.IronHide: return "IH";
                case PerkKind.FieldMedic: return "FM";
                default: return "FF";
            }
        }

        public static string EffectOf(PerkKind p)
        {
            switch (p)
            {
                case PerkKind.SwiftHands: return "faster reloads and weapon swaps";
                case PerkKind.IronHide: return "+50 max health";
                case PerkKind.FieldMedic: return "revive teammates twice as fast";
                default: return "faster sprint";
            }
        }

        public static Color ColorOf(PerkKind p)
        {
            switch (p)
            {
                case PerkKind.SwiftHands: return new Color(0.3f, 0.85f, 0.35f);
                case PerkKind.IronHide: return new Color(0.95f, 0.25f, 0.2f);
                case PerkKind.FieldMedic: return new Color(0.3f, 0.65f, 1f);
                default: return new Color(1f, 0.75f, 0.2f);
            }
        }

        public static int PriceOf(PerkKind p)
        {
            switch (p)
            {
                case PerkKind.SwiftHands: return 1200;
                case PerkKind.IronHide: return 1500;
                case PerkKind.FieldMedic: return 1000;
                default: return 1000;
            }
        }

        public static PerkMachine Create(Transform parent, int index, PerkKind perk, Vector3 floor, float yaw)
        {
            var root = new GameObject($"Perk Machine #{index} ({NameOf(perk)})");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(floor, Quaternion.Euler(0f, yaw, 0f));
            var m = root.AddComponent<PerkMachine>();
            m.Index = index;
            m.Perk = perk;
            Color tint = ColorOf(perk);
            Color body = Color.Lerp(tint, new Color(0.12f, 0.12f, 0.13f), 0.55f);
            Color trim = new Color(0.16f, 0.17f, 0.18f);
            Part(root.transform, new Vector3(0f, 0.95f, 0f), new Vector3(0.84f, 1.9f, 0.6f), body, true);                    // cabinet (solid)
            Part(root.transform, new Vector3(0f, 0.04f, 0.02f), new Vector3(0.9f, 0.08f, 0.64f), trim, false);               // plinth
            Part(root.transform, new Vector3(0f, 1.94f, 0.02f), new Vector3(0.9f, 0.1f, 0.64f), trim, false);                // cap
            // Lit sign across the top and a glass front with a glowing core behind it.
            Renderer sign = Part(root.transform, new Vector3(0f, 1.72f, 0.302f), new Vector3(0.7f, 0.2f, 0.01f), Color.white, false);
            sign.sharedMaterial = RuntimeMaterials.CreateEmissive(tint, 1.8f);
            Part(root.transform, new Vector3(0f, 1.15f, 0.301f), new Vector3(0.6f, 0.62f, 0.01f), new Color(0.05f, 0.06f, 0.07f), false);
            GameObject core = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            core.transform.SetParent(root.transform, false);
            core.transform.localPosition = new Vector3(0f, 1.15f, 0.27f);
            core.transform.localScale = new Vector3(0.18f, 0.22f, 0.18f);
            core.GetComponent<Renderer>().sharedMaterial = RuntimeMaterials.CreateEmissive(tint, 2.2f);
            Destroy(core.GetComponent<Collider>());
            // Coin slot and dispense tray.
            Part(root.transform, new Vector3(0.26f, 0.72f, 0.305f), new Vector3(0.08f, 0.14f, 0.02f), new Color(0.75f, 0.6f, 0.25f), false);
            Part(root.transform, new Vector3(0f, 0.42f, 0.31f), new Vector3(0.5f, 0.16f, 0.04f), trim, false);
            var lightGo = new GameObject("Glow");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 1.5f, 0.7f);
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 3.5f;
            light.intensity = 1f;
            light.color = Color.Lerp(tint, Color.white, 0.3f);
            light.shadows = LightShadows.None;
            InteractGlow.Add(root, InteractGlow.PowerUps, () => Running);
            return m;
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

        public string InteractPrompt(GameObject player)
        {
            if (!Running) return $"{NameOf(Perk)} (start a run to use)";
            if (PerkMachineDirector.LocalHas(Perk)) return $"{NameOf(Perk)} (owned)";
            return $"Buy {NameOf(Perk)}: {EffectOf(Perk)} ({PriceOf(Perk):N0} credits)";
        }

        public bool CanInteract(GameObject player) => Running && !PerkMachineDirector.LocalHas(Perk);

        public void Interact(GameObject player) => PerkMachineDirector.Current?.RequestPerk(Index);
    }
}
