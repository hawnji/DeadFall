using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.DebugTools
{
    /// <summary>
    /// Placeholder weapon definitions for the Milestone 1 sandbox, created in memory so the sandbox
    /// runs with zero asset setup. These become WeaponDefinition assets in Data/Weapons in M6.
    /// Default loadout follows D-07: two primaries, a sidearm, and a melee weapon.
    /// </summary>
    public static class SandboxContent
    {
        public static List<WeaponDefinition> DefaultLoadout() => new List<WeaponDefinition>
        {
            HalberdAR(),
            Breacher12(),
            Ranger9(),
            Saber7(),
        };

        public static WeaponDefinition HalberdAR() => WeaponDefinition.CreateRuntime("weapon.halberd_ar", "Halberd AR", WeaponKind.Firearm,
            new WeaponStats
            {
                Category = WeaponCategory.AssaultRifle, FireMode = FireMode.FullAuto,
                Damage = 26f, RoundsPerMinute = 660f, MagazineSize = 30, MaxReserve = 210,
                TacticalReloadSeconds = 1.9f, EmptyReloadSeconds = 2.4f,
                EffectiveRange = 35f, MaxRange = 150f, MinRangeMultiplier = 0.6f,
                HipSpread = 2.0f, AimSpread = 0.2f, MoveSpreadPenalty = 1.4f, BloomPerShot = 0.3f, MaxBloom = 2.2f,
                RecoilPitch = 0.5f, RecoilYaw = 0.22f, EquipSeconds = 0.45f, SprintToFireSeconds = 0.18f,
                AmmoTypeId = "ammo.rifle",
            }, null, new Color(0.28f, 0.32f, 0.3f));

        public static WeaponDefinition Breacher12() => WeaponDefinition.CreateRuntime("weapon.breacher_12", "Breacher-12", WeaponKind.Firearm,
            new WeaponStats
            {
                Category = WeaponCategory.Shotgun, FireMode = FireMode.SemiAuto,
                Damage = 14f, PelletsPerShot = 9, RoundsPerMinute = 75f, MagazineSize = 6, MaxReserve = 42,
                TacticalReloadSeconds = 2.6f, EmptyReloadSeconds = 3.1f, ReloadCommitFraction = 0.7f,
                EffectiveRange = 8f, MaxRange = 35f, MinRangeMultiplier = 0.25f, HeadMultiplier = 1.5f,
                HipSpread = 5.5f, AimSpread = 4f, MoveSpreadPenalty = 0.8f, BloomPerShot = 0f, MaxBloom = 0f,
                RecoilPitch = 3.2f, RecoilYaw = 0.8f, RecoilRecoveryPerSecond = 7f,
                EquipSeconds = 0.55f, SprintToFireSeconds = 0.22f, MoveSpeedMultiplier = 0.96f,
                AmmoTypeId = "ammo.shell",
            }, null, new Color(0.35f, 0.25f, 0.18f));

        public static WeaponDefinition Ranger9() => WeaponDefinition.CreateRuntime("weapon.ranger_9", "Ranger-9", WeaponKind.Firearm,
            new WeaponStats
            {
                Category = WeaponCategory.Pistol, FireMode = FireMode.SemiAuto,
                Damage = 30f, RoundsPerMinute = 400f, MagazineSize = 12, MaxReserve = 96,
                TacticalReloadSeconds = 1.3f, EmptyReloadSeconds = 1.6f,
                EffectiveRange = 20f, MaxRange = 80f, MinRangeMultiplier = 0.55f,
                HipSpread = 1.4f, AimSpread = 0.3f, MoveSpreadPenalty = 0.7f, BloomPerShot = 0.5f, MaxBloom = 1.8f,
                RecoilPitch = 1.1f, RecoilYaw = 0.3f, EquipSeconds = 0.3f, SprintToFireSeconds = 0.12f,
                MoveSpeedMultiplier = 1.04f, AimFovMultiplier = 0.9f, AmmoTypeId = "ammo.pistol",
            }, null, new Color(0.2f, 0.2f, 0.22f));

        public static WeaponDefinition Saber7() => WeaponDefinition.CreateRuntime("weapon.saber_7", "Saber-7", WeaponKind.Melee,
            null,
            new MeleeStats
            {
                Damage = 70f, HeadMultiplier = 1.5f, Range = 2.6f, ArcDegrees = 90f,
                WindupSeconds = 0.1f, RecoverySeconds = 0.5f, MaxTargets = 3, MoveSpeedMultiplier = 1.08f,
            }, new Color(0.6f, 0.62f, 0.65f));
    }
}
