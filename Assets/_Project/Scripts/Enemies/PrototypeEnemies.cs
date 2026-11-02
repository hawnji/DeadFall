using System.Collections.Generic;
using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Enemies
{
    /// <summary>
    /// The three M2 enemy types, created in memory so they work with zero asset setup. They become
    /// EnemyDefinition assets in Data/Enemies once tuning settles. Numbers are first-pass and meant to be tuned
    /// against the prototype weapons (Halberd AR: 26 damage, x2 head).
    /// </summary>
    public static class PrototypeEnemies
    {
        private static EnemyDefinition _husk, _bolter, _bulwark;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _husk = _bolter = _bulwark = null;

        /// <summary>Standard infected: walks at you, swipes, comes in numbers. ~5 body shots, 2 head shots.</summary>
        public static EnemyDefinition Husk => _husk != null ? _husk : _husk = EnemyDefinition.CreateRuntime(
            "enemy.husk", "Husk",
            new EnemyStats
            {
                MaxHealth = 110f, ChaseSpeed = 2.6f, Acceleration = 10f, TurnSpeedDegrees = 300f, DetectRange = 40f,
                AttackRange = 1.6f, AttackDamage = 18f, AttackWindupSeconds = 0.55f, AttackRecoverSeconds = 0.8f,
                StaggerThreshold = 55f, StaggerSeconds = 0.6f,
            },
            EnemyBodyType.Standard, 1.8f, 0.35f, new Color(0.45f, 0.52f, 0.4f), threatCost: 1f);

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
            EnemyBodyType.Lean, 1.65f, 0.3f, new Color(0.62f, 0.32f, 0.28f), threatCost: 1.5f);

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
            EnemyBodyType.Plated, 2.3f, 0.6f, new Color(0.38f, 0.36f, 0.42f), threatCost: 6f);

        public static IReadOnlyList<EnemyDefinition> All => new[] { Husk, Bolter, Bulwark };
    }
}
