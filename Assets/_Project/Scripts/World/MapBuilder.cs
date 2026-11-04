using System.Collections.Generic;
using Deadfall.Core;
using Deadfall.Rules.Common;
using Deadfall.Rules.World;
using UnityEngine;

namespace Deadfall.World
{
    /// <summary>An enemy spawn point inside a built room.</summary>
    public struct MapSpawnPoint
    {
        /// <summary>Centre of the breach closet the enemy appears in.</summary>
        public Vector3 Position;
        public int Room;
        public int Sector;
        /// <summary>Index into <see cref="Layout.Breaches"/> and <see cref="BuiltMap.Breaches"/>.</summary>
        public int Breach;
    }

    /// <summary>A loot slot that got an item.</summary>
    public struct MapLootPoint
    {
        /// <summary>Stable id (same on every peer): room * 16 + slot.</summary>
        public int Id;
        public Vector3 Position;
        public float Yaw;
        public LootKind Kind;
        public int Room;
        public int Sector;
    }

    /// <summary>One floor grate of the crawl-vent network.</summary>
    public struct CrawlVent
    {
        public Vector3 Position;
        public int Room;
        public Transform Grate;
    }

    /// <summary>What <see cref="MapBuilder"/> made: the geometry root plus every point gameplay needs.</summary>
    public sealed class BuiltMap
    {
        public Layout Layout;
        public Transform Root;
        public Bounds Bounds;
        public readonly List<Bounds> RoomBounds = new List<Bounds>();
        public readonly List<Vector3> PlayerSpawns = new List<Vector3>();
        public float PlayerSpawnYaw;
        public readonly List<MapSpawnPoint> EnemySpawns = new List<MapSpawnPoint>();
        public readonly List<MapLootPoint> Loot = new List<MapLootPoint>();
        public readonly List<SectorDoor> Doors = new List<SectorDoor>();
        public readonly List<SecretPanel> SecretPanels = new List<SecretPanel>();
        /// <summary>One per <see cref="Layout.Breaches"/> entry, same order (built after the bake).</summary>
        public readonly List<BreachBarricade> Breaches = new List<BreachBarricade>();
        public readonly List<DoorSwitch> Switches = new List<DoorSwitch>();
        /// <summary>Floor grates of the crawl-vent network Creepers use to flank (one or two per room, by a wall).</summary>
        public readonly List<CrawlVent> CrawlVents = new List<CrawlVent>();
        public float CellSize = 4f;

        /// <summary>Room index at a world position, or -1 (exact for shaped rooms).</summary>
        public int RoomAt(Vector3 p) => Layout.RoomAt(new Cell(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize)));

        /// <summary>Middle of the room's centre cell (inside the room even for L shapes).</summary>
        public Vector3 RoomCenter(int room)
        {
            Cell c = Layout.Rooms[room].CenterCell;
            return new Vector3((c.X + 0.5f) * CellSize, RoomBounds[room].center.y, (c.Z + 0.5f) * CellSize);
        }
    }

    /// <summary>
    /// Turns a <see cref="Layout"/> into geometry: floor, ceiling and walls per room cell edge (doorways where
    /// sockets connect, boarded openings at breaches, solid wall elsewhere), lights, columns, cover props and themed set dressing (MapBuilder.Dressing.cs), sealed
    /// closets behind breaches, and points for player spawns, enemy spawns (the closets) and loot. Locked doors,
    /// their switches and the breach barricades are built by <see cref="BuildDoors"/> after the NavMesh bake, so the
    /// bake sees every opening open and those carve it while closed (T-32).
    /// Everything is deterministic from the layout seed, so every peer builds the same map.
    /// </summary>
    public sealed partial class MapBuilder
    {
        public const float WallThickness = 0.3f;
        public const float DoorWidth = 2.4f;
        public const float DoorHeight = 2.9f;
        public const float VentWidth = 1.1f;
        public const float VentHeight = 1.45f; // crouch height is 1.3 m

        private readonly Dictionary<Color, Material> _materials = new Dictionary<Color, Material>();
        private Transform _root;
        private float _cs, _h;

        public static readonly Color[] SectorColors =
        {
            new Color(0.35f, 0.75f, 0.45f), new Color(0.35f, 0.6f, 0.95f), new Color(0.95f, 0.7f, 0.25f),
            new Color(0.85f, 0.35f, 0.35f), new Color(0.75f, 0.45f, 0.9f), new Color(0.5f, 0.85f, 0.85f),
        };

        public static Color SectorColor(int sector) => SectorColors[((sector % SectorColors.Length) + SectorColors.Length) % SectorColors.Length];

        public BuiltMap Build(Layout layout, Transform parent)
        {
            _cs = layout.Kit.CellSize;
            _h = layout.Kit.WallHeight;
            _root = new GameObject($"[Map {layout.Seed}]").transform;
            _root.SetParent(parent, false);
            var map = new BuiltMap { Layout = layout, Root = _root, CellSize = _cs };
            var all = new Bounds();
            bool first = true;

            foreach (PlacedRoom room in layout.Rooms)
            {
                Bounds b = RoomBoundsOf(room);
                map.RoomBounds.Add(b);
                if (first) { all = b; first = false; } else all.Encapsulate(b);
            }
            // Breach closets sit outside the rooms; the NavMesh bake uses these bounds, so they must be inside.
            foreach (Breach br in layout.Breaches)
                all.Encapsulate(new Bounds(new Vector3((br.Closet.X + 0.5f) * _cs, all.center.y, (br.Closet.Z + 0.5f) * _cs),
                    new Vector3(_cs, all.size.y, _cs)));
            map.Bounds = all;

            foreach (PlacedRoom room in layout.Rooms) BuildRoom(layout, map, room);
            for (int i = 0; i < layout.Breaches.Count; i++) BuildCloset(layout, map, i);
            BuildSpawnPoints(layout, map);
            BuildLoot(layout, map);
            return map;
        }

        /// <summary>Call after the NavMesh bake: locked sector doors and secret vent panels.</summary>
        public void BuildDoors(BuiltMap map)
        {
            Layout layout = map.Layout;
            for (int i = 0; i < layout.Connections.Count; i++)
            {
                Connection c = layout.Connections[i];
                if (!c.IsDoor && !c.Secret) continue;
                PlacedRoom a = layout.Rooms[c.RoomA];
                Dir facing = a.SocketFacing(c.SocketA);
                Vector3 edge = EdgeMid(a.SocketCell(c.SocketA), facing);
                bool alongX = facing == Dir.North || facing == Dir.South;
                if (c.Secret)
                {
                    var panel = SecretPanel.Create(edge + Vector3.up * (VentHeight * 0.5f), alongX, VentWidth + 0.2f, VentHeight + 0.15f,
                        Mat(new Color(0.42f, 0.43f, 0.45f)), _root);
                    panel.ConnectionIndex = i;
                    map.SecretPanels.Add(panel);
                }
                else
                {
                    var door = SectorDoor.Create(edge, alongX, DoorWidth + 0.3f, DoorHeight + 0.1f, c.UnlockSector,
                        Mat(Color.Lerp(new Color(0.3f, 0.3f, 0.32f), SectorColor(c.UnlockSector), 0.45f)), _root);
                    door.ConnectionIndex = i;
                    door.Lock = c.Lock;
                    door.Cost = c.Cost;
                    map.Doors.Add(door);
                    if (c.Lock == DoorLock.Switch || c.Lock == DoorLock.Power) BuildSwitch(layout, map, i, c);
                }
            }

            BuildCrawlVents(layout, map);

            // Breach barricades (they carve the NavMesh, so after the bake too).
            Material wood = Mat(new Color(0.48f, 0.36f, 0.22f)), frame = Mat(new Color(0.25f, 0.25f, 0.27f));
            for (int i = 0; i < layout.Breaches.Count; i++)
            {
                Breach br = layout.Breaches[i];
                Vector3 edge = EdgeMid(br.Inner, br.Facing);
                var outward = new Vector3(br.Facing.Dx(), 0f, br.Facing.Dz());
                map.Breaches.Add(BreachBarricade.Create(i, br.Room, edge, outward, wood, frame, _root));
            }
        }

        /// <summary>
        /// Crawl-vent floor grates: one per room by a wall away from doorways, two in big rooms. Deterministic from the
        /// layout, so every peer has the same network. No colliders: they sit flush on the floor.
        /// </summary>
        private void BuildCrawlVents(Layout layout, BuiltMap map)
        {
            Color frame = new Color(0.2f, 0.21f, 0.22f), slat = new Color(0.36f, 0.37f, 0.38f), dark = new Color(0.02f, 0.02f, 0.02f);
            var root = new GameObject("CrawlVents").transform;
            root.SetParent(_root, false);
            foreach (PlacedRoom r in layout.Rooms)
            {
                RoomRole role = r.Template.Role;
                if (role == RoomRole.Secret) continue;
                List<Vector3> pts = WallPoints(layout, r, 0.85f);
                if (pts.Count == 0) continue;
                int cells = 0;
                foreach (Cell _ in r.Cells()) cells++;
                int want = cells >= 9 ? 2 : 1;
                for (int k = 0; k < want; k++)
                {
                    Vector3 p = pts[(r.Index * 13 + 5 + k * (pts.Count / 2 + 1)) % pts.Count];
                    bool clash = false;
                    foreach (CrawlVent v in map.CrawlVents) if ((v.Position - p).sqrMagnitude < 9f) clash = true;
                    if (clash) continue;
                    var t = new GameObject($"Vent {map.CrawlVents.Count}").transform;
                    t.SetParent(root, false);
                    t.position = p;
                    Box(t, "Hole", p + Vector3.up * 0.005f, new Vector3(0.8f, 0.01f, 0.8f), dark, collider: false);
                    for (int e = 0; e < 4; e++)
                    {
                        bool alongX = e < 2;
                        float off = (e % 2 == 0 ? -1f : 1f) * 0.42f;
                        Vector3 c = p + (alongX ? new Vector3(0f, 0.02f, off) : new Vector3(off, 0.02f, 0f));
                        Box(t, "Grate frame", c, alongX ? new Vector3(0.92f, 0.04f, 0.08f) : new Vector3(0.08f, 0.04f, 0.92f), frame, collider: false);
                    }
                    for (int i = -2; i <= 2; i++)
                        Box(t, "Grate slat", p + new Vector3(i * 0.15f, 0.025f, 0f), new Vector3(0.05f, 0.03f, 0.78f), slat, collider: false);
                    map.CrawlVents.Add(new CrawlVent { Position = p, Room = r.Index, Grate = t });
                }
            }
        }

        /// <summary>The switch or power relay for a door, against a wall of its room, away from doorways.</summary>
        private void BuildSwitch(Layout layout, BuiltMap map, int connection, Connection c)
        {
            if (c.SwitchRoom < 0) return;
            PlacedRoom room = layout.Rooms[c.SwitchRoom];
            List<Vector3> pts = WallPoints(layout, room, c.Lock == DoorLock.Power ? 0.75f : 0.45f);
            Vector3 p = pts.Count > 0 ? pts[(connection * 7 + 3) % pts.Count] : map.RoomCenter(room.Index);
            Vector3 toCenter = map.RoomCenter(room.Index) - p;
            toCenter.y = 0f;
            float yaw = toCenter.sqrMagnitude > 0.01f ? Mathf.Atan2(toCenter.x, toCenter.z) * Mathf.Rad2Deg : 0f;
            Color body = c.Lock == DoorLock.Power ? new Color(0.55f, 0.5f, 0.2f) : new Color(0.3f, 0.35f, 0.45f);
            map.Switches.Add(DoorSwitch.Create(connection, c.Lock, new Vector3(p.x, 0f, p.z), yaw, Mat(body), _root));
        }

        /// <summary>A sealed closet behind a breach: floor, ceiling and three walls (the room wall has the opening).</summary>
        private void BuildCloset(Layout layout, BuiltMap map, int index)
        {
            Breach br = layout.Breaches[index];
            Cell c = br.Closet;
            var t = new GameObject($"Closet #{index}").transform;
            t.SetParent(_root, false);
            var center = new Vector3((c.X + 0.5f) * _cs, 0f, (c.Z + 0.5f) * _cs);
            // Near-black, fully matte surfaces: light spilling through the boards finds almost nothing to bounce off,
            // so the closet reads as a dark void and enemies inside are hard to make out until they reach the boards.
            Color dark = ClosetBlack;
            Matte(Box(t, "Floor", center + Vector3.down * 0.1f, new Vector3(_cs, 0.2f, _cs), new Color(0.03f, 0.028f, 0.027f)));
            Matte(Box(t, "Ceiling", center + Vector3.up * (_h + 0.1f), new Vector3(_cs, 0.2f, _cs), dark));
            Dir back = br.Facing.Opposite();
            int first = t.childCount;
            for (int d = 0; d < 4; d++)
            {
                var dir = (Dir)d;
                if (dir == back) continue;
                BuildWall(t, c, dir, Opening.None, _h, dark);
            }
            for (int i = first; i < t.childCount; i++) Matte(t.GetChild(i).gameObject);

            // A black lining just inside the opening (sides and above), so the boards frame a void, not a lit box.
            Vector3 inward = new Vector3(-br.Facing.Dx(), 0f, -br.Facing.Dz()); // closet -> room
            bool alongX = br.Facing == Dir.North || br.Facing == Dir.South;
            Vector3 mouth = EdgeMid(br.Inner, br.Facing) - inward * 0.35f;
            float w = BreachBarricade.OpeningWidth, oh = BreachBarricade.OpeningHeight;
            Vector3 axis = alongX ? Vector3.right : Vector3.forward;
            Vector3 Size(float along, float height, float depth) => alongX ? new Vector3(along, height, depth) : new Vector3(depth, height, along);
            for (int side = -1; side <= 1; side += 2)
                Matte(Box(t, "Lining", mouth + axis * (side * (w * 0.5f + 0.05f)) + Vector3.up * (oh * 0.5f), Size(0.1f, oh, 0.5f), dark, collider: false));
            Matte(Box(t, "Lining", mouth + Vector3.up * (oh + 0.05f), Size(w + 0.2f, 0.1f, 0.5f), dark, collider: false));
        }

        public void Clear(BuiltMap map)
        {
            if (map != null && map.Root != null)
            {
                map.Root.gameObject.SetActive(false); // out of physics and NavMesh collection this frame
                Object.Destroy(map.Root.gameObject);
            }
            foreach (Material m in _materials.Values) if (m != null) Object.Destroy(m);
            _materials.Clear();
        }

        public Bounds RoomBoundsOf(PlacedRoom r)
        {
            var min = new Vector3(r.Origin.X * _cs, 0f, r.Origin.Z * _cs);
            var size = new Vector3(r.Width * _cs, _h, r.Depth2 * _cs);
            return new Bounds(min + size * 0.5f, size);
        }

        /// <summary>World centre of a cell's edge on the floor.</summary>
        public Vector3 EdgeMid(Cell c, Dir facing)
        {
            float x = (c.X + 0.5f) * _cs, z = (c.Z + 0.5f) * _cs;
            return new Vector3(x + facing.Dx() * _cs * 0.5f, 0f, z + facing.Dz() * _cs * 0.5f);
        }

        private void BuildRoom(Layout layout, BuiltMap map, PlacedRoom room)
        {
            Bounds b = map.RoomBounds[room.Index];
            var go = new GameObject($"{room.Index:00} {room.Template.DisplayName} (S{room.Sector})");
            go.transform.SetParent(_root, false);
            Transform t = go.transform;

            RoomRole role = room.Template.Role;
            Theme theme = ThemeFor(room);
            Color floor = theme.FloorAlt * 0.55f; // shows as grout between the tiles (DressRoom)
            floor.a = 1f;
            float ceilingY = role == RoomRole.Secret ? 1.6f : _h;

            // Floor and ceiling as one box per run of cells in each row (shaped rooms leave their notches empty).
            for (int z = 0; z < room.Depth2; z++)
            {
                int x = 0;
                while (x < room.Width)
                {
                    if (!room.Contains(new Cell(room.Origin.X + x, room.Origin.Z + z))) { x++; continue; }
                    int start = x;
                    while (x < room.Width && room.Contains(new Cell(room.Origin.X + x, room.Origin.Z + z))) x++;
                    float x0 = (room.Origin.X + start) * _cs, x1 = (room.Origin.X + x) * _cs, zc = (room.Origin.Z + z + 0.5f) * _cs;
                    Box(t, "Floor", new Vector3((x0 + x1) * 0.5f, -0.1f, zc), new Vector3(x1 - x0, 0.2f, _cs), floor);
                    Box(t, "Ceiling", new Vector3((x0 + x1) * 0.5f, ceilingY + 0.1f, zc), new Vector3(x1 - x0, 0.2f, _cs), new Color(0.2f, 0.2f, 0.22f));
                }
            }
            // A square of the sector colour in the middle helps reading the map while sectors are new.
            Vector3 mid = map.RoomCenter(room.Index);
            Box(t, "SectorMark", new Vector3(mid.x, 0.026f, mid.z), new Vector3(1.2f, 0.01f, 1.2f), SectorColor(room.Sector), collider: false);

            // Walls: one segment per boundary cell edge.
            Color wall = theme.Wall;
            for (int x = 0; x < room.Width; x++)
                for (int z = 0; z < room.Depth2; z++)
                {
                    var c = new Cell(room.Origin.X + x, room.Origin.Z + z);
                    for (int d = 0; d < 4; d++)
                    {
                        var dir = (Dir)d;
                        if (room.Contains(c.Step(dir))) continue;
                        Opening opening = OpeningAt(layout, room, c, dir);
                        BuildWall(t, c, dir, opening, ceilingY, wall);
                    }
                }

            // Lights: one for small rooms, a few spread over the cells of big ones. Some flicker.
            int lights = Mathf.Clamp(room.Template.CellCount / 10, 1, 4);
            var lightCells = new List<Cell>(room.Cells());
            var lightPoints = new List<Vector3>(lights);
            for (int li = 0; li < lights; li++)
            {
                Cell lc = lights == 1 ? room.CenterCell : lightCells[(li * lightCells.Count) / lights + lightCells.Count / (lights * 2)];
                var lightGo = new GameObject("Light");
                lightGo.transform.SetParent(t, false);
                lightGo.transform.position = new Vector3((lc.X + 0.5f) * _cs, ceilingY - 0.4f, (lc.Z + 0.5f) * _cs);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.shadows = LightShadows.None;
                light.range = lights == 1 ? Mathf.Max(b.size.x, b.size.z) * 0.85f + 2f : 16f;
                light.intensity = (role == RoomRole.Corridor ? 1.1f : 1.5f) * theme.LightScale;
                light.color = theme.Light;
                lightPoints.Add(lightGo.transform.position);
                if (LightFlicker.ShouldFlicker(layout.Seed, room.Index, li)) lightGo.AddComponent<LightFlicker>();
            }

            DressRoom(layout, map, room, t, ceilingY, theme, lightPoints);
            BuildProps(layout, room, t, b, theme);
        }

        private enum Opening { None, Door, Vent, Breach }

        private static Opening OpeningAt(Layout layout, PlacedRoom room, Cell c, Dir dir)
        {
            foreach (Breach br in layout.Breaches)
                if (br.Room == room.Index && br.Facing == dir && br.Inner.Equals(c)) return Opening.Breach;
            for (int s = 0; s < room.Template.Sockets.Count; s++)
            {
                if (room.SocketFacing(s) != dir || !room.SocketCell(s).Equals(c)) continue;
                if (!layout.IsConnected(room.Index, s, out _)) return Opening.None;
                return room.Template.Sockets[s].Type == SocketTypes.Vent ? Opening.Vent : Opening.Door;
            }
            return Opening.None;
        }

        private void BuildWall(Transform parent, Cell c, Dir dir, Opening opening, float height, Color color)
        {
            Vector3 mid = EdgeMid(c, dir);
            Vector3 inward = new Vector3(-dir.Dx(), 0f, -dir.Dz());
            Vector3 center = mid + inward * (WallThickness * 0.5f);
            bool alongX = dir == Dir.North || dir == Dir.South;
            Vector3 axis = alongX ? Vector3.right : Vector3.forward;

            Vector3 Size(float length, float h) => alongX ? new Vector3(length, h, WallThickness) : new Vector3(WallThickness, h, length);

            if (opening == Opening.None)
            {
                Box(parent, "Wall", center + Vector3.up * (height * 0.5f), Size(_cs, height), color);
                return;
            }
            float gap = opening == Opening.Door ? DoorWidth : opening == Opening.Breach ? BreachBarricade.OpeningWidth : VentWidth;
            float gapH = opening == Opening.Door ? DoorHeight : opening == Opening.Breach ? BreachBarricade.OpeningHeight : VentHeight;
            float side = (_cs - gap) * 0.5f;
            Box(parent, "Wall", center + axis * (gap * 0.5f + side * 0.5f) + Vector3.up * (height * 0.5f), Size(side, height), color);
            Box(parent, "Wall", center - axis * (gap * 0.5f + side * 0.5f) + Vector3.up * (height * 0.5f), Size(side, height), color);
            Box(parent, "Lintel", center + Vector3.up * (gapH + (height - gapH) * 0.5f), Size(gap, height - gapH), color);
        }

        /// <summary>
        /// Columns at inner cell corners of big rooms (they break sight lines and change how the room flows), plus a
        /// few crates and lockers. Everything stays clear of doorways, breaches, the room's centre (objective props)
        /// and the wall strip where loot and switches go.
        /// </summary>
        private void BuildProps(Layout layout, PlacedRoom room, Transform parent, Bounds b, Theme theme)
        {
            RoomRole role = room.Template.Role;
            if (role == RoomRole.Corridor || role == RoomRole.Secret || room.Template.CellCount < 9) return;
            var rng = new DeterministicRandom(layout.Seed ^ (ulong)(room.Index * 7919 + 17));
            List<Vector3> keepClear = DoorPoints(layout, room);
            Vector3 mid = new Vector3((room.CenterCell.X + 0.5f) * _cs, 0f, (room.CenterCell.Z + 0.5f) * _cs);

            var placedProps = new List<Vector4>(); // xyz = floor point, w = footprint radius

            bool Clear(Vector3 p, float doorRadius)
            {
                if ((p - mid).sqrMagnitude < 9f) return false;
                foreach (Vector3 d in keepClear) if ((p - d).sqrMagnitude < doorRadius * doorRadius) return false;
                return true;
            }

            bool Spaced(Vector3 p, float radius)
            {
                foreach (Vector4 q in placedProps)
                {
                    float min = radius + q.w + 0.9f; // a walkable gap between pieces
                    if ((new Vector3(q.x, 0f, q.z) - p).sqrMagnitude < min * min) return false;
                }
                return true;
            }

            // Columns: corners shared by four room cells, every other corner, some skipped at random.
            if (room.Template.CellCount >= 16 && role != RoomRole.Spawn)
            {
                Color col = new Color(0.33f, 0.34f, 0.37f);
                int placed = 0;
                for (int x = 1; x < room.Width; x++)
                    for (int z = 1; z < room.Depth2; z++)
                    {
                        if (((x + z) & 1) != 0 || (x % 2 != 0 && z % 2 != 0)) continue;
                        int cx = room.Origin.X + x, cz = room.Origin.Z + z;
                        if (!room.Contains(new Cell(cx, cz)) || !room.Contains(new Cell(cx - 1, cz)) ||
                            !room.Contains(new Cell(cx, cz - 1)) || !room.Contains(new Cell(cx - 1, cz - 1))) continue;
                        var p = new Vector3(cx * _cs, 0f, cz * _cs);
                        if (!Clear(p, 3.2f) || rng.NextFloat() < 0.3f) continue;
                        float w = role == RoomRole.Boss ? 1.2f : 0.8f;
                        ModelLibrary.Skin(Box(parent, "Column", p + Vector3.up * (_h * 0.5f), new Vector3(w, _h, w), col), "prop_column");
                        placedProps.Add(new Vector4(p.x, 0f, p.z, w * 0.75f));
                        placed++;
                    }
                // Rooms too narrow for corner columns get a low partition wall instead.
                if (placed == 0 && room.Template.CellCount >= 20)
                {
                    var p = mid + (room.Width >= room.Depth2 ? new Vector3(_cs * 1.5f, 0f, 0f) : new Vector3(0f, 0f, _cs * 1.5f));
                    if (room.Contains(new Cell(Mathf.FloorToInt(p.x / _cs), Mathf.FloorToInt(p.z / _cs))) && Clear(p, 3.2f))
                    {
                        Vector3 size = room.Width >= room.Depth2 ? new Vector3(0.4f, 1.2f, _cs * 0.9f) : new Vector3(_cs * 0.9f, 1.2f, 0.4f);
                        ModelLibrary.Skin(Box(parent, "Partition", p + Vector3.up * 0.6f, size, new Color(0.35f, 0.36f, 0.38f)), "prop_partition", longAlongX: true);
                        placedProps.Add(new Vector4(p.x, 0f, p.z, _cs * 0.45f));
                    }
                }
            }

            var cells = new List<Cell>(room.Cells());
            bool Interior(Cell c) => room.Contains(c.Step(Dir.North)) && room.Contains(c.Step(Dir.South)) && room.Contains(c.Step(Dir.East)) && room.Contains(c.Step(Dir.West));

            // Themed furniture first (about one piece per six interior cells), squared to the room.
            int interiorCells = 0;
            foreach (Cell c in cells) if (Interior(c)) interiorCells++;
            int furniture = Mathf.Min(theme.Furniture.Length, Mathf.Max(1, interiorCells / 3));
            for (int f = 0; f < furniture; f++)
            {
                string id = theme.Furniture[f];
                float radius = FootprintRadius(id);
                for (int tries = 0; tries < 16; tries++)
                {
                    Cell c = cells[rng.Range(0, cells.Count)];
                    // Interior cells only (the wall strip is for loot, switches and the build ghost's clearance).
                    if (!Interior(c)) continue;
                    var p = new Vector3((c.X + rng.Range(0.3f, 0.7f)) * _cs, 0f, (c.Z + rng.Range(0.3f, 0.7f)) * _cs);
                    if (!Clear(p, 3f + radius * 0.5f) || !Spaced(p, radius)) continue;
                    float yaw = (room.Width >= room.Depth2 ? 0f : 90f) + rng.Range(0, 2) * 180f + rng.Range(-3f, 3f);
                    PlaceFurniture(parent, id, p, yaw);
                    placedProps.Add(new Vector4(p.x, 0f, p.z, radius));
                    break;
                }
            }

            int count = role == RoomRole.Boss ? 2 : rng.Range(1, 4);
            for (int i = 0, tries = 0; i < count && tries < 40; tries++)
            {
                Cell c = cells[rng.Range(0, cells.Count)];
                if (!Interior(c)) continue;
                var p = new Vector3((c.X + rng.Range(0.25f, 0.75f)) * _cs, 0f, (c.Z + rng.Range(0.25f, 0.75f)) * _cs);
                if (!Clear(p, 3f) || !Spaced(p, 1f)) continue;
                bool tall = rng.NextFloat() < 0.3f;
                var size = tall ? new Vector3(1.2f, 1.8f, 1.2f) : new Vector3(rng.Range(1.2f, 2.4f), rng.Range(0.8f, 1.1f), rng.Range(0.8f, 1.4f));
                GameObject prop = Box(parent, tall ? "Locker" : "Crate", p + Vector3.up * (size.y * 0.5f), size, new Color(0.4f, 0.37f, 0.32f));
                prop.transform.rotation = Quaternion.Euler(0f, rng.Range(0, 4) * 90f + rng.Range(-8f, 8f), 0f);
                ModelLibrary.Skin(prop, tall ? "prop_locker" : "prop_crate");
                placedProps.Add(new Vector4(p.x, 0f, p.z, Mathf.Max(size.x, size.z) * 0.5f));
                i++;
            }
        }

        /// <summary>Inner points just inside each connected doorway and breach of the room.</summary>
        private List<Vector3> DoorPoints(Layout layout, PlacedRoom room)
        {
            var list = new List<Vector3>();
            for (int s = 0; s < room.Template.Sockets.Count; s++)
            {
                if (!layout.IsConnected(room.Index, s, out _)) continue;
                Dir f = room.SocketFacing(s);
                list.Add(EdgeMid(room.SocketCell(s), f) - new Vector3(f.Dx(), 0f, f.Dz()) * 1.5f);
            }
            foreach (Breach br in layout.Breaches)
                if (br.Room == room.Index) list.Add(EdgeMid(br.Inner, br.Facing) - new Vector3(br.Facing.Dx(), 0f, br.Facing.Dz()) * 1.5f);
            return list;
        }

        /// <summary>Candidate points along the inside of a room's walls, in a fixed order, skipping doorways and breaches.</summary>
        private List<Vector3> WallPoints(Layout layout, PlacedRoom room, float inset)
        {
            var doors = DoorPoints(layout, room);
            var list = new List<Vector3>();
            foreach (Cell c in room.Cells())
            {
                float px = (c.X + 0.5f) * _cs, pz = (c.Z + 0.5f) * _cs;
                bool edge = false;
                // Push towards each wall this cell touches.
                if (!room.Contains(c.Step(Dir.West))) { px = c.X * _cs + inset; edge = true; }
                else if (!room.Contains(c.Step(Dir.East))) { px = (c.X + 1) * _cs - inset; edge = true; }
                if (!room.Contains(c.Step(Dir.South))) { pz = c.Z * _cs + inset; edge = true; }
                else if (!room.Contains(c.Step(Dir.North))) { pz = (c.Z + 1) * _cs - inset; edge = true; }
                if (!edge) continue;
                var p = new Vector3(px, 0f, pz);
                bool clear = true;
                foreach (Vector3 d in doors) if ((p - d).sqrMagnitude < 4f) clear = false;
                if (clear) list.Add(p);
            }
            return list;
        }

        private void BuildSpawnPoints(Layout layout, BuiltMap map)
        {
            // Players: around the centre of the spawn room, facing its first doorway.
            PlacedRoom spawn = layout.SpawnRoom;
            Vector3 c = map.RoomCenter(0);
            c.y = 0.05f;
            Vector3 toDoor = Vector3.forward;
            var doors = new List<Vector3>();
            for (int s = 0; s < spawn.Template.Sockets.Count; s++)
            {
                if (!layout.IsConnected(spawn.Index, s, out _)) continue;
                Dir f = spawn.SocketFacing(s);
                doors.Add(EdgeMid(spawn.SocketCell(s), f));
            }
            if (doors.Count > 0) { toDoor = doors[0] - c; toDoor.y = 0f; toDoor.Normalize(); }
            map.PlayerSpawnYaw = Mathf.Atan2(toDoor.x, toDoor.z) * Mathf.Rad2Deg;
            Vector3 right = Vector3.Cross(Vector3.up, toDoor).normalized;
            map.PlayerSpawns.Add(c - right * 1.2f);
            map.PlayerSpawns.Add(c + right * 1.2f);
            map.PlayerSpawns.Add(c - right * 1.2f - toDoor * 1.8f);
            map.PlayerSpawns.Add(c + right * 1.2f - toDoor * 1.8f);

            // Enemies: the middle of each breach closet (they break in through the boards).
            for (int i = 0; i < layout.Breaches.Count; i++)
            {
                Breach br = layout.Breaches[i];
                Cell k = br.Closet;
                var p = new Vector3((k.X + 0.5f) * _cs, 0f, (k.Z + 0.5f) * _cs);
                map.EnemySpawns.Add(new MapSpawnPoint { Position = p, Room = br.Room, Sector = layout.Rooms[br.Room].Sector, Breach = i });
            }
        }

        private void BuildLoot(Layout layout, BuiltMap map)
        {
            foreach (LootPlacement l in layout.Loot)
            {
                PlacedRoom room = layout.Rooms[l.Room];
                List<Vector3> pts = WallPoints(layout, room, 0.9f);
                Vector3 p = pts.Count > 0 ? pts[(l.Slot * 5 + 2) % pts.Count] : map.RoomCenter(l.Room);
                Vector3 toCenter = map.RoomCenter(l.Room) - p;
                toCenter.y = 0f;
                map.Loot.Add(new MapLootPoint
                {
                    Id = l.Room * 16 + l.Slot, Position = p, Kind = l.Kind, Room = l.Room, Sector = room.Sector,
                    Yaw = toCenter.sqrMagnitude > 0.01f ? Mathf.Atan2(toCenter.x, toCenter.z) * Mathf.Rad2Deg : 0f,
                });
            }
        }

        private static readonly Color ClosetBlack = new Color(0.012f, 0.012f, 0.014f);

        /// <summary>No specular highlights or reflections on this box (closet surfaces).</summary>
        private static void Matte(GameObject go)
        {
            Renderer r = go != null ? go.GetComponent<Renderer>() : null;
            if (r == null) return;
            Material m = r.sharedMaterial;
            if (m == null) return;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0f);
            if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 0f);
            if (m.HasProperty("_EnvironmentReflections")) m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        }

        private Material Mat(Color color)
        {
            if (!_materials.TryGetValue(color, out Material m) || m == null)
            {
                m = RuntimeMaterials.CreateLit(color);
                _materials[color] = m;
            }
            return m;
        }

        private GameObject Box(Transform parent, string name, Vector3 position, Vector3 size, Color color, bool collider = true)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = size;
            go.isStatic = true;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>()); // gone before the NavMesh bake this frame
            if (!SurfaceArt.ApplyAuto(go, color)) go.GetComponent<Renderer>().sharedMaterial = Mat(color);
            return go;
        }
    }
}
