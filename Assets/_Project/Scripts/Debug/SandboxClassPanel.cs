using Deadfall.Classes;
using Deadfall.Player;
using UnityEngine;

namespace Deadfall.DebugTools
{
    /// <summary>
    /// Class select for the sandbox (M4): five buttons on the pause screen. The choice is remembered between
    /// sessions. A real class select screen comes with the FOB (M10).
    /// </summary>
    public sealed class SandboxClassPanel : MonoBehaviour
    {
        private const string PrefKey = "deadfall.sandbox.class";
        private PlayerClass _class;
        private PlayerCursorLock _cursor;
        private GUIStyle _box, _title, _desc;

        public void Bind(GameObject player)
        {
            _class = player.GetComponent<PlayerClass>();
            if (_class == null) _class = player.AddComponent<PlayerClass>();
            _cursor = player.GetComponent<PlayerCursorLock>();
            string saved = PlayerPrefs.GetString(PrefKey, "class.gunner");
            _class.SetClass(PrototypeClasses.ById(saved) ?? PrototypeClasses.All[0]);
        }

        private void OnGUI()
        {
            Deadfall.Core.HudKit.Begin();
            GUI.skin = Deadfall.Core.HudKit.Skin;
            DrawGui();
            Deadfall.Core.HudKit.End();
        }

        private void DrawGui()
        {
            if (_class == null) return;
            bool paused = _cursor == null || !_cursor.IsLocked;
            if (!paused) return;
            if (Deadfall.Core.UiModal.Active) return;
            if (_box == null)
            {
                _box = new GUIStyle(GUI.skin.box);
                _title = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
                if (Deadfall.Core.HudKit.HeadFont != null) _title.font = Deadfall.Core.HudKit.HeadFont;
                _title.normal.textColor = Deadfall.Core.HudKit.Accent;
                _desc = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
                _desc.normal.textColor = Deadfall.Core.HudKit.TextDim;
            }
            GUI.depth = -10;
            const float w = 320f, h = 420f;
            var area = new Rect(24f, Deadfall.Core.HudKit.H * 0.5f - h * 0.5f, w, h);
            Deadfall.Core.HudKit.Panel(area);
            GUILayout.BeginArea(new Rect(area.x + 12f, area.y + 10f, w - 24f, h - 20f));
            GUILayout.Label("CLASS", _title);
            foreach (ClassDefinition c in PrototypeClasses.All)
            {
                bool current = _class.Class == c;
                GUI.color = current ? c.Color : Color.white;
                if (GUILayout.Button(current ? $"▶ {c.DisplayName}  ·  {c.Role}" : $"{c.DisplayName}  ·  {c.Role}", GUILayout.Height(28f)))
                {
                    _class.SetClass(c);
                    PlayerPrefs.SetString(PrefKey, c.ContentId);
                }
                GUI.color = Color.white;
            }
            ClassDefinition sel = _class.Class;
            if (sel != null && sel.Ability != null)
            {
                GUILayout.Space(6f);
                GUILayout.Label($"Q  {sel.Ability.DisplayName}: {sel.Ability.Description}", _desc);
                foreach (PassiveDefinition p in sel.Passives)
                    GUILayout.Label($"{p.DisplayName}: {p.Description}", _desc);
                GUILayout.Label($"Prefers: {string.Join(", ", sel.PreferredCategories)}", _desc);
            }
            GUILayout.EndArea();
        }
    }
}
