using System;
using System.Collections.Generic;
using Deadfall.Rules.Common;

namespace Deadfall.Rules.Combat
{
    /// <summary>
    /// The Gun Dispenser (Survival and Endless): pay Field Credits for a random weapon. Every peer sees the weapons
    /// cycle, then the result waits for the buyer. After a few uses a pull can come up empty instead: the buyer gets
    /// a refund and the dispenser moves to another open sector. Pure numbers so they can be tested and tuned.
    /// </summary>
    public static class GunDispenserRules
    {
        public const int Price = 950;
        public const float SpinSeconds = 4f;
        /// <summary>How long the result waits for the buyer before it is gone.</summary>
        public const float OfferSeconds = 12f;
        /// <summary>How long the dispenser shows it is leaving before it reappears somewhere else.</summary>
        public const float LeaveSeconds = 5f;
        /// <summary>Uses at one spot before a pull can make it move.</summary>
        public const int MinUsesBeforeMove = 4;
        public const float MoveChanceStep = 0.12f;
        public const float MaxMoveChance = 0.5f;
        /// <summary>The quality roll counts as this many surges later than loot drops.</summary>
        public const int TierSurgeBonus = 3;
        public const float UseRange = 4f;

        /// <summary>Chance the next pull makes it move, after <paramref name="uses"/> paid pulls at this spot.</summary>
        public static float MoveChance(int uses)
        {
            if (uses < MinUsesBeforeMove) return 0f;
            return Math.Min(MaxMoveChance, (uses - MinUsesBeforeMove + 1) * MoveChanceStep);
        }

        /// <summary>Rolls whether this pull moves the dispenser (only when there is somewhere else to go).</summary>
        public static bool RollMove(DeterministicRandom rng, int uses, bool canMove)
        {
            if (rng == null || !canMove) return false;
            return rng.NextFloat() < MoveChance(uses);
        }

        /// <summary>
        /// Picks one of <paramref name="candidates"/> (weapon catalog indexes), never <paramref name="exclude"/>
        /// (the buyer's current weapon) unless it is the only one. Returns -1 for an empty list.
        /// </summary>
        public static int PickIndex(DeterministicRandom rng, IReadOnlyList<int> candidates, int exclude)
        {
            if (rng == null || candidates == null || candidates.Count == 0) return -1;
            int allowed = 0;
            for (int i = 0; i < candidates.Count; i++) if (candidates[i] != exclude) allowed++;
            if (allowed == 0) return candidates[0];
            int pick = rng.Range(0, allowed);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i] == exclude) continue;
                if (pick-- == 0) return candidates[i];
            }
            return candidates[0];
        }

        public static WeaponTier RollTier(DeterministicRandom rng, int surge) => LootRules.RollTier(rng, Math.Max(1, surge) + TierSurgeBonus);
    }
}
