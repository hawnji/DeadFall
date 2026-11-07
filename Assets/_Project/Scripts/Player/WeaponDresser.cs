using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Player
{
    /// <summary>
    /// Fits attachment models (att_* from Tools/Blender/build_models.py) onto a weapon model, in the weapon's model space:
    /// optics on the rail, muzzle devices on the muzzle, underbarrel parts under the handguard, and replacement mags and
    /// stocks in place of the gun's own (its "Mag" / "Stock" child is hidden). Used by the first-person viewmodel and the
    /// Mod Bench preview, so both show the same thing. Falls back to simple boxes when a model is missing.
    /// </summary>
    public static class WeaponDresser
    {
        /// <summary>Where a gun takes its parts (model space, from the Blender builders).</summary>
        private struct Mounts
        {
            public Vector2 Under;      // (y, z) of the underbarrel clamp
            public Vector2 MagWell;    // (y, z) of the mag well (the "Mag" child's origin)
            public float Bore;         // barrel diameter (muzzle devices scale to it)
            public string ExtMag;      // extended mag model, or null for no visual change
            public float ExtScale;
            public string FastMag;     // fast mag model, or null
            public float FastScale;
        }

        private static Mounts For(WeaponDefinition def, float muzzleZ, float boreY)
        {
            switch (def != null ? def.ContentId : null)
            {
                case "weapon.halberd_ar": return M(-0.032f, 0.40f, -0.045f, 0.164f, 0.019f, "att_ext_mag_curved", 1f, "att_fast_mag", 1f);
                case "weapon.breacher_12": return M(-0.035f, 0.70f, 0f, 0f, 0.03f, null, 1f, null, 1f);
                case "weapon.ranger_9": return M(-0.022f, 0.165f, -0.12f, 0.009f, 0.018f, "att_ext_mag_stick", 0.6f, null, 1f);
                case "weapon.wasp_smg": return M(-0.028f, 0.26f, -0.04f, 0.1625f, 0.022f, "att_ext_mag_stick", 1f, "att_fast_mag", 0.9f);
                case "weapon.anvil_lmg": return M(-0.03f, 0.62f, -0.06f, 0.28f, 0.034f, "att_ext_drum", 1.1f, null, 1f);
                case "weapon.mule_launcher": return M(-0.015f, 0.42f, 0f, 0f, 0.08f, null, 1f, null, 1f);
                case "weapon.vesper_dmr": return M(-0.028f, 0.45f, -0.042f, 0.205f, 0.017f, "att_ext_mag_stick", 1f, "att_fast_mag", 0.85f);
                case "weapon.bulldog_44": return M(-0.008f, 0.17f, 0f, 0f, 0.02f, null, 1f, null, 1f);
                case "weapon.hailstorm": return M(-0.05f, 0.5f, -0.05f, 0.25f, 0.024f, "att_ext_drum", 1f, null, 1f);
                case "weapon.kestrel_pdw": return M(-0.04f, 0.32f, 0.05f, 0.06f, 0.018f, "att_ext_mag_top", 1f, null, 1f);
                default: return M(boreY - 0.035f, muzzleZ * 0.6f, -0.045f, 0.16f, 0.02f, null, 1f, null, 1f);
            }
        }

        private static Mounts M(float uy, float uz, float my, float mz, float bore, string ext, float extScale, string fast, float fastScale) =>
            new Mounts { Under = new Vector2(uy, uz), MagWell = new Vector2(my, mz), Bore = bore, ExtMag = ext, ExtScale = extScale, FastMag = fast, FastScale = fastScale };

        private static readonly Color Dark = new Color(0.09f, 0.09f, 0.1f), Metal = new Color(0.25f, 0.26f, 0.28f);
        private static readonly Color RedDot = new Color(1f, 0.15f, 0.1f), HoloGreen = new Color(0.3f, 1f, 0.45f);

        /// <summary>
        /// Fits <paramref name="mods"/>' attachments under <paramref name="root"/> (weapon model space). <paramref name="muzzle"/>
        /// is the bare muzzle point; returns how far muzzle devices push it forward. Parts get <paramref name="layer"/>.
        /// </summary>
        public static float Fit(Transform root, WeaponDefinition def, WeaponMods mods, Vector3 muzzle, int layer)
        {
            if (root == null || mods == null || def == null || def.Kind != WeaponKind.Firearm) return 0f;
            WeaponSights.Sight sight = WeaponSights.For(def);
            Mounts mt = For(def, muzzle.z, muzzle.y);
            float extend = 0f;

            AttachmentDef optic = WeaponSights.Optic(def, mods);
            if (optic != null)
            {
                float rail = sight.RailY, z = sight.RailZ, line = rail + WeaponSights.OpticLift(optic);
                switch (optic.Id)
                {
                    case "att.red_dot":
                        if (Spawn("att_red_dot", root, new Vector3(0f, rail, z), 1f, layer) == null)
                        {
                            Part(root, "RedDotHousing", new Vector3(0.03f, 0.006f, 0.04f), new Vector3(0f, line - 0.015f, z), Dark, layer);
                            Part(root, "RedDotTop", new Vector3(0.034f, 0.006f, 0.042f), new Vector3(0f, line + 0.018f, z), Dark, layer);
                        }
                        // The reticle: a glowing dot on the sight line, in the open window.
                        Glow(root, "RedDot", new Vector3(0.0035f, 0.0035f, 0.001f), new Vector3(0f, line, z + 0.012f), RedDot, layer);
                        break;
                    case "att.holo_scope":
                        if (Spawn("att_holo", root, new Vector3(0f, rail, z), 1f, layer) == null)
                            Part(root, "HoloBase", new Vector3(0.034f, 0.012f, 0.07f), new Vector3(0f, rail + 0.016f, z), Dark, layer);
                        // The reticle: a centre dot inside a broken ring of four short bars.
                        float rz = z + 0.03f;
                        Glow(root, "HoloDot", new Vector3(0.0025f, 0.0025f, 0.001f), new Vector3(0f, line, rz), HoloGreen, layer);
                        Glow(root, "HoloRingT", new Vector3(0.006f, 0.0012f, 0.001f), new Vector3(0f, line + 0.008f, rz), HoloGreen, layer);
                        Glow(root, "HoloRingB", new Vector3(0.006f, 0.0012f, 0.001f), new Vector3(0f, line - 0.008f, rz), HoloGreen, layer);
                        Glow(root, "HoloRingL", new Vector3(0.0012f, 0.006f, 0.001f), new Vector3(-0.008f, line, rz), HoloGreen, layer);
                        Glow(root, "HoloRingR", new Vector3(0.0012f, 0.006f, 0.001f), new Vector3(0.008f, line, rz), HoloGreen, layer);
                        break;
                    case "att.sniper_scope":
                        // Aimed in, the full-screen scope view takes over (WeaponViewmodel hides the gun).
                        if (Spawn("att_sniper_scope", root, new Vector3(0f, rail, z - 0.02f), 1f, layer) == null)
                            Part(root, "ScopeTube", new Vector3(0.03f, 0.03f, 0.26f), new Vector3(0f, line, z), Dark, layer);
                        break;
                }
            }

            foreach (AttachmentDef a in mods.Attachments)
            {
                float bore = Mathf.Max(0.8f, mt.Bore / 0.019f);
                Vector3 under = new Vector3(0f, mt.Under.x, mt.Under.y);
                Vector3 well = new Vector3(0f, mt.MagWell.x, mt.MagWell.y);
                switch (a.Id)
                {
                    case "att.compensator":
                        if (Spawn("att_compensator", root, muzzle, bore, layer) == null)
                            Part(root, "Compensator", new Vector3(0.026f, 0.026f, 0.06f), muzzle + new Vector3(0f, 0f, 0.03f), Dark, layer);
                        extend = 0.07f * bore;
                        break;
                    case "att.long_barrel":
                        if (Spawn("att_long_barrel", root, muzzle, bore, layer) == null)
                            Part(root, "LongBarrel", new Vector3(0.02f, 0.02f, 0.16f), muzzle + new Vector3(0f, 0f, 0.08f), Metal, layer);
                        extend = 0.165f * bore;
                        break;
                    case "att.foregrip":
                        if (Spawn("att_foregrip", root, under, 1f, layer) == null)
                            Part(root, "Foregrip", new Vector3(0.024f, 0.075f, 0.028f), under + new Vector3(0f, -0.045f, 0f), Dark, layer);
                        break;
                    case "att.laser":
                        if (Spawn("att_laser", root, under, 1f, layer) == null)
                            Part(root, "Laser", new Vector3(0.026f, 0.026f, 0.06f), under + new Vector3(0f, -0.02f, 0f), Dark, layer);
                        Glow(root, "LaserEmitter", new Vector3(0.008f, 0.008f, 0.002f), under + new Vector3(-0.006f, -0.016f, 0.037f), RedDot, layer);
                        break;
                    case "att.extended_mag":
                        if (mt.ExtMag != null && Spawn(mt.ExtMag, root, well, mt.ExtScale, layer) != null) HideOwn(root, "Mag");
                        break;
                    case "att.fast_mag":
                        if (mt.FastMag != null && Spawn(mt.FastMag, root, well, mt.FastScale, layer) != null) HideOwn(root, "Mag");
                        break;
                    case "att.light_stock":
                    case "att.heavy_stock":
                        if (Spawn(a.Id == "att.light_stock" ? "att_light_stock" : "att_heavy_stock", root, Vector3.zero, 1f, layer) != null) HideOwn(root, "Stock");
                        break;
                }
            }
            return extend;
        }

        /// <summary>Instantiates an attachment model at a mount point (model space), with shadows off.</summary>
        private static GameObject Spawn(string id, Transform root, Vector3 at, float scale, int layer)
        {
            GameObject go = ModelLibrary.Spawn(id, root);
            if (go == null) return null;
            go.name = "Att_" + id;
            go.transform.localPosition = at;
            go.transform.localScale = Vector3.one * scale;
            Prepare(go, layer);
            return go;
        }

        private static void Prepare(GameObject go, int layer)
        {
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        /// <summary>The weapon model's own part ("Mag", "Stock"; Blender may suffix ".001"), anywhere under root but not in a fitted attachment.</summary>
        private static List<Transform> Own(Transform root, string part)
        {
            var found = new List<Transform>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root || !t.name.StartsWith(part)) continue;
                if (t.parent != null && t.parent.name.StartsWith("Att_")) continue;
                found.Add(t);
            }
            return found;
        }

        private static void HideOwn(Transform root, string part)
        {
            foreach (Transform t in Own(root, part)) t.gameObject.SetActive(false);
        }

        private static void Part(Transform root, string partName, Vector3 size, Vector3 at, Color color, int layer)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = partName;
            Object.Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(root, false);
            part.transform.localScale = size;
            part.transform.localPosition = at;
            part.GetComponent<Renderer>().sharedMaterial = RuntimeMaterials.CreateLit(color);
            Prepare(part, layer);
        }

        private static void Glow(Transform root, string partName, Vector3 size, Vector3 at, Color color, int layer)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = partName;
            Object.Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(root, false);
            part.transform.localScale = size;
            part.transform.localPosition = at;
            part.GetComponent<Renderer>().sharedMaterial = RuntimeMaterials.CreateEmissive(color, 3f);
            Prepare(part, layer);
        }
    }
}
