using System.Collections.Generic;
using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Enemies
{
    /// <summary>
    /// The prototype enemy types (M2: Husk, Bolter, Bulwark; M7: Creeper, Pyre, Retcher, Alpha, Goliath, Stalker,
    /// Specimen Zero). The original three, created in memory so they work with zero asset setup. They become
    /// EnemyDefinition assets in Data/Enemies once tuning settles. Numbers are first-pass and meant to be tuned
    /// against the prototype weapons (Halberd AR: 26 damage, x2 head).
    /// </summary>
    public static class PrototypeEnemies
    {
        private static EnemyDefinition _husk, _bolter, _bulwark, _creeper, _pyre, _retcher, _alpha, _goliath, _stalker, _specimen, _brood;
        private static EnemyDefinition[] _all;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _husk = _bolter = _bulwark = _creeper = _pyre = _retcher = _alpha = _goliath = _stalker = _specimen = _brood = null;
            _all = null;
        }

        /// <summary>Standard infected: walks at you, swipes, comes in numbers. ~5 body shots, 2 head shots.</summary>
        public static EnemyDefinition Husk => _husk != null ? _husk : _husk = EnemyDefinition.CreateRuntime(
            "enemy.husk", "Husk",
            new EnemyStats
            {
                MaxHealth = 110f, ChaseSpeed = 2.6f, Acceleration = 10f, TurnSpeedDegrees = 300f, DetectRange = 40f,
                AttackRange = 1.6f, AttackDamage = 18f, AttackWindupSeconds = 0.55f, AttackRecoverSeconds = 0.8f,
                StaggerThreshold = 55f, StaggerSeconds = 0.6f,
            },
            EnemyBodyType.Standard, 1.8f, 0.35f, new Color(0.45f, 0.52f, 0.4f), threatCost: 1f, unlockSurge: 1, spawnWeight: 10f);

        /// <summary>Fast and fragile: weaves while sprinting, then lunges. Punishes ignoring the flanks.</summary>
        public static EnemyDefinition Bolter => _bolter != null ? _bolter : _bolter = EnemyDefinition.CreateRuntime(
            "enemy.bolter", "Bolter",
            new EnemyStats
            {
                MaxHealth = 65f, ChaseSpeed = 6.2f, Acceleration = 30f, TurnSpeedDegrees = 720f, DetectRange = 55f,
                WeaveAmplitude = 2.2f, WeaveFrequency = 1.6f,
                AttackRange = 1.5f, AttackDamage = 10f, AttackWindupSeconds = 0.25f, AttackRecoverSeconds = 0.45f,
                StaggerThreshold = 30f, StaggerSeconds = 0.45f,
                CanCharge = true, ChargeMinRange = 4f, ChargeMaxRange = 8f, ChargeWindupSeconds = 0.3f,
                ChargeSpeed = 11f, ChargeSeconds = 0.55f, ChargeCooldownSeconds = 5f, ChargeDamage = 16f,
            },
            EnemyBodyType.Lean, 1.65f, 0.3f, new Color(0.62f, 0.32f, 0.28f), threatCost: 1.5f, unlockSurge: 3, spawnWeight: 4f);

        /// <summary>Armored brute: slow, plated front, telegraphed charge. Flank it and hit the pack on its back.</summary>
        public static EnemyDefinition Bulwark => _bulwark != null ? _bulwark : _bulwark = EnemyDefinition.CreateRuntime(
            "enemy.bulwark", "Bulwark",
            new EnemyStats
            {
                MaxHealth = 650f, ChaseSpeed = 1.9f, Acceleration = 6f, TurnSpeedDegrees = 120f, DetectRange = 40f,
                AttackRange = 2.2f, AttackDamage = 40f, AttackWindupSeconds = 0.9f, AttackRecoverSeconds = 1.1f,
                AttackArcDegrees = 140f,
                StaggerThreshold = 260f, StaggerSeconds = 0.8f, StaggerImmunitySeconds = 3f,
                CanCharge = true, ChargeMinRange = 6f, ChargeMaxRange = 16f, ChargeWindupSeconds = 1.1f,
                ChargeSpeed = 8.5f, ChargeSeconds = 1.6f, ChargeCooldownSeconds = 9f, ChargeDamage = 45f,
                ChargeCanBeStaggered = false,
            },
            EnemyBodyType.Plated, 2.3f, 0.6f, new Color(0.38f, 0.36f, 0.42f), threatCost: 6f, unlockSurge: 6, spawnWeight: 1f, maxBudgetShare: 0.4f);

        /// <summary>Crawler: low, quick, ankle bites. Hard to see in a crowd; shotguns and looking down.</summary>
        public static EnemyDefinition Creeper => _creeper != null ? _creeper : _creeper = EnemyDefinition.CreateRuntime(
            "enemy.creeper", "Creeper",
            new EnemyStats
            {
                MaxHealth = 55f, ChaseSpeed = 4.2f, Acceleration = 22f, TurnSpeedDegrees = 540f, DetectRange = 35f,
                WeaveAmplitude = 0.8f, WeaveFrequency = 2.2f,
                AttackRange = 1.2f, AttackDamage = 8f, AttackWindupSeconds = 0.2f, AttackRecoverSeconds = 0.4f, AttackArcDegrees = 120f,
                StaggerThreshold = 25f, StaggerSeconds = 0.4f,
            },
            EnemyBodyType.Crawler, 0.7f, 0.3f, new Color(0.5f, 0.45f, 0.36f), threatCost: 0.8f, unlockSurge: 4, spawnWeight: 5f,
            canSplit: false);

        /// <summary>Exploder: runs at the biggest cluster and bursts on contact or death. Kill it at range.</summary>
        public static EnemyDefinition Pyre => _pyre != null ? _pyre : _pyre = EnemyDefinition.CreateRuntime(
            "enemy.pyre", "Pyre",
            new EnemyStats
            {
                MaxHealth = 80f, ChaseSpeed = 4.6f, Acceleration = 16f, TurnSpeedDegrees = 360f, DetectRange = 45f,
                AttackRange = 1.5f, AttackDamage = 0f,
                StaggerThreshold = 40f, StaggerSeconds = 0.5f,
                CanExplode = true, ExplodeRange = 2.4f, ExplodeRadius = 4.5f, ExplodeDamage = 55f, ExplodeWindupSeconds = 0.7f,
                ExplodesOnDeath = true,
            },
            EnemyBodyType.Bloated, 1.75f, 0.42f, new Color(0.78f, 0.48f, 0.22f), threatCost: 2f, unlockSurge: 5, spawnWeight: 2f,
            maxBudgetShare: 0.25f);

        /// <summary>Ranged: keeps its distance and lobs acid that leaves burning pools. Close the gap or use cover.</summary>
        public static EnemyDefinition Retcher => _retcher != null ? _retcher : _retcher = EnemyDefinition.CreateRuntime(
            "enemy.retcher", "Retcher",
            new EnemyStats
            {
                MaxHealth = 130f, ChaseSpeed = 2.4f, Acceleration = 10f, TurnSpeedDegrees = 280f, DetectRange = 45f,
                AttackRange = 1.6f, AttackDamage = 12f, AttackWindupSeconds = 0.5f, AttackRecoverSeconds = 0.7f,
                StaggerThreshold = 50f, StaggerSeconds = 0.6f,
                CanShoot = true, PreferredRange = 13f, RangedMaxRange = 22f, RangedWindupSeconds = 0.8f, RangedRecoverSeconds = 0.6f,
                RangedCooldownSeconds = 3f, RangedDamage = 12f, ProjectileSpeed = 16f,
                PoolDamagePerSecond = 8f, PoolRadius = 1.8f, PoolSeconds = 5f,
            },
            EnemyBodyType.Spitter, 1.7f, 0.38f, new Color(0.46f, 0.55f, 0.25f), threatCost: 2.5f, unlockSurge: 8, spawnWeight: 2f,
            maxBudgetShare: 0.3f);

        /// <summary>Elite: buffs nearby infected (speed, damage reduction). Focus the glowing growth (x3).</summary>
        public static EnemyDefinition Alpha => _alpha != null ? _alpha : _alpha = EnemyDefinition.CreateRuntime(
            "enemy.alpha", "Alpha",
            new EnemyStats
            {
                MaxHealth = 520f, DamageReduction = 0.1f, ChaseSpeed = 3f, Acceleration = 12f, TurnSpeedDegrees = 300f, DetectRange = 50f,
                AttackRange = 1.9f, AttackDamage = 28f, AttackWindupSeconds = 0.5f, AttackRecoverSeconds = 0.7f, AttackArcDegrees = 130f,
                StaggerThreshold = 120f, StaggerSeconds = 0.6f, StaggerImmunitySeconds = 2f,
                AuraRadius = 9f, AuraSpeedBonus = 0.25f, AuraDamageReduction = 0.2f,
            },
            EnemyBodyType.Alpha, 2.15f, 0.42f, new Color(0.36f, 0.4f, 0.5f), threatCost: 5f, unlockSurge: 10, spawnWeight: 0.6f,
            maxBudgetShare: 0.3f, role: EnemyRole.Elite, weakPointMultiplier: 3f);

        /// <summary>Elite heavy: a bigger, angrier Bulwark with a wide slam. Appears from surge 14 (earlier in Elite surges).</summary>
        public static EnemyDefinition Goliath => _goliath != null ? _goliath : _goliath = EnemyDefinition.CreateRuntime(
            "enemy.goliath", "Goliath",
            new EnemyStats
            {
                MaxHealth = 2400f, ChaseSpeed = 2.1f, Acceleration = 6f, TurnSpeedDegrees = 110f, DetectRange = 50f,
                AttackRange = 2.8f, AttackDamage = 55f, AttackWindupSeconds = 1f, AttackRecoverSeconds = 1.2f, AttackArcDegrees = 220f,
                StaggerThreshold = 600f, StaggerSeconds = 0.9f, StaggerImmunitySeconds = 4f,
                CanCharge = true, ChargeMinRange = 6f, ChargeMaxRange = 18f, ChargeWindupSeconds = 1.2f,
                ChargeSpeed = 9f, ChargeSeconds = 1.6f, ChargeCooldownSeconds = 8f, ChargeDamage = 60f, ChargeCanBeStaggered = false,
            },
            EnemyBodyType.Plated, 3f, 0.85f, new Color(0.3f, 0.27f, 0.32f), threatCost: 14f, unlockSurge: 14, spawnWeight: 0.4f,
            maxBudgetShare: 0.5f, role: EnemyRole.Elite);

        /// <summary>Hunter (Hunt surges): fast, tough, never loses the scent, pounces. Stick together.</summary>
        public static EnemyDefinition Stalker => _stalker != null ? _stalker : _stalker = EnemyDefinition.CreateRuntime(
            "enemy.stalker", "The Stalker",
            new EnemyStats
            {
                MaxHealth = 900f, DamageReduction = 0.1f, ChaseSpeed = 6.8f, Acceleration = 30f, TurnSpeedDegrees = 720f, DetectRange = 999f,
                WeaveAmplitude = 1.5f, WeaveFrequency = 1.1f,
                AttackRange = 1.7f, AttackDamage = 22f, AttackWindupSeconds = 0.3f, AttackRecoverSeconds = 0.5f,
                StaggerThreshold = 160f, StaggerSeconds = 0.7f, StaggerImmunitySeconds = 2.5f,
                CanCharge = true, ChargeMinRange = 5f, ChargeMaxRange = 14f, ChargeWindupSeconds = 0.45f,
                ChargeSpeed = 14f, ChargeSeconds = 0.6f, ChargeCooldownSeconds = 4f, ChargeDamage = 35f,
            },
            EnemyBodyType.Lean, 2f, 0.38f, new Color(0.18f, 0.16f, 0.2f), threatCost: 10f, unlockSurge: 6, spawnWeight: 1f,
            role: EnemyRole.Hunter);

        /// <summary>
        /// Specimen Zero, the facility's escaped test subject (GAME_DESIGN §7). Three phases by health: stalk, rampage
        /// with summons and charges, exposed core with acid. Phase stats come from SpecimenRules.
        /// </summary>
        public static EnemyDefinition SpecimenZero => _specimen != null ? _specimen : _specimen = EnemyDefinition.CreateRuntime(
            "enemy.specimen_zero", "Specimen Zero",
            new EnemyStats
            {
                MaxHealth = 6000f, DamageReduction = 0.3f, ChaseSpeed = 3.4f, Acceleration = 10f, TurnSpeedDegrees = 200f, DetectRange = 999f,
                AttackRange = 3f, AttackDamage = 45f, AttackWindupSeconds = 0.7f, AttackRecoverSeconds = 0.9f, AttackArcDegrees = 160f,
                StaggerThreshold = 0f, StaggerSeconds = 1.2f, StaggerImmunitySeconds = 0f,
                CanCharge = true, ChargeMinRange = 6f, ChargeMaxRange = 20f, ChargeWindupSeconds = 1f,
                ChargeSpeed = 11f, ChargeSeconds = 1.4f, ChargeCooldownSeconds = 7f, ChargeDamage = 60f, ChargeCanBeStaggered = false,
                CanShoot = true, PreferredRange = 16f, RangedMaxRange = 26f, RangedWindupSeconds = 1f, RangedRecoverSeconds = 0.8f,
                RangedCooldownSeconds = 3.5f, RangedDamage = 18f, ProjectileSpeed = 18f,
                PoolDamagePerSecond = 12f, PoolRadius = 2.5f, PoolSeconds = 6f,
            },
            EnemyBodyType.Specimen, 3.4f, 1f, new Color(0.62f, 0.5f, 0.52f), threatCost: 40f, unlockSurge: 10, spawnWeight: 1f,
            role: EnemyRole.Boss);

        /// <summary>
        /// The Broodmother (M12), Harrow Line's Specimen: a swollen egg-bearer that never charges. She spits acid from
        /// range in every phase and keeps calling Creepers; enraged she spits faster; with her core exposed she slows
        /// and leaves wider pools. Same three phases and weak points as Specimen Zero.
        /// </summary>
        public static EnemyDefinition Broodmother => _brood != null ? _brood : _brood = EnemyDefinition.CreateRuntime(
            "enemy.broodmother", "The Broodmother",
            new EnemyStats
            {
                MaxHealth = 6800f, DamageReduction = 0.25f, ChaseSpeed = 2.8f, Acceleration = 8f, TurnSpeedDegrees = 160f, DetectRange = 999f,
                AttackRange = 3.2f, AttackDamage = 40f, AttackWindupSeconds = 0.8f, AttackRecoverSeconds = 1f, AttackArcDegrees = 170f,
                StaggerThreshold = 0f, StaggerSeconds = 1.2f, StaggerImmunitySeconds = 0f,
                CanShoot = true, PreferredRange = 14f, RangedMaxRange = 28f, RangedWindupSeconds = 0.9f, RangedRecoverSeconds = 0.7f,
                RangedCooldownSeconds = 2.6f, RangedDamage = 16f, ProjectileSpeed = 17f,
                PoolDamagePerSecond = 12f, PoolRadius = 2.6f, PoolSeconds = 7f,
            },
            EnemyBodyType.Brood, 2.6f, 1.3f, new Color(0.46f, 0.5f, 0.38f), threatCost: 40f, unlockSurge: 10, spawnWeight: 1f,
            role: EnemyRole.Boss);

        /// <summary>Fixed order: the network numbers enemy types by position here (EnemyCatalog). Append only.</summary>
        public static IReadOnlyList<EnemyDefinition> All
        {
            get
            {
                if (_all == null || _all[0] == null) _all = new[] { Husk, Bolter, Bulwark, Creeper, Pyre, Retcher, Alpha, Goliath, Stalker, SpecimenZero, Broodmother };
                return _all;
            }
        }
    }
}
