using System;
using Deadfall.Rules.Common;

namespace Deadfall.Rules.Combat
{
    /// <summary>Mutation traits a regular enemy can roll (GAME_DESIGN §7 "Variant"). Sent as one byte per enemy.</summary>
    [Flags]
    public enum EnemyTraits : byte
    {
        None = 0,
        /// <summary>Heals 4% of max health per second after 3 s without taking damage. Tell: pulsing green.</summary>
        Regenerating = 1,
        /// <summary>Bursts into two Creepers when killed. Tell: swollen, lumpy back.</summary>
        Splitting = 2,
        /// <summary>+25% damage reduction (cap 60%) and +20% health. Tell: grey, crusted skin.</summary>
        Hardened = 4,
        /// <summary>+35% move speed and 20% faster wind-ups. Tell: red streaks.</summary>
        Swift = 8,
    }

    /// <summary>How the planner and director treat an enemy type.</summary>
    public enum EnemyRole : byte
    {
        /// <summary>Bought with the surge's threat budget.</summary>
        Regular,
        /// <summary>Elite (Alpha, Goliath): bought with the budget but rare; guaranteed in Elite surges.</summary>
        Elite,
        /// <summary>A pursuer placed only by Hunt surges (Stalker).</summary>
        Hunter,
        /// <summary>A Specimen placed only by Specimen surges (Specimen Zero).</summary>
        Boss,
    }

    /// <summary>Rolling and applying mutation traits. Deterministic given the planner's random stream.</summary>
    public static class VariantRules
    {
        public const int FirstVariantSurge = 5;
        public const float ChancePerSurge = 0.03f;
        public const float MaxChance = 0.35f;
        /// <summary>From this surge a variant may roll a second trait.</summary>
        public const int SecondTraitSurge = 12;
        public const float SecondTraitChance = 0.25f;
        public const float RegenFraction = 0.04f;
        public const float RegenDelaySeconds = 3f;
        public const float HardenedReduction = 0.25f;
        public const float HardenedHealth = 1.2f;
        public const float MaxReduction = 0.6f;
        public const float SwiftSpeed = 1.35f;
        public const float SwiftWindup = 0.8f;
        public const int SplitCount = 2;

        private static readonly EnemyTraits[] Pool = { EnemyTraits.Regenerating, EnemyTraits.Splitting, EnemyTraits.Hardened, EnemyTraits.Swift };

        /// <summary>Chance that one regular enemy of this surge is a variant (before surge-type multipliers).</summary>
        public static float Chance(int surge) =>
            surge < FirstVariantSurge ? 0f : Math.Min(MaxChance, ChancePerSurge * (surge - FirstVariantSurge + 1));

        /// <summary>Rolls traits for one enemy. Hunters and bosses never mutate; <paramref name="canSplit"/> false for Creepers.</summary>
        public static EnemyTraits Roll(DeterministicRandom rng, int surge, EnemyRole role, bool canSplit, float chanceMultiplier = 1f)
        {
            if (rng == null || role == EnemyRole.Hunter || role == EnemyRole.Boss) return EnemyTraits.None;
            float chance = Math.Min(0.9f, Chance(surge) * Math.Max(0f, chanceMultiplier));
            if (chance <= 0f || rng.NextFloat() >= chance) return EnemyTraits.None;
            EnemyTraits t = Pick(rng, EnemyTraits.None, canSplit);
            if (surge >= SecondTraitSurge && rng.NextFloat() < SecondTraitChance) t |= Pick(rng, t, canSplit);
            return t;
        }

        private static EnemyTraits Pick(DeterministicRandom rng, EnemyTraits exclude, bool canSplit)
        {
            for (int tries = 0; tries < 8; tries++)
            {
                EnemyTraits t = Pool[rng.Range(0, Pool.Length)];
                if ((exclude & t) != 0 || (!canSplit && t == EnemyTraits.Splitting)) continue;
                return t;
            }
            return (exclude & EnemyTraits.Hardened) == 0 ? EnemyTraits.Hardened : EnemyTraits.None;
        }

        /// <summary>Changes <paramref name="stats"/> (a per-enemy copy) for the traits.</summary>
        public static void Apply(EnemyStats stats, EnemyTraits traits)
        {
            if (stats == null || traits == EnemyTraits.None) return;
            if ((traits & EnemyTraits.Hardened) != 0)
            {
                stats.DamageReduction = Math.Min(MaxReduction, stats.DamageReduction + HardenedReduction);
                stats.MaxHealth *= HardenedHealth;
            }
            if ((traits & EnemyTraits.Swift) != 0)
            {
                stats.ChaseSpeed *= SwiftSpeed;
                stats.AttackWindupSeconds *= SwiftWindup;
                stats.ChargeWindupSeconds *= SwiftWindup;
                stats.RangedWindupSeconds *= SwiftWindup;
            }
            if ((traits & EnemyTraits.Regenerating) != 0) stats.RegenPerSecond = stats.MaxHealth * RegenFraction;
        }

        /// <summary>Kill credits are worth more for mutated enemies: +50% per trait.</summary>
        public static float CreditMultiplier(EnemyTraits traits)
        {
            int n = 0;
            for (int b = 0; b < 8; b++) if (((int)traits & (1 << b)) != 0) n++;
            return 1f + 0.5f * n;
        }

        public static string Describe(EnemyTraits traits)
        {
            if (traits == EnemyTraits.None) return string.Empty;
            var parts = new System.Collections.Generic.List<string>(2);
            if ((traits & EnemyTraits.Regenerating) != 0) parts.Add("Regenerating");
            if ((traits & EnemyTraits.Splitting) != 0) parts.Add("Splitting");
            if ((traits & EnemyTraits.Hardened) != 0) parts.Add("Hardened");
            if ((traits & EnemyTraits.Swift) != 0) parts.Add("Swift");
            return string.Join(" ", parts);
        }
    }

    public enum SpecimenPhase : byte
    {
        /// <summary>Stalks the squad; armored body, only the head is a weak point.</summary>
        Stalk,
        /// <summary>Charges and summons infected; back growths open as weak points.</summary>
        Rampage,
        /// <summary>Slow, spits acid; the chest core is exposed and the plates are gone.</summary>
        ExposedCore,
    }

    /// <summary>Run modifiers rolled for a Specimen in late surges.</summary>
    [Flags]
    public enum SpecimenModifiers : byte
    {
        None = 0,
        /// <summary>+20% speed, +25% damage.</summary>
        Enraged = 1,
        /// <summary>+20% damage reduction.</summary>
        Armored = 2,
        /// <summary>Summons 50% more infected.</summary>
        Brood = 4,
    }

    /// <summary>The Specimen (boss) framework: phases by health, per-phase stats and weak points, summons.</summary>
    public static class SpecimenRules
    {
        public const float RampageAt = 0.66f;
        public const float ExposedAt = 0.33f;
        public const float SummonIntervalSeconds = 9f;
        public const int SummonBase = 3;
        public const int FirstModifierSurge = 20;
        public const int SecondModifierSurge = 40;
        /// <summary>Damage multiplier of the chest core while exposed.</summary>
        public const float CoreMultiplier = 3f;
        public const float GrowthMultiplier = 2.5f;

        /// <summary>Bounty paid to every player when a Specimen dies.</summary>
        public const int Bounty = 1000;

        /// <summary>Health multiplier for one-per-squad enemies (Specimens, hunters): +60% per extra player.</summary>
        public static float PlayerHealthScale(int players) => 1f + 0.6f * (Math.Max(1, Math.Min(4, players)) - 1);

        public static SpecimenPhase PhaseFor(float health01)
        {
            if (health01 <= ExposedAt) return SpecimenPhase.ExposedCore;
            if (health01 <= RampageAt) return SpecimenPhase.Rampage;
            return SpecimenPhase.Stalk;
        }

        /// <summary>Infected summoned per wave in the Rampage phase.</summary>
        public static int SummonCount(int players, SpecimenModifiers mods)
        {
            int n = SummonBase + Math.Max(0, players - 1);
            if ((mods & SpecimenModifiers.Brood) != 0) n = (int)Math.Ceiling(n * 1.5f);
            return n;
        }

        public static SpecimenModifiers RollModifiers(DeterministicRandom rng, int surge)
        {
            if (rng == null || surge < FirstModifierSurge) return SpecimenModifiers.None;
            int count = surge >= SecondModifierSurge ? 2 : 1;
            var result = SpecimenModifiers.None;
            for (int i = 0; i < count; i++)
            {
                for (int tries = 0; tries < 8; tries++)
                {
                    var m = (SpecimenModifiers)(1 << rng.Range(0, 3));
                    if ((result & m) != 0) continue;
                    result |= m;
                    break;
                }
            }
            return result;
        }

        /// <summary>Writes the phase's stats into <paramref name="target"/> (the boss's own copy), starting from its base stats.</summary>
        public static void ApplyPhase(EnemyStats baseStats, EnemyStats target, SpecimenPhase phase, SpecimenModifiers mods)
        {
            if (baseStats == null || target == null) return;
            target.CopyFrom(baseStats);
            switch (phase)
            {
                case SpecimenPhase.Stalk:
                    target.CanCharge = false;
                    target.CanShoot = false;
                    break;
                case SpecimenPhase.Rampage:
                    target.CanCharge = true;
                    target.CanShoot = false;
                    target.ChaseSpeed *= 1.3f;
                    target.DamageReduction = Math.Max(0f, target.DamageReduction - 0.15f);
                    break;
                case SpecimenPhase.ExposedCore:
                    target.CanCharge = false;
                    target.CanShoot = true;
                    target.ChaseSpeed *= 0.75f;
                    target.DamageReduction = 0f;
                    break;
            }
            if ((mods & SpecimenModifiers.Enraged) != 0)
            {
                target.ChaseSpeed *= 1.2f;
                target.AttackDamage *= 1.25f;
                target.ChargeDamage *= 1.25f;
                target.RangedDamage *= 1.25f;
            }
            if ((mods & SpecimenModifiers.Armored) != 0) target.DamageReduction = Math.Min(0.7f, target.DamageReduction + 0.2f);
        }

        public static string Name(SpecimenPhase p) => p switch
        {
            SpecimenPhase.Rampage => "Rampage",
            SpecimenPhase.ExposedCore => "Exposed core",
            _ => "Stalking",
        };

        public static string Describe(SpecimenModifiers m)
        {
            if (m == SpecimenModifiers.None) return string.Empty;
            var parts = new System.Collections.Generic.List<string>(3);
            if ((m & SpecimenModifiers.Enraged) != 0) parts.Add("Enraged");
            if ((m & SpecimenModifiers.Armored) != 0) parts.Add("Armored");
            if ((m & SpecimenModifiers.Brood) != 0) parts.Add("Brood");
            return string.Join(" · ", parts);
        }

        /// <summary>Snapshot byte for a boss: phase in the low 2 bits, modifiers above.</summary>
        public static byte Pack(SpecimenPhase phase, SpecimenModifiers mods) => (byte)(((byte)phase & 3) | ((byte)mods << 2));

        public static void Unpack(byte value, out SpecimenPhase phase, out SpecimenModifiers mods)
        {
            int p = value & 3;
            phase = p > (int)SpecimenPhase.ExposedCore ? SpecimenPhase.ExposedCore : (SpecimenPhase)p;
            mods = (SpecimenModifiers)((value >> 2) & 7);
        }
    }
}
