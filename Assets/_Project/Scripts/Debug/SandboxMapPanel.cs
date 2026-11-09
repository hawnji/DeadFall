using Deadfall.Game;
using Deadfall.Net;
using Deadfall.Player;
using UnityEngine;

namespace Deadfall.DebugTools
{
    /// <summary>
    /// Pause-screen map controls (M5): seed field, Generate, Random, Back to sandbox, and a debug button that opens
    /// the next sector. Clients see what the host chose but can't change it. The last seed is remembered.
    /// </summary>
    public sealed class SandboxMapPanel : MonoBehaviour
    {
        private const string PrefKey = "deadfall.sandbox.seed";
        private string _seedText;
        private PlayerCursorLock _cursor;
        private GUIStyle _title, _small;

        private void Start() => _seedText = PlayerPrefs.GetString(PrefKey, "1");

        private void OnGUI()
        {
            Deadfall.Core.HudKit.Begin();
            GUI.skin = Deadfall.Core.HudKit.Skin;
            DrawGui();
            Deadfall.Core.HudKit.End();
        }

        private void DrawGui()
        {
            SandboxBootstrap boot = SandboxBootstrap.Current;
            if (boot == null) return;
            if (_cursor == null && PlayerIdentityLocal() != null) _cursor = PlayerIdentityLocal().GetComponent<PlayerCursorLock>();
            if (_cursor != null && _cursor.IsLocked) return;
            if (Deadfall.Core.UiModal.Active) return;
            if (_title == null)
            {
                _title = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
                if (Deadfall.Core.HudKit.HeadFont != null) _title.font = Deadfall.Core.HudKit.HeadFont;
                _title.normal.textColor = Deadfall.Core.HudKit.Accent;
                _small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
                _small.normal.textColor = Deadfall.Core.HudKit.TextDim;
            }
            GUI.depth = -10;
            const float w = 300f, h = 330f;
            var area = new Rect(Deadfall.Core.HudKit.W - w - 24f, Deadfall.Core.HudKit.H * 0.5f - h * 0.5f, w, h);
            Deadfall.Core.HudKit.Panel(area);
            GUILayout.BeginArea(new Rect(area.x + 12f, area.y + 10f, w - 24f, h - 20f));
            GUILayout.Label("MAP", _title);

            MapSession map = boot.MapSession;
            SessionService session = SessionService.Current;
            bool client = session != null && session.Role == SessionRole.Client;
            GUILayout.Label(boot.InMap ? $"{map.Environment.DisplayName}, seed {map.Seed} ({map.Map.Layout.Rooms.Count} rooms, {map.SectorCount} sectors). M: map overlay." : "Movement and combat sandbox.", _small);

            if (client)
            {
                GUILayout.Label("The host picks the map.", _small);
                GUILayout.EndArea();
                return;
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("Seed", GUILayout.Width(40f));
            _seedText = GUILayout.TextField(_seedText ?? "1", 18);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            _harrow = GUILayout.Toggle(_harrow, " Harrow Line (second environment)");
            if (GUILayout.Button("Generate", GUILayout.Height(26f)) && ulong.TryParse(_seedText, out ulong seed)) Enter(boot, seed);
            if (GUILayout.Button("Random", GUILayout.Height(26f)))
            {
                ulong r = (ulong)Random.Range(1, int.MaxValue);
                _seedText = r.ToString();
                Enter(boot, r);
            }
            GUILayout.EndHorizontal();
            if (boot.InMap)
            {
                if (map.OpenSectors < map.SectorCount && GUILayout.Button("Open next sector (debug)", GUILayout.Height(24f))) map.OpenNextSector();
                ObjectiveDirector objectives = ObjectiveDirector.Current;
                if (objectives != null && GUILayout.Button("Start this map's Operation", GUILayout.Height(24f))) objectives.StartOperation();
                if (GUILayout.Button("Back to sandbox", GUILayout.Height(24f))) boot.EnterSandbox();
            }
            GUILayout.Label("Deploy from Command Center at the FOB, or start this map's Operation here.", _small);
            GUILayout.EndArea();
        }

        private bool _harrow;

        private void Enter(SandboxBootstrap boot, ulong seed)
        {
            PlayerPrefs.SetString(PrefKey, seed.ToString());
            boot.EnterMap(_harrow ? Deadfall.Rules.World.EnvironmentCatalog.Encode(seed, 1) : seed);
        }

        private static GameObject PlayerIdentityLocal() => Deadfall.Combat.PlayerIdentity.Local != null ? Deadfall.Combat.PlayerIdentity.Local.gameObject : null;
    }
}
