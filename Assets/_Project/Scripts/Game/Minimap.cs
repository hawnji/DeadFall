using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Rules.World;
using Deadfall.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deadfall.Game
{
    /// <summary>
    /// A square minimap under the location card that turns with the player (forward is always up). The layout is
    /// painted once into a pixel buffer (open rooms only, tinted by sector, walls outlined; locked sectors stay blank) and repainted when
    /// sectors or side doors open; each frame the visible square is resampled, rotated, into a small view texture
    /// that is drawn with plain IMGUI. (It used to be a GL quad, which some graphics backends drew upside down at the
    /// bottom left of the screen, away from its frame and markers.) Markers on top: closed doors, objectives, the Gun Dispenser, grid generators, Supply Caches and the
    /// squad. M opens the full map: the whole open layout, north up, centred on screen at half opacity, with the same
    /// markers and an arrow for you (the minimap hides while it is open).
    /// </summary>
    public sealed class Minimap : MonoBehaviour
    {
        private const int CellPixels = 8;
        private const float Size = 220f;
        private const float RadiusMeters = 34f;
        private const float Top = 110f, Left = 28f;

        private static readonly Color Background = new Color(0.04f, 0.05f, 0.06f, 0.92f);
        private static readonly Color Sealed = new Color(0.11f, 0.12f, 0.13f, 1f);
        private static readonly Color Wall = new Color(0.75f, 0.8f, 0.85f, 1f);

        /// <summary>Resolution of the view texture drawn in the frame.</summary>
        private const int ViewPixels = 192;

        private Color32[] _px;
        private int _pxW, _pxH;
        private Texture2D _view;
        private Color32[] _viewPx;
        private ulong _paintedSeed;
        private int _paintedState = -1;
        private int _minX, _minZ, _cellsX, _cellsZ;
        private float _cellSize = 4f;
        private bool _fullOpen;
        private Texture2D _full;
        private const float FullAlpha = 0.5f;
        private InputAction _toggle;
        private readonly List<Vector3> _markers = new List<Vector3>(16);

        private void Awake()
        {
            _toggle = new InputAction("Minimap", InputActionType.Button, "<Keyboard>/m");
            _toggle.Enable();
        }

        private void OnDestroy()
        {
            _toggle?.Disable();
            if (_view != null) Destroy(_view);
            if (_full != null) Destroy(_full);
        }

        private void Update()
        {
            if (_toggle != null && _toggle.WasPressedThisFrame() && !UiModal.Active) _fullOpen = !_fullOpen;
        }

        // ---------------------------------------------------------------- painting

        /// <summary>Changes whenever the open area changes (sectors, side doors).</summary>
        private static int OpenState(MapSession s) => s.OpenSectors * 1000 + s.DoorsOpened;

        private void Paint(MapSession s)
        {
            Layout layout = s.Map.Layout;
            _cellSize = s.Map.CellSize;
            _minX = _minZ = int.MaxValue;
            int maxX = int.MinValue, maxZ = int.MinValue;
            var owner = new Dictionary<long, int>();
            foreach (PlacedRoom r in layout.Rooms)
                foreach (Cell c in r.Cells())
                {
                    owner[c.Key] = r.Index;
                    _minX = Mathf.Min(_minX, c.X); _minZ = Mathf.Min(_minZ, c.Z);
                    maxX = Mathf.Max(maxX, c.X); maxZ = Mathf.Max(maxZ, c.Z);
                }
            if (owner.Count == 0) return;
            _minX -= 1; _minZ -= 1; // a blank cell of margin all round
            _cellsX = maxX - _minX + 2;
            _cellsZ = maxZ - _minZ + 2;
            int w = _cellsX * CellPixels, h = _cellsZ * CellPixels;

            // Doorways and archways: edges between connected socket cells stay open.
            var openings = new HashSet<long>();
            foreach (Connection c in layout.Connections)
            {
                if (c.Secret) continue;
                Cell a = layout.Rooms[c.RoomA].SocketCell(c.SocketA);
                Cell b = layout.Rooms[c.RoomB].SocketCell(c.SocketB);
                openings.Add(EdgeKey(a, b));
            }

            var px = new Color32[w * h];
            Color32 bg = Background;
            for (int i = 0; i < px.Length; i++) px[i] = bg;
            foreach (PlacedRoom r in layout.Rooms)
            {
                // Only the open part of the map: locked sectors and unopened side rooms (and secrets) stay blank.
                if (!s.IsRoomOpen(r.Index)) continue;
                bool open = true;
                Color sector = MapBuilder.SectorColor(r.Sector);
                Color fill = !open ? Sealed
                    : r.Template.Role == RoomRole.Corridor ? Color.Lerp(new Color(0.3f, 0.32f, 0.34f), sector, 0.15f)
                    : Color.Lerp(new Color(0.24f, 0.26f, 0.28f), sector, 0.35f);
                Color32 f = fill, wall = open ? Wall : new Color(0.3f, 0.32f, 0.34f, 1f);
                foreach (Cell c in r.Cells())
                {
                    int ox = (c.X - _minX) * CellPixels, oz = (c.Z - _minZ) * CellPixels;
                    for (int y = 0; y < CellPixels; y++)
                        for (int x = 0; x < CellPixels; x++) px[(oz + y) * w + ox + x] = f;
                    for (int d = 0; d < 4; d++)
                    {
                        var dir = (Dir)d;
                        Cell n = c.Step(dir);
                        if (owner.TryGetValue(n.Key, out int other) && other == r.Index) continue; // same room
                        if (openings.Contains(EdgeKey(c, n))) continue;
                        for (int k = 0; k < CellPixels; k++)
                        {
                            int x = dir == Dir.East ? CellPixels - 1 : dir == Dir.West ? 0 : k;
                            int y = dir == Dir.North ? CellPixels - 1 : dir == Dir.South ? 0 : k;
                            px[(oz + y) * w + ox + x] = wall;
                        }
                    }
                }
            }
            _px = px;
            _pxW = w;
            _pxH = h;
            if (_full != null) Destroy(_full);
            _full = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point, name = "FullMap" };
            _full.SetPixels32(px);
            _full.Apply(false);
        }

        /// <summary>Resamples the square around the player, turned so forward is up, into the view texture.</summary>
        private void UpdateView(Vector3 pos, Vector3 fwd, Vector3 right)
        {
            if (_view == null)
            {
                _view = new Texture2D(ViewPixels, ViewPixels, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "MinimapView" };
                _viewPx = new Color32[ViewPixels * ViewPixels];
            }
            Color32 bg = Background;
            float pxPerMeter = CellPixels / _cellSize;
            float ox = pos.x * pxPerMeter - _minX * CellPixels, oz = pos.z * pxPerMeter - _minZ * CellPixels;
            float step = 2f * RadiusMeters / ViewPixels * pxPerMeter;
            // Texture rows go bottom to top: row 0 is the far edge behind the player (dy = -1).
            for (int y = 0; y < ViewPixels; y++)
            {
                float dy = (y + 0.5f) - ViewPixels * 0.5f;
                for (int x = 0; x < ViewPixels; x++)
                {
                    float dx = (x + 0.5f) - ViewPixels * 0.5f;
                    float sx = ox + (right.x * dx + fwd.x * dy) * step;
                    float sz = oz + (right.z * dx + fwd.z * dy) * step;
                    int ix = (int)sx, iz = (int)sz;
                    _viewPx[y * ViewPixels + x] = sx < 0f || sz < 0f || ix >= _pxW || iz >= _pxH ? bg : _px[iz * _pxW + ix];
                }
            }
            _view.SetPixels32(_viewPx);
            _view.Apply(false);
        }

        private static long EdgeKey(Cell a, Cell b)
        {
            long ka = a.Key, kb = b.Key;
            return ka < kb ? ka * 1000003L ^ kb : kb * 1000003L ^ ka;
        }

        // ---------------------------------------------------------------- drawing

        private void OnGUI()
        {
            if (UiModal.Active || Event.current.type != EventType.Repaint) return;
            MapSession s = MapSession.Current;
            PlayerIdentity me = PlayerIdentity.Local;
            Camera cam = Camera.main;
            if (s == null || !s.HasMap || me == null || cam == null) return;
            if (_paintedSeed != s.Seed || _paintedState != OpenState(s) || _px == null)
            {
                _paintedSeed = s.Seed;
                _paintedState = OpenState(s);
                Paint(s);
            }
            if (_px == null) return;

            Vector3 pos = me.transform.position;
            Vector3 fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = me.transform.forward;
            fwd.Normalize();
            var right = new Vector3(fwd.z, 0f, -fwd.x);

            if (_fullOpen)
            {
                DrawFull(s, pos, fwd);
                return;
            }
            UpdateView(pos, fwd, right);
            HudKit.Begin();
            var frame = new Rect(Left, Top, Size, Size);
            HudKit.Panel(new Rect(frame.x - 4f, frame.y - 4f, frame.width + 8f, frame.height + 8f));
            GUI.DrawTexture(frame, _view, ScaleMode.StretchToFill, false);
            Vector2 centre = frame.center;
            // Closed doors.
            foreach (SectorDoor d in s.Map.Doors)
                if (d != null && !d.IsOpen) Marker(centre, pos, fwd, right, d.transform.position, new Color(1f, 0.6f, 0.15f), 5f);
            // Objectives.
            _markers.Clear();
            ObjectiveDirector.Current?.CollectMarkers(_markers);
            foreach (Vector3 m in _markers) if (Revealed(s, m)) Marker(centre, pos, fwd, right, m, InteractGlow.Objectives, 7f, edge: true);
            PowerGrid grid = PowerGrid.Current;
            if (grid != null)
                foreach (GridGenerator g in grid.Generators)
                    if (g != null && Revealed(s, g.transform.position)) Marker(centre, pos, fwd, right, g.transform.position, g.Online ? new Color(0.4f, 0.75f, 1f) : new Color(1f, 0.3f, 0.25f), 6f);
            // Guns.
            GunDispenser dispenser = GunDispenserDirector.Current != null ? GunDispenserDirector.Current.Dispenser : null;
            if (dispenser != null && dispenser.State != GunDispenser.Mode.Hidden) Marker(centre, pos, fwd, right, dispenser.transform.position, InteractGlow.Guns, 8f, edge: true);
            RunEconomy economy = RunEconomy.Current;
            if (economy != null)
                foreach (SupplyCache c in economy.Caches) if (c != null && Revealed(s, c.transform.position)) Marker(centre, pos, fwd, right, c.transform.position, InteractGlow.Guns, 6f);
            // Squad.
            foreach (PlayerIdentity p in PlayerIdentity.All)
                if (p != null && !p.IsLocal) Marker(centre, pos, fwd, right, p.transform.position, Color.white, 6f, edge: true);
            // You: an arrow pointing up (the map turns, not the arrow).
            HudKit.Label(new Rect(centre.x - 10f, centre.y - 12f, 20f, 22f), "▲", 16, HudKit.Accent, TextAnchor.MiddleCenter);
            HudKit.Label(new Rect(frame.x + 6f, frame.yMax - 18f, frame.width - 12f, 16f), "M  full map", 10, HudKit.TextDim, TextAnchor.MiddleRight);
            HudKit.End();
        }

        // ---------------------------------------------------------------- full map

        private Rect _fullRect;

        /// <summary>The whole open layout, north up, in the middle of the screen at half opacity.</summary>
        private void DrawFull(MapSession s, Vector3 pos, Vector3 fwd)
        {
            if (_full == null) return;
            HudKit.Begin();
            float maxW = HudKit.W * 0.7f, maxH = HudKit.H * 0.78f;
            float scale = Mathf.Min(maxW / _pxW, maxH / _pxH);
            float w = _pxW * scale, h = _pxH * scale;
            _fullRect = new Rect((HudKit.W - w) * 0.5f, (HudKit.H - h) * 0.5f, w, h);
            Color was = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, FullAlpha);
            GUI.DrawTexture(_fullRect, _full, ScaleMode.StretchToFill, true);
            GUI.color = was;
            HudKit.Outline(new Rect(_fullRect.x - 2f, _fullRect.y - 2f, _fullRect.width + 4f, _fullRect.height + 4f), new Color(1f, 1f, 1f, 0.25f));

            foreach (SectorDoor d in s.Map.Doors)
                if (d != null && !d.IsOpen) FullMarker(d.transform.position, new Color(1f, 0.6f, 0.15f), 7f);
            _markers.Clear();
            ObjectiveDirector.Current?.CollectMarkers(_markers);
            foreach (Vector3 m in _markers) if (Revealed(s, m)) FullMarker(m, InteractGlow.Objectives, 9f);
            PowerGrid grid = PowerGrid.Current;
            if (grid != null)
                foreach (GridGenerator g in grid.Generators)
                    if (g != null && Revealed(s, g.transform.position)) FullMarker(g.transform.position, g.Online ? new Color(0.4f, 0.75f, 1f) : new Color(1f, 0.3f, 0.25f), 8f);
            GunDispenser dispenser = GunDispenserDirector.Current != null ? GunDispenserDirector.Current.Dispenser : null;
            if (dispenser != null && dispenser.State != GunDispenser.Mode.Hidden) FullMarker(dispenser.transform.position, InteractGlow.Guns, 10f);
            RunEconomy economy = RunEconomy.Current;
            if (economy != null)
                foreach (SupplyCache c in economy.Caches) if (c != null && Revealed(s, c.transform.position)) FullMarker(c.transform.position, InteractGlow.Guns, 8f);
            foreach (PlayerIdentity p in PlayerIdentity.All)
                if (p != null && !p.IsLocal) FullMarker(p.transform.position, Color.white, 8f);

            // You: an arrow turned to where you're facing.
            Vector2 me = ToFull(pos);
            Matrix4x4 m0 = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg, me);
            HudKit.Label(new Rect(me.x - 12f, me.y - 13f, 24f, 26f), "▲", 20, HudKit.Accent, TextAnchor.MiddleCenter);
            GUI.matrix = m0;

            DrawLegend(grid != null && grid.Generators.Count > 0, dispenser != null && dispenser.State != GunDispenser.Mode.Hidden);

            HudKit.Label(new Rect(_fullRect.x, _fullRect.y - 26f, _fullRect.width, 22f), "MAP", 16, Color.white, TextAnchor.MiddleLeft, head: true);
            HudKit.Label(new Rect(_fullRect.x, _fullRect.y - 26f, _fullRect.width, 22f), "M  close", 12, HudKit.TextDim, TextAnchor.MiddleRight, head: true);
            HudKit.End();
        }

        private static readonly List<(string label, Color color, bool arrow)> LegendRows = new List<(string, Color, bool)>(10);

        /// <summary>Key to the full map's marks, in its bottom-left corner. Generator rows only on maps with a power grid.</summary>
        private void DrawLegend(bool generators, bool dispenser)
        {
            LegendRows.Clear();
            LegendRows.Add(("You", HudKit.Accent, true));
            LegendRows.Add(("Teammate", Color.white, false));
            LegendRows.Add(("Objective", InteractGlow.Objectives, false));
            LegendRows.Add(("Locked door", new Color(1f, 0.6f, 0.15f), false));
            LegendRows.Add((dispenser ? "Gun Dispenser / Supply Cache" : "Supply Cache", InteractGlow.Guns, false));
            if (generators)
            {
                LegendRows.Add(("Generator (online)", new Color(0.4f, 0.75f, 1f), false));
                LegendRows.Add(("Generator (offline)", new Color(1f, 0.3f, 0.25f), false));
            }
            const float rowH = 18f, pad = 10f;
            float w = 0f;
            foreach (var row in LegendRows) w = Mathf.Max(w, HudKit.TextWidth(row.label, 12));
            w += 34f + pad;
            float h = LegendRows.Count * rowH + pad * 2f + 16f;
            var box = new Rect(_fullRect.x + 8f, _fullRect.yMax - h - 8f, w, h);
            HudKit.Panel(box, 0.85f);
            HudKit.Label(new Rect(box.x + pad, box.y + 6f, box.width - pad * 2f, 16f), "LEGEND", 11, HudKit.TextDim, TextAnchor.MiddleLeft, head: true);
            float y = box.y + pad + 16f;
            foreach (var row in LegendRows)
            {
                var swatch = new Rect(box.x + pad + 2f, y + rowH * 0.5f - 4.5f, 9f, 9f);
                if (row.arrow) HudKit.Label(new Rect(swatch.x - 4f, y, 17f, rowH), "▲", 13, row.color, TextAnchor.MiddleCenter);
                else
                {
                    HudKit.Fill(new Rect(swatch.x - 1f, swatch.y - 1f, swatch.width + 2f, swatch.height + 2f), new Color(0f, 0f, 0f, 0.8f));
                    HudKit.Fill(swatch, row.color);
                }
                HudKit.Label(new Rect(box.x + pad + 20f, y, box.width - pad - 24f, rowH), row.label, 12, HudKit.Text, TextAnchor.MiddleLeft);
                y += rowH;
            }
        }

        /// <summary>World point to a point on the full map (north up; texture row 0 is the south edge).</summary>
        private Vector2 ToFull(Vector3 world)
        {
            float pxPerMeter = CellPixels / _cellSize;
            float u = world.x * pxPerMeter - _minX * CellPixels, v = world.z * pxPerMeter - _minZ * CellPixels;
            return new Vector2(_fullRect.x + u / _pxW * _fullRect.width, _fullRect.yMax - v / _pxH * _fullRect.height);
        }

        private void FullMarker(Vector3 world, Color color, float size)
        {
            Vector2 p = ToFull(world);
            if (!_fullRect.Contains(p)) return;
            var r = new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size);
            HudKit.Fill(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), new Color(0f, 0f, 0f, 0.8f));
            HudKit.Fill(r, color);
        }

        /// <summary>True when <paramref name="world"/> is in an open room (or outside every room), so its marker may show.</summary>
        private static bool Revealed(MapSession s, Vector3 world)
        {
            int room = s.Map.RoomAt(world);
            return room < 0 || s.IsRoomOpen(room);
        }

        /// <summary>A dot for a world point; <paramref name="edge"/> pins it to the frame edge when it is out of range.</summary>
        private static void Marker(Vector2 centre, Vector3 pos, Vector3 fwd, Vector3 right, Vector3 world, Color color, float size, bool edge = false)
        {
            Vector3 d = world - pos;
            float sx = Vector3.Dot(d, right) / RadiusMeters, sy = Vector3.Dot(d, fwd) / RadiusMeters;
            float m = Mathf.Max(Mathf.Abs(sx), Mathf.Abs(sy));
            if (m > 1f)
            {
                if (!edge) return;
                sx /= m;
                sy /= m;
                size *= 0.75f;
            }
            float half = Size * 0.5f - size * 0.5f - 1f;
            var r = new Rect(centre.x + sx * half - size * 0.5f, centre.y - sy * half - size * 0.5f, size, size);
            HudKit.Fill(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), new Color(0f, 0f, 0f, 0.8f));
            HudKit.Fill(r, color);
        }
    }
}
