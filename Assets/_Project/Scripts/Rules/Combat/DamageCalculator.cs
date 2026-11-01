using System;

namespace Deadfall.Rules.Combat
{
    public readonly struct DamageRequest
    {
        public readonly float BaseDamage;
        public readonly HitZone Zone;
        public readonly float Distance;
        /// <summary>Multiplier for HitZone.Head (from the weapon).</summary>
        public readonly float HeadMultiplier;
        public readonly float LimbMultiplier;
        /// <summary>Multiplier for HitZone.WeakPoint (from the target's hurtbox).</summary>
        public readonly float WeakPointMultiplier;
        public readonly float EffectiveRange;
        public readonly float MaxRange;
        public readonly float MinRangeMultiplier;
        /// <summary>Attacker-side multiplier (class passives, Field Mods, events).</summary>
        public readonly float OutgoingMultiplier;
        /// <summary>Target-side reduction 0..1 (armor, damage resistance).</summary>
        public readonly float TargetReduction;

        public DamageRequest(float baseDamage, HitZone zone, float distance, float headMultiplier, float limbMultiplier,
            float weakPointMultiplier, float effectiveRange, float maxRange, float minRangeMultiplier,
            float outgoingMultiplier = 1f, float targetReduction = 0f)
        {
            BaseDamage = baseDamage;
            Zone = zone;
            Distance = distance;
            HeadMultiplier = headMultiplier;
            LimbMultiplier = limbMultiplier;
            WeakPointMultiplier = weakPointMultiplier;
            EffectiveRange = effectiveRange;
            MaxRange = maxRange;
            MinRangeMultiplier = minRangeMultiplier;
            OutgoingMultiplier = outgoingMultiplier;
            TargetReduction = targetReduction;
        }

        public static DamageRequest FromWeapon(WeaponStats w, HitZone zone, float distance, float weakPointMultiplier = 1f,
            float outgoingMultiplier = 1f, float targetReduction = 0f)
            => new DamageRequest(w.Damage, zone, distance, w.HeadMultiplier, w.LimbMultiplier, weakPointMultiplier,
                w.EffectiveRange, w.MaxRange, w.MinRangeMultiplier, outgoingMultiplier, targetReduction);

        public static DamageRequest FromMelee(MeleeStats m, HitZone zone, float weakPointMultiplier = 1f,
            float outgoingMultiplier = 1f, float targetReduction = 0f)
            => new DamageRequest(m.Damage, zone, 0f, m.HeadMultiplier, 1f, weakPointMultiplier,
                m.Range, m.Range, 1f, outgoingMultiplier, targetReduction);
    }

    public readonly struct DamageResult
    {
        public readonly float Amount;
        /// <summary>True for head and weak-point hits; drives the critical hit marker.</summary>
        public readonly bool IsCritical;

        public DamageResult(float amount, bool isCritical)
        {
            Amount = amount;
            IsCritical = isCritical;
        }
    }

    /// <summary>The single damage formula used by every weapon, ability, and structure.</summary>
    public static class DamageCalculator
    {
        public static float RangeMultiplier(float distance, float effectiveRange, float maxRange, float minMultiplier)
        {
            if (distance <= effectiveRange) return 1f;
            if (distance >= maxRange || maxRange <= effectiveRange) return minMultiplier;
            float t = (distance - effectiveRange) / (maxRange - effectiveRange);
            return 1f + (minMultiplier - 1f) * t;
        }

        public static float ZoneMultiplier(in DamageRequest r)
        {
            switch (r.Zone)
            {
                case HitZone.Head: return r.HeadMultiplier;
                case HitZone.Limb: return r.LimbMultiplier;
                case HitZone.WeakPoint: return r.WeakPointMultiplier;
                default: return 1f;
            }
        }

        public static DamageResult Compute(in DamageRequest r)
        {
            float amount = r.BaseDamage
                           * RangeMultiplier(r.Distance, r.EffectiveRange, r.MaxRange, r.MinRangeMultiplier)
                           * ZoneMultiplier(r)
                           * Math.Max(0f, r.OutgoingMultiplier)
                           * (1f - Math.Min(1f, Math.Max(0f, r.TargetReduction)));
            bool crit = r.Zone == HitZone.Head || r.Zone == HitZone.WeakPoint;
            return new DamageResult(Math.Max(0f, amount), crit);
        }
    }
}
