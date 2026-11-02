using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Player;

namespace Deadfall.Game
{
    /// <summary>
    /// Starting loadout per class (D-07 slot layout: two primaries, sidearm, melee), given at the start of every
    /// deployment. Placeholder until the FOB loadout screen (M10) lets players choose.
    /// </summary>
    public static class RunLoadouts
    {
        public static List<PlayerWeapons.LoadoutEntry> For(string classId)
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
