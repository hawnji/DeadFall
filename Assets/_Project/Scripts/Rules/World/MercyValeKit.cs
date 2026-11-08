namespace Deadfall.Rules.World
{
    /// <summary>
    /// Room kit for the third environment, Mercy Vale General Hospital: a hospital overrun on the night of the
    /// outbreak. Wards, a reception hall, operating theatres, a pharmacy, the morgue and radiology, linked by long
    /// clinical halls; the isolation wing holds the objective and the quarantine atrium the boss. Same 4 m cells and
    /// generator rules as <see cref="CinderRidgeKit"/>; the builder themes rooms by tag.
    /// </summary>
    public static class MercyValeKit
    {
        public const string Id = "env.mercy_vale";

        public static RoomKit Create()
        {
            var kit = new RoomKit { Id = Id, CellSize = 4f, WallHeight = 5f };

            // Spawn: the ambulance bay. The back (south) wall is closed.
            kit.Templates.Add(Room("ambulance_bay", "Ambulance Bay", RoomRole.Spawn, 4, 4, 1, 1, 10, new[] { "spawn", "bay" }, null,
                N(4, 4), E(4, 4), W(4, 4)));

            // Halls.
            kit.Templates.Add(Room("hall_straight", "Ward Hall", RoomRole.Corridor, 1, 3, 0, 0, 12, new[] { "hall" }, null,
                new SocketDef(0, 0, Dir.South), new SocketDef(0, 2, Dir.North)));
            kit.Templates.Add(Room("hall_long", "Long Hall", RoomRole.Corridor, 1, 5, 0, 1, 7, new[] { "hall" }, null,
                new SocketDef(0, 0, Dir.South), new SocketDef(0, 4, Dir.North)));
            kit.Templates.Add(Room("hall_bend", "Hall Corner", RoomRole.Corridor, 2, 2, 0, 0, 10, new[] { "hall" }, new[] { "#.", "##" },
                new SocketDef(0, 1, Dir.North), new SocketDef(1, 0, Dir.East)));
            kit.Templates.Add(Room("hall_junction", "Nurses' Junction", RoomRole.Corridor, 3, 1, 0, 0, 8, new[] { "hall" }, null,
                new SocketDef(0, 0, Dir.West), new SocketDef(2, 0, Dir.East), new SocketDef(1, 0, Dir.North)));
            kit.Templates.Add(Room("service_hall", "Service Hall", RoomRole.Corridor, 1, 4, 0, 1, 6, new[] { "hall" }, null,
                new SocketDef(0, 0, Dir.South), new SocketDef(0, 3, Dir.North), new SocketDef(0, 2, Dir.East, SocketTypes.Vent)));

            // Main rooms.
            kit.Templates.Add(Room("ward", "Ward", RoomRole.Room, 9, 4, 2, 3, 12, new[] { "ward" }, null,
                new SocketDef(2, 3, Dir.North), new SocketDef(6, 3, Dir.North), new SocketDef(4, 0, Dir.South),
                new SocketDef(8, 1, Dir.East), new SocketDef(0, 2, Dir.West), new SocketDef(8, 2, Dir.East, SocketTypes.Vent)));
            kit.Templates.Add(Room("reception", "Reception Hall", RoomRole.Room, 7, 6, 3, 4, 9, new[] { "reception" },
                new[]
                {
                    ".#####.",
                    "#######",
                    "#######",
                    "#######",
                    "#######",
                    ".#####.",
                },
                new SocketDef(3, 5, Dir.North), new SocketDef(3, 0, Dir.South), new SocketDef(6, 2, Dir.East), new SocketDef(0, 3, Dir.West)));
            kit.Templates.Add(Room("morgue", "Morgue", RoomRole.Room, 8, 6, 2, 3, 9, new[] { "morgue" },
                new[]
                {
                    "....####",
                    "....####",
                    "########",
                    "########",
                    "####....",
                    "####....",
                },
                new SocketDef(6, 5, Dir.North), new SocketDef(1, 0, Dir.South), new SocketDef(0, 2, Dir.West),
                new SocketDef(7, 3, Dir.East), new SocketDef(1, 3, Dir.North, SocketTypes.Vent)));
            kit.Templates.Add(Room("operating_theatre", "Operating Theatre", RoomRole.Room, 6, 5, 2, 2, 8, new[] { "surgery" },
                new[]
                {
                    "######",
                    "######",
                    "..##..",
                    "..##..",
                    "..##..",
                },
                new SocketDef(2, 4, Dir.North), new SocketDef(3, 0, Dir.South), new SocketDef(5, 3, Dir.East),
                new SocketDef(0, 4, Dir.West), new SocketDef(2, 1, Dir.West, SocketTypes.Vent)));
            kit.Templates.Add(Room("pharmacy", "Pharmacy", RoomRole.Room, 5, 4, 2, 2, 7, new[] { "pharmacy" },
                new[]
                {
                    "##...",
                    "##...",
                    "#####",
                    "#####",
                },
                new SocketDef(0, 3, Dir.North), new SocketDef(2, 0, Dir.South), new SocketDef(4, 1, Dir.East), new SocketDef(0, 1, Dir.West)));
            kit.Templates.Add(Room("radiology", "Radiology", RoomRole.Room, 5, 3, 1, 2, 6, new[] { "radiology", "terminal" }, null,
                N(5, 3), S(5, 3), E(5, 3), W(5, 3)));

            // Objective and boss.
            kit.Templates.Add(Room("isolation_wing", "Isolation Wing", RoomRole.Objective, 7, 7, 2, 4, 10, new[] { "isolation", "objective", "terminal" },
                new[]
                {
                    "..###..",
                    ".#####.",
                    "#######",
                    "#######",
                    "#######",
                    ".#####.",
                    "..###..",
                },
                N(7, 7), S(7, 7), E(7, 7), W(7, 7)));
            kit.Templates.Add(Room("quarantine_atrium", "Quarantine Atrium", RoomRole.Boss, 9, 9, 2, 4, 10, new[] { "boss_arena", "quarantine" }, null,
                new SocketDef(4, 0, Dir.South), new SocketDef(4, 8, Dir.North)));

            // Secret.
            kit.Templates.Add(Room("laundry_chute", "Laundry Chute", RoomRole.Secret, 1, 3, 1, 0, 10, new[] { "secret" }, null,
                new SocketDef(0, 0, Dir.South, SocketTypes.Vent)));
            return kit;
        }

        /// <summary>Survival shape: the same length as Harrow Line (long halls between wards).</summary>
        public static GraphTemplate SurvivalGraph() => new GraphTemplate
        {
            Id = "survival",
            CriticalRoomsMin = 14, CriticalRoomsMax = 18,
            BranchesMin = 26, BranchesMax = 32,
            NestedBranchChance = 0.45f,
            Secrets = 3,
            BossRoom = true,
            Sectors = 6,
            FirstSectorRooms = 2,
            MinSpawnsPerSector = 2,
            MinLootSlots = 6,
            MaxAttempts = 32,
        };

        private static RoomTemplate Room(string id, string name, RoomRole role, int w, int d, int loot, int breaches, int weight,
            string[] tags, string[] mask, params SocketDef[] sockets)
        {
            var t = new RoomTemplate
            {
                Id = "room." + id, DisplayName = name, Role = role, Width = w, Depth = d, Mask = mask,
                LootSlots = loot, EnemySpawns = breaches, Weight = weight, Tags = tags ?? System.Array.Empty<string>(),
            };
            t.Sockets.AddRange(sockets);
            return t;
        }

        private static SocketDef N(int w, int d) => new SocketDef(w / 2, d - 1, Dir.North);
        private static SocketDef S(int w, int d) => new SocketDef(w / 2, 0, Dir.South);
        private static SocketDef E(int w, int d) => new SocketDef(w - 1, d / 2, Dir.East);
        private static SocketDef W(int w, int d) => new SocketDef(0, d / 2, Dir.West);
    }
}
