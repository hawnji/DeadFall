using System;
using System.Collections.Generic;
using Deadfall.Rules.Common;

namespace Deadfall.Rules.Engineering
{
    /// <summary>
    /// The map's power grid (GAME_DESIGN §10): a few generators feed power-locked doors, the lights, and the squad's
    /// Arc Fences and Sentries. Each online generator adds <see cref="CapacityPerGenerator"/>; structures draw from it
    /// oldest first and the newest ones brown out when it runs short. Generators trip during surges (and all of them
    /// in a Blackout); players restart them by standing at them. Pure numbers so they can be tested and tuned.
    /// </summary>
    public static class PowerGridRules
    {
        public const int MinGenerators = 2;
        public const int MaxGenerators = 3;
        public const int CapacityPerGenerator = 4;
        public const float RestartSeconds = 4f;
        /// <summary>From this surge on, each surge may trip one generator.</summary>
        public const int FirstTripSurge = 4;
        public const float TripChance = 0.3f;
        /// <summary>The trip comes this many seconds into the surge (a random point in the range).</summary>
        public const float TripMinSeconds = 12f;
        public const float TripMaxSeconds = 35f;

        /// <summary>One generator per sector, between <see cref="MinGenerators"/> and <see cref="MaxGenerators"/>.</summary>
        public static int GeneratorCount(int sectors) => Math.Max(MinGenerators, Math.Min(MaxGenerators, sectors));

        public static int Capacity(int online) => Math.Max(0, online) * CapacityPerGenerator;

        /// <summary>Power each structure kind draws (barricades and drones run on nothing).</summary>
        public static int Draw(StructureKind kind) => kind switch
        {
            StructureKind.ArcFence => 2,
            StructureKind.Sentry => 1,
            _ => 0,
        };

        /// <summary>
        /// Which structures get power: <paramref name="draws"/> in build order (oldest first); each is powered while
        /// what is left covers it. Zero-draw structures are always powered. Results go into <paramref name="powered"/>.
        /// </summary>
        public static void Allocate(int capacity, IReadOnlyList<int> draws, List<bool> powered)
        {
            powered.Clear();
            int left = capacity;
            for (int i = 0; i < draws.Count; i++)
            {
                int d = Math.Max(0, draws[i]);
                bool on = d == 0 || d <= left;
                if (on) left -= d;
                powered.Add(on);
            }
        }

        /// <summary>
        /// Rolled when a surge starts: seconds into the surge when one online generator trips, or -1. Never while only
        /// one generator is still up (the squad always keeps some power outside a Blackout).
        /// </summary>
        public static float RollTrip(DeterministicRandom rng, int surge, int online)
        {
            if (rng == null || surge < FirstTripSurge || online < 2) return -1f;
            if (rng.NextFloat() >= TripChance) return -1f;
            return TripMinSeconds + rng.NextFloat() * (TripMaxSeconds - TripMinSeconds);
        }
    }
}
