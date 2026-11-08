using System;

namespace Deadfall.Rules.World
{
    /// <summary>One playable environment: its room kit, map shape, boss and Operation (M12).</summary>
    public sealed class EnvironmentDef
    {
        public int Index;
        public string Id;
        public string DisplayName;
        /// <summary>Upper-case short name for the HUD ("CINDER RIDGE").</summary>
        public string ShortName;
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
                Index = 0, Id = CinderRidgeKit.Id, DisplayName = "Cinder Ridge Research Complex", ShortName = "CINDER RIDGE",
                Blurb = "A mountain research site: labs, server halls, cold storage and the reactor.",
                CreateKit = CinderRidgeKit.Create, CreateGraph = CinderRidgeKit.SurvivalGraph,
                BossId = "enemy.specimen_zero", MissionId = "mission.cold_start",
            },
            new EnvironmentDef
            {
                Index = 1, Id = HarrowLineKit.Id, DisplayName = "Harrow Line Transit Depot", ShortName = "HARROW LINE",
                Blurb = "An abandoned metro depot: platform halls, a ticket concourse, pump rooms and a flooded turntable.",
                CreateKit = HarrowLineKit.Create, CreateGraph = HarrowLineKit.SurvivalGraph,
                BossId = "enemy.broodmother", MissionId = "mission.last_train",
            },
            new EnvironmentDef
            {
                Index = 2, Id = MercyValeKit.Id, DisplayName = "Mercy Vale General Hospital", ShortName = "MERCY VALE",
                Blurb = "A hospital overrun on the night of the outbreak: wards, operating theatres, the morgue and a sealed isolation wing.",
                CreateKit = MercyValeKit.Create, CreateGraph = MercyValeKit.SurvivalGraph,
                BossId = "enemy.orderly", MissionId = "mission.quarantine",
            },
        };

        /// <summary>Bit 55 marks a Gauntlet map: the short graph with extra ammo and no Supply Caches (every peer builds it from the seed).</summary>
        private const ulong GauntletFlag = 1UL << 55;

        public static bool IsGauntlet(ulong seed) => (seed & Marker) != 0 && (seed & GauntletFlag) != 0;

        /// <summary>Bit 54 marks an Escort map: a medium route from spawn to the far room for the cart.</summary>
        private const ulong EscortFlag = 1UL << 54;

        public static bool IsEscort(ulong seed) => (seed & Marker) != 0 && (seed & EscortFlag) != 0 && (seed & GauntletFlag) == 0;

        /// <summary>An Escort map of an environment from any seed.</summary>
        public static ulong EscortSeed(ulong seed, int index) => EncodeRaw((seed & (EscortFlag - 1UL)) | EscortFlag, index);

        /// <summary>The fixed Gauntlet map of an environment.</summary>
        public static ulong GauntletSeed(int index, ulong mapSeed) => EncodeRaw((mapSeed & (GauntletFlag - 1UL)) | GauntletFlag, index);

        /// <summary>The graph a seed builds: the environment's own, or the short Gauntlet shape.</summary>
        public static GraphTemplate GraphFor(ulong seed)
        {
            GraphTemplate g = ForSeed(seed).CreateGraph();
            if (IsEscort(seed))
            {
                // A route the cart can cover in a run: medium length, four sectors, a boss room at the end.
                g.Id = "escort";
                g.CriticalRoomsMin = 7; g.CriticalRoomsMax = 9;
                g.BranchesMin = 8; g.BranchesMax = 12;
                g.NestedBranchChance = 0.2f;
                g.Secrets = 1;
                g.BossRoom = true;
                g.Sectors = 4;
                return g;
            }
            if (!IsGauntlet(seed)) return g;
            g.Id = "gauntlet";
            g.CriticalRoomsMin = 5; g.CriticalRoomsMax = 6;
            g.BranchesMin = 3; g.BranchesMax = 5;
            g.NestedBranchChance = 0f;
            g.Secrets = 0;
            g.BossRoom = true;
            g.Sectors = 3;
            g.FirstSectorRooms = 2;
            g.MinSpawnsPerSector = 2;
            g.SupplyCaches = false;
            // Slightly more ammo to make up for having no shops: about half the slots get loot, mostly ammo.
            g.LootChance = 0.5f;
            g.AmmoShare = 0.72f;
            return g;
        }

        public static EnvironmentDef At(int index) => index >= 0 && index < All.Length ? All[index] : All[0];

        public static int IndexOf(ulong seed)
        {
            if ((seed & Marker) == 0) return 0;
            int i = (int)((seed >> Shift) & 0x7F);
            return i < All.Length ? i : 0;
        }

        public static EnvironmentDef ForSeed(ulong seed) => At(IndexOf(seed));

        /// <summary>The seed with <paramref name="index"/> folded into its top byte. Never returns 0 (0 means "no map").</summary>
        public static ulong Encode(ulong seed, int index) => EncodeRaw(seed & ~(GauntletFlag | EscortFlag), index);

        /// <summary><see cref="Encode"/> without clearing the mode bits (Gauntlet, Escort).</summary>
        private static ulong EncodeRaw(ulong seed, int index)
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
