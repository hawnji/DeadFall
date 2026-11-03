using Deadfall.Core;
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
            HudKit.Begin();
            bool running = WaveManager.Current != null && WaveManager.Current.IsRunning;
            // Scrap pill above the class card (bottom left stack: health 66, class 86, gaps 10).
            float h = HudKit.H;
            string scrap = running ? d.Scrap.ToString() : "free";
            var pill = new Rect(28f, h - 28f - 66f - 10f - 86f - 10f - 32f, 200f, 32f);
            HudKit.Panel(pill, 0.9f);
            HudKit.Label(new Rect(pill.x + 14f, pill.y, 80f, pill.height), "SCRAP", 12, HudKit.TextDim, TextAnchor.MiddleLeft, head: true);
            HudKit.Label(new Rect(pill.x + 66f, pill.y, 60f, pill.height), scrap, 16, HudKit.Warn, TextAnchor.MiddleLeft, head: true);
            float kx = pill.xMax - 74f;
            HudKit.KeyChip(kx, pill.y + 5f, "B", 22f);
            HudKit.Label(new Rect(kx + 28f, pill.y, 50f, pill.height), "Build", 13, HudKit.Text, TextAnchor.MiddleLeft, head: true);

            if (_tool == null || !_tool.Active) { HudKit.End(); return; }
            bool engineer = d.LocalIsEngineer;
            const float bw = 520f;
            int n = StructureCatalog.All.Length;
            float bh = 52f + n * 34f + 40f;
            var box = new Rect(HudKit.W * 0.5f - bw * 0.5f, h - 120f - bh, bw, bh);
            HudKit.Panel(box);
            HudKit.Section(new Rect(box.x + 16f, box.y + 10f, bw - 32f, 22f), "Build");
            HudKit.Label(new Rect(box.x + 16f, box.y + 10f, bw - 32f, 22f), "1-4 / wheel pick  ·  R turn  ·  click place  ·  RMB / B exit", 12, HudKit.TextDim, TextAnchor.MiddleRight);
            for (int i = 0; i < n; i++)
            {
                StructureDef def = StructureCatalog.All[i];
                bool can = !def.EngineerOnly || engineer;
                int cost = ScrapRules.BuildCost(def, engineer);
                bool selected = _tool.Selected == def.Kind;
                var row = new Rect(box.x + 12f, box.y + 42f + i * 34f, bw - 24f, 30f);
                if (selected)
                {
                    HudKit.Round(row, new Color(HudKit.Accent.r, HudKit.Accent.g, HudKit.Accent.b, 0.18f), true);
                    HudKit.Outline(row, new Color(HudKit.Accent.r, HudKit.Accent.g, HudKit.Accent.b, 0.6f));
                }
                HudKit.KeyChip(row.x + 6f, row.y + 4f, (i + 1).ToString(), 22f, can);
                HudKit.Label(new Rect(row.x + 38f, row.y, 300f, row.height), def.Name + (can ? string.Empty : "  (Engineer only)"), 14, can ? Color.white : HudKit.TextDim, TextAnchor.MiddleLeft, head: true);
                HudKit.Label(new Rect(row.x, row.y, row.width - 10f, row.height), d.Free ? "free" : cost + " scrap", 13, can ? HudKit.Warn : HudKit.TextDim, TextAnchor.MiddleRight, head: true);
            }
            bool ok = _tool.LastError == PlacementError.Ok;
            HudKit.Label(new Rect(box.x + 16f, box.yMax - 34f, bw - 32f, 26f), PlacementRules.Explain(_tool.LastError), 13, ok ? HudKit.Good : HudKit.Bad, TextAnchor.MiddleLeft, head: true);
            HudKit.End();
        }
    }
}
