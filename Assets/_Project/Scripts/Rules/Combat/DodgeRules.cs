using System;

namespace Deadfall.Rules.Combat
{
    /// <summary>
    /// Standard infected (Husks) may sidestep left or right when shot: 10% per roll up to surge 5, then +1% a surge
    /// up to 25% at surge 20. Shield, fast and special infected never dodge. A roll happens at most every
    /// <see cref="RollCooldown"/> seconds (so automatic fire doesn't roll every bullet) and a dodge can't follow
    /// another within <see cref="DodgeCooldown"/>.
    /// </summary>
    public static class DodgeRules
    {
        public const float BaseChance = 0.10f;
        public const float MaxChance = 0.25f;
        public const int RampStartSurge = 5;
        public const float ChancePerSurge = 0.01f;
        public const float RollCooldown = 0.6f;
        public const float DodgeCooldown = 2.5f;
        /// <summary>How far one sidestep goes, in metres, and how fast.</summary>
        public const float Distance = 2.2f;
        public const float Speed = 9f;

        public static float Chance(int surge) =>
            Math.Min(MaxChance, BaseChance + ChancePerSurge * Math.Max(0, surge - RampStartSurge));

        /// <summary>Only the standard Husk dodges.</summary>
        public static bool CanDodge(string enemyContentId) => enemyContentId == "enemy.husk";

        /// <summary>Gunfire only: melee, explosions, turrets and abilities never trigger a dodge.</summary>
        public static bool IsGunfire(string weaponId, bool isMelee) =>
            !isMelee && weaponId != null && weaponId.StartsWith("weapon.", StringComparison.Ordinal);
    }
}
