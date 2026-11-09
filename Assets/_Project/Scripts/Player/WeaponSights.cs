using Deadfall.Combat;
using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Player
{
    /// <summary>
    /// Where each weapon model's sights are (model space, from Tools/Blender/build_models.py), so aiming down sights
    /// puts the sight line exactly on the screen centre: the aim point the shots go to, and the HUD's aiming dot.
    /// The models have no sights of their own; fitted optics (Red Dot, Holo Scope, Sniper Scope) sit on the receiver and raise the sight line to their own.
    /// </summary>
    public static class WeaponSights
    {
        public struct Sight
        {
            /// <summary>Height of the iron or built-in sight line.</summary>
            public float LineY;
            /// <summary>Top of the receiver, where an optic mounts, and how far along the barrel it sits.</summary>
            public float RailY, RailZ;
            /// <summary>The model already has an optic (scope, holo, carry handle): no optic attachments.</summary>
            public bool BuiltInOptic;
            /// <summary>The built-in optic is a magnified scope (scope view when aimed).</summary>
            public bool BuiltInScope;
        }

        public static Sight For(WeaponDefinition def)
        {
            string id = def != null ? def.ContentId : null;
            switch (id)
            {
                // The models carry no sights, optics or muzzle devices (they're attachments now): the unaided line
                // runs just over the top of each gun, so nothing on the model rises into the view when aiming.
                case "weapon.halberd_ar": return new Sight { LineY = 0.07f, RailY = 0.052f, RailZ = 0.15f };
                case "weapon.breacher_12": return new Sight { LineY = 0.051f, RailY = 0.046f, RailZ = 0.14f };
                case "weapon.ranger_9": return new Sight { LineY = 0.05f, RailY = 0.044f, RailZ = 0.09f };
                case "weapon.wasp_smg": return new Sight { LineY = 0.058f, RailY = 0.052f, RailZ = 0.115f };
                case "weapon.anvil_lmg": return new Sight { LineY = 0.095f, RailY = 0.087f, RailZ = 0.2f };
                case "weapon.mule_launcher": return new Sight { LineY = 0.115f, RailY = 0.105f, RailZ = 0.12f };
                case "weapon.vesper_dmr": return new Sight { LineY = 0.074f, RailY = 0.052f, RailZ = 0.15f }; // clears the stock's cheek riser (top 0.066)
                case "weapon.bulldog_44": return new Sight { LineY = 0.054f, RailY = 0.047f, RailZ = 0.11f };
                case "weapon.hailstorm": return new Sight { LineY = 0.058f, RailY = 0.05f, RailZ = 0.18f };
                case "weapon.kestrel_pdw": return new Sight { LineY = 0.072f, RailY = 0.065f, RailZ = 0.12f };
                default: return new Sight { LineY = 0.06f, RailY = 0.05f, RailZ = 0.12f };
            }
        }

        public static bool CanFitOptic(WeaponDefinition def) => def != null && def.Kind == WeaponKind.Firearm && !For(def).BuiltInOptic;

        /// <summary>The fitted optic, if any (ignored on weapons with a built-in one).</summary>
        public static AttachmentDef Optic(WeaponDefinition def, WeaponMods mods)
        {
            if (mods == null || !CanFitOptic(def)) return null;
            foreach (AttachmentDef a in mods.Attachments) if (a.Slot == AttachmentSlot.Optic) return a;
            return null;
        }

        /// <summary>Height above the rail of each optic's sight line.</summary>
        public static float OpticLift(AttachmentDef optic) => optic == null ? 0f : optic.Id == "att.sniper_scope" ? 0.048f : optic.Id == "att.holo_scope" ? 0.036f : 0.03f;

        /// <summary>The sight line actually used when aiming.</summary>
        public static float LineY(WeaponDefinition def, WeaponMods mods)
        {
            Sight s = For(def);
            AttachmentDef optic = Optic(def, mods);
            return optic != null ? s.RailY + OpticLift(optic) : s.LineY;
        }

        /// <summary>Aiming shows the full-screen scope (magnified, model hidden).</summary>
        public static bool Scoped(WeaponDefinition def, WeaponMods mods)
        {
            if (def == null || def.Kind != WeaponKind.Firearm) return false;
            if (For(def).BuiltInScope) return true;
            AttachmentDef optic = Optic(def, mods);
            return optic != null && optic.Id == "att.sniper_scope";
        }

        /// <summary>Zoom while aiming: scopes magnify a lot, the holo a little, everything else uses the weapon's own value.</summary>
        public static float AimFov(WeaponDefinition def, WeaponMods mods, float weaponMultiplier)
        {
            if (Scoped(def, mods)) return 0.3f;
            AttachmentDef optic = Optic(def, mods);
            if (optic != null && optic.Id == "att.holo_scope") return Mathf.Min(weaponMultiplier, 0.72f);
            if (optic != null && optic.Id == "att.red_dot") return Mathf.Min(weaponMultiplier, 0.85f);
            return weaponMultiplier;
        }
    }
}
