using System.Collections.Generic;
using Deadfall.Core;
using Deadfall.Rules.Progression;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// Greybox FOB Halcyon (GAME_DESIGN §13), built from primitives: a hangar floor with the seven facility booths
    /// around the walls, each with a console (<see cref="FobTerminal"/>). Art replaces the booths later; the
    /// consoles and layout stay.
    /// </summary>
    public static class FobBuilder
    {
        public const float Width = 40f, Depth = 30f, WallHeight = 5.5f;

        public readonly struct Built
        {
            public readonly Transform Root;
            public readonly Vector3 Spawn;
            public readonly float SpawnYaw;
            public readonly List<FobTerminal> Terminals;
            public Built(Transform root, Vector3 spawn, float yaw, List<FobTerminal> terminals) { Root = root; Spawn = spawn; SpawnYaw = yaw; Terminals = terminals; }
        }

        public static Color ColorOf(FacilityId f) => f switch
        {
            FacilityId.CommandCenter => new Color(0.85f, 0.65f, 0.2f),
            FacilityId.Armory => new Color(0.75f, 0.3f, 0.25f),
            FacilityId.TrainingRange => new Color(0.35f, 0.7f, 0.35f),
            FacilityId.EngineeringWorkshop => new Color(0.95f, 0.55f, 0.15f),
            FacilityId.ResearchLab => new Color(0.3f, 0.6f, 0.9f),
            FacilityId.IntelligenceCenter => new Color(0.6f, 0.4f, 0.85f),
            _ => new Color(0.9f, 0.85f, 0.5f),
        };

        public static Built Build(Transform parent, Vector3 origin)
        {
            var root = new GameObject("[FOB Halcyon]").transform;
            root.SetParent(parent, false);
            root.position = origin;

            Color wall = new Color(0.28f, 0.3f, 0.32f);
            Box(root, "Floor", new Vector3(0f, -0.25f, 0f), new Vector3(Width, 0.5f, Depth), new Color(0.33f, 0.34f, 0.33f));
            Box(root, "WallN", new Vector3(0f, WallHeight * 0.5f, Depth * 0.5f), new Vector3(Width, WallHeight, 1f), wall);
            Box(root, "WallS", new Vector3(0f, WallHeight * 0.5f, -Depth * 0.5f), new Vector3(Width, WallHeight, 1f), wall);
            Box(root, "WallE", new Vector3(Width * 0.5f, WallHeight * 0.5f, 0f), new Vector3(1f, WallHeight, Depth), wall);
            Box(root, "WallW", new Vector3(-Width * 0.5f, WallHeight * 0.5f, 0f), new Vector3(1f, WallHeight, Depth), wall);
            Box(root, "Roof", new Vector3(0f, WallHeight + 0.25f, 0f), new Vector3(Width, 0.5f, Depth), new Color(0.2f, 0.21f, 0.22f));
            // Floor markings: the deployment lane to Command Center.
            Box(root, "Lane", new Vector3(0f, 0.01f, 2f), new Vector3(3f, 0.02f, Depth - 8f), new Color(0.7f, 0.6f, 0.2f), collider: false);
            // Some crates and a parked vehicle for scale and cover.
            ModelLibrary.Skin(Box(root, "Crates1", new Vector3(-6f, 0.6f, -6f), new Vector3(2.4f, 1.2f, 1.2f), new Color(0.35f, 0.4f, 0.28f)), "fob_crates");
            ModelLibrary.Skin(Box(root, "Crates2", new Vector3(6.5f, 0.9f, -7f), new Vector3(1.6f, 1.8f, 1.6f), new Color(0.35f, 0.4f, 0.28f)), "prop_crate");
            ModelLibrary.Skin(Box(root, "Transport", new Vector3(0f, 1.1f, -11f), new Vector3(3.2f, 2.2f, 6f), new Color(0.3f, 0.36f, 0.3f)), "fob_transport");

            Lights(root);

            var terminals = new List<FobTerminal>(7);
            float hx = Width * 0.5f - 3f, hz = Depth * 0.5f - 3f;
            terminals.Add(Booth(root, FacilityId.CommandCenter, new Vector3(0f, 0f, hz), 180f, wide: true));
            terminals.Add(Booth(root, FacilityId.Armory, new Vector3(-hx, 0f, 6f), 90f));
            terminals.Add(Booth(root, FacilityId.ResearchLab, new Vector3(-hx, 0f, -4f), 90f));
            terminals.Add(Booth(root, FacilityId.EngineeringWorkshop, new Vector3(hx, 0f, 6f), -90f));
            terminals.Add(Booth(root, FacilityId.IntelligenceCenter, new Vector3(hx, 0f, -4f), -90f));
            terminals.Add(Booth(root, FacilityId.TrainingRange, new Vector3(-11f, 0f, -hz), 0f));
            terminals.Add(Booth(root, FacilityId.TrophyRoom, new Vector3(11f, 0f, -hz), 0f));

            Vector3 spawn = origin + new Vector3(0f, 0.05f, -4f);
            return new Built(root, spawn, 0f, terminals);
        }

        private static FobTerminal Booth(Transform root, FacilityId facility, Vector3 pos, float yaw, bool wide = false)
        {
            var booth = new GameObject(Facilities.Name(facility)).transform;
            booth.SetParent(root, false);
            booth.localPosition = pos;
            booth.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Color c = ColorOf(facility);
            float w = wide ? 9f : 6f;
            // Back panel against the wall, a coloured header strip, side partitions, and the console desk.
            Box(booth, "Panel", new Vector3(0f, 2f, -2.2f), new Vector3(w, 4f, 0.3f), new Color(0.22f, 0.23f, 0.25f));
            Box(booth, "Header", new Vector3(0f, 4.2f, -2.1f), new Vector3(w, 0.5f, 0.4f), c, collider: false);
            Box(booth, "SideL", new Vector3(-w * 0.5f, 1.25f, -1f), new Vector3(0.2f, 2.5f, 2.6f), new Color(0.26f, 0.27f, 0.29f));
            Box(booth, "SideR", new Vector3(w * 0.5f, 1.25f, -1f), new Vector3(0.2f, 2.5f, 2.6f), new Color(0.26f, 0.27f, 0.29f));
            Box(booth, "Screen", new Vector3(0f, 2.2f, -2.0f), new Vector3(w * 0.5f, 1.4f, 0.05f), c * 0.6f, collider: false);
            GameObject desk = Box(booth, "Console", new Vector3(0f, 0.55f, 0f), new Vector3(wide ? 3.2f : 2f, 1.1f, 0.9f), new Color(0.3f, 0.32f, 0.35f));
            Box(booth, "Glow", new Vector3(0f, 1.12f, 0f), new Vector3(0.9f, 0.04f, 0.8f), c, collider: false);
            // Blender model (T-58): one piece for the whole booth; the boxes above keep their colliders.
            GameObject model = ModelLibrary.Spawn(wide ? "fob_booth_wide" : "fob_booth", booth, slot => ModelLibrary.SlotFromAccent(slot, c));
            if (model != null) ModelLibrary.HideExcept(booth, model.transform);
            var t = desk.AddComponent<FobTerminal>();
            t.Facility = facility;
            InteractGlow.Add(model != null ? model : desk, InteractGlow.Objectives);
            return t;
        }

        private static void Lights(Transform root)
        {
            for (int i = -1; i <= 1; i++)
            {
                var go = new GameObject("Lamp");
                go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3(i * 12f, WallHeight - 0.6f, 0f);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 20f;
                l.intensity = 1.6f;
                l.color = new Color(1f, 0.95f, 0.85f);
            }
        }

        public static GameObject Box(Transform parent, string name, Vector3 local, Vector3 size, Color color, bool collider = true)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = size;
            if (!collider) Object.Destroy(go.GetComponent<Collider>());
            go.isStatic = true;
            if (!SurfaceArt.ApplyAuto(go, color)) go.GetComponent<Renderer>().sharedMaterial = RuntimeMaterials.CreateLit(color);
            return go;
        }
    }
}
