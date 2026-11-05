namespace Deadfall.Rules.World
{
    /// <summary>
    /// Greybox room kit for the first environment, Cinder Ridge Research Complex (GAME_DESIGN §10.2). Cells are
    /// 4 m. Main rooms are large (20-36 m across) and many are L, T, Z or plus shaped; the builder adds columns
    /// inside the big ones. Everything is on one floor for now. <see cref="RoomTemplate.EnemySpawns"/> is the number
    /// of breach closets the generator tries to give a room. Socket helpers put a doorway in the middle of a wall.
    /// </summary>
    public static class CinderRidgeKit
    {
        public const string Id = "env.cinder_ridge";

        public static RoomKit Create()
        {
            var kit = new RoomKit { Id = Id, CellSize = 4f, WallHeight = 4.5f };

            // Spawn: decon airlock. The back (south) wall is closed.
            kit.Templates.Add(Room("decon_airlock", "Decon Airlock", RoomRole.Spawn, 4, 4, 1, 1, 10, new[] { "spawn" }, null,
                N(4, 4), E(4, 4), W(4, 4)));

            // Corridor pieces.
            kit.Templates.Add(Room("corridor_straight", "Corridor", RoomRole.Corridor, 1, 3, 0, 0, 12, null, null,
                new SocketDef(0, 0, Dir.South), new SocketDef(0, 2, Dir.North)));
            kit.Templates.Add(Room("corridor_long", "Long Corridor", RoomRole.Corridor, 1, 5, 0, 1, 6, null, null,
                new SocketDef(0, 0, Dir.South), new SocketDef(0, 4, Dir.North)));
            kit.Templates.Add(Room("corridor_l", "Corridor Bend", RoomRole.Corridor, 2, 2, 0, 0, 10, null, new[] { "#.", "##" },
                new SocketDef(0, 1, Dir.North), new SocketDef(1, 0, Dir.East)));
            kit.Templates.Add(Room("corridor_t", "Corridor Junction", RoomRole.Corridor, 3, 1, 0, 0, 8, null, null,
                new SocketDef(0, 0, Dir.West), new SocketDef(2, 0, Dir.East), new SocketDef(1, 0, Dir.North)));
            kit.Templates.Add(Room("maintenance_tunnels", "Maintenance Tunnels", RoomRole.Corridor, 1, 4, 0, 1, 6, null, null,
                new SocketDef(0, 0, Dir.South), new SocketDef(0, 3, Dir.North), new SocketDef(0, 2, Dir.East, SocketTypes.Vent)));

            // Main rooms.
            kit.Templates.Add(Room("lab_bay", "Lab Bay", RoomRole.Room, 7, 5, 2, 3, 12, new[] { "lab" },
                new[]
                {
                    "###....",
                    "###....",
                    "#######",
                    "#######",
                    "#######",
                },
                new SocketDef(1, 4, Dir.North), new SocketDef(5, 2, Dir.North), new SocketDef(5, 0, Dir.South),
                new SocketDef(6, 1, Dir.East), new SocketDef(0, 2, Dir.West), new SocketDef(2, 3, Dir.East, SocketTypes.Vent)));
            kit.Templates.Add(Room("server_hall", "Server Hall", RoomRole.Room, 8, 4, 2, 3, 10, new[] { "servers" }, null,
                new SocketDef(2, 3, Dir.North), new SocketDef(5, 3, Dir.North), new SocketDef(4, 0, Dir.South),
                new SocketDef(7, 1, Dir.East), new SocketDef(0, 2, Dir.West), new SocketDef(7, 2, Dir.East, SocketTypes.Vent)));
            kit.Templates.Add(Room("cold_storage", "Cold Storage", RoomRole.Room, 7, 5, 2, 2, 8, new[] { "storage" },
                new[]
                {
                    "#######",
                    "#######",
                    "..###..",
                    "..###..",
                    "..###..",
                },
                new SocketDef(3, 4, Dir.North), new SocketDef(3, 0, Dir.South), new SocketDef(6, 3, Dir.East),
                new SocketDef(0, 3, Dir.West), new SocketDef(2, 1, Dir.West, SocketTypes.Vent)));
            kit.Templates.Add(Room("cafeteria", "Cafeteria", RoomRole.Room, 7, 6, 3, 4, 8, new[] { "cafeteria" },
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
            kit.Templates.Add(Room("atrium", "Atrium", RoomRole.Room, 8, 6, 2, 3, 8, new[] { "atrium" },
                new[]
                {
                    "####....",
                    "####....",
                    "########",
                    "########",
                    "....####",
                    "....####",
                },
                new SocketDef(1, 5, Dir.North), new SocketDef(6, 0, Dir.South), new SocketDef(0, 3, Dir.West),
                new SocketDef(7, 2, Dir.East), new SocketDef(6, 3, Dir.North, SocketTypes.Vent)));
            kit.Templates.Add(Room("security_checkpoint", "Security Checkpoint", RoomRole.Room, 5, 3, 1, 2, 6, new[] { "checkpoint", "terminal" }, null,
                N(5, 3), S(5, 3), E(5, 3), W(5, 3)));

            // Objective and boss.
            kit.Templates.Add(Room("reactor_control", "Reactor Control", RoomRole.Objective, 7, 7, 2, 4, 10, new[] { "reactor", "objective", "terminal" },
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
            kit.Templates.Add(Room("specimen_containment", "Specimen Containment", RoomRole.Boss, 9, 8, 2, 4, 10, new[] { "boss_arena" }, null,
                new SocketDef(4, 0, Dir.South), new SocketDef(4, 7, Dir.North)));

            // Secret.
            kit.Templates.Add(Room("vent_crawl", "Vent Crawl", RoomRole.Secret, 1, 3, 1, 0, 10, new[] { "secret" }, null,
                new SocketDef(0, 0, Dir.South, SocketTypes.Vent)));
            return kit;
        }

        /// <summary>Default Survival shape (GAME_DESIGN §3.1): small start, then a locked door before every main room.</summary>
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

    /// <summary>
    /// Original surge-based sector timetable (T-33). Not used at runtime since the map rework: players open sectors by
    /// buying doors. Kept for the layout validator and tests.
    /// </summary>
    public static class SectorSchedule
    {
        public static int UnlockAfterSurge(int sector)
        {
            if (sector <= 0) return 0;
            if (sector == 1) return 3;
            if (sector == 2) return 7;
            return 12 + (sector - 3) * 5;
        }

        /// <summary>Sectors open (count, sector 0 included) once <paramref name="completedSurges"/> surges are cleared.</summary>
        public static int OpenSectors(int completedSurges, int sectorCount)
        {
            int open = 1;
            while (open < sectorCount && completedSurges >= UnlockAfterSurge(open)) open++;
            return open;
        }
    }
}
