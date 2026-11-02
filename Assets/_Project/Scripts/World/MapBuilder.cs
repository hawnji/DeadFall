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
        public Vector3 Position;
        public int Room;
        public int Sector;
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

        /// <summary>Room index at a world position, or -1.</summary>
        public int RoomAt(Vector3 p)
        {
            for (int i = 0; i < RoomBounds.Count; i++)
            {
                Bounds b = RoomBounds[i];
                if (p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z) return i;
            }
            return -1;
        }

        public Vector3 RoomCenter(int room) => RoomBounds[room].center;
    }

    /// <summary>
    /// Turns a <see cref="Layout"/> into greybox geometry: floor, ceiling and walls per room (doorways where
    /// sockets connect, solid wall where they don't), a light per room, a few cover props, and points for player
    /// spawns, enemy spawns and loot. Doors for locked sector connections are built by <see cref="BuildDoors"/>
    /// after the NavMesh bake, so the bake sees every doorway open and doors carve it while closed (T-32).
    /// Everything is deterministic from the layout seed, so every peer builds the same map.
    /// </summary>
    public sealed class MapBuilder
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
            var map = new BuiltMap { Layout = layout, Root = _root };
            var all = new Bounds();
            bool first = true;

            foreach (PlacedRoom room in layout.Rooms)
            {
                Bounds b = RoomBoundsOf(room);
                map.RoomBounds.Add(b);
                if (first) { all = b; first = false; } else all.Encapsulate(b);
            }
            map.Bounds = all;

            foreach (PlacedRoom room in layout.Rooms) BuildRoom(layout, map, room);
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
                if (!c.Locked && !c.Secret) continue;
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
                    map.Doors.Add(door);
                }
            }
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
            Color floor = role == RoomRole.Corridor ? new Color(0.27f, 0.28f, 0.3f) : new Color(0.31f, 0.32f, 0.33f);
            if (role == RoomRole.Spawn) floor = new Color(0.28f, 0.34f, 0.3f);
            if (role == RoomRole.Objective) floor = new Color(0.36f, 0.32f, 0.24f);
            if (role == RoomRole.Boss) floor = new Color(0.34f, 0.24f, 0.24f);
            float ceilingY = role == RoomRole.Secret ? 1.6f : _h;

            Box(t, "Floor", new Vector3(b.center.x, -0.1f, b.center.z), new Vector3(b.size.x, 0.2f, b.size.z), floor);
            Box(t, "Ceiling", new Vector3(b.center.x, ceilingY + 0.1f, b.center.z), new Vector3(b.size.x, 0.2f, b.size.z), new Color(0.2f, 0.2f, 0.22f));
            // A stripe of the sector colour along the floor's centre helps reading the map while sectors are new.
            Box(t, "SectorMark", new Vector3(b.center.x, 0.005f, b.center.z), new Vector3(Mathf.Min(b.size.x - 1f, 1.2f), 0.01f, Mathf.Min(b.size.z - 1f, 1.2f)), SectorColor(room.Sector), collider: false);

            // Walls: one segment per boundary cell edge.
            Color wall = new Color(0.36f, 0.37f, 0.4f);
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

            // Light.
            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(t, false);
            lightGo.transform.position = new Vector3(b.center.x, ceilingY - 0.4f, b.center.z);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.shadows = LightShadows.None;
            light.range = Mathf.Max(b.size.x, b.size.z) * 0.85f + 2f;
            light.intensity = role == RoomRole.Corridor ? 1.1f : 1.5f;
            light.color = role == RoomRole.Boss ? new Color(1f, 0.75f, 0.7f) : new Color(0.95f, 0.97f, 1f);

            BuildProps(layout, room, t, b);
        }

        private enum Opening { None, Door, Vent }

        private static Opening OpeningAt(Layout layout, PlacedRoom room, Cell c, Dir dir)
        {
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
            float gap = opening == Opening.Door ? DoorWidth : VentWidth;
            float gapH = opening == Opening.Door ? DoorHeight : VentHeight;
            float side = (_cs - gap) * 0.5f;
            Box(parent, "Wall", center + axis * (gap * 0.5f + side * 0.5f) + Vector3.up * (height * 0.5f), Size(side, height), color);
            Box(parent, "Wall", center - axis * (gap * 0.5f + side * 0.5f) + Vector3.up * (height * 0.5f), Size(side, height), color);
            Box(parent, "Lintel", center + Vector3.up * (gapH + (height - gapH) * 0.5f), Size(gap, height - gapH), color);
        }

        /// <summary>A few crates and benches inside bigger rooms, kept clear of doorways and the room centre line.</summary>
        private void BuildProps(Layout layout, PlacedRoom room, Transform parent, Bounds b)
        {
            RoomRole role = room.Template.Role;
            if (role == RoomRole.Corridor || role == RoomRole.Secret || room.Width < 3 || room.Depth2 < 3) return;
            var rng = new DeterministicRandom(layout.Seed ^ (ulong)(room.Index * 7919 + 17));
            int count = role == RoomRole.Boss ? 4 : rng.Range(2, 5);
            var doors = DoorPoints(layout, room);
            for (int i = 0, tries = 0; i < count && tries < 30; tries++)
            {
                float x = rng.Range(b.min.x + 2f, b.max.x - 2f);
                float z = rng.Range(b.min.z + 2f, b.max.z - 2f);
                var p = new Vector3(x, 0f, z);
                bool clear = (p - new Vector3(b.center.x, 0f, b.center.z)).sqrMagnitude > 4f;
                foreach (Vector3 d in doors) if ((p - d).sqrMagnitude < 9f) clear = false;
                if (!clear) continue;
                bool tall = rng.NextFloat() < 0.3f;
                var size = tall ? new Vector3(1.2f, 1.8f, 1.2f) : new Vector3(rng.Range(1.2f, 2.4f), rng.Range(0.8f, 1.1f), rng.Range(0.8f, 1.4f));
                GameObject prop = Box(parent, tall ? "Locker" : "Crate", p + Vector3.up * (size.y * 0.5f), size, new Color(0.4f, 0.37f, 0.32f));
                prop.transform.rotation = Quaternion.Euler(0f, rng.Range(0, 4) * 90f + rng.Range(-8f, 8f), 0f);
                i++;
            }
        }

        /// <summary>Inner points just inside each connected doorway of the room.</summary>
        private List<Vector3> DoorPoints(Layout layout, PlacedRoom room)
        {
            var list = new List<Vector3>();
            for (int s = 0; s < room.Template.Sockets.Count; s++)
            {
                if (!layout.IsConnected(room.Index, s, out _)) continue;
                Dir f = room.SocketFacing(s);
                list.Add(EdgeMid(room.SocketCell(s), f) - new Vector3(f.Dx(), 0f, f.Dz()) * 1.5f);
            }
            return list;
        }

        /// <summary>Candidate points along the inside of a room's walls, in a fixed order, skipping doorways.</summary>
        private List<Vector3> WallPoints(Layout layout, PlacedRoom room, float inset)
        {
            Bounds b = RoomBoundsOf(room);
            var doors = DoorPoints(layout, room);
            var list = new List<Vector3>();
            for (int x = 0; x < room.Width; x++)
                for (int z = 0; z < room.Depth2; z++)
                {
                    bool edge = x == 0 || z == 0 || x == room.Width - 1 || z == room.Depth2 - 1;
                    if (!edge && room.Width > 2 && room.Depth2 > 2) continue;
                    float px = (room.Origin.X + x + 0.5f) * _cs, pz = (room.Origin.Z + z + 0.5f) * _cs;
                    // Push towards the nearest wall(s).
                    if (x == 0) px = b.min.x + inset;
                    if (x == room.Width - 1 && room.Width > 1) px = b.max.x - inset;
                    if (z == 0) pz = b.min.z + inset;
                    if (z == room.Depth2 - 1 && room.Depth2 > 1) pz = b.max.z - inset;
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
            Bounds sb = map.RoomBounds[0];
            Vector3 c = new Vector3(sb.center.x, 0.05f, sb.center.z);
            Vector3 toDoor = Vector3.forward;
            List<Vector3> doors = DoorPoints(layout, spawn);
            if (doors.Count > 0) toDoor = (doors[0] - c).normalized;
            map.PlayerSpawnYaw = Mathf.Atan2(toDoor.x, toDoor.z) * Mathf.Rad2Deg;
            Vector3 right = Vector3.Cross(Vector3.up, toDoor).normalized;
            map.PlayerSpawns.Add(c - right * 1.2f);
            map.PlayerSpawns.Add(c + right * 1.2f);
            map.PlayerSpawns.Add(c - right * 1.2f - toDoor * 1.8f);
            map.PlayerSpawns.Add(c + right * 1.2f - toDoor * 1.8f);

            // Enemies: spread over wall points of each room (vents and hatches come with the art pass).
            foreach (PlacedRoom room in layout.Rooms)
            {
                int n = room.Template.EnemySpawns;
                if (n <= 0) continue;
                List<Vector3> pts = WallPoints(layout, room, 1.2f);
                if (pts.Count == 0) pts.Add(map.RoomCenter(room.Index));
                for (int i = 0; i < n; i++)
                {
                    Vector3 p = pts[(i * pts.Count / n + i) % pts.Count];
                    map.EnemySpawns.Add(new MapSpawnPoint { Position = p, Room = room.Index, Sector = room.Sector });
                }
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
            go.GetComponent<Renderer>().sharedMaterial = Mat(color);
            return go;
        }
    }
}
