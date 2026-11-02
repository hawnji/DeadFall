using Deadfall.Combat;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>Shared target picking for sentries and drones: nearest living hostile in range with a clear line of fire.</summary>
    public static class EngineeringTargeting
    {
        public static IHostile Nearest(Vector3 from, float range, Transform ignore)
        {
            IHostile best = null;
            float bestSq = range * range;
            var all = Hostiles.All;
            for (int i = 0; i < all.Count; i++)
            {
                IHostile h = all[i];
                if (h == null || !h.IsAlive) continue;
                Vector3 to = h.AimPoint - from;
                float sq = to.sqrMagnitude;
                if (sq >= bestSq) continue;
                float dist = Mathf.Sqrt(sq);
                if (CombatQueries.Raycast(from, to / Mathf.Max(0.001f, dist), dist, ignore, out RaycastHit hit)
                    && hit.distance < dist - 0.8f && !Hurtbox.TryResolve(hit.collider, out _, out _, out _)) continue;
                bestSq = sq;
                best = h;
            }
            return best;
        }
    }
}
