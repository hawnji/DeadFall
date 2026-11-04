using System;

namespace Deadfall.Rules.World
{
    /// <summary>
    /// Creepers using crawl vents (GAME_DESIGN §7 "uses vents"): a Creeper that is far from its target and close to a
    /// floor grate can drop in and climb out of the grate nearest the squad, flanking them. Pure numbers and checks.
    /// </summary>
    public static class VentRules
    {
        /// <summary>How often the host looks for Creepers to send through vents.</summary>
        public const float CheckSeconds = 0.5f;
        /// <summary>Chance per check that an eligible Creeper takes the vent (so they don't all vanish at once).</summary>
        public const float DiveChance = 0.3f;
        /// <summary>Only Creepers at least this far from their target bother.</summary>
        public const float MinTargetDistance = 14f;
        /// <summary>How close to a grate the Creeper must be to drop in.</summary>
        public const float GrateReach = 3f;
        /// <summary>The exit grate must be within this distance of the target, and no closer than <see cref="MinExitDistance"/>.</summary>
        public const float MaxExitDistance = 13f;
        public const float MinExitDistance = 3.5f;
        /// <summary>The vent must save at least this much distance, or walking is just as good.</summary>
        public const float MinGain = 8f;
        public const float CrawlSpeed = 9f;
        public const float MinTravelSeconds = 1.2f;
        public const float MaxTravelSeconds = 5f;
        /// <summary>A Creeper that just used a vent walks for a while before the next one.</summary>
        public const float CooldownSeconds = 10f;

        public static bool ShouldDive(float toTarget, float toGrate, float exitToTarget)
        {
            if (toTarget < MinTargetDistance || toGrate > GrateReach) return false;
            if (exitToTarget > MaxExitDistance || exitToTarget < MinExitDistance) return false;
            return toTarget - exitToTarget >= MinGain;
        }

        public static float TravelSeconds(float distance) => Math.Max(MinTravelSeconds, Math.Min(MaxTravelSeconds, distance / CrawlSpeed));
    }
}
