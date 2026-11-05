using System;
using UnityEngine;

namespace Deadfall.Combat
{
    /// <summary>
    /// A hook for squad-wide damage effects (Squad Doctrines) that sit above the combat layer. Whoever applies damage
    /// on the authority (enemies, players and their host-side proxies) scales the amount by <see cref="For"/>.
    /// </summary>
    public static class DamageModifiers
    {
        /// <summary>(hit, the GameObject being hit) to a damage multiplier. Set by the class layer.</summary>
        public static Func<DamageInfo, GameObject, float> Scale;

        public static float For(in DamageInfo info, GameObject target)
        {
            if (Scale == null || target == null) return 1f;
            float m = Scale(info, target);
            return float.IsNaN(m) ? 1f : Mathf.Max(0f, m);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Scale = null;
    }
}
