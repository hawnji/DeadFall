using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Enemies
{
    /// <summary>
    /// The special attacks of M7 enemies: acid globs and pools (Retcher, Specimen) and blasts (Pyre). Damage only
    /// happens where enemies are simulated (offline or host); <see cref="EnemyFxRelay"/> replays the visuals on
    /// clients. Players are hurt through their ICombatTarget, the same path as melee strikes.
    /// </summary>
    public static class EnemyAttacks
    {
        public const float GlobSplashRadius = 1.3f;
        public const float ClusterRadius = 4f;

        /// <summary>Host: a visual effect to replay on clients (kind, a, b, radius, seconds).</summary>
        public static event System.Action<EnemyFxKind, Vector3, Vector3, float, float> FxPlayed;

        private static readonly List<GameObject> Live = new List<GameObject>(16);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Live.Clear();
            FxPlayed = null;
        }

        /// <summary>Lobs an acid glob from <paramref name="from"/> to land at <paramref name="to"/>.</summary>
        public static void Spit(Enemy source, Vector3 from, Vector3 to, EnemyStats s)
        {
            float flight = Mathf.Clamp(Vector3.Distance(from, to) / Mathf.Max(1f, s.ProjectileSpeed), 0.15f, 3f);
            Track(AcidGlob.Launch(from, to, flight, true, s.RangedDamage, s.PoolDamagePerSecond, s.PoolRadius, s.PoolSeconds,
                source != null ? source.Definition.ContentId : "enemy"));
            FxPlayed?.Invoke(EnemyFxKind.Glob, from, to, s.PoolDamagePerSecond > 0f ? s.PoolRadius : 0f, flight);
        }

        /// <summary>Called by a landed glob (host): splash damage and a pool.</summary>
        internal static void GlobLanded(Vector3 at, float damage, float poolDps, float poolRadius, float poolSeconds, string weaponId)
        {
            HurtTargets(at, GlobSplashRadius, damage, false, weaponId);
            if (poolDps > 0f && poolSeconds > 0f)
            {
                Track(AcidPool.Create(at, poolRadius, poolSeconds, true, poolDps, weaponId));
                FxPlayed?.Invoke(EnemyFxKind.Pool, at, at, poolRadius, poolSeconds);
            }
        }

        /// <summary>
        /// A Pyre bursting: hurts players (falloff, line of sight) and other infected. <paramref name="credit"/> is who
        /// gets kill credit for infected caught in it (the player who shot it, or the Pyre itself).
        /// </summary>
        public static void Blast(Enemy source, Vector3 center, float radius, float damage, GameObject credit)
        {
            string id = source != null ? source.Definition.ContentId : "enemy";
            Explosions.Detonate(center, radius, damage, credit, id, 0f, 4f);
            HurtTargets(center, radius, damage, true, id, source != null ? source.gameObject : null);
            ExplosionFx.Spawn(center, radius);
            FxPlayed?.Invoke(EnemyFxKind.Blast, center, center, radius, 0f);
        }

        /// <summary>Damages every combat target (players, decoys, structures) within <paramref name="radius"/>.</summary>
        public static void HurtTargets(Vector3 center, float radius, float damage, bool falloff, string weaponId, GameObject source = null)
        {
            IReadOnlyList<ICombatTarget> all = CombatTargets.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                ICombatTarget t = all[i];
                if (t == null || t.TargetTransform == null || t.Damageable == null || !t.Damageable.IsAlive) continue;
                Vector3 p = t.TargetTransform.position + Vector3.up;
                float d = Vector3.Distance(center, p);
                if (d > radius + 0.4f) continue;
                if (falloff && !Explosions.HasLineOfSight(center, p, null)) continue;
                float amount = (falloff ? Explosions.Falloff(damage, d, radius) : damage) * (1f - Mathf.Clamp01(t.Damageable.DamageReduction));
                if (amount <= 0f) continue;
                Vector3 dir = p - center;
                t.Damageable.ApplyDamage(new DamageInfo
                {
                    Amount = amount,
                    Zone = HitZone.Body,
                    Point = p,
                    Direction = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.up,
                    Source = source,
                    WeaponId = weaponId,
                });
            }
        }

        /// <summary>The target with the most other targets near it (Pyres go for clusters); ties go to the nearest.</summary>
        public static ICombatTarget BiggestCluster(Vector3 from, out float distance)
        {
            IReadOnlyList<ICombatTarget> all = CombatTargets.All;
            ICombatTarget best = null;
            int bestCount = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < all.Count; i++)
            {
                ICombatTarget t = all[i];
                if (t == null || !t.IsTargetable || t.TargetTransform == null) continue;
                Vector3 p = t.TargetTransform.position;
                int near = 0;
                for (int k = 0; k < all.Count; k++)
                {
                    ICombatTarget o = all[k];
                    if (k == i || o == null || !o.IsTargetable || o.TargetTransform == null) continue;
                    if ((o.TargetTransform.position - p).sqrMagnitude <= ClusterRadius * ClusterRadius) near++;
                }
                float dist = Vector3.Distance(from, p);
                if (near > bestCount || (near == bestCount && dist < bestDist))
                {
                    best = t;
                    bestCount = near;
                    bestDist = dist;
                }
            }
            distance = best != null ? bestDist : float.MaxValue;
            return best;
        }

        /// <summary>Client: replays a host effect without damage.</summary>
        public static void PlayRemote(EnemyFxKind kind, Vector3 a, Vector3 b, float radius, float seconds)
        {
            switch (kind)
            {
                case EnemyFxKind.Glob: Track(AcidGlob.Launch(a, b, Mathf.Max(0.1f, seconds), false, 0f, 0f, 0f, 0f, null)); break;
                case EnemyFxKind.Pool: Track(AcidPool.Create(a, radius, seconds, false, 0f, null)); break;
                case EnemyFxKind.Blast: ExplosionFx.Spawn(a, radius); break;
            }
        }

        /// <summary>Removes every glob and pool (run reset, map change).</summary>
        public static void ClearAll()
        {
            foreach (GameObject go in Live)
                if (go != null) Object.Destroy(go);
            Live.Clear();
        }

        private static void Track(GameObject go)
        {
            Live.RemoveAll(g => g == null);
            if (go != null) Live.Add(go);
        }
    }

    public enum EnemyFxKind : byte
    {
        Glob,
        Pool,
        Blast,
    }
}
