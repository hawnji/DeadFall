using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Player;

namespace Deadfall.Game
{
    /// <summary>
    /// Starting loadout per class (D-07 slot layout: two primaries, sidearm, melee), given at the start of every
    /// deployment. The Armory (M10) saves a custom one per class in the profile; an illegal or missing one falls
    /// back to the class default below.
    /// </summary>
    public static class RunLoadouts
    {
        public static List<PlayerWeapons.LoadoutEntry> For(string classId)
        {
            // Gauntlet: one fixed loadout for everyone, whatever the class or Armory picks.
            if (MapSession.Current != null && Deadfall.Rules.World.EnvironmentCatalog.IsGauntlet(MapSession.Current.Seed)) return Gauntlet();
            List<PlayerWeapons.LoadoutEntry> def = Default(classId);
            ProfileService profile = ProfileService.Current;
            if (profile == null) return def;
            string[] ids = profile.LoadoutFor(classId, DefaultIds(classId));
            var list = new List<PlayerWeapons.LoadoutEntry>(4);
            foreach (string id in ids)
            {
                WeaponDefinition w = WeaponCatalog.ById(id);
                if (w == null) return def;
                list.Add(new PlayerWeapons.LoadoutEntry(w, ArmoryMods(profile, w)));
            }
            return list;
        }

        /// <summary>The Gauntlet's fixed loadout: Halberd AR with a Red Dot, Breacher 12, Ranger 9, Saber-7.</summary>
        public static List<PlayerWeapons.LoadoutEntry> Gauntlet()
        {
            WeaponDefinition ar = WeaponCatalog.HalberdAR;
            var list = new List<PlayerWeapons.LoadoutEntry>(4)
            {
                new PlayerWeapons.LoadoutEntry(ar, ar != null ? Deadfall.Rules.Combat.ArmoryAttachments.Build(new[] { "att.red_dot" }, ar.ContentId, ar.Category) : null),
            };
            Add(list, WeaponCatalog.Breacher12, WeaponCatalog.Ranger9, WeaponCatalog.Saber7);
            return list;
        }

        /// <summary>
        /// Attachments from the Armory. Always none now: attachments are run-only add-ons (loot, Gun Dispenser,
        /// Mod Bench), so every run starts with bare guns. Attachments saved by older builds are ignored.
        /// </summary>
        public static Deadfall.Rules.Combat.WeaponMods ArmoryMods(ProfileService profile, WeaponDefinition w) => null;

        /// <summary>Content ids of the class default, in slot order.</summary>
        public static string[] DefaultIds(string classId)
        {
            List<PlayerWeapons.LoadoutEntry> def = Default(classId);
            var ids = new string[def.Count];
            for (int i = 0; i < def.Count; i++) ids[i] = def[i].Definition.ContentId;
            return ids;
        }

        public static List<PlayerWeapons.LoadoutEntry> Default(string classId)
        {
            var list = new List<PlayerWeapons.LoadoutEntry>(4);
            switch (classId)
            {
                case "class.tank":
                    Add(list, WeaponCatalog.Breacher12, WeaponCatalog.AnvilLmg, WeaponCatalog.Ranger9, WeaponCatalog.TetherCleaver);
                    break;
                case "class.warrior":
                    Add(list, WeaponCatalog.WaspSmg, WeaponCatalog.Breacher12, WeaponCatalog.Ranger9, WeaponCatalog.Saber7);
                    break;
                case "class.engineer":
                    Add(list, WeaponCatalog.WaspSmg, WeaponCatalog.HalberdAR, WeaponCatalog.Ranger9, WeaponCatalog.Saber7);
                    break;
                case "class.demolitionist":
                    Add(list, WeaponCatalog.Breacher12, WeaponCatalog.MuleLauncher, WeaponCatalog.Ranger9, WeaponCatalog.TetherCleaver);
                    break;
                default: // Gunner and anyone without a class
                    Add(list, WeaponCatalog.HalberdAR, WeaponCatalog.WaspSmg, WeaponCatalog.Ranger9, WeaponCatalog.Saber7);
                    break;
            }
            return list;
        }

        private static void Add(List<PlayerWeapons.LoadoutEntry> list, params WeaponDefinition[] defs)
        {
            foreach (WeaponDefinition d in defs) list.Add(new PlayerWeapons.LoadoutEntry(d));
        }
    }
}
