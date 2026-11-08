using System;
using System.Collections.Generic;

namespace Deadfall.Rules.World
{
    /// <summary>Grid directions seen from above: North is +Z, East is +X.</summary>
    public enum Dir { North = 0, East = 1, South = 2, West = 3 }

    public static class DirExt
    {
        public static Dir Opposite(this Dir d) => (Dir)(((int)d + 2) & 3);
        public static Dir Rotate(this Dir d, int quarterTurns) => (Dir)(((int)d + quarterTurns) & 3);
        public static int Dx(this Dir d) => d == Dir.East ? 1 : d == Dir.West ? -1 : 0;
        public static int Dz(this Dir d) => d == Dir.North ? 1 : d == Dir.South ? -1 : 0;
    }

    /// <summary>A grid cell. One cell is <see cref="RoomKit.CellSize"/> metres square.</summary>
    public readonly struct Cell : IEquatable<Cell>
    {
        public readonly int X, Z;
        public Cell(int x, int z) { X = x; Z = z; }
        public Cell Step(Dir d) => new Cell(X + d.Dx(), Z + d.Dz());
        public long Key => ((long)X << 32) ^ (uint)Z;
        public bool Equals(Cell o) => X == o.X && Z == o.Z;
        public override bool Equals(object obj) => obj is Cell c && Equals(c);
        public override int GetHashCode() => X * 73856093 ^ Z * 19349663;
        public override string ToString() => $"({X},{Z})";
    }

    /// <summary>What a room is for. Generation places rooms by role; objectives and events look rooms up by role and tag.</summary>
    public enum RoomRole { Spawn, Corridor, Room, Objective, Boss, Secret }

    /// <summary>
    /// A typed doorway on a room's edge. (X, Z) is the room-local cell the doorway sits in, and Facing is the wall
    /// it goes through. Two sockets connect when their types match, they face each other and their cells touch.
    /// </summary>
    public sealed class SocketDef
    {
        public readonly int X, Z;
        public readonly Dir Facing;
        public readonly string Type;
        public SocketDef(int x, int z, Dir facing, string type = SocketTypes.Door) { X = x; Z = z; Facing = facing; Type = type; }
    }

    public static class SocketTypes
    {
        /// <summary>Standard 2 m doorway.</summary>
        public const string Door = "door";
        /// <summary>Crawl-height vent opening that leads to a secret room.</summary>
        public const string Vent = "vent";
    }

    /// <summary>
    /// One room or corridor piece of a kit. The footprint is a Width x Depth box of cells, optionally cut into an
    /// L, T, Z or plus shape by <see cref="Mask"/>.
    /// </summary>
    public sealed class RoomTemplate
    {
        public string Id;
        public string DisplayName;
        public RoomRole Role;
        public int Width = 1, Depth = 1;
        /// <summary>
        /// Optional shape: one string per row, top row first (z = Depth - 1), '#' for a room cell and '.' for empty.
        /// Null means the full rectangle.
        /// </summary>
        public string[] Mask;
        public readonly List<SocketDef> Sockets = new List<SocketDef>();
        public string[] Tags = Array.Empty<string>();
        /// <summary>Supply cache / pickup slots (D-05: caches are randomised across tagged slots).</summary>
        public int LootSlots;
        /// <summary>Enemy spawn points inside the room (vents, hatches).</summary>
        public int EnemySpawns;
        /// <summary>Relative pick weight among templates of the same role.</summary>
        public int Weight = 10;

        public bool HasTag(string tag) => Array.IndexOf(Tags, tag) >= 0;

        /// <summary>True if the template-local cell is part of the room.</summary>
        public bool HasCell(int lx, int lz)
        {
            if (lx < 0 || lz < 0 || lx >= Width || lz >= Depth) return false;
            if (Mask == null) return true;
            string row = Mask[Depth - 1 - lz];
            return lx < row.Length && row[lx] == '#';
        }

        public int CellCount
        {
            get
            {
                int n = 0;
                for (int x = 0; x < Width; x++) for (int z = 0; z < Depth; z++) if (HasCell(x, z)) n++;
                return n;
            }
        }

        /// <summary>True when the template is not a plain rectangle.</summary>
        public bool IsShaped => Mask != null && CellCount < Width * Depth;
    }

    /// <summary>A set of room templates that share a visual style (an environment's room kit).</summary>
    public sealed class RoomKit
    {
        public string Id;
        public float CellSize = 4f;
        public float WallHeight = 4f;
        public readonly List<RoomTemplate> Templates = new List<RoomTemplate>();

        public RoomTemplate Find(string id)
        {
            foreach (RoomTemplate t in Templates) if (t.Id == id) return t;
            return null;
        }

        public IEnumerable<RoomTemplate> WithRole(RoomRole role)
        {
            foreach (RoomTemplate t in Templates) if (t.Role == role) yield return t;
        }
    }

    /// <summary>Shape of a map: how long the main route is, how many side rooms, how many sectors.</summary>
    public sealed class GraphTemplate
    {
        public string Id = "survival";
        /// <summary>Main rooms between spawn and the objective room (corridors not counted).</summary>
        public int CriticalRoomsMin = 4, CriticalRoomsMax = 6;
        /// <summary>Side branches hanging off main-route rooms.</summary>
        public int BranchesMin = 2, BranchesMax = 4;
        /// <summary>Chance a side branch grows off another side room instead of the main route (bigger, wider maps).</summary>
        public float NestedBranchChance = 0f;
        public int Secrets = 1;
        public bool BossRoom = true;
        /// <summary>Sectors (spawn sector included). Sector 0 is open at the start.</summary>
        public int Sectors = 4;
        /// <summary>Main rooms (spawn included) in the first sector: GAME_DESIGN §3.1 "starts small (3–4 rooms)".</summary>
        public int FirstSectorRooms = 2;
        /// <summary>Validation: each sector needs at least this many enemy spawn points.</summary>
        public int MinSpawnsPerSector = 4;
        /// <summary>Validation: the whole map needs at least this many loot slots.</summary>
        public int MinLootSlots = 6;
        public int MaxAttempts = 16;
        /// <summary>One Supply Cache per sector (off in Gauntlet: no shops).</summary>
        public bool SupplyCaches = true;
        /// <summary>Chance a free loot slot gets an ammo crate or medkit, and the share of those that are ammo.</summary>
        public float LootChance = 0.35f, AmmoShare = 0.65f;
        /// <summary>Chance a main-route link is a corridor piece rather than a direct room-to-room door.</summary>
        public float CorridorChance = 0.7f;
        /// <summary>Chance a side branch sits behind its own locked door (opened with credits).</summary>
        public float BranchDoorChance = 0.6f;
        /// <summary>Chance a sector door opens from a power relay or security switch instead of credits.</summary>
        public float SpecialDoorChance = 0.35f;
        /// <summary>Credit price of the door into sector 1; each later sector adds <see cref="SectorDoorCostStep"/>.</summary>
        public int SectorDoorCost = 750;
        public int SectorDoorCostStep = 250;
        public int BranchDoorCost = 500;
    }

    /// <summary>What opens a locked door.</summary>
    public enum DoorLock : byte
    {
        None,
        /// <summary>Bought with Field Credits at the door.</summary>
        Credits,
        /// <summary>A security switch somewhere earlier on the map.</summary>
        Switch,
        /// <summary>A power relay somewhere earlier on the map; the door opens once power is restored.</summary>
        Power,
    }

    /// <summary>
    /// A boarded opening in a room's outer wall with a sealed closet behind it (the cell <see cref="Closet"/>).
    /// Wave enemies appear in the closet and have to break the boards to get in; players can't enter closets.
    /// </summary>
    public struct Breach
    {
        public int Room;
        /// <summary>The room cell the opening is in.</summary>
        public Cell Inner;
        /// <summary>The wall the opening goes through (out of the room).</summary>
        public Dir Facing;
        public Cell Closet => Inner.Step(Facing);
    }

    /// <summary>A room placed on the grid. Origin is the lowest (x, z) cell of its rotated footprint.</summary>
    public sealed class PlacedRoom
    {
        public int Index;
        public RoomTemplate Template;
        public Cell Origin;
        /// <summary>Quarter turns clockwise seen from above.</summary>
        public int Rotation;
        public int Sector;
        /// <summary>Rooms between this one and spawn.</summary>
        public int Depth;
        public bool OnCriticalPath;
        /// <summary>Room this one grew from (-1 for spawn).</summary>
        public int Parent = -1;

        public int Width => (Rotation & 1) == 0 ? Template.Width : Template.Depth;

        /// <summary>Template-local cell of a world cell inside the bounding box (inverse of <see cref="ToWorld"/>).</summary>
        public void ToLocal(Cell c, out int lx, out int lz)
        {
            int w = Template.Width, d = Template.Depth, dx = c.X - Origin.X, dz = c.Z - Origin.Z;
            switch (Rotation & 3)
            {
                case 0: lx = dx; lz = dz; break;
                case 1: lx = w - 1 - dz; lz = dx; break;
                case 2: lx = w - 1 - dx; lz = d - 1 - dz; break;
                default: lx = dz; lz = d - 1 - dx; break;
            }
        }
        public int Depth2 => (Rotation & 1) == 0 ? Template.Depth : Template.Width;

        /// <summary>World cell of a template-local cell.</summary>
        public Cell ToWorld(int lx, int lz)
        {
            int w = Template.Width, d = Template.Depth;
            switch (Rotation & 3)
            {
                case 0: return new Cell(Origin.X + lx, Origin.Z + lz);
                case 1: return new Cell(Origin.X + lz, Origin.Z + (w - 1 - lx));
                case 2: return new Cell(Origin.X + (w - 1 - lx), Origin.Z + (d - 1 - lz));
                default: return new Cell(Origin.X + (d - 1 - lz), Origin.Z + lx);
            }
        }

        public Cell SocketCell(int socket) { SocketDef s = Template.Sockets[socket]; return ToWorld(s.X, s.Z); }
        public Dir SocketFacing(int socket) => Template.Sockets[socket].Facing.Rotate(Rotation);

        public IEnumerable<Cell> Cells()
        {
            for (int x = 0; x < Width; x++)
                for (int z = 0; z < Depth2; z++)
                {
                    var c = new Cell(Origin.X + x, Origin.Z + z);
                    if (Contains(c)) yield return c;
                }
        }

        public bool Contains(Cell c)
        {
            if (c.X < Origin.X || c.X >= Origin.X + Width || c.Z < Origin.Z || c.Z >= Origin.Z + Depth2) return false;
            if (Template.Mask == null) return true;
            ToLocal(c, out int lx, out int lz);
            return Template.HasCell(lx, lz);
        }

        /// <summary>The room cell nearest the middle of its bounding box (always inside, even for L shapes).</summary>
        public Cell CenterCell
        {
            get
            {
                float cx = Origin.X + (Width - 1) * 0.5f, cz = Origin.Z + (Depth2 - 1) * 0.5f;
                Cell best = Origin;
                float bestD = float.MaxValue;
                foreach (Cell c in Cells())
                {
                    float d = (c.X - cx) * (c.X - cx) + (c.Z - cz) * (c.Z - cz);
                    if (d < bestD) { bestD = d; best = c; }
                }
                return best;
            }
        }
    }

    /// <summary>A doorway between two placed rooms.</summary>
    public struct Connection
    {
        public int RoomA, SocketA, RoomB, SocketB;
        /// <summary>Locked until this sector opens (0 = always open).</summary>
        public int UnlockSector;
        /// <summary>True for a vent into a secret room (hidden panel instead of a door).</summary>
        public bool Secret;
        /// <summary>What opens the door here (None: open doorway).</summary>
        public DoorLock Lock;
        /// <summary>Credit price for <see cref="DoorLock.Credits"/>.</summary>
        public int Cost;
        /// <summary>Room holding the switch or power relay for <see cref="DoorLock.Switch"/> / <see cref="DoorLock.Power"/>, else -1.</summary>
        public int SwitchRoom;
        /// <summary>A door between sectors (as opposed to a side door into a branch).</summary>
        public bool Locked => UnlockSector > 0;
        public bool IsDoor => Lock != DoorLock.None;
    }

    public enum LootKind { SupplyCache, AmmoCrate, Medkit, SecretStash }

    public struct LootPlacement
    {
        public int Room;
        public int Slot;
        public LootKind Kind;
    }

    /// <summary>The output of <see cref="LayoutGenerator"/>: every peer builds the same one from the seed (T-09).</summary>
    public sealed class Layout
    {
        public ulong Seed;
        public int Attempt;
        public bool UsedFallback;
        public RoomKit Kit;
        public GraphTemplate Graph;
        public readonly List<PlacedRoom> Rooms = new List<PlacedRoom>();
        public readonly List<Connection> Connections = new List<Connection>();
        public readonly List<LootPlacement> Loot = new List<LootPlacement>();
        public readonly List<Breach> Breaches = new List<Breach>();
        public int SectorCount;

        /// <summary>Index of the room containing the cell, or -1.</summary>
        public int RoomAt(Cell c)
        {
            foreach (PlacedRoom r in Rooms) if (r.Contains(c)) return r.Index;
            return -1;
        }

        /// <summary>
        /// The locked side door (not a sector door) on the way from spawn to <paramref name="room"/>, nearest the
        /// room, or -1. Rooms form a tree, so walking parents finds it.
        /// </summary>
        public int SideDoorTo(int room, Func<int, bool> isOpen = null)
        {
            int r = room;
            while (r > 0)
            {
                int parent = Rooms[r].Parent;
                for (int i = 0; i < Connections.Count; i++)
                {
                    Connection c = Connections[i];
                    if (!c.IsDoor || c.Locked) continue;
                    if ((c.RoomA == parent && c.RoomB == r) || (c.RoomB == parent && c.RoomA == r))
                        if (isOpen == null || !isOpen(i)) return i;
                }
                r = parent;
            }
            return -1;
        }

        public PlacedRoom SpawnRoom => Rooms.Count > 0 ? Rooms[0] : null;

        public PlacedRoom FirstWithRole(RoomRole role)
        {
            foreach (PlacedRoom r in Rooms) if (r.Template.Role == role) return r;
            return null;
        }

        /// <summary>True if the socket on this room has a connection.</summary>
        public bool IsConnected(int room, int socket, out Connection connection)
        {
            foreach (Connection c in Connections)
                if ((c.RoomA == room && c.SocketA == socket) || (c.RoomB == room && c.SocketB == socket)) { connection = c; return true; }
            connection = default;
            return false;
        }

        public int CountInSector(int sector)
        {
            int n = 0;
            foreach (PlacedRoom r in Rooms) if (r.Sector == sector) n++;
            return n;
        }

        /// <summary>Order-sensitive digest of the layout, for determinism checks and the debug overlay.</summary>
        public ulong Hash()
        {
            ulong h = 1469598103934665603UL;
            void Mix(long v) { h ^= (ulong)v; h *= 1099511628211UL; }
            foreach (PlacedRoom r in Rooms) { Mix(StableHash(r.Template.Id)); Mix(r.Origin.Key); Mix(r.Rotation); Mix(r.Sector); }
            foreach (Connection c in Connections) { Mix(c.RoomA); Mix(c.SocketA); Mix(c.RoomB); Mix(c.SocketB); Mix(c.UnlockSector); Mix((int)c.Lock); Mix(c.Cost); Mix(c.SwitchRoom); }
            foreach (Breach b in Breaches) { Mix(b.Room); Mix(b.Inner.Key); Mix((int)b.Facing); }
            foreach (LootPlacement l in Loot) { Mix(l.Room); Mix(l.Slot); Mix((int)l.Kind); }
            return h;
        }

        /// <summary>String hash that is the same on every runtime (string.GetHashCode is not).</summary>
        public static long StableHash(string s)
        {
            ulong h = 1469598103934665603UL;
            if (s != null) foreach (char ch in s) { h ^= ch; h *= 1099511628211UL; }
            return (long)h;
        }
    }
}
