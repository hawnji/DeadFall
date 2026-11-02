namespace Deadfall.Rules.World
{
    /// <summary>
    /// Greybox room kit for the first environment, Cinder Ridge Research Complex (GAME_DESIGN §10.2). Cells are
    /// 4 m. Everything is on one floor for now: stairs and multi-level rooms come with the art pass. Socket
    /// helpers put a doorway in the middle of a wall unless a cell is given.
    /// </summary>
    public static class CinderRidgeKit
    {
        public const string Id = "env.cinder_ridge";

        public static RoomKit Create()
        {
            var kit = new RoomKit { Id = Id, CellSize = 4f, WallHeight = 4f };

            // Spawn: decon airlock. The back (south) wall is closed.
            kit.Templates.Add(Room("decon_airlock", "Decon Airlock", RoomRole.Spawn, 3, 3, 1, 0, 10, new[] { "spawn" },
                N(3, 3), E(3, 3), W(3, 3)));

            // Corridor pieces.
            kit.Templates.Add(Room("corridor_straight", "Corridor", RoomRole.Corridor, 1, 3, 0, 1, 12, null,
                new SocketDef(0, 0, Dir.South), new SocketDef(0, 2, Dir.North)));
            kit.Templates.Add(Room("corridor_long", "Long Corridor", RoomRole.Corridor, 1, 5, 0, 2, 6, null,
                new SocketDef(0, 0, Dir.South), new SocketDef(0, 4, Dir.North)));
            kit.Templates.Add(Room("corridor_l", "Corridor Bend", RoomRole.Corridor, 2, 2, 0, 1, 10, null,
                new SocketDef(0, 0, Dir.South), new SocketDef(1, 1, Dir.East)));
            kit.Templates.Add(Room("corridor_t", "Corridor Junction", RoomRole.Corridor, 3, 1, 0, 1, 8, null,
                new SocketDef(0, 0, Dir.West), new SocketDef(2, 0, Dir.East), new SocketDef(1, 0, Dir.North)));
            kit.Templates.Add(Room("maintenance_tunnels", "Maintenance Tunnels", RoomRole.Corridor, 1, 4, 0, 2, 6, null,
                new SocketDef(0, 0, Dir.South), new SocketDef(0, 3, Dir.North), new SocketDef(0, 2, Dir.East, SocketTypes.Vent)));

            // Main rooms.
            kit.Templates.Add(Room("lab_bay", "Lab Bay", RoomRole.Room, 4, 3, 2, 3, 12, new[] { "lab" },
                new SocketDef(1, 2, Dir.North), new SocketDef(2, 0, Dir.South), E(4, 3), W(4, 3), new SocketDef(3, 0, Dir.East, SocketTypes.Vent)));
            kit.Templates.Add(Room("server_hall", "Server Hall", RoomRole.Room, 5, 3, 2, 3, 10, new[] { "servers" },
                new SocketDef(1, 2, Dir.North), new SocketDef(3, 2, Dir.North), S(5, 3), E(5, 3), W(5, 3), new SocketDef(4, 0, Dir.East, SocketTypes.Vent)));
            kit.Templates.Add(Room("cold_storage", "Cold Storage", RoomRole.Room, 3, 3, 2, 2, 8, new[] { "storage" },
                N(3, 3), S(3, 3), E(3, 3), W(3, 3), new SocketDef(0, 0, Dir.West, SocketTypes.Vent)));
            kit.Templates.Add(Room("cafeteria", "Cafeteria", RoomRole.Room, 4, 4, 3, 4, 8, new[] { "cafeteria" },
                new SocketDef(1, 3, Dir.North), new SocketDef(2, 0, Dir.South), new SocketDef(3, 2, Dir.East), new SocketDef(0, 1, Dir.West)));
            kit.Templates.Add(Room("security_checkpoint", "Security Checkpoint", RoomRole.Room, 3, 2, 1, 2, 6, new[] { "checkpoint", "terminal" },
                new SocketDef(1, 1, Dir.North), new SocketDef(1, 0, Dir.South), new SocketDef(2, 0, Dir.East)));

            // Objective and boss.
            kit.Templates.Add(Room("reactor_control", "Reactor Control", RoomRole.Objective, 5, 5, 2, 4, 10, new[] { "reactor", "objective", "terminal" },
                N(5, 5), S(5, 5), E(5, 5), W(5, 5)));
            kit.Templates.Add(Room("specimen_containment", "Specimen Containment", RoomRole.Boss, 6, 6, 2, 4, 10, new[] { "boss_arena" },
                new SocketDef(2, 0, Dir.South), new SocketDef(3, 5, Dir.North)));

            // Secret.
            kit.Templates.Add(Room("vent_crawl", "Vent Crawl", RoomRole.Secret, 1, 3, 1, 0, 10, new[] { "secret" },
                new SocketDef(0, 0, Dir.South, SocketTypes.Vent)));
            return kit;
        }

        /// <summary>Default Survival shape (GAME_DESIGN §3.1).</summary>
        public static GraphTemplate SurvivalGraph() => new GraphTemplate
        {
            Id = "survival",
            CriticalRoomsMin = 4, CriticalRoomsMax = 6,
            BranchesMin = 2, BranchesMax = 4,
            Secrets = 1,
            BossRoom = true,
            Sectors = 4,
            FirstSectorRooms = 2,
            MinSpawnsPerSector = 2,
            MinLootSlots = 6,
        };

        private static RoomTemplate Room(string id, string name, RoomRole role, int w, int d, int loot, int spawns, int weight,
            string[] tags, params SocketDef[] sockets)
        {
            var t = new RoomTemplate
            {
                Id = "room." + id, DisplayName = name, Role = role, Width = w, Depth = d,
                LootSlots = loot, EnemySpawns = spawns, Weight = weight, Tags = tags ?? System.Array.Empty<string>(),
            };
            t.Sockets.AddRange(sockets);
            return t;
        }

        private static SocketDef N(int w, int d) => new SocketDef(w / 2, d - 1, Dir.North);
        private static SocketDef S(int w, int d) => new SocketDef(w / 2, 0, Dir.South);
        private static SocketDef E(int w, int d) => new SocketDef(w - 1, d / 2, Dir.East);
        private static SocketDef W(int w, int d) => new SocketDef(0, d / 2, Dir.West);
    }

    /// <summary>When sectors open in Survival: sector 1 after surge 3, sector 2 after 7, sector 3 after 12, then every 5.</summary>
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
