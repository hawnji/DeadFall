using System;

namespace Deadfall.Rules.Combat
{
    /// <summary>
    /// Tunable numbers for a firearm. Lives in a WeaponDefinition asset; logic never hardcodes these.
    /// Public fields so Unity can serialize it while this assembly stays engine-free.
    /// </summary>
    [Serializable]
    public sealed class WeaponStats
    {
        public WeaponCategory Category = WeaponCategory.AssaultRifle;
        public FireMode FireMode = FireMode.FullAuto;

        public float Damage = 28f;
        public int PelletsPerShot = 1;
        public float RoundsPerMinute = 650f;

        public int MagazineSize = 30;
        public int MaxReserve = 180;
        /// <summary>Reload time when rounds remain in the magazine.</summary>
        public float TacticalReloadSeconds = 1.9f;
        /// <summary>Reload time from an empty magazine (includes chambering).</summary>
        public float EmptyReloadSeconds = 2.4f;
        /// <summary>
        /// Fraction of the reload after which the new magazine counts as inserted. Interrupting after this
        /// point keeps the ammo; interrupting before it keeps the old magazine.
        /// </summary>
        public float ReloadCommitFraction = 0.75f;

        /// <summary>Full damage up to this distance (m).</summary>
        public float EffectiveRange = 30f;
        /// <summary>Damage falls linearly to MinRangeMultiplier at this distance; nothing hits beyond it.</summary>
        public float MaxRange = 120f;
        public float MinRangeMultiplier = 0.6f;
        public float HeadMultiplier = 2f;
        public float LimbMultiplier = 0.75f;

        /// <summary>Cone half-angle in degrees.</summary>
        public float HipSpread = 2.2f;
        public float AimSpread = 0.25f;
        /// <summary>Added at full movement speed.</summary>
        public float MoveSpreadPenalty = 1.5f;
        /// <summary>Added per shot, decays over time.</summary>
        public float BloomPerShot = 0.35f;
        public float MaxBloom = 2.5f;
        public float BloomRecoveryPerSecond = 6f;

        /// <summary>Upward camera kick per shot in degrees.</summary>
        public float RecoilPitch = 0.55f;
        /// <summary>Random horizontal kick range per shot in degrees (±).</summary>
        public float RecoilYaw = 0.25f;
        /// <summary>Fraction of recoil the view returns per second after firing stops.</summary>
        public float RecoilRecoveryPerSecond = 8f;
        public float AimRecoilMultiplier = 0.65f;

        public float EquipSeconds = 0.45f;
        public float SprintToFireSeconds = 0.18f;
        public float AimInSeconds = 0.2f;
        public float AimFovMultiplier = 0.8f;
        /// <summary>Movement speed multiplier while this weapon is held (heavier weapons are slower).</summary>
        public float MoveSpeedMultiplier = 1f;

        public string AmmoTypeId = "ammo.rifle";

        public float SecondsBetweenShots => RoundsPerMinute > 0f ? 60f / RoundsPerMinute : float.MaxValue;
    }

    /// <summary>Tunable numbers for a melee attack (dedicated melee weapon or quick melee).</summary>
    [Serializable]
    public sealed class MeleeStats
    {
        public float Damage = 55f;
        public float HeadMultiplier = 1.5f;
        public float Range = 2.2f;
        /// <summary>Full width of the hit arc in degrees.</summary>
        public float ArcDegrees = 70f;
        /// <summary>Delay between pressing melee and the hit landing.</summary>
        public float WindupSeconds = 0.08f;
        /// <summary>Time from press until the next melee can start.</summary>
        public float RecoverySeconds = 0.55f;
        /// <summary>Maximum targets one swing can hit.</summary>
        public int MaxTargets = 3;
        public float MoveSpeedMultiplier = 1.05f;
    }
}
