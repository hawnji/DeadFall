using System;

namespace Deadfall.Rules.Combat
{
    /// <summary>Where a player is in the down/revive cycle (D-01, D-02).</summary>
    public enum LifeState : byte
    {
        /// <summary>Up and fighting.</summary>
        Alive = 0,
        /// <summary>At 0 HP: crawling with a sidearm, bleeding out, can be revived.</summary>
        Downed = 1,
        /// <summary>Bled out. Spectates until the end of the next surge or objective (D-02).</summary>
        Eliminated = 2,
    }

    /// <summary>
    /// Downed, revive and squad-wipe rules (D-01, D-02). Engine-free so the numbers are tested.
    /// Bleed-out shortens with each down in a run (30, 20, then 10 seconds); teammate revives are unlimited.
    /// </summary>
    public static class DownedRules
    {
        public const float ReviveSeconds = 4f;
        public const float SelfReviveSeconds = 2.5f;
        /// <summary>Health a revived player stands up with, as a fraction of max.</summary>
        public const float ReviveHealthFraction = 0.4f;
        /// <summary>Self-revive kits the squad starts a run with: none, they're bought (syringes at ammo stations).</summary>
        public const int TeamSelfReviveKits = 0;
        /// <summary>How close (metres, feet to feet) a teammate must be to revive.</summary>
        public const float ReviveRange = 2.2f;
        /// <summary>Extra slack the host allows on a revive claim (latency).</summary>
        public const float ReviveRangeSlack = 1.5f;
        /// <summary>Downed crawl speed as a fraction of crouch speed.</summary>
        public const float CrawlMultiplier = 0.55f;
        /// <summary>Bleed-out for the first, second and later downs.</summary>
        public static readonly float[] BleedOut = { 30f, 20f, 10f };

        /// <param name="downNumber">1 for the first down this run, 2 for the second, and so on.</param>
        public static float BleedOutSeconds(int downNumber)
        {
            int i = Math.Max(1, downNumber) - 1;
            return BleedOut[Math.Min(i, BleedOut.Length - 1)];
        }

        /// <summary>
        /// True when nobody can bring the squad back: nobody is up, and either nobody is downed or no
        /// self-revive kits remain for the downed to use (the squad starts with none, so all down ends the run
        /// unless syringes were bought).
        /// </summary>
        public static bool SquadWiped(int alive, int downed, int kits)
        {
            if (alive > 0) return false;
            return downed <= 0 || kits <= 0;
        }

        public static bool CanSelfRevive(LifeState state, int kits) => state == LifeState.Downed && kits > 0;

        /// <summary>A revive claim is valid when the reviver is up, the target is downed, and they are close enough.</summary>
        public static bool ReviveValid(LifeState reviver, LifeState target, float distance) =>
            reviver == LifeState.Alive && target == LifeState.Downed && distance <= ReviveRange + ReviveRangeSlack;

        /// <summary>Eliminated players return at the end of the next surge, or the next objective checkpoint in Operations (D-02).</summary>
        public static bool ReturnsNow(LifeState state, bool surgeCleared, bool objectiveCompleted) =>
            state == LifeState.Eliminated && (surgeCleared || objectiveCompleted);
    }
}
