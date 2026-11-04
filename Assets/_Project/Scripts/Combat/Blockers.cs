using System.Collections.Generic;
using UnityEngine;

namespace Deadfall.Combat
{
    /// <summary>
    /// Something that blocks enemy paths (barricades). Not an ordinary combat target: enemies only go for it when
    /// they can't reach a player, so a wall of barricades never makes the squad unreachable (GAME_DESIGN §11).
    /// </summary>
    public interface IBlocker : ICombatTarget
    {
        /// <summary>Distance from <paramref name="point"/> to the blocker's surface (melee checks use this).</summary>
        float DistanceFrom(Vector3 point);
        /// <summary>Whether an enemy standing at <paramref name="point"/> is on the side it should break in from.</summary>
        bool BreakableFrom(Vector3 point);
    }

    public static class Blockers
    {
        private static readonly List<IBlocker> All = new List<IBlocker>(8);

        public static int Count => All.Count;

        public static void Register(IBlocker b)
        {
            if (b != null && !All.Contains(b)) All.Add(b);
        }

        public static void Unregister(IBlocker b) => All.Remove(b);

        /// <summary>The nearest standing blocker within <paramref name="maxDistance"/>, or null.</summary>
        public static IBlocker Nearest(Vector3 from, float maxDistance) => Nearest(from, maxDistance, null);

        /// <summary>The nearest standing blocker within <paramref name="maxDistance"/> that <paramref name="accept"/> allows, or null.</summary>
        public static IBlocker Nearest(Vector3 from, float maxDistance, System.Predicate<IBlocker> accept)
        {
            IBlocker best = null;
            float bestDist = maxDistance;
            for (int i = 0; i < All.Count; i++)
            {
                IBlocker b = All[i];
                if (b == null || !b.IsTargetable) continue;
                float d = b.DistanceFrom(from);
                if (d < bestDist && (accept == null || accept(b)))
                {
                    bestDist = d;
                    best = b;
                }
            }
            return best;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => All.Clear();
    }
}
