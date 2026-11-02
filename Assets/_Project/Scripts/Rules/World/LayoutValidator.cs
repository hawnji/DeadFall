using System.Collections.Generic;

namespace Deadfall.Rules.World
{
    /// <summary>
    /// Checks a layout (GAME_DESIGN §10.1 step 3): no overlapping rooms, every connection joins two compatible
    /// facing sockets, every room is reachable, required rooms exist, sectors open in order (each sector is
    /// reachable once the sectors before it are open), enough enemy spawn points per sector, enough loot, a Supply
    /// Cache in the spawn sector and a bounded footprint. Returns readable errors; empty means valid.
    /// </summary>
    public static class LayoutValidator
    {
        /// <summary>Largest map side in cells (keeps the runtime NavMesh bake bounded).</summary>
        public const int MaxExtentCells = 48;

        public static List<string> Validate(Layout layout)
        {
            var errors = new List<string>();
            if (layout == null || layout.Rooms.Count == 0) { errors.Add("Layout has no rooms."); return errors; }
            GraphTemplate g = layout.Graph ?? new GraphTemplate();
            List<PlacedRoom> rooms = layout.Rooms;

            if (rooms[0].Template.Role != RoomRole.Spawn) errors.Add("Room 0 is not the spawn room.");

            // Overlap and extent.
            var cells = new Dictionary<long, int>();
            int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
            foreach (PlacedRoom r in rooms)
            {
                foreach (Cell c in r.Cells())
                {
                    if (cells.TryGetValue(c.Key, out int other)) errors.Add($"Rooms {other} and {r.Index} overlap at {c}.");
                    else cells[c.Key] = r.Index;
                    if (c.X < minX) minX = c.X;
                    if (c.Z < minZ) minZ = c.Z;
                    if (c.X > maxX) maxX = c.X;
                    if (c.Z > maxZ) maxZ = c.Z;
                }
            }
            if (maxX - minX + 1 > MaxExtentCells || maxZ - minZ + 1 > MaxExtentCells)
                errors.Add($"Map is {maxX - minX + 1}x{maxZ - minZ + 1} cells (max {MaxExtentCells}).");

            // Connections.
            var usedSockets = new HashSet<long>();
            var adjacency = new List<(int to, int unlock)>[rooms.Count];
            for (int i = 0; i < rooms.Count; i++) adjacency[i] = new List<(int, int)>();
            foreach (Connection c in layout.Connections)
            {
                if (c.RoomA < 0 || c.RoomA >= rooms.Count || c.RoomB < 0 || c.RoomB >= rooms.Count)
                {
                    errors.Add($"Connection {c.RoomA}-{c.RoomB} points at a missing room.");
                    continue;
                }
                PlacedRoom a = rooms[c.RoomA], b = rooms[c.RoomB];
                if (c.SocketA < 0 || c.SocketA >= a.Template.Sockets.Count || c.SocketB < 0 || c.SocketB >= b.Template.Sockets.Count)
                {
                    errors.Add($"Connection {c.RoomA}-{c.RoomB} uses a missing socket.");
                    continue;
                }
                if (!usedSockets.Add(((long)c.RoomA << 8) | (uint)c.SocketA) || !usedSockets.Add(((long)c.RoomB << 8) | (uint)c.SocketB))
                    errors.Add($"A socket in connection {c.RoomA}-{c.RoomB} is used twice.");
                if (a.Template.Sockets[c.SocketA].Type != b.Template.Sockets[c.SocketB].Type)
                    errors.Add($"Connection {c.RoomA}-{c.RoomB} joins different socket types.");
                if (a.SocketFacing(c.SocketA) != b.SocketFacing(c.SocketB).Opposite())
                    errors.Add($"Connection {c.RoomA}-{c.RoomB} sockets don't face each other.");
                if (!a.SocketCell(c.SocketA).Step(a.SocketFacing(c.SocketA)).Equals(b.SocketCell(c.SocketB)))
                    errors.Add($"Connection {c.RoomA}-{c.RoomB} sockets aren't adjacent.");
                int expected = a.Sector == b.Sector ? 0 : System.Math.Max(a.Sector, b.Sector);
                if (c.UnlockSector != expected)
                    errors.Add($"Connection {c.RoomA}-{c.RoomB} unlocks with sector {c.UnlockSector}, expected {expected}.");
                adjacency[c.RoomA].Add((c.RoomB, c.UnlockSector));
                adjacency[c.RoomB].Add((c.RoomA, c.UnlockSector));
            }

            // Reachability, all doors open.
            bool[] all = Reach(adjacency, rooms.Count, int.MaxValue);
            for (int i = 0; i < rooms.Count; i++)
                if (!all[i]) errors.Add($"Room {i} ({rooms[i].Template.Id}) is unreachable.");

            // Sectors open in order.
            for (int s = 0; s < layout.SectorCount; s++)
            {
                bool[] open = Reach(adjacency, rooms.Count, s);
                for (int i = 0; i < rooms.Count; i++)
                {
                    if (rooms[i].Sector < 0 || rooms[i].Sector >= layout.SectorCount)
                    {
                        if (s == 0) errors.Add($"Room {i} has sector {rooms[i].Sector} outside 0..{layout.SectorCount - 1}.");
                        continue;
                    }
                    if (rooms[i].Sector <= s && !open[i]) errors.Add($"Room {i} (sector {rooms[i].Sector}) can't be reached with sectors 0..{s} open.");
                    if (rooms[i].Sector > s && open[i]) errors.Add($"Room {i} (sector {rooms[i].Sector}) is reachable before its sector opens.");
                }
            }
            if (rooms[0].Sector != 0) errors.Add("The spawn room is not in sector 0.");

            // Required rooms.
            int spawns = 0, objectives = 0, bosses = 0, secrets = 0;
            foreach (PlacedRoom r in rooms)
            {
                switch (r.Template.Role)
                {
                    case RoomRole.Spawn: spawns++; break;
                    case RoomRole.Objective: objectives++; break;
                    case RoomRole.Boss: bosses++; break;
                    case RoomRole.Secret: secrets++; break;
                }
            }
            if (spawns != 1) errors.Add($"Expected one spawn room, found {spawns}.");
            if (objectives < 1) errors.Add("No objective room.");
            if (g.BossRoom && bosses < 1) errors.Add("No boss room.");
            if (secrets < g.Secrets) errors.Add($"Expected {g.Secrets} secret rooms, found {secrets}.");

            // Enemy spawn points per sector.
            for (int s = 0; s < layout.SectorCount; s++)
            {
                int n = 0;
                foreach (PlacedRoom r in rooms) if (r.Sector == s) n += r.Template.EnemySpawns;
                if (n < g.MinSpawnsPerSector) errors.Add($"Sector {s} has {n} enemy spawn points (min {g.MinSpawnsPerSector}).");
            }

            // Loot.
            int slots = 0;
            foreach (PlacedRoom r in rooms) slots += r.Template.LootSlots;
            if (slots < g.MinLootSlots) errors.Add($"Map has {slots} loot slots (min {g.MinLootSlots}).");
            bool cacheInSpawnSector = false;
            var lootKeys = new HashSet<long>();
            foreach (LootPlacement l in layout.Loot)
            {
                if (l.Room < 0 || l.Room >= rooms.Count || l.Slot < 0 || l.Slot >= rooms[l.Room].Template.LootSlots)
                {
                    errors.Add($"Loot points at a missing slot (room {l.Room}, slot {l.Slot}).");
                    continue;
                }
                if (!lootKeys.Add(((long)l.Room << 8) | (uint)l.Slot)) errors.Add($"Two loot items share room {l.Room} slot {l.Slot}.");
                if (l.Kind == LootKind.SupplyCache && rooms[l.Room].Sector == 0) cacheInSpawnSector = true;
            }
            if (!cacheInSpawnSector) errors.Add("No Supply Cache in the spawn sector (D-05).");
            return errors;
        }

        /// <summary>Rooms reachable from spawn using connections that unlock at or below <paramref name="openSector"/>.</summary>
        private static bool[] Reach(List<(int to, int unlock)>[] adjacency, int count, int openSector)
        {
            var seen = new bool[count];
            var queue = new Queue<int>();
            seen[0] = true;
            queue.Enqueue(0);
            while (queue.Count > 0)
            {
                int r = queue.Dequeue();
                foreach ((int to, int unlock) in adjacency[r])
                {
                    if (seen[to] || unlock > openSector) continue;
                    seen[to] = true;
                    queue.Enqueue(to);
                }
            }
            return seen;
        }
    }
}
