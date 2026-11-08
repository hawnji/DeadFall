using System;

namespace Deadfall.Rules.Encounters
{
    /// <summary>
    /// Gauntlet: a short, hard run for speedruns and replays. The same small map every time (per environment), one
    /// fixed loadout for everyone, no shops, free doors, extra ammo crates, six surges that play like surges 5-10
    /// (so the last is a Specimen), short fixed lulls, and a clock from the first surge to the win.
    /// </summary>
    public static class GauntletRules
    {
        /// <summary>Surges in a Gauntlet run.</summary>
        public const int Surges = 6;
        /// <summary>Surge N plays like surge N + this (difficulty, type rotation): 1-6 play as 5-10, Specimen last.</summary>
        public const int SurgeOffset = 4;
        /// <summary>Fixed lull between surges, in seconds (no shops to visit, so just a breather).</summary>
        public const float LullSeconds = 8f;
        /// <summary>The time to beat, shown next to the clock and in the Trophy Room (first guess, tune in play).</summary>
        public const float ParSeconds = 480f;
        /// <summary>The fixed map seed (low bits) for every Gauntlet, so times compare fairly.</summary>
        public const ulong MapSeed = 0x6A0E7E70UL;

        /// <summary>The difficulty surge used to plan run surge <paramref name="surge"/>.</summary>
        public static int PlannedSurge(int surge) => Math.Max(1, surge) + SurgeOffset;

        /// <summary>"7:42.3" (minutes, seconds, tenths); hours roll into minutes.</summary>
        public static string Clock(double seconds)
        {
            if (seconds < 0) seconds = 0;
            long tenths = (long)Math.Floor(seconds * 10.0);
            long m = tenths / 600, s = tenths / 10 % 60, t = tenths % 10;
            return $"{m}:{s:00}.{t}";
        }
    }
}
