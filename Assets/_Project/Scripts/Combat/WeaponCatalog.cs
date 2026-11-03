using System.Collections.Generic;
using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Combat
{
    /// <summary>
    /// The eight prototype weapons (GAME_DESIGN §6, D-18), created in memory so they work with no asset setup.
    /// One shared instance per weapon: loadouts, pickups and the host's hit checks all use these. They become
    /// WeaponDefinition assets in Data/Weapons once tuning settles. Order is used on the wire: append only.
    /// </summary>
    public static class WeaponCatalog
    {
        private static WeaponDefinition[] _all;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _all = null;

        public static IReadOnlyList<WeaponDefinition> All
        {
            get
            {
                if (_all == null || _all[0] == null) _all = Build();
                return _all;
            }
        }

        public static WeaponDefinition ById(string id)
        {
            foreach (WeaponDefinition d in All) if (d.ContentId == id) return d;
            return null;
        }

        public static int IndexOf(WeaponDefinition def)
        {
            IReadOnlyList<WeaponDefinition> all = All;
            for (int i = 0; i < all.Count; i++) if (all[i] == def) return i;
            return -1;
        }

        public static WeaponDefinition At(int index) => index >= 0 && index < All.Count ? All[index] : null;

        public static WeaponDefinition HalberdAR => ById("weapon.halberd_ar");
        public static WeaponDefinition Breacher12 => ById("weapon.breacher_12");
        public static WeaponDefinition Ranger9 => ById("weapon.ranger_9");
        public static WeaponDefinition Saber7 => ById("weapon.saber_7");
        public static WeaponDefinition WaspSmg => ById("weapon.wasp_smg");
        public static WeaponDefinition AnvilLmg => ById("weapon.anvil_lmg");
        public static WeaponDefinition TetherCleaver => ById("weapon.tether_cleaver");
        public static WeaponDefinition MuleLauncher => ById("weapon.mule_launcher");
        public static WeaponDefinition VesperDmr => ById("weapon.vesper_dmr");
        public static WeaponDefinition Bulldog44 => ById("weapon.bulldog_44");
        public static WeaponDefinition Hailstorm => ById("weapon.hailstorm");
        public static WeaponDefinition KestrelPdw => ById("weapon.kestrel_pdw");
        public static WeaponDefinition RailspikeMaul => ById("weapon.railspike_maul");

        private static WeaponDefinition[] Build() => new[]
        {
            WeaponDefinition.CreateRuntime("weapon.halberd_ar", "Halberd AR", WeaponKind.Firearm, new WeaponStats
            {
                Category = WeaponCategory.AssaultRifle, FireMode = FireMode.FullAuto,
                Damage = 26f, RoundsPerMinute = 660f, MagazineSize = 30, MaxReserve = 210,
                TacticalReloadSeconds = 1.9f, EmptyReloadSeconds = 2.4f,
                EffectiveRange = 35f, MaxRange = 150f, MinRangeMultiplier = 0.6f,
                HipSpread = 2.0f, AimSpread = 0.2f, MoveSpreadPenalty = 1.4f, BloomPerShot = 0.3f, MaxBloom = 2.2f,
                RecoilPitch = 0.5f, RecoilYaw = 0.22f, EquipSeconds = 0.45f, SprintToFireSeconds = 0.18f,
                AmmoTypeId = "ammo.rifle",
            }, null, new Color(0.28f, 0.32f, 0.3f)),

            WeaponDefinition.CreateRuntime("weapon.breacher_12", "Breacher-12", WeaponKind.Firearm, new WeaponStats
            {
                Category = WeaponCategory.Shotgun, FireMode = FireMode.SemiAuto,
                Damage = 14f, PelletsPerShot = 9, RoundsPerMinute = 75f, MagazineSize = 6, MaxReserve = 42,
                TacticalReloadSeconds = 2.6f, EmptyReloadSeconds = 3.1f, ReloadCommitFraction = 0.7f,
                EffectiveRange = 8f, MaxRange = 35f, MinRangeMultiplier = 0.25f, HeadMultiplier = 1.5f,
                HipSpread = 5.5f, AimSpread = 4f, MoveSpreadPenalty = 0.8f, BloomPerShot = 0f, MaxBloom = 0f,
                RecoilPitch = 3.2f, RecoilYaw = 0.8f, RecoilRecoveryPerSecond = 7f,
                EquipSeconds = 0.55f, SprintToFireSeconds = 0.22f, MoveSpeedMultiplier = 0.96f,
                AmmoTypeId = "ammo.shell",
            }, null, new Color(0.35f, 0.25f, 0.18f)),

            WeaponDefinition.CreateRuntime("weapon.ranger_9", "Ranger-9", WeaponKind.Firearm, new WeaponStats
            {
                Category = WeaponCategory.Pistol, FireMode = FireMode.SemiAuto,
                Damage = 30f, RoundsPerMinute = 400f, MagazineSize = 12, MaxReserve = 96,
                TacticalReloadSeconds = 1.3f, EmptyReloadSeconds = 1.6f,
                EffectiveRange = 20f, MaxRange = 80f, MinRangeMultiplier = 0.55f,
                HipSpread = 1.4f, AimSpread = 0.3f, MoveSpreadPenalty = 0.7f, BloomPerShot = 0.5f, MaxBloom = 1.8f,
                RecoilPitch = 1.1f, RecoilYaw = 0.3f, EquipSeconds = 0.3f, SprintToFireSeconds = 0.12f,
                MoveSpeedMultiplier = 1.04f, AimFovMultiplier = 0.9f, AmmoTypeId = "ammo.pistol",
            }, null, new Color(0.2f, 0.2f, 0.22f)),

            WeaponDefinition.CreateRuntime("weapon.saber_7", "Saber-7", WeaponKind.Melee, null, new MeleeStats
            {
                Damage = 70f, HeadMultiplier = 1.5f, Range = 2.6f, ArcDegrees = 90f,
                WindupSeconds = 0.1f, RecoverySeconds = 0.5f, MaxTargets = 3, MoveSpeedMultiplier = 1.08f,
            }, new Color(0.6f, 0.62f, 0.65f)),

            WeaponDefinition.CreateRuntime("weapon.wasp_smg", "Wasp SMG", WeaponKind.Firearm, new WeaponStats
            {
                Category = WeaponCategory.SubmachineGun, FireMode = FireMode.FullAuto,
                Damage = 18f, RoundsPerMinute = 900f, MagazineSize = 32, MaxReserve = 256,
                TacticalReloadSeconds = 1.6f, EmptyReloadSeconds = 2.0f,
                EffectiveRange = 15f, MaxRange = 70f, MinRangeMultiplier = 0.5f,
                HipSpread = 2.2f, AimSpread = 0.5f, MoveSpreadPenalty = 1.0f, BloomPerShot = 0.25f, MaxBloom = 2.0f,
                RecoilPitch = 0.35f, RecoilYaw = 0.25f, EquipSeconds = 0.35f, SprintToFireSeconds = 0.12f,
                MoveSpeedMultiplier = 1.03f, AmmoTypeId = "ammo.smg",
            }, null, new Color(0.3f, 0.28f, 0.34f)),

            WeaponDefinition.CreateRuntime("weapon.anvil_lmg", "Anvil LMG", WeaponKind.Firearm, new WeaponStats
            {
                Category = WeaponCategory.LightMachineGun, FireMode = FireMode.FullAuto,
                Damage = 24f, RoundsPerMinute = 720f, MagazineSize = 100, MaxReserve = 300,
                TacticalReloadSeconds = 4.5f, EmptyReloadSeconds = 5.4f,
                EffectiveRange = 35f, MaxRange = 150f, MinRangeMultiplier = 0.6f,
                HipSpread = 3.2f, AimSpread = 0.45f, MoveSpreadPenalty = 1.6f, BloomPerShot = 0.2f, MaxBloom = 3f,
                RecoilPitch = 0.45f, RecoilYaw = 0.3f, EquipSeconds = 0.8f, SprintToFireSeconds = 0.3f, AimInSeconds = 0.32f,
                MoveSpeedMultiplier = 0.9f, AmmoTypeId = "ammo.rifle",
            }, null, new Color(0.25f, 0.27f, 0.22f)),

            WeaponDefinition.CreateRuntime("weapon.tether_cleaver", "Tether Cleaver", WeaponKind.Melee, null, new MeleeStats
            {
                Damage = 110f, HeadMultiplier = 1.4f, Range = 2.4f, ArcDegrees = 110f,
                WindupSeconds = 0.22f, RecoverySeconds = 0.85f, MaxTargets = 4, MoveSpeedMultiplier = 0.98f,
            }, new Color(0.45f, 0.35f, 0.3f)),

            WeaponDefinition.CreateRuntime("weapon.mule_launcher", "Mule Launcher", WeaponKind.Firearm, new WeaponStats
            {
                Category = WeaponCategory.Heavy, FireMode = FireMode.SemiAuto,
                Damage = 120f, RoundsPerMinute = 70f, MagazineSize = 6, MaxReserve = 18,
                TacticalReloadSeconds = 3.6f, EmptyReloadSeconds = 4.2f,
                EffectiveRange = 40f, MaxRange = 80f, MinRangeMultiplier = 1f,
                HipSpread = 1.5f, AimSpread = 0.5f, MoveSpreadPenalty = 1f, BloomPerShot = 0f, MaxBloom = 0f,
                RecoilPitch = 3.5f, RecoilYaw = 0.6f, EquipSeconds = 0.7f, SprintToFireSeconds = 0.3f,
                MoveSpeedMultiplier = 0.93f, AmmoTypeId = "ammo.grenade",
                ProjectileSpeed = 32f, ExplosionRadius = 4.2f,
            }, null, new Color(0.3f, 0.34f, 0.25f)),

            // M12 additions (appended: the network numbers weapons by position).
            WeaponDefinition.CreateRuntime("weapon.vesper_dmr", "Vesper DMR", WeaponKind.Firearm, new WeaponStats
            {
                Category = WeaponCategory.AssaultRifle, FireMode = FireMode.SemiAuto,
                Damage = 64f, RoundsPerMinute = 260f, MagazineSize = 15, MaxReserve = 120, HeadMultiplier = 2.2f,
                TacticalReloadSeconds = 2.2f, EmptyReloadSeconds = 2.7f,
                EffectiveRange = 55f, MaxRange = 200f, MinRangeMultiplier = 0.75f,
                HipSpread = 2.6f, AimSpread = 0.05f, MoveSpreadPenalty = 1.6f, BloomPerShot = 0.6f, MaxBloom = 2.4f,
                RecoilPitch = 1.6f, RecoilYaw = 0.25f, EquipSeconds = 0.55f, SprintToFireSeconds = 0.22f, AimInSeconds = 0.3f,
                MoveSpeedMultiplier = 0.95f, AimFovMultiplier = 0.65f, AmmoTypeId = "ammo.rifle",
            }, null, new Color(0.32f, 0.3f, 0.26f)),

            WeaponDefinition.CreateRuntime("weapon.bulldog_44", "Bulldog .44", WeaponKind.Firearm, new WeaponStats
            {
                Category = WeaponCategory.Pistol, FireMode = FireMode.SemiAuto,
                Damage = 72f, RoundsPerMinute = 150f, MagazineSize = 6, MaxReserve = 48, HeadMultiplier = 2f,
                TacticalReloadSeconds = 2.3f, EmptyReloadSeconds = 2.3f,
                EffectiveRange = 22f, MaxRange = 80f, MinRangeMultiplier = 0.6f,
                HipSpread = 1.6f, AimSpread = 0.2f, MoveSpreadPenalty = 0.8f, BloomPerShot = 1.2f, MaxBloom = 2.5f,
                RecoilPitch = 3.2f, RecoilYaw = 0.6f, RecoilRecoveryPerSecond = 8f, EquipSeconds = 0.35f, SprintToFireSeconds = 0.14f,
                MoveSpeedMultiplier = 1.03f, AimFovMultiplier = 0.88f, AmmoTypeId = "ammo.pistol",
            }, null, new Color(0.36f, 0.3f, 0.22f)),

            WeaponDefinition.CreateRuntime("weapon.hailstorm", "Hailstorm Auto-Shotgun", WeaponKind.Firearm, new WeaponStats
            {
                Category = WeaponCategory.Shotgun, FireMode = FireMode.FullAuto,
                Damage = 10f, PelletsPerShot = 8, RoundsPerMinute = 230f, MagazineSize = 12, MaxReserve = 60,
                TacticalReloadSeconds = 2.8f, EmptyReloadSeconds = 3.4f,
                EffectiveRange = 9f, MaxRange = 35f, MinRangeMultiplier = 0.25f, HeadMultiplier = 1.4f,
                HipSpread = 6f, AimSpread = 4.5f, MoveSpreadPenalty = 0.8f, BloomPerShot = 0.4f, MaxBloom = 2f,
                RecoilPitch = 2.2f, RecoilYaw = 0.9f, RecoilRecoveryPerSecond = 7f,
                EquipSeconds = 0.65f, SprintToFireSeconds = 0.25f, MoveSpeedMultiplier = 0.93f,
                AmmoTypeId = "ammo.shell",
            }, null, new Color(0.24f, 0.26f, 0.3f)),

            WeaponDefinition.CreateRuntime("weapon.kestrel_pdw", "Kestrel PDW", WeaponKind.Firearm, new WeaponStats
            {
                Category = WeaponCategory.SubmachineGun, FireMode = FireMode.FullAuto,
                Damage = 15f, RoundsPerMinute = 1050f, MagazineSize = 40, MaxReserve = 280,
                TacticalReloadSeconds = 1.7f, EmptyReloadSeconds = 2.1f,
                EffectiveRange = 13f, MaxRange = 60f, MinRangeMultiplier = 0.45f,
                HipSpread = 1.8f, AimSpread = 0.6f, MoveSpreadPenalty = 0.8f, BloomPerShot = 0.18f, MaxBloom = 2.2f,
                RecoilPitch = 0.3f, RecoilYaw = 0.3f, EquipSeconds = 0.3f, SprintToFireSeconds = 0.1f,
                MoveSpeedMultiplier = 1.06f, AmmoTypeId = "ammo.smg",
            }, null, new Color(0.22f, 0.28f, 0.3f)),

            WeaponDefinition.CreateRuntime("weapon.railspike_maul", "Railspike Maul", WeaponKind.Melee, null, new MeleeStats
            {
                Damage = 160f, HeadMultiplier = 1.5f, Range = 2.7f, ArcDegrees = 120f,
                WindupSeconds = 0.35f, RecoverySeconds = 1.05f, MaxTargets = 5, MoveSpeedMultiplier = 0.95f,
            }, new Color(0.4f, 0.38f, 0.36f)),
        };
    }
}
