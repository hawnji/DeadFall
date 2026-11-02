using System.Collections.Generic;
using Deadfall.Combat;

namespace Deadfall.DebugTools
{
    /// <summary>Default sandbox loadout (D-07: two primaries, a sidearm and a melee weapon) from the shared <see cref="WeaponCatalog"/>.</summary>
    public static class SandboxContent
    {
        public static List<WeaponDefinition> DefaultLoadout() => new List<WeaponDefinition>
        {
            WeaponCatalog.HalberdAR,
            WeaponCatalog.Breacher12,
            WeaponCatalog.Ranger9,
            WeaponCatalog.Saber7,
        };
    }
}
