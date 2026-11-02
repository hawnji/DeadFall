using System.Collections.Generic;
using Deadfall.Core;
using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Combat
{
    /// <summary>Raised wherever an explosion goes off (visuals, audio, network relay).</summary>
    public struct ExplosionHappened
    {
        public Vector3 Position;
        public float Radius;
        public GameObject Source;
    }

    /// <summary>
    /// Area damage shared by every explosive (Cluster Charge now; grenades, launchers and Pyres later). Call it only
    /// where damage is authoritative (offline or on the host). Damage falls off from full at the centre to 30% at
    /// the edge. Other players are never hurt (PvE); the thrower takes <c>selfDamageMultiplier</c> of the damage.
    /// </summary>
    public static class Explosions
    {
        public const float EdgeDamageFraction = 0.3f;
        private static readonly Collider[] Overlap = new Collider[64];
        private static readonly HashSet<IDamageable> Seen = new HashSet<IDamageable>();

        /// <summary>Returns how many targets took damage.</summary>
        public static int Detonate(Vector3 center, float radius, float damage, GameObject source, string weaponId,
            float selfDamageMultiplier = 1f, float knockback = 0f)
        {
            Seen.Clear();
            int hits = 0;
            int count = Physics.OverlapSphereNonAlloc(center, radius, Overlap, CombatQueries.HitMask, QueryTriggerInteraction.Ignore);
            PlayerIdentity attacker = PlayerIdentity.Of(source);
            for (int i = 0; i < count; i++)
            {
                if (!Hurtbox.TryResolve(Overlap[i], out IDamageable target, out _, out _)) continue;
                if (target == null || !target.IsAlive || !Seen.Add(target)) continue;
                if (target is Component c && PlayerIdentity.Of(c.gameObject) != null) continue; // players handled below
                Vector3 point = Overlap[i].ClosestPoint(center);
                if (Apply(target, center, point, radius, damage, source, weaponId, knockback, attacker)) hits++;
            }

            // The thrower is on the Ignore Raycast layer, so check them directly.
            if (selfDamageMultiplier > 0f && source != null && source.TryGetComponent(out IDamageable self) && self.IsAlive && Seen.Add(self))
            {
                Vector3 p = source.transform.position + Vector3.up;
                if ((p - center).sqrMagnitude <= radius * radius)
                    Apply(self, center, p, radius, damage * selfDamageMultiplier, source, weaponId, 0f, null);
            }

            EventBus<ExplosionHappened>.Publish(new ExplosionHappened { Position = center, Radius = radius, Source = source });
            return hits;
        }

        /// <summary>Damage at <paramref name="distance"/> from the centre.</summary>
        public static float Falloff(float damage, float distance, float radius) =>
            radius <= 0f ? damage : damage * Mathf.Lerp(1f, EdgeDamageFraction, Mathf.Clamp01(distance / radius));

        private static bool Apply(IDamageable target, Vector3 center, Vector3 point, float radius, float damage, GameObject source,
            string weaponId, float knockback, PlayerIdentity attacker)
        {
            float amount = Falloff(damage, Vector3.Distance(center, point), radius) * (1f - Mathf.Clamp01(target.DamageReduction));
            Vector3 dir = point - center;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.up;
            float applied = target.ApplyDamage(new DamageInfo
            {
                Amount = amount,
                Zone = HitZone.Body,
                Point = point,
                Direction = dir,
                Source = source,
                WeaponId = weaponId,
                Knockback = knockback,
            });
            if (applied <= 0f) return false;
            bool killed = !target.IsAlive;
            if (killed && target is Component c)
                EventBus<DamageableKilled>.Publish(new DamageableKilled { Victim = c.gameObject, Killer = source });
            if (attacker != null && attacker.IsLocal)
                EventBus<HitConfirmed>.Publish(new HitConfirmed { Attacker = source, Amount = applied, Killed = killed, Point = point });
            return true;
        }
    }
}
