using System;
using Deadfall.Rules.Common;

namespace Deadfall.Rules.Encounters
{
    /// <summary>
    /// Lull ambushes (GAME_DESIGN §7): a short surprise spawn during a lull, at most once every
    /// <see cref="MinGapSurges"/> surges, announced <see cref="WarningSeconds"/> ahead. The ambushers count toward the
    /// coming surge if they are still alive when it starts.
    /// </summary>
    public sealed class AmbushScheduler
    {
        /// <summary>First surge whose lull can hold an ambush.</summary>
        public const int FirstSurge = 3;
        public const int MinGapSurges = 5;
        public const float Chance = 0.35f;
        public const float WarningSeconds = 3f;
        /// <summary>Shorter lulls (Operations extraction, Endless late game) never get one.</summary>
        public const float MinLullSeconds = 15f;
        public const int MinCount = 3;
        public const int MaxCount = 10;

        private int _lastSurge = int.MinValue / 2;

        public void Reset() => _lastSurge = int.MinValue / 2;

        /// <summary>
        /// Rolled when the lull before <paramref name="upcomingSurge"/> starts. Returns the seconds into the lull when the
        /// ambush spawns, or -1 for none. Always leaves room for the warning before and the fight after.
        /// </summary>
        public float Roll(DeterministicRandom rng, int upcomingSurge, SurgeType upcomingType, float lullSeconds)
        {
            if (rng == null || upcomingSurge < FirstSurge || lullSeconds < MinLullSeconds) return -1f;
            if (upcomingType == SurgeType.Specimen) return -1f;
            if (upcomingSurge - _lastSurge < MinGapSurges) return -1f;
            if (rng.NextFloat() >= Chance) return -1f;
            _lastSurge = upcomingSurge;
            float earliest = Math.Max(WarningSeconds + 2f, lullSeconds * 0.3f);
            float latest = Math.Max(earliest, lullSeconds * 0.65f);
            return earliest + rng.NextFloat() * (latest - earliest);
        }

        /// <summary>How many infected jump the squad.</summary>
        public static int Count(int surge, int players) => Math.Max(MinCount, Math.Min(MaxCount, 2 + surge / 3 + Math.Max(0, players - 1)));

        /// <summary>How many of them are Creepers (the rest are Husks).</summary>
        public static int Creepers(int count) => count / 3;
    }
}
