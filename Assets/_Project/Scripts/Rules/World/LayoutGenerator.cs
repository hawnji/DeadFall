using System.Collections.Generic;
using Deadfall.Rules.Common;

namespace Deadfall.Rules.World
{
    /// <summary>
    /// Builds a map layout from a kit, a graph template and a seed (GAME_DESIGN §10.1):
    /// spawn room → main route of rooms (often joined by corridor pieces) ending in the objective room → boss room
    /// off the objective → side branches → secret vent rooms, then sectors, locked doors (credits, switch or power),
    /// loot, and breach closets for enemy spawns. Each attempt is checked by
    /// <see cref="LayoutValidator"/>; a failed attempt retries with the next sub-seed, and after
    /// <see cref="GraphTemplate.MaxAttempts"/> it falls back to <see cref="FallbackSeed"/>. Rooms form a tree
    /// (no loops yet), which keeps sector locking simple. Pure C#: every peer gets the same layout (T-09).
    /// </summary>
    public static class LayoutGenerator
    {
        /// <summary>Known-good seed (tests check it validates with the shipped kit and template).</summary>
        public const ulong FallbackSeed = 1;

        public static Layout Generate(RoomKit kit, GraphTemplate graph, ulong seed)
        {
            for (int attempt = 0; attempt < graph.MaxAttempts; attempt++)
            {
                Layout l = TryBuild(kit, graph, seed, attempt);
                if (l != null && LayoutValidator.Validate(l).Count == 0) return l;
            }
            for (int attempt = 0; attempt < graph.MaxAttempts * 4; attempt++)
            {
                Layout l = TryBuild(kit, graph, FallbackSeed, attempt);
                if (l != null && LayoutValidator.Validate(l).Count == 0)
                {
                    l.Seed = seed;
                    l.UsedFallback = true;
                    return l;
                }
            }
            return null; // Only with a broken kit; tests guard the shipped one.
        }

        /// <summary>One attempt. Returns null when a required room could not be placed.</summary>
        public static Layout TryBuild(RoomKit kit, GraphTemplate g, ulong seed, int attempt)
        {
            var b = new Builder(kit, g, seed, attempt);
            return b.Run() ? b.Layout : null;
        }

        private sealed class Builder
        {
            private readonly RoomKit _kit;
            private readonly GraphTemplate _g;
            private readonly DeterministicRandom _rng;
            private readonly HashSet<long> _occupied = new HashSet<long>();
            private readonly HashSet<long> _usedSockets = new HashSet<long>();
            public readonly Layout Layout;

            public Builder(RoomKit kit, GraphTemplate g, ulong seed, int attempt)
            {
                _kit = kit;
                _g = g;
                _rng = new DeterministicRandom(seed * 0x9E3779B97F4A7C15UL + (ulong)(attempt + 1) * 0xD1B54A32D192ED03UL);
                Layout = new Layout { Seed = seed, Attempt = attempt, Kit = kit, Graph = g };
            }

            public bool Run()
            {
                RoomTemplate spawnT = PickWeighted(new List<RoomTemplate>(_kit.WithRole(RoomRole.Spawn)));
                if (spawnT == null) return false;
                PlacedRoom spawn = Place(spawnT, new Cell(0, 0), _rng.Range(0, 4), null);
                spawn.OnCriticalPath = true;

                // Main route.
                int mainRooms = _rng.Range(_g.CriticalRoomsMin, _g.CriticalRoomsMax + 1);
                PlacedRoom current = spawn;
                var main = new List<PlacedRoom> { spawn };
                // Position along the main route (main-room index; a corridor counts with the room it leads to).
                var mainIndex = new Dictionary<int, int> { [spawn.Index] = 0 };
                for (int i = 0; i < mainRooms; i++)
                {
                    RoomRole role = i == mainRooms - 1 ? RoomRole.Objective : RoomRole.Room;
                    PlacedRoom from = current;
                    if (_rng.NextFloat() < _g.CorridorChance)
                    {
                        PlacedRoom corridor = Attach(current, RoomRole.Corridor, SocketTypes.Door);
                        if (corridor != null) { corridor.OnCriticalPath = true; from = corridor; mainIndex[corridor.Index] = i + 1; }
                    }
                    PlacedRoom next = Attach(from, role, SocketTypes.Door);
                    if (next == null && from != current)
                    {
                        mainIndex[from.Index] = i; // dead-end corridor stays with the room it hangs off
                        next = Attach(current, role, SocketTypes.Door);
                    }
                    if (next == null) return false;
                    next.OnCriticalPath = true;
                    mainIndex[next.Index] = i + 1;
                    main.Add(next);
                    current = next;
                }
                PlacedRoom objective = current;

                // Boss arena hangs off the objective room (or the room before it).
                if (_g.BossRoom)
                {
                    PlacedRoom boss = AttachVia(objective, RoomRole.Boss) ?? AttachVia(main[main.Count - 2], RoomRole.Boss);
                    if (boss == null) return false;
                }

                // Side branches from main-route rooms (not the objective).
                int branches = _rng.Range(_g.BranchesMin, _g.BranchesMax + 1);
                var hosts = new List<PlacedRoom>();
                foreach (PlacedRoom r in Layout.Rooms)
                    if (r.OnCriticalPath && r.Template.Role != RoomRole.Objective) hosts.Add(r);
                // At most one branch in the first sector, so the starting area stays small (GAME_DESIGN §3.1).
                int firstSectorBranches = 0;
                for (int i = 0, tries = 0; i < branches && tries < branches * 4; tries++)
                {
                    PlacedRoom host = hosts[_rng.Range(0, hosts.Count)];
                    bool first = mainIndex.TryGetValue(host.Index, out int mi) && mi < _g.FirstSectorRooms;
                    if (first && firstSectorBranches > 0) continue;
                    if (AttachVia(host, RoomRole.Room) != null) { i++; if (first) firstSectorBranches++; }
                }

                // Secret rooms behind vents.
                for (int i = 0; i < _g.Secrets; i++)
                {
                    var candidates = new List<PlacedRoom>(Layout.Rooms);
                    Shuffle(candidates);
                    bool placed = false;
                    foreach (PlacedRoom host in candidates)
                    {
                        if (host.Template.Role == RoomRole.Secret || host.Template.Role == RoomRole.Spawn) continue;
                        if (Attach(host, RoomRole.Secret, SocketTypes.Vent) != null) { placed = true; break; }
                    }
                    if (!placed) return false;
                }

                AssignSectors(main);
                AssignDoors();
                AssignLoot();
                AssignBreaches();
                return true;
            }

            /// <summary>Attaches a room of the role, sometimes through a corridor piece.</summary>
            private PlacedRoom AttachVia(PlacedRoom host, RoomRole role)
            {
                if (_rng.NextFloat() < _g.CorridorChance)
                {
                    int roomsBefore = Layout.Rooms.Count, connsBefore = Layout.Connections.Count;
                    PlacedRoom corridor = Attach(host, RoomRole.Corridor, SocketTypes.Door);
                    if (corridor != null)
                    {
                        PlacedRoom r = Attach(corridor, role, SocketTypes.Door);
                        if (r != null) return r;
                        Remove(corridor, roomsBefore, connsBefore); // dead-end corridor: undo it
                    }
                }
                return Attach(host, role, SocketTypes.Door);
            }

            private PlacedRoom Attach(PlacedRoom parent, RoomRole role, string socketType)
            {
                var sockets = new List<int>();
                for (int i = 0; i < parent.Template.Sockets.Count; i++)
                    if (parent.Template.Sockets[i].Type == socketType && !_usedSockets.Contains(SocketKey(parent.Index, i))) sockets.Add(i);
                if (sockets.Count == 0) return null;
                Shuffle(sockets);

                var templates = WeightedOrder(new List<RoomTemplate>(_kit.WithRole(role)));
                foreach (int ps in sockets)
                {
                    Dir pf = parent.SocketFacing(ps);
                    Cell target = parent.SocketCell(ps).Step(pf);
                    Dir need = pf.Opposite();
                    foreach (RoomTemplate t in templates)
                    {
                        int start = _rng.Range(0, t.Sockets.Count);
                        for (int k = 0; k < t.Sockets.Count; k++)
                        {
                            int cs = (start + k) % t.Sockets.Count;
                            SocketDef s = t.Sockets[cs];
                            if (s.Type != socketType) continue;
                            int rot = ((int)need - (int)s.Facing + 4) & 3;
                            var probe = new PlacedRoom { Template = t, Rotation = rot, Origin = new Cell(0, 0) };
                            Cell at = probe.SocketCell(cs);
                            probe.Origin = new Cell(target.X - at.X, target.Z - at.Z);
                            if (!Fits(probe)) continue;
                            PlacedRoom placed = Place(t, probe.Origin, rot, parent);
                            _usedSockets.Add(SocketKey(parent.Index, ps));
                            _usedSockets.Add(SocketKey(placed.Index, cs));
                            Layout.Connections.Add(new Connection
                            {
                                RoomA = parent.Index, SocketA = ps, RoomB = placed.Index, SocketB = cs,
                                Secret = socketType == SocketTypes.Vent,
                            });
                            return placed;
                        }
                    }
                }
                return null;
            }

            private bool Fits(PlacedRoom probe)
            {
                foreach (Cell c in probe.Cells()) if (_occupied.Contains(c.Key)) return false;
                return true;
            }

            private PlacedRoom Place(RoomTemplate t, Cell origin, int rotation, PlacedRoom parent)
            {
                var r = new PlacedRoom
                {
                    Index = Layout.Rooms.Count, Template = t, Origin = origin, Rotation = rotation,
                    Parent = parent != null ? parent.Index : -1, Depth = parent != null ? parent.Depth + 1 : 0,
                };
                Layout.Rooms.Add(r);
                foreach (Cell c in r.Cells()) _occupied.Add(c.Key);
                return r;
            }

            private void Remove(PlacedRoom r, int roomsBefore, int connsBefore)
            {
                foreach (Cell c in r.Cells()) _occupied.Remove(c.Key);
                for (int i = connsBefore; i < Layout.Connections.Count; i++)
                {
                    Connection c = Layout.Connections[i];
                    _usedSockets.Remove(SocketKey(c.RoomA, c.SocketA));
                    _usedSockets.Remove(SocketKey(c.RoomB, c.SocketB));
                }
                Layout.Connections.RemoveRange(connsBefore, Layout.Connections.Count - connsBefore);
                Layout.Rooms.RemoveRange(roomsBefore, Layout.Rooms.Count - roomsBefore);
            }

            /// <summary>
            /// Sector 0 holds spawn and the first main rooms; the rest of the main route is split evenly. Corridors
            /// take the sector of the main room they lead to, so the locked door sits at the corridor's near end.
            /// Branches, the boss room and secrets take their parent's sector.
            /// </summary>
            private void AssignSectors(List<PlacedRoom> main)
            {
                int rest = main.Count - _g.FirstSectorRooms;
                int sectors = System.Math.Max(1, System.Math.Min(_g.Sectors, 1 + rest));
                Layout.SectorCount = sectors;
                for (int i = 0; i < main.Count; i++)
                {
                    int s = 0;
                    if (i >= _g.FirstSectorRooms && sectors > 1)
                        s = 1 + (i - _g.FirstSectorRooms) * (sectors - 1) / System.Math.Max(1, rest);
                    main[i].Sector = s;
                }
                // Main-route corridors: sector of the child main room. Everything else: parent's sector.
                foreach (PlacedRoom r in Layout.Rooms)
                {
                    if (r.Parent < 0 || main.Contains(r)) continue;
                    if (r.OnCriticalPath && r.Template.Role == RoomRole.Corridor)
                    {
                        PlacedRoom child = null;
                        foreach (PlacedRoom m in main) if (m.Parent == r.Index) { child = m; break; }
                        r.Sector = child != null ? child.Sector : Layout.Rooms[r.Parent].Sector;
                    }
                }
                foreach (PlacedRoom r in Layout.Rooms) // parents always come before children
                {
                    if (r.Parent < 0 || r.OnCriticalPath) continue;
                    r.Sector = Layout.Rooms[r.Parent].Sector;
                }
                for (int i = 0; i < Layout.Connections.Count; i++)
                {
                    Connection c = Layout.Connections[i];
                    int a = Layout.Rooms[c.RoomA].Sector, b = Layout.Rooms[c.RoomB].Sector;
                    c.UnlockSector = a == b ? 0 : System.Math.Max(a, b);
                    Layout.Connections[i] = c;
                }
            }

            /// <summary>
            /// Every door between sectors is locked: usually bought with credits (pricier deeper in), sometimes opened
            /// by a security switch or a power relay in a main-route room of the sector before. Side branches are
            /// often behind a cheaper credit door of their own.
            /// </summary>
            private void AssignDoors()
            {
                for (int i = 0; i < Layout.Connections.Count; i++)
                {
                    Connection c = Layout.Connections[i];
                    c.SwitchRoom = -1;
                    if (c.Secret) { Layout.Connections[i] = c; continue; }
                    PlacedRoom a = Layout.Rooms[c.RoomA], b = Layout.Rooms[c.RoomB];
                    if (c.UnlockSector > 0)
                    {
                        c.Lock = DoorLock.Credits;
                        c.Cost = _g.SectorDoorCost + _g.SectorDoorCostStep * (c.UnlockSector - 1);
                        if (_rng.NextFloat() < _g.SpecialDoorChance)
                        {
                            int room = PickSwitchRoom(c.UnlockSector - 1);
                            if (room >= 0)
                            {
                                c.Lock = _rng.NextFloat() < 0.5f ? DoorLock.Switch : DoorLock.Power;
                                c.Cost = 0;
                                c.SwitchRoom = room;
                            }
                        }
                    }
                    else if (a.OnCriticalPath && !b.OnCriticalPath && (b.Template.Role == RoomRole.Room || b.Template.Role == RoomRole.Corridor)
                             && _rng.NextFloat() < _g.BranchDoorChance)
                    {
                        c.Lock = DoorLock.Credits;
                        c.Cost = _g.BranchDoorCost;
                    }
                    Layout.Connections[i] = c;
                }
            }

            /// <summary>A main-route room of the sector, preferring real rooms over corridors and the spawn airlock.</summary>
            private int PickSwitchRoom(int sector)
            {
                var rooms = new List<int>();
                foreach (PlacedRoom r in Layout.Rooms)
                    if (r.OnCriticalPath && r.Sector == sector && (r.Template.Role == RoomRole.Room || r.Template.Role == RoomRole.Objective)) rooms.Add(r.Index);
                if (rooms.Count == 0)
                    foreach (PlacedRoom r in Layout.Rooms)
                        if (r.OnCriticalPath && r.Sector == sector) rooms.Add(r.Index);
                return rooms.Count > 0 ? rooms[_rng.Range(0, rooms.Count)] : -1;
            }

            /// <summary>
            /// Breach closets: for each room, up to <see cref="RoomTemplate.EnemySpawns"/> boarded openings in outer
            /// walls with a free cell behind them, as far from the room's doorways as possible and spread apart.
            /// </summary>
            private void AssignBreaches()
            {
                var closets = new HashSet<long>();
                foreach (PlacedRoom room in Layout.Rooms)
                {
                    int want = room.Template.EnemySpawns;
                    if (want <= 0 || room.Template.Role == RoomRole.Secret) continue;
                    var doors = new List<Cell>();
                    for (int s = 0; s < room.Template.Sockets.Count; s++)
                        if (Layout.IsConnected(room.Index, s, out _)) doors.Add(room.SocketCell(s));

                    var cand = new List<(Cell cell, Dir dir, int score)>();
                    foreach (Cell c in room.Cells())
                        for (int d = 0; d < 4; d++)
                        {
                            var dir = (Dir)d;
                            Cell outside = c.Step(dir);
                            if (room.Contains(outside) || _occupied.Contains(outside.Key) || closets.Contains(outside.Key)) continue;
                            if (IsSocketEdge(room, c, dir)) continue; // keep unused doorway spots plain wall
                            int score = 99;
                            foreach (Cell dc in doors) score = System.Math.Min(score, System.Math.Abs(dc.X - c.X) + System.Math.Abs(dc.Z - c.Z));
                            cand.Add((c, dir, score));
                        }
                    Shuffle(cand);
                    var chosen = new List<Cell>();
                    for (int n = 0; n < want && cand.Count > 0; n++)
                    {
                        int best = -1, bestScore = int.MinValue;
                        for (int i = 0; i < cand.Count; i++)
                        {
                            if (closets.Contains(cand[i].cell.Step(cand[i].dir).Key)) continue;
                            int spread = 99;
                            foreach (Cell ch in chosen) spread = System.Math.Min(spread, System.Math.Abs(ch.X - cand[i].cell.X) + System.Math.Abs(ch.Z - cand[i].cell.Z));
                            if (spread < 2) continue;
                            int sc = System.Math.Min(cand[i].score, 3) * 10 + System.Math.Min(spread, 6);
                            if (sc > bestScore) { bestScore = sc; best = i; }
                        }
                        if (best < 0) break;
                        var pick = cand[best];
                        cand.RemoveAt(best);
                        closets.Add(pick.cell.Step(pick.dir).Key);
                        _occupied.Add(pick.cell.Step(pick.dir).Key);
                        chosen.Add(pick.cell);
                        Layout.Breaches.Add(new Breach { Room = room.Index, Inner = pick.cell, Facing = pick.dir });
                    }
                }
            }

            private static bool IsSocketEdge(PlacedRoom room, Cell c, Dir dir)
            {
                for (int s = 0; s < room.Template.Sockets.Count; s++)
                    if (room.SocketFacing(s) == dir && room.SocketCell(s).Equals(c)) return true;
                return false;
            }

            /// <summary>
            /// D-05: one Supply Cache per sector (so one is guaranteed in the spawn sector) on a random slot, a
            /// stash in every secret room, and roughly a third of the other slots get an ammo crate or medkit.
            /// </summary>
            private void AssignLoot()
            {
                var used = new HashSet<long>();
                for (int s = 0; s < Layout.SectorCount; s++)
                {
                    var slots = new List<long>();
                    foreach (PlacedRoom r in Layout.Rooms)
                        if (r.Sector == s && r.Template.Role != RoomRole.Secret && Layout.SideDoorTo(r.Index) < 0)
                            for (int i = 0; i < r.Template.LootSlots; i++) slots.Add(SocketKey(r.Index, i));
                    if (slots.Count == 0) continue;
                    long pick = slots[_rng.Range(0, slots.Count)];
                    used.Add(pick);
                    Layout.Loot.Add(new LootPlacement { Room = (int)(pick >> 8), Slot = (int)(pick & 0xFF), Kind = LootKind.SupplyCache });
                }
                foreach (PlacedRoom r in Layout.Rooms)
                {
                    for (int i = 0; i < r.Template.LootSlots; i++)
                    {
                        long key = SocketKey(r.Index, i);
                        if (used.Contains(key)) continue;
                        if (r.Template.Role == RoomRole.Secret)
                        {
                            if (i == 0) Layout.Loot.Add(new LootPlacement { Room = r.Index, Slot = i, Kind = LootKind.SecretStash });
                            continue;
                        }
                        if (_rng.NextFloat() >= 0.35f) continue;
                        Layout.Loot.Add(new LootPlacement { Room = r.Index, Slot = i, Kind = _rng.NextFloat() < 0.65f ? LootKind.AmmoCrate : LootKind.Medkit });
                    }
                }
            }

            private static long SocketKey(int room, int socket) => ((long)room << 8) | (uint)socket;

            private RoomTemplate PickWeighted(List<RoomTemplate> list)
            {
                List<RoomTemplate> order = WeightedOrder(list);
                return order.Count > 0 ? order[0] : null;
            }

            /// <summary>Weighted shuffle: repeatedly draws by weight without replacement.</summary>
            private List<RoomTemplate> WeightedOrder(List<RoomTemplate> list)
            {
                var result = new List<RoomTemplate>(list.Count);
                var pool = new List<RoomTemplate>(list);
                while (pool.Count > 0)
                {
                    int total = 0;
                    foreach (RoomTemplate t in pool) total += System.Math.Max(1, t.Weight);
                    int roll = _rng.Range(0, total), i = 0;
                    for (; i < pool.Count - 1; i++)
                    {
                        roll -= System.Math.Max(1, pool[i].Weight);
                        if (roll < 0) break;
                    }
                    result.Add(pool[i]);
                    pool.RemoveAt(i);
                }
                return result;
            }

            private void Shuffle<T>(List<T> list)
            {
                for (int i = list.Count - 1; i > 0; i--)
                {
                    int j = _rng.Range(0, i + 1);
                    (list[i], list[j]) = (list[j], list[i]);
                }
            }
        }
    }
}
