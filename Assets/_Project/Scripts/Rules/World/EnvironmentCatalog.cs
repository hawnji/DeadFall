using System;

namespace Deadfall.Rules.World
{
    /// <summary>One playable environment: its room kit, map shape, boss and Operation (M12).</summary>
    public sealed class EnvironmentDef
    {
        public int Index;
        public string Id;
        public string DisplayName;
        public string Blurb;
        public Func<RoomKit> CreateKit;
        public Func<GraphTemplate> CreateGraph;
        /// <summary>Enemy content id of the boss Specimen surges prefer here (null: any boss).</summary>
        public string BossId;
        /// <summary>Operation mission played here (see ObjectiveRules.MissionById).</summary>
        public string MissionId;
    }

    /// <summary>
    /// Every environment, in a fixed order (the index travels over the network). The environment is carried
    /// inside the map seed: bit 63 marks an encoded seed and bits 56-62 hold the index, so every place that already
    /// syncs or types a seed picks the right environment. Plain seeds (bit 63 clear) are Cinder Ridge.
    /// </summary>
    public static class EnvironmentCatalog
    {
        private const ulong Marker = 1UL << 63;
        private const int Shift = 56;
        private const ulong LowMask = (1UL << Shift) - 1UL;

        public static readonly EnvironmentDef[] All =
        {
            new EnvironmentDef
            {
                Index = 0, Id = CinderRidgeKit.Id, DisplayName = "Cinder Ridge Research Complex",
                Blurb = "A mountain research site: labs, server halls, cold storage and the reactor.",
                CreateKit = CinderRidgeKit.Create, CreateGraph = CinderRidgeKit.SurvivalGraph,
                BossId = "enemy.specimen_zero", MissionId = "mission.cold_start",
            },
            new EnvironmentDef
            {
                Index = 1, Id = HarrowLineKit.Id, DisplayName = "Harrow Line Transit Depot",
                Blurb = "An abandoned metro depot: platform halls, a ticket concourse, pump rooms and a flooded turntable.",
                CreateKit = HarrowLineKit.Create, CreateGraph = HarrowLineKit.SurvivalGraph,
                BossId = "enemy.broodmother", MissionId = "mission.last_train",
            },
        };

        public static EnvironmentDef At(int index) => index >= 0 && index < All.Length ? All[index] : All[0];

        public static int IndexOf(ulong seed)
        {
            if ((seed & Marker) == 0) return 0;
            int i = (int)((seed >> Shift) & 0x7F);
            return i < All.Length ? i : 0;
        }

        public static EnvironmentDef ForSeed(ulong seed) => At(IndexOf(seed));

        /// <summary>The seed with <paramref name="index"/> folded into its top byte. Never returns 0 (0 means "no map").</summary>
        public static ulong Encode(ulong seed, int index)
        {
            index = Math.Max(0, Math.Min(index, All.Length - 1));
            ulong low = seed & LowMask;
            if (low == 0) low = 1;
            return Marker | ((ulong)index << Shift) | low;
        }

        public static EnvironmentDef ById(string id)
        {
            foreach (EnvironmentDef e in All) if (e.Id == id) return e;
            return null;
        }
    }
}
