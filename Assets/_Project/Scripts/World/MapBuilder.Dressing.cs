using System.Collections.Generic;
using Deadfall.Core;
using Deadfall.Rules.Common;
using Deadfall.Rules.World;
using UnityEngine;

namespace Deadfall.World
{
    /// <summary>
    /// Set dressing for generated rooms (T-59): a look per room type (wall, panel and floor colours, light tint and
    /// furniture), floor tiles, wall trim with a sector-coloured guide stripe, pilasters, door frames, ceiling beams,
    /// light fixtures, pipe runs, wall clutter and floor grime. Trim has no colliders (the NavMesh and movement
    /// only see walls, columns and furniture), and each room's trim is static-batched into a few meshes.
    /// Furniture is placed by <see cref="BuildProps"/> with colliders, like the crates. Deterministic from the seed.
    /// </summary>
    public sealed partial class MapBuilder
    {
        private struct Theme
        {
            public Color Wall, Panel, Floor, FloorAlt, Light;
            public float LightScale;
            public bool Pipes;
            /// <summary>Service tunnel: brick walls, bare concrete floor and ceiling instead of panels and tiles.</summary>
            public bool Tunnel;
            /// <summary>Furniture placed first, in order (ids from <see cref="FurnitureSizes"/>, or "rack_row" / "barrels").</summary>
            public string[] Furniture;
        }

        /// <summary>In-game box of each furniture fit model (x is the long side).</summary>
        private static readonly Dictionary<string, Vector3> FurnitureSizes = new Dictionary<string, Vector3>
        {
            { "prop_lab_bench", new Vector3(2.4f, 0.95f, 0.9f) },
            { "prop_tank", new Vector3(1.2f, 2.4f, 1.2f) },
            { "prop_server_rack", new Vector3(0.8f, 2.2f, 1.1f) },
            { "prop_shelf", new Vector3(2.4f, 2.2f, 0.7f) },
            { "prop_table", new Vector3(2.2f, 0.8f, 1.6f) },
            { "prop_planter", new Vector3(1.4f, 1.8f, 1.4f) },
            { "prop_desk", new Vector3(2.0f, 1.2f, 0.9f) },
            { "prop_generator", new Vector3(1.8f, 1.8f, 1.4f) },
            { "prop_locker", new Vector3(1.2f, 1.8f, 1.2f) },
            { "rack_row", new Vector3(2.5f, 2.2f, 1.1f) },
            { "barrels", new Vector3(1.4f, 0.9f, 1.4f) },
        };

        private static readonly Color TrimDark = new Color(0.15f, 0.155f, 0.17f);
        private static readonly Color FrameDark = new Color(0.2f, 0.21f, 0.23f);
        private static readonly Color Hazard = new Color(0.85f, 0.65f, 0.13f);

        private static Theme ThemeFor(PlacedRoom room)
        {
            RoomTemplate t = room.Template;
            var th = new Theme
            {
                Wall = new Color(0.36f, 0.37f, 0.4f), Panel = new Color(0.26f, 0.27f, 0.3f),
                Floor = new Color(0.3f, 0.31f, 0.33f), FloorAlt = new Color(0.27f, 0.28f, 0.3f),
                Light = new Color(0.95f, 0.97f, 1f), LightScale = 1f, Furniture = System.Array.Empty<string>(),
            };
            if (t.Role == RoomRole.Corridor && t.HasTag("tunnel"))
            {
                // Harrow Line service tunnels: sooty brick-red walls, wet concrete, sodium lights.
                th.Wall = new Color(0.33f, 0.24f, 0.21f); th.Panel = new Color(0.22f, 0.17f, 0.15f);
                th.Floor = new Color(0.24f, 0.25f, 0.25f); th.FloorAlt = new Color(0.21f, 0.22f, 0.22f);
                th.Light = new Color(1f, 0.78f, 0.5f); th.LightScale = 0.85f; th.Pipes = true; th.Tunnel = true;
            }
            else if (t.Role == RoomRole.Corridor && t.HasTag("hall"))
            {
                // Mercy Vale halls: pale mint walls over a teal dado, scuffed grey linoleum, cold fluorescents.
                th.Wall = new Color(0.6f, 0.66f, 0.62f); th.Panel = new Color(0.22f, 0.4f, 0.4f);
                th.Floor = new Color(0.4f, 0.42f, 0.42f); th.FloorAlt = new Color(0.33f, 0.36f, 0.37f);
                th.Light = new Color(0.86f, 0.95f, 1f); th.LightScale = 0.9f;
            }
            else if (t.Role == RoomRole.Corridor) { th.Pipes = true; th.Floor = new Color(0.27f, 0.28f, 0.3f); th.FloorAlt = new Color(0.25f, 0.26f, 0.28f); }
            else if (t.HasTag("platform"))
            {
                // Cream tiles with a green band, worn platform floor.
                th.Wall = new Color(0.62f, 0.6f, 0.52f); th.Panel = new Color(0.2f, 0.38f, 0.3f);
                th.Floor = new Color(0.36f, 0.35f, 0.33f); th.FloorAlt = new Color(0.55f, 0.48f, 0.18f);
                th.Light = new Color(0.92f, 1f, 0.9f);
                th.Furniture = new[] { "prop_desk", "barrels", "prop_desk", "barrels" };
            }
            else if (t.HasTag("concourse"))
            {
                th.Wall = new Color(0.52f, 0.48f, 0.42f); th.Panel = new Color(0.45f, 0.24f, 0.18f);
                th.Floor = new Color(0.44f, 0.42f, 0.39f); th.FloorAlt = new Color(0.3f, 0.3f, 0.31f);
                th.Light = new Color(1f, 0.92f, 0.78f);
                th.Furniture = new[] { "prop_desk", "prop_planter", "prop_desk", "prop_planter", "prop_table" };
            }
            else if (t.HasTag("yard"))
            {
                th.Wall = new Color(0.36f, 0.36f, 0.34f); th.Panel = new Color(0.5f, 0.4f, 0.14f);
                th.Floor = new Color(0.29f, 0.29f, 0.28f); th.FloorAlt = new Color(0.26f, 0.26f, 0.25f);
                th.Light = new Color(1f, 0.85f, 0.6f); th.LightScale = 1.05f; th.Pipes = true;
                th.Furniture = new[] { "prop_generator", "barrels", "prop_shelf", "barrels", "prop_generator" };
            }
            else if (t.HasTag("pumps"))
            {
                th.Wall = new Color(0.3f, 0.36f, 0.36f); th.Panel = new Color(0.18f, 0.3f, 0.32f);
                th.Floor = new Color(0.25f, 0.29f, 0.29f); th.FloorAlt = new Color(0.22f, 0.26f, 0.26f);
                th.Light = new Color(0.75f, 1f, 0.95f); th.LightScale = 0.9f; th.Pipes = true;
                th.Furniture = new[] { "prop_tank", "prop_generator", "prop_tank", "barrels" };
            }
            else if (t.HasTag("lockers"))
            {
                th.Wall = new Color(0.45f, 0.47f, 0.5f); th.Panel = new Color(0.25f, 0.32f, 0.42f);
                th.Furniture = new[] { "prop_locker", "prop_locker", "prop_table", "prop_locker" };
            }
            else if (t.HasTag("signal"))
            {
                th.Wall = new Color(0.4f, 0.36f, 0.3f); th.Panel = new Color(0.32f, 0.26f, 0.18f);
                th.Light = new Color(1f, 0.88f, 0.7f);
                th.Furniture = new[] { "prop_desk", "prop_server_rack", "prop_desk" };
            }
            else if (t.HasTag("control"))
            {
                th.Wall = new Color(0.34f, 0.36f, 0.4f); th.Panel = new Color(0.2f, 0.3f, 0.45f);
                th.Floor = new Color(0.3f, 0.31f, 0.34f); th.FloorAlt = new Color(0.26f, 0.27f, 0.3f);
                th.Light = new Color(0.75f, 0.88f, 1f); th.Pipes = true;
                th.Furniture = new[] { "prop_desk", "prop_server_rack", "prop_desk", "prop_server_rack", "prop_generator" };
            }
            else if (t.HasTag("turntable"))
            {
                th.Wall = new Color(0.28f, 0.27f, 0.25f); th.Panel = new Color(0.22f, 0.16f, 0.12f);
                th.Floor = new Color(0.18f, 0.22f, 0.21f); th.FloorAlt = new Color(0.15f, 0.19f, 0.18f);
                th.Light = new Color(0.8f, 1f, 0.7f); th.LightScale = 1.1f; th.Pipes = true;
                th.Furniture = new[] { "prop_generator", "barrels", "prop_tank", "barrels" };
            }
            else if (t.HasTag("ward"))
            {
                // Beds in rows (tables stand in), privacy-curtain green, bright clinical light.
                th.Wall = new Color(0.66f, 0.68f, 0.64f); th.Panel = new Color(0.3f, 0.46f, 0.4f);
                th.Floor = new Color(0.44f, 0.45f, 0.44f); th.FloorAlt = new Color(0.38f, 0.4f, 0.4f);
                th.Light = new Color(0.92f, 0.97f, 1f);
                th.Furniture = new[] { "prop_table", "prop_table", "prop_locker", "prop_table", "prop_table" };
            }
            else if (t.HasTag("reception"))
            {
                th.Wall = new Color(0.62f, 0.6f, 0.56f); th.Panel = new Color(0.25f, 0.35f, 0.48f);
                th.Floor = new Color(0.46f, 0.45f, 0.43f); th.FloorAlt = new Color(0.34f, 0.35f, 0.37f);
                th.Light = new Color(1f, 0.96f, 0.88f);
                th.Furniture = new[] { "prop_desk", "prop_planter", "prop_desk", "prop_table", "prop_planter" };
            }
            else if (t.HasTag("surgery"))
            {
                th.Wall = new Color(0.5f, 0.62f, 0.6f); th.Panel = new Color(0.2f, 0.42f, 0.42f);
                th.Floor = new Color(0.36f, 0.42f, 0.42f); th.FloorAlt = new Color(0.3f, 0.36f, 0.36f);
                th.Light = new Color(0.9f, 1f, 1f); th.LightScale = 1.15f;
                th.Furniture = new[] { "prop_lab_bench", "prop_tank", "prop_server_rack", "prop_lab_bench" };
            }
            else if (t.HasTag("pharmacy"))
            {
                th.Wall = new Color(0.6f, 0.6f, 0.58f); th.Panel = new Color(0.45f, 0.28f, 0.26f);
                th.Light = new Color(1f, 0.95f, 0.9f);
                th.Furniture = new[] { "prop_shelf", "prop_shelf", "prop_desk", "prop_shelf" };
            }
            else if (t.HasTag("morgue"))
            {
                // Steel cold-store walls (lockers stand in for the drawers), dim blue light.
                th.Wall = new Color(0.42f, 0.46f, 0.5f); th.Panel = new Color(0.3f, 0.34f, 0.4f);
                th.Floor = new Color(0.3f, 0.32f, 0.35f); th.FloorAlt = new Color(0.26f, 0.28f, 0.31f);
                th.Light = new Color(0.7f, 0.85f, 1f); th.LightScale = 0.8f; th.Pipes = true;
                th.Furniture = new[] { "prop_locker", "prop_table", "prop_locker", "prop_table", "prop_locker" };
            }
            else if (t.HasTag("radiology"))
            {
                th.Wall = new Color(0.4f, 0.42f, 0.46f); th.Panel = new Color(0.32f, 0.3f, 0.4f);
                th.Light = new Color(0.8f, 0.85f, 1f); th.LightScale = 0.9f;
                th.Furniture = new[] { "prop_generator", "prop_desk", "prop_server_rack" };
            }
            else if (t.HasTag("isolation"))
            {
                th.Wall = new Color(0.56f, 0.6f, 0.56f); th.Panel = new Color(0.55f, 0.42f, 0.12f);
                th.Floor = new Color(0.38f, 0.4f, 0.38f); th.FloorAlt = new Color(0.33f, 0.35f, 0.33f);
                th.Light = new Color(0.85f, 1f, 0.85f); th.Pipes = true;
                th.Furniture = new[] { "prop_tank", "prop_desk", "prop_tank", "prop_server_rack", "prop_table" };
            }
            else if (t.HasTag("quarantine"))
            {
                // Plastic-sheeted atrium under red emergency lighting.
                th.Wall = new Color(0.46f, 0.44f, 0.42f); th.Panel = new Color(0.4f, 0.16f, 0.14f);
                th.Floor = new Color(0.34f, 0.32f, 0.31f); th.FloorAlt = new Color(0.29f, 0.27f, 0.26f);
                th.Light = new Color(1f, 0.58f, 0.52f); th.LightScale = 1.1f;
                th.Furniture = new[] { "prop_table", "prop_planter", "prop_tank", "prop_table", "prop_planter" };
            }
            else if (t.HasTag("bay"))
            {
                th.Wall = new Color(0.46f, 0.47f, 0.48f); th.Panel = new Color(0.6f, 0.16f, 0.14f);
                th.Floor = new Color(0.3f, 0.31f, 0.32f); th.FloorAlt = new Color(0.55f, 0.48f, 0.18f);
                th.Light = new Color(0.95f, 0.98f, 1f);
            }
            else if (t.Role == RoomRole.Spawn)
            {
                th.Wall = new Color(0.44f, 0.48f, 0.45f); th.Panel = new Color(0.3f, 0.37f, 0.33f);
                th.Floor = new Color(0.3f, 0.34f, 0.31f); th.FloorAlt = new Color(0.27f, 0.3f, 0.28f); th.Light = new Color(0.9f, 1f, 0.94f);
            }
            else if (t.HasTag("lab"))
            {
                th.Wall = new Color(0.62f, 0.64f, 0.63f); th.Panel = new Color(0.4f, 0.46f, 0.5f);
                th.Floor = new Color(0.42f, 0.44f, 0.44f); th.FloorAlt = new Color(0.37f, 0.39f, 0.39f); th.Light = new Color(0.88f, 0.95f, 1f);
                th.Furniture = new[] { "prop_lab_bench", "prop_tank", "prop_lab_bench", "prop_tank", "prop_desk" };
            }
            else if (t.HasTag("servers"))
            {
                th.Wall = new Color(0.27f, 0.29f, 0.33f); th.Panel = new Color(0.19f, 0.2f, 0.23f);
                th.Floor = new Color(0.34f, 0.35f, 0.37f); th.FloorAlt = new Color(0.3f, 0.31f, 0.33f);
                th.Light = new Color(0.7f, 0.85f, 1f); th.LightScale = 0.9f; th.Pipes = true;
                th.Furniture = new[] { "rack_row", "rack_row", "rack_row", "prop_desk" };
            }
            else if (t.HasTag("storage"))
            {
                th.Wall = new Color(0.5f, 0.56f, 0.6f); th.Panel = new Color(0.33f, 0.4f, 0.46f);
                th.Floor = new Color(0.34f, 0.37f, 0.4f); th.FloorAlt = new Color(0.31f, 0.34f, 0.37f); th.Light = new Color(0.8f, 0.92f, 1f);
                th.Pipes = true;
                th.Furniture = new[] { "prop_shelf", "prop_shelf", "barrels", "prop_shelf" };
            }
            else if (t.HasTag("cafeteria"))
            {
                th.Wall = new Color(0.55f, 0.5f, 0.42f); th.Panel = new Color(0.45f, 0.3f, 0.24f);
                th.Floor = new Color(0.5f, 0.48f, 0.44f); th.FloorAlt = new Color(0.25f, 0.25f, 0.26f);
                th.Light = new Color(1f, 0.9f, 0.75f);
                th.Furniture = new[] { "prop_table", "prop_table", "prop_table", "prop_table" };
            }
            else if (t.HasTag("atrium"))
            {
                th.Wall = new Color(0.5f, 0.49f, 0.46f); th.Panel = new Color(0.36f, 0.35f, 0.33f);
                th.Floor = new Color(0.43f, 0.41f, 0.38f); th.FloorAlt = new Color(0.38f, 0.36f, 0.33f);
                th.Light = new Color(1f, 0.95f, 0.85f);
                th.Furniture = new[] { "prop_planter", "prop_planter", "prop_planter", "prop_desk" };
            }
            else if (t.HasTag("checkpoint"))
            {
                th.Wall = new Color(0.38f, 0.4f, 0.44f); th.Panel = new Color(0.24f, 0.3f, 0.4f);
                th.Furniture = new[] { "prop_desk", "prop_locker" };
            }
            else if (t.Role == RoomRole.Objective)
            {
                th.Wall = new Color(0.35f, 0.37f, 0.33f); th.Panel = new Color(0.29f, 0.33f, 0.24f);
                th.Floor = new Color(0.33f, 0.31f, 0.27f); th.FloorAlt = new Color(0.29f, 0.27f, 0.23f);
                th.Light = new Color(0.85f, 1f, 0.8f); th.Pipes = true;
                th.Furniture = new[] { "prop_generator", "prop_generator", "barrels", "prop_desk" };
            }
            else if (t.Role == RoomRole.Boss)
            {
                th.Wall = new Color(0.34f, 0.3f, 0.3f); th.Panel = new Color(0.25f, 0.18f, 0.18f);
                th.Floor = new Color(0.32f, 0.26f, 0.25f); th.FloorAlt = new Color(0.28f, 0.22f, 0.21f);
                th.Light = new Color(1f, 0.62f, 0.56f); th.LightScale = 1.1f; th.Pipes = true;
                th.Furniture = new[] { "prop_tank", "prop_tank", "prop_generator", "prop_tank" };
            }
            else if (t.Role == RoomRole.Secret)
            {
                th.Wall = new Color(0.3f, 0.3f, 0.31f); th.Floor = th.FloorAlt = new Color(0.25f, 0.25f, 0.26f);
            }
            return th;
        }

        // ------------------------------------------------------------------------------------------- trim

        /// <summary>One straight piece of wall face: cells from A to B (world units along the wall) on a boundary line.</summary>
        private struct WallRun
        {
            public Dir Facing;
            public float Line, A, B;
        }

        private struct WallGap
        {
            public Dir Facing;
            public Opening Kind;
            public Vector3 Edge; // floor point on the boundary, middle of the opening
            public float Width, Height;
        }

        private void DressRoom(Layout layout, BuiltMap map, PlacedRoom room, Transform parent, float ceilingY, Theme th, List<Vector3> lights)
        {
            var trim = new GameObject("Trim").transform;
            trim.SetParent(parent, false);
            var rng = new DeterministicRandom(layout.Seed ^ (ulong)(room.Index * 104729 + 31));
            RoomRole role = room.Template.Role;

            // Floor tiles: one plate per cell, the darker floor below shows as grout.
            foreach (Cell c in room.Cells())
            {
                Color tile = ((c.X + c.Z) & 1) == 0 ? th.Floor : th.FloorAlt;
                Box(trim, "Tile", new Vector3((c.X + 0.5f) * _cs, 0.01f, (c.Z + 0.5f) * _cs), new Vector3(_cs - 0.08f, 0.02f, _cs - 0.08f), tile,
                    collider: false, surface: th.Tunnel ? Surface.Concrete : Surface.FloorTile);
            }
            if (role == RoomRole.Secret)
            {
                Batch(trim);
                return;
            }

            CollectWalls(layout, room, out List<WallRun> runs, out List<WallRun> fullRuns, out List<WallGap> gaps);
            Color stripe = SectorColor(room.Sector) * 0.85f;
            stripe.a = 1f;
            Color pilaster = th.Wall * 0.86f;
            pilaster.a = 1f;
            foreach (WallRun r in runs)
            {
                float len = r.B - r.A;
                if (len < 0.05f) continue;
                Strip(trim, r, 0f, 0.16f, 0.05f, TrimDark);                 // baseboard
                Strip(trim, r, 0.16f, 1.1f, 0.025f, th.Panel);              // lower panel
                Strip(trim, r, 1.2f, 0.1f, 0.04f, stripe);                   // sector guide stripe
                if (ceilingY > 3f) Strip(trim, r, ceilingY - 0.3f, 0.3f, 0.14f, TrimDark); // cornice
                // Pilasters at the cell lines inside the run.
                int k0 = Mathf.CeilToInt(r.A / _cs + 0.01f), k1 = Mathf.FloorToInt(r.B / _cs - 0.01f);
                for (int k = k0; k <= k1; k++)
                    if (k * _cs - r.A > 0.4f && r.B - k * _cs > 0.4f) Piece(trim, r.Facing, r.Line, k * _cs, 0.34f, 0f, ceilingY, 0.11f, pilaster);
            }
            foreach (WallGap g in gaps) DressGap(trim, g, room.Sector);

            if (ceilingY > 3f) Beams(trim, room, ceilingY);
            foreach (Vector3 lp in lights) Fixture(trim, room, lp, ceilingY);
            if (ceilingY > 3f && (th.Pipes || rng.NextFloat() < 0.4f)) Pipes(trim, room, fullRuns, ceilingY, rng);
            WallClutter(parent, runs, role, rng, room.Index);
            Batch(trim);
        }

        /// <summary>Static-batches a room's trim (built-in primitives are readable, so this works at runtime).</summary>
        private static void Batch(Transform trim)
        {
            if (trim.childCount > 1) StaticBatchingUtility.Combine(trim.gameObject);
        }

        /// <summary>Wall faces of a room merged into straight runs: <paramref name="runs"/> skip openings, <paramref name="fullRuns"/> don't.</summary>
        private void CollectWalls(Layout layout, PlacedRoom room, out List<WallRun> runs, out List<WallRun> fullRuns, out List<WallGap> gaps)
        {
            var pieces = new Dictionary<(Dir, int), List<Vector2>>();
            var whole = new Dictionary<(Dir, int), List<Vector2>>();
            gaps = new List<WallGap>();
            foreach (Cell c in room.Cells())
                for (int d = 0; d < 4; d++)
                {
                    var dir = (Dir)d;
                    if (room.Contains(c.Step(dir))) continue;
                    bool alongX = dir == Dir.North || dir == Dir.South;
                    int line = dir == Dir.North ? c.Z + 1 : dir == Dir.South ? c.Z : dir == Dir.East ? c.X + 1 : c.X;
                    float a = (alongX ? c.X : c.Z) * _cs, b = a + _cs;
                    var key = (dir, line);
                    if (!pieces.TryGetValue(key, out List<Vector2> list)) pieces[key] = list = new List<Vector2>();
                    if (!whole.TryGetValue(key, out List<Vector2> all)) whole[key] = all = new List<Vector2>();
                    all.Add(new Vector2(a, b));
                    Opening op = OpeningAt(layout, room, c, dir);
                    if (op == Opening.None) { list.Add(new Vector2(a, b)); continue; }
                    float w = op == Opening.Door ? DoorWidth : op == Opening.Breach ? BreachBarricade.OpeningWidth : VentWidth;
                    float h = op == Opening.Door ? DoorHeight : op == Opening.Breach ? BreachBarricade.OpeningHeight : VentHeight;
                    float m = (a + b) * 0.5f;
                    list.Add(new Vector2(a, m - w * 0.5f));
                    list.Add(new Vector2(m + w * 0.5f, b));
                    gaps.Add(new WallGap { Facing = dir, Kind = op, Edge = EdgeMid(c, dir), Width = w, Height = h });
                }
            runs = Merge(pieces);
            fullRuns = Merge(whole);
        }

        private List<WallRun> Merge(Dictionary<(Dir, int), List<Vector2>> byLine)
        {
            var result = new List<WallRun>();
            var keys = new List<(Dir, int)>(byLine.Keys);
            keys.Sort((x, y) => x.Item1 != y.Item1 ? x.Item1.CompareTo(y.Item1) : x.Item2.CompareTo(y.Item2)); // fixed order
            foreach ((Dir dir, int line) in keys)
            {
                List<Vector2> list = byLine[(dir, line)];
                list.Sort((p, q) => p.x.CompareTo(q.x));
                Vector2 cur = list[0];
                for (int i = 1; i <= list.Count; i++)
                {
                    if (i < list.Count && list[i].x <= cur.y + 0.01f) { cur.y = Mathf.Max(cur.y, list[i].y); continue; }
                    result.Add(new WallRun { Facing = dir, Line = line * _cs, A = cur.x, B = cur.y });
                    if (i < list.Count) cur = list[i];
                }
            }
            return result;
        }

        /// <summary>Into-the-room direction for a wall facing <paramref name="dir"/>.</summary>
        private static Vector3 Inward(Dir dir) => new Vector3(-dir.Dx(), 0f, -dir.Dz());

        /// <summary>A box against the wall face: centred at <paramref name="along"/>, <paramref name="depth"/> out from the face.</summary>
        private GameObject Piece(Transform parent, Dir facing, float line, float along, float length, float y0, float height, float depth, Color color, float faceOffset = 0f)
        {
            bool alongX = facing == Dir.North || facing == Dir.South;
            Vector3 inward = Inward(facing);
            Vector3 onLine = alongX ? new Vector3(along, 0f, line) : new Vector3(line, 0f, along);
            Vector3 p = onLine + inward * (WallThickness + faceOffset + depth * 0.5f) + Vector3.up * (y0 + height * 0.5f);
            Vector3 size = alongX ? new Vector3(length, height, depth) : new Vector3(depth, height, length);
            return Box(parent, "Trim", p, size, color, collider: false);
        }

        private void Strip(Transform parent, WallRun r, float y0, float height, float depth, Color color) =>
            Piece(parent, r.Facing, r.Line, (r.A + r.B) * 0.5f, r.B - r.A, y0, height, depth, color);

        /// <summary>Door frames with a sector header, vent frames, and hazard stripes on the floor in front of breaches.</summary>
        private void DressGap(Transform parent, WallGap g, int sector)
        {
            bool alongX = g.Facing == Dir.North || g.Facing == Dir.South;
            float line = alongX ? g.Edge.z : g.Edge.x, mid = alongX ? g.Edge.x : g.Edge.z;
            Vector3 inward = Inward(g.Facing);
            Vector3 axis = alongX ? Vector3.right : Vector3.forward;
            if (g.Kind == Opening.Breach)
            {
                // Hazard chevrons on the floor: this is where they break in.
                for (int i = 0; i < 5; i++)
                {
                    float off = (i - 2) * (g.Width / 5f);
                    Vector3 p = g.Edge + inward * 0.45f + axis * off + Vector3.up * 0.024f;
                    Vector3 size = alongX ? new Vector3(g.Width / 5f - 0.02f, 0.008f, 0.5f) : new Vector3(0.5f, 0.008f, g.Width / 5f - 0.02f);
                    Box(parent, "Hazard", p, size, (i & 1) == 0 ? Hazard : TrimDark, collider: false);
                }
                return;
            }
            float frameDepth = WallThickness + 0.06f;
            float jamb = g.Kind == Opening.Door ? 0.18f : 0.1f;
            for (int s = -1; s <= 1; s += 2)
                Piece(parent, g.Facing, line, mid + s * (g.Width * 0.5f + jamb * 0.5f), jamb, 0f, g.Height + jamb, frameDepth, FrameDark, -WallThickness);
            Piece(parent, g.Facing, line, mid, g.Width + jamb * 2f, g.Height, jamb, frameDepth, FrameDark, -WallThickness);
            if (g.Kind != Opening.Door) return;
            Color sc = SectorColor(sector);
            Piece(parent, g.Facing, line, mid, g.Width, g.Height + 0.05f, 0.08f, 0.02f, sc, 0.06f); // header stripe
            // Threshold plate on this room's side (the neighbour lays its own).
            Vector3 tp = g.Edge + inward * 0.3f + Vector3.up * 0.025f;
            Box(parent, "Threshold", tp, alongX ? new Vector3(g.Width, 0.01f, 0.6f) : new Vector3(0.6f, 0.01f, g.Width), new Color(0.24f, 0.25f, 0.27f), collider: false);
        }

        /// <summary>Beams across the short side of the room at every cell line.</summary>
        private void Beams(Transform parent, PlacedRoom room, float ceilingY)
        {
            bool linesOnX = room.Width >= room.Depth2; // beams sit on x = const lines and run along z
            int lines = linesOnX ? room.Width : room.Depth2, across = linesOnX ? room.Depth2 : room.Width;
            Color beam = new Color(0.24f, 0.245f, 0.26f);
            for (int k = 1; k < lines; k++)
            {
                int start = -1;
                for (int j = 0; j <= across; j++)
                {
                    bool both = j < across && In(room, linesOnX, k, j) && In(room, linesOnX, k - 1, j);
                    if (both && start < 0) start = j;
                    if (both || start < 0) continue;
                    float a = (start + (linesOnX ? room.Origin.Z : room.Origin.X)) * _cs, b = (j + (linesOnX ? room.Origin.Z : room.Origin.X)) * _cs;
                    float l = ((linesOnX ? room.Origin.X : room.Origin.Z) + k) * _cs;
                    Vector3 p = linesOnX ? new Vector3(l, ceilingY - 0.2f, (a + b) * 0.5f) : new Vector3((a + b) * 0.5f, ceilingY - 0.2f, l);
                    Box(parent, "Beam", p, linesOnX ? new Vector3(0.3f, 0.4f, b - a) : new Vector3(b - a, 0.4f, 0.3f), beam, collider: false);
                    start = -1;
                }
            }
        }

        private static bool In(PlacedRoom room, bool linesOnX, int k, int j) =>
            room.Contains(linesOnX ? new Cell(room.Origin.X + k, room.Origin.Z + j) : new Cell(room.Origin.X + j, room.Origin.Z + k));

        private void Fixture(Transform parent, PlacedRoom room, Vector3 lightPos, float ceilingY)
        {
            bool longX = room.Width >= room.Depth2;
            var size = new Vector3(1.4f, 0.14f, 0.5f);
            GameObject box = Box(parent, "Fixture", new Vector3(lightPos.x, ceilingY - 0.07f, lightPos.z), longX ? size : new Vector3(0.5f, 0.14f, 1.4f),
                new Color(0.85f, 0.86f, 0.84f), collider: false);
            box.transform.SetParent(parent.parent, true); // the model is not readable: keep it out of the batched trim
            ModelLibrary.Skin(box, "prop_light_fixture", longAlongX: true);
        }

        /// <summary>Two or three pipes with brackets high along one long wall.</summary>
        private void Pipes(Transform parent, PlacedRoom room, List<WallRun> fullRuns, float ceilingY, DeterministicRandom rng)
        {
            bool alongX = room.Width >= room.Depth2;
            var candidates = new List<WallRun>();
            foreach (WallRun r in fullRuns)
                if ((r.Facing == Dir.North || r.Facing == Dir.South) == alongX && r.B - r.A >= _cs * 0.99f) candidates.Add(r);
            if (candidates.Count == 0) return;
            Dir side = candidates[rng.Range(0, candidates.Count)].Facing;
            Color[] colors = { new Color(0.43f, 0.3f, 0.2f), new Color(0.24f, 0.36f, 0.28f), new Color(0.42f, 0.44f, 0.47f) };
            float[] diameters = { 0.2f, 0.14f, 0.26f };
            int count = rng.Range(2, 4);
            int first = rng.Range(0, 3);
            foreach (WallRun r in candidates)
            {
                if (r.Facing != side) continue;
                float len = r.B - r.A, mid = (r.A + r.B) * 0.5f;
                for (int i = 0; i < count; i++)
                {
                    int s = (first + i) % 3;
                    float d = diameters[s];
                    float y = ceilingY - 0.6f - i * 0.3f;
                    Vector3 inward = Inward(r.Facing);
                    Vector3 onLine = alongX ? new Vector3(mid, y, r.Line) : new Vector3(r.Line, y, mid);
                    Vector3 p = onLine + inward * (WallThickness + 0.2f);
                    GameObject pipe = Cyl(parent, p, d, len, alongX, colors[s]);
                    pipe.name = "Pipe";
                }
                for (float a = r.A + 1f; a < r.B - 0.5f; a += 2f)
                    Piece(parent, r.Facing, r.Line, a, 0.06f, ceilingY - 0.6f - (count - 1) * 0.3f - 0.18f, (count - 1) * 0.3f + 0.36f, 0.32f, TrimDark);
            }
        }

        /// <summary>Fuse boxes, vents and extinguishers on the wall, high enough to clear switches and loot.</summary>
        private void WallClutter(Transform parent, List<WallRun> runs, RoomRole role, DeterministicRandom rng, int room)
        {
            int wanted = role == RoomRole.Corridor ? 1 : 3;
            int placed = 0;
            for (int i = 0; i < runs.Count && placed < wanted; i++)
            {
                WallRun r = runs[(i * 7 + rng.Range(0, runs.Count)) % runs.Count];
                if (r.B - r.A < 1.6f || rng.NextFloat() < 0.35f) continue;
                float along = rng.Range(r.A + 0.7f, r.B - 0.7f);
                float pick = rng.NextFloat();
                string id = pick < 0.45f ? "prop_fusebox" : pick < 0.8f ? "prop_wall_vent" : "prop_extinguisher";
                float y = id == "prop_fusebox" ? 2.1f : id == "prop_wall_vent" ? 3.3f : 1.55f;
                if (id == "prop_extinguisher" && role == RoomRole.Corridor) y = 1.9f;
                bool alongX = r.Facing == Dir.North || r.Facing == Dir.South;
                Vector3 inward = Inward(r.Facing);
                Vector3 p = (alongX ? new Vector3(along, y, r.Line) : new Vector3(r.Line, y, along)) + inward * (WallThickness + 0.02f);
                GameObject go = ModelLibrary.Spawn(id, parent);
                if (go == null) return;
                go.transform.position = p;
                go.transform.rotation = Quaternion.LookRotation(inward, Vector3.up);
                go.isStatic = true;
                if (y < 2.5f) UseWall(room, p); // low enough to clash with a rack or locker
                placed++;
            }
        }

        // ------------------------------------------------------------------------------------------- furniture

        /// <summary>
        /// Places one piece of themed furniture: a collider box (it blocks movement and the NavMesh like the crates)
        /// dressed with its model. Rack rows are three racks side by side; barrels are a cluster of drums.
        /// </summary>
        private void PlaceFurniture(Transform parent, string id, Vector3 floor, float yaw)
        {
            Vector3 size = FurnitureSizes[id];
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            if (id == "rack_row")
            {
                for (int i = -1; i <= 1; i++)
                {
                    Vector3 p = floor + rot * new Vector3(i * 0.84f, 0f, 0f);
                    GameObject rack = Box(parent, "ServerRack", p + Vector3.up * 1.1f, FurnitureSizes["prop_server_rack"], new Color(0.15f, 0.16f, 0.18f));
                    rack.transform.rotation = rot;
                    ModelLibrary.Skin(rack, "prop_server_rack");
                }
                return;
            }
            if (id == "barrels")
            {
                Vector3[] offsets = { new Vector3(-0.35f, 0f, -0.3f), new Vector3(0.36f, 0f, -0.25f), new Vector3(0f, 0f, 0.36f) };
                foreach (Vector3 o in offsets)
                {
                    Vector3 p = floor + rot * o;
                    GameObject barrel = Box(parent, "Barrel", p + Vector3.up * 0.45f, new Vector3(0.6f, 0.9f, 0.6f), new Color(0.25f, 0.35f, 0.48f));
                    GameObject model = ModelLibrary.Spawn("prop_barrel", parent);
                    if (model == null) continue;
                    model.transform.position = p;
                    model.transform.rotation = rot * Quaternion.Euler(0f, o.x * 90f, 0f);
                    model.isStatic = true;
                    barrel.GetComponent<Renderer>().enabled = false;
                }
                return;
            }
            GameObject box = Box(parent, id.Substring(5), floor + Vector3.up * (size.y * 0.5f), size, new Color(0.4f, 0.4f, 0.42f));
            box.transform.rotation = rot;
            ModelLibrary.Skin(box, id);
        }

        /// <summary>Furniture that stands against a wall instead of out in the room.</summary>
        private static bool AgainstWall(string id) => id == "prop_server_rack" || id == "rack_row" || id == "prop_locker" || id == "prop_shelf" || id == "prop_desk";

        /// <summary>Floor footprint radius used to keep furniture apart.</summary>
        private static float FootprintRadius(string id)
        {
            Vector3 s = FurnitureSizes.TryGetValue(id, out Vector3 v) ? v : new Vector3(1.8f, 1f, 1.4f);
            return Mathf.Max(s.x, s.z) * 0.5f;
        }

        /// <summary>A static cylinder with no collider, lying along X or Z.</summary>
        private GameObject Cyl(Transform parent, Vector3 center, float diameter, float length, bool alongX, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.rotation = alongX ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(diameter, length * 0.5f, diameter); // the primitive is 2 m tall
            go.isStatic = true;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = Mat(color);
            return go;
        }
    }
}
