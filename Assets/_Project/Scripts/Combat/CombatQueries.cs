using System.Collections.Generic;
using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Combat
{
    /// <summary>Allocation-free physics queries shared by hitscan weapons, melee, and (later) abilities and turrets.</summary>
    public static class CombatQueries
    {
        private static readonly RaycastHit[] RayHits = new RaycastHit[16];
        private static readonly Collider[] OverlapHits = new Collider[32];
        private static readonly List<IDamageable> MeleeSeen = new List<IDamageable>(8);
        private static readonly RaycastHitDistanceComparer DistanceComparer = new RaycastHitDistanceComparer();

        /// <summary>Layers bullets and melee interact with. Players live on Ignore Raycast so their own capsule never blocks.</summary>
        public static int HitMask = Physics.DefaultRaycastLayers;

        /// <summary>Nearest solid hit along a ray, ignoring the attacker's own colliders.</summary>
        public static bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, Transform ignoreRoot, out RaycastHit hit)
        {
            int count = Physics.RaycastNonAlloc(origin, direction, RayHits, maxDistance, HitMask, QueryTriggerInteraction.Ignore);
            if (count > 1) System.Array.Sort(RayHits, 0, count, DistanceComparer);
            for (int i = 0; i < count; i++)
            {
                if (ignoreRoot != null && RayHits[i].transform.IsChildOf(ignoreRoot)) continue;
                hit = RayHits[i];
                return true;
            }
            hit = default;
            return false;
        }

        /// <summary>
        /// Finds damageables inside a forward arc with clear line of sight, nearest first, one entry per target.
        /// Results are written to <paramref name="results"/> (cleared first).
        /// </summary>
        public static void MeleeArc(Vector3 origin, Vector3 forward, MeleeStats stats, Transform ignoreRoot, List<MeleeTarget> results)
        {
            results.Clear();
            MeleeSeen.Clear();
            int count = Physics.OverlapSphereNonAlloc(origin, stats.Range, OverlapHits, HitMask, QueryTriggerInteraction.Ignore);
            float halfArc = stats.ArcDegrees * 0.5f;
            for (int i = 0; i < count; i++)
            {
                Collider col = OverlapHits[i];
                if (ignoreRoot != null && col.transform.IsChildOf(ignoreRoot)) continue;
                if (!Hurtbox.TryResolve(col, out IDamageable target, out HitZone zone, out float weak)) continue;
                if (!target.IsAlive) continue;

                Vector3 point = col.ClosestPoint(origin);
                Vector3 to = point - origin;
                float dist = to.magnitude;
                if (dist > 0.05f && Vector3.Angle(forward, to) > halfArc) continue;

                // Line of sight: nothing solid between us and the target.
                if (dist > 0.05f && Raycast(origin, to / dist, dist - 0.01f, ignoreRoot, out RaycastHit block) && block.collider != col
                    && !BelongsTo(block.collider, target)) continue;

                int existing = MeleeSeen.IndexOf(target);
                var entry = new MeleeTarget { Target = target, Zone = zone, WeakPointMultiplier = weak, Point = point, Distance = dist };
                if (existing >= 0)
                {
                    // Keep the most valuable zone for this target (head beats body).
                    if (ZoneRank(zone) > ZoneRank(results[existing].Zone)) results[existing] = entry;
                    continue;
                }
                MeleeSeen.Add(target);
                results.Add(entry);
            }
            results.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            if (results.Count > stats.MaxTargets) results.RemoveRange(stats.MaxTargets, results.Count - stats.MaxTargets);
        }

        private static bool BelongsTo(Collider collider, IDamageable target)
            => Hurtbox.TryResolve(collider, out IDamageable owner, out _, out _) && ReferenceEquals(owner, target);

        private static int ZoneRank(HitZone z) => z == HitZone.WeakPoint ? 3 : z == HitZone.Head ? 2 : z == HitZone.Body ? 1 : 0;

        private sealed class RaycastHitDistanceComparer : IComparer<RaycastHit>
        {
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }
    }

    public struct MeleeTarget
    {
        public IDamageable Target;
        public HitZone Zone;
        public float WeakPointMultiplier;
        public Vector3 Point;
        public float Distance;
    }
}
