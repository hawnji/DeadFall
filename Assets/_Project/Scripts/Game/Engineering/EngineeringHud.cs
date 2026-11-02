using Deadfall.Encounters;
using Deadfall.Rules.Engineering;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>Scrap counter, the build menu while in build mode, and the reason a placement is refused.</summary>
    public sealed class EngineeringHud : MonoBehaviour
    {
        private BuildTool _tool;
        private EngineeringDirector _bound;
        private GUIStyle _line, _head, _sel;

        public void Bind(BuildTool tool) => _tool = tool;

        private void Update()
        {
            EngineeringDirector d = EngineeringDirector.Current;
            if (d == _bound) return;
            if (_bound != null) _bound.LocalMessage -= OnMessage;
            _bound = d;
            if (_bound != null) _bound.LocalMessage += OnMessage;
        }

        private void OnDestroy()
        {
            if (_bound != null) _bound.LocalMessage -= OnMessage;
        }

        private static void OnMessage(string text) => RunToasts.Show(text, new Color(1f, 0.8f, 0.5f), 2f);

        private void OnGUI()
        {
            EngineeringDirector d = _bound;
            if (d == null) return;
            if (_line == null)
            {
                _line = new GUIStyle(GUI.skin.label) { fontSize = 13 };
                _head = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
                _sel = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
            }
            bool running = WaveManager.Current != null && WaveManager.Current.IsRunning;
            float x = 24f, y = Screen.height - 130f;
            GUI.Label(new Rect(x, y, 400f, 20f), running ? $"SCRAP  {d.Scrap}   ·   B build" : "SCRAP  free outside runs   ·   B build", _head);

            if (_tool == null || !_tool.Active) return;
            bool engineer = d.LocalIsEngineer;
            float bx = Screen.width * 0.5f - 230f, by = Screen.height - 250f;
            GUI.Box(new Rect(bx, by, 460f, 112f), GUIContent.none);
            GUI.Label(new Rect(bx + 10f, by + 4f, 440f, 20f), "BUILD  ·  1-4 or wheel pick  ·  R turn  ·  click place  ·  right click / B exit", _line);
            for (int i = 0; i < StructureCatalog.All.Length; i++)
            {
                StructureDef def = StructureCatalog.All[i];
                bool can = !def.EngineerOnly || engineer;
                int cost = ScrapRules.BuildCost(def, engineer);
                string text = $"{i + 1}  {def.Name}  {(d.Free ? "free" : cost + " Scrap")}" + (can ? "" : "  (Engineer only)");
                bool selected = _tool.Selected == def.Kind;
                GUI.color = !can ? new Color(0.6f, 0.6f, 0.6f) : selected ? new Color(0.5f, 1f, 0.6f) : Color.white;
                GUI.Label(new Rect(bx + 10f, by + 24f + i * 18f, 440f, 18f), (selected ? "> " : "   ") + text, selected ? _sel : _line);
            }
            GUI.color = _tool.LastError == PlacementError.Ok ? new Color(0.5f, 1f, 0.6f) : new Color(1f, 0.55f, 0.45f);
            GUI.Label(new Rect(bx + 10f, by + 24f + 4 * 18f + 2f, 440f, 18f), PlacementRules.Explain(_tool.LastError), _sel);
            GUI.color = Color.white;
        }
    }
}
