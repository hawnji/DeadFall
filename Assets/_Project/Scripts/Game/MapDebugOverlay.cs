using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Enemies;
using Deadfall.Rules.World;
using Deadfall.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deadfall.Game
{
    /// <summary>
    /// M: map debug overlay (GAME_DESIGN §10.1). A minimap of the generated layout with rooms in sector colours
    /// (faded while locked), the room graph, doors, enemy spawn points, loot, players and enemies, plus the seed,
    /// attempt count, build and bake times and any validation errors.
    /// </summary>
    public sealed class MapDebugOverlay : MonoBehaviour
    {
        private InputAction _toggle;
        private bool _visible;
        private GUIStyle _small;
        private BuiltMap _validated;
        private List<string> _errors = new List<string>();

        private void OnEnable()
        {
            _toggle = new InputAction("MapOverlay", InputActionType.Button, "<Keyboard>/m");
            _toggle.Enable();
        }

        private void OnDisable()
        {
            _toggle?.Disable();
            _toggle?.Dispose();
        }

        private void Update()
        {
            if (_toggle != null && _toggle.WasPressedThisFrame()) _visible = !_visible;
        }

        private void OnGUI()
        {
            MapSession s = MapSession.Current;
            if (!_visible || s == null || !s.HasMap) return;
            if (_small == null) _small = new GUIStyle(GUI.skin.label) { fontSize = 11 };
            BuiltMap map = s.Map;
            if (_validated != map) { _validated = map; _errors = LayoutValidator.Validate(map.Layout); }

            const float size = 340f;
            var area = new Rect(Screen.width - size - 16f, 130f, size, size);
            Fill(new Rect(area.x - 6f, area.y - 6f, size + 12f, size + 96f), new Color(0f, 0f, 0f, 0.7f));
            Bounds b = map.Bounds;
            float scale = size / Mathf.Max(b.size.x, b.size.z);
            Vector2 ToScreen(Vector3 p) => new Vector2(area.x + (p.x - b.min.x) * scale, area.y + size - (p.z - b.min.z) * scale);

            Layout layout = map.Layout;
            for (int i = 0; i < layout.Rooms.Count; i++)
            {
                Bounds rb = map.RoomBounds[i];
                Vector2 a = ToScreen(new Vector3(rb.min.x, 0f, rb.max.z)), c = ToScreen(new Vector3(rb.max.x, 0f, rb.min.z));
                Color col = MapBuilder.SectorColor(layout.Rooms[i].Sector);
                col.a = layout.Rooms[i].Sector < s.OpenSectors ? 0.55f : 0.15f;
                Fill(new Rect(a.x + 1f, a.y + 1f, c.x - a.x - 2f, c.y - a.y - 2f), col);
                RoomRole role = layout.Rooms[i].Template.Role;
                if (role != RoomRole.Corridor)
                {
                    string tag = role == RoomRole.Spawn ? "S" : role == RoomRole.Objective ? "OBJ" : role == RoomRole.Boss ? "BOSS" : role == RoomRole.Secret ? "?" : i.ToString();
                    GUI.Label(new Rect(a.x + 2f, a.y + 1f, 60f, 16f), tag, _small);
                }
            }
            foreach (SectorDoor d in map.Doors)
                if (d != null) Dot(ToScreen(d.transform.position), d.IsOpen ? new Color(0.4f, 0.9f, 0.4f) : Color.red, 6f);
            foreach (SecretPanel p in map.SecretPanels)
                if (p != null) Dot(ToScreen(p.transform.position), Color.cyan, 5f);
            foreach (MapSpawnPoint p in map.EnemySpawns) Dot(ToScreen(p.Position), new Color(1f, 0.45f, 0.3f, p.Sector < s.OpenSectors ? 0.9f : 0.3f), 3f);
            foreach (MapLootPoint p in map.Loot) Dot(ToScreen(p.Position), p.Kind == LootKind.SupplyCache ? new Color(0.3f, 0.9f, 0.5f) : new Color(1f, 0.85f, 0.2f), 4f);
            if (EnemyManager.Current != null)
                foreach (Enemy e in EnemyManager.Current.Active)
                    if (e != null) Dot(ToScreen(e.transform.position), new Color(0.9f, 0.1f, 0.1f), 3f);
            foreach (PlayerIdentity p in PlayerIdentity.All)
                if (p != null) Dot(ToScreen(p.transform.position), p.IsLocal ? Color.green : Color.white, 7f);

            float y = area.y + size + 6f;
            GUI.Label(new Rect(area.x, y, size, 16f), $"Seed {layout.Seed}  ·  attempt {layout.Attempt}{(layout.UsedFallback ? "  ·  FALLBACK" : string.Empty)}  ·  hash {layout.Hash() & 0xFFFFFF:X6}", _small);
            GUI.Label(new Rect(area.x, y + 16f, size, 16f), $"{layout.Rooms.Count} rooms  ·  {layout.SectorCount} sectors ({s.OpenSectors} open)  ·  {map.EnemySpawns.Count} spawns  ·  {map.Loot.Count} loot", _small);
            GUI.Label(new Rect(area.x, y + 32f, size, 16f), $"Build {s.LastBuildMilliseconds:0} ms  ·  NavMesh {s.LastBakeMilliseconds:0} ms", _small);
            GUI.color = _errors.Count == 0 ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.5f, 0.4f);
            GUI.Label(new Rect(area.x, y + 48f, size, 32f), _errors.Count == 0 ? "Validation: OK" : $"Validation: {_errors.Count} error(s). {_errors[0]}", _small);
            GUI.color = Color.white;
        }

        private static void Fill(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        private static void Dot(Vector2 p, Color c, float s) => Fill(new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s), c);
    }
}
