using System.Collections.Generic;
using Deadfall.Rules.Common;

namespace Deadfall.Rules.Encounters
{
    /// <summary>
    /// Which breach closet a wave enemy appears in (generated maps). Closets are sealed behind boarded openings, so
    /// they need no distance or sight checks. Part of each surge comes from the few breaches nearest the squad (the
    /// pressure is right there); the rest is spread over every open breach within <see cref="SpreadRadius"/> of a
    /// player, so enemies arrive from several sides but never from the far end of the map (T-57).
    /// </summary>
    public static class BreachSpawns
    {
        /// <summary>Share of spawns that use one of the nearest breaches.</summary>
        public const float CloseShare = 0.4f;
        public const int CloseCount = 3;
        /// <summary>The rest spawn at breaches within this many metres of a player...</summary>
        public const float SpreadRadius = 40f;
        /// <summary>...or at the nearest this-many breaches when fewer are that close.</summary>
        public const int MinSpread = 6;

        /// <summary>
        /// <paramref name="distances"/>[i] is how far breach i is from the nearest player (negative = unusable).
        /// Returns the breach index, or -1 when none is usable.
        /// </summary>
        public static int Pick(IReadOnlyList<float> distances, DeterministicRandom rng, List<int> scratch = null)
        {
            List<int> order = scratch ?? new List<int>(distances.Count);
            order.Clear();
            for (int i = 0; i < distances.Count; i++) if (distances[i] >= 0f) order.Add(i);
            if (order.Count == 0) return -1;
            order.Sort((a, b) =>
            {
                int c = distances[a].CompareTo(distances[b]);
                return c != 0 ? c : a.CompareTo(b);
            });
            if (rng.NextFloat() < CloseShare) return order[rng.Range(0, System.Math.Min(CloseCount, order.Count))];
            int within = 0;
            while (within < order.Count && distances[order[within]] <= SpreadRadius) within++;
            int pool = System.Math.Max(within, System.Math.Min(MinSpread, order.Count));
            return order[rng.Range(0, pool)];
        }
    }
}
