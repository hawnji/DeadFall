using System.Collections.Generic;
using Deadfall.Core;
using Deadfall.Rules.Progression;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>Floating facility name plates over the FOB consoles (<see cref="HudKit"/> style), greyed while a facility is locked.</summary>
    public sealed class FobLabels : MonoBehaviour
    {
        private static readonly List<FobTerminal> Terminals = new List<FobTerminal>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Terminals.Clear();

        public static void Register(IEnumerable<FobTerminal> terminals)
        {
            Terminals.RemoveAll(t => t == null);
            Terminals.AddRange(terminals);
        }

        private void OnGUI()
        {
            if (UiModal.Active || Terminals.Count == 0) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            HudKit.Begin();
            PlayerProfile p = ProfileService.Profile;
            int level = p != null ? p.OperatorLevel : 1;
            foreach (FobTerminal t in Terminals)
            {
                if (t == null) continue;
                Vector3 world = t.transform.position + Vector3.up * 2.6f;
                Vector3 to = world - cam.transform.position;
                float dist = to.magnitude;
                if (dist > StationLabels.Range || Vector3.Dot(to, cam.transform.forward) <= 0f) continue;
                Vector2 s = HudKit.FromScreen(cam.WorldToScreenPoint(world));
                bool open = Facilities.IsOpen(t.Facility, level);
                Color c = open ? FobBuilder.ColorOf(t.Facility) : new Color(0.6f, 0.6f, 0.6f);
                float fade = Mathf.Clamp01((StationLabels.Range - dist) / 10f);
                string name = Facilities.Name(t.Facility).ToUpperInvariant();
                string sub = open ? (dist < 6f ? "Press F at the console" : null) : $"Operator level {Facilities.UnlockLevel(t.Facility)}";
                float w = Mathf.Max(HudKit.TextWidth(name, 16, head: true), sub != null ? HudKit.TextWidth(sub, 12) : 0f) + 36f;
                var r = new Rect(s.x - w * 0.5f, s.y - 22f, w, sub != null ? 48f : 32f);
                HudKit.Panel(r, 0.85f * fade);
                HudKit.Fill(new Rect(r.x + 12f, r.yMax - 3f, r.width - 24f, 2f), new Color(c.r, c.g, c.b, fade));
                HudKit.Label(new Rect(r.x, r.y + 4f, r.width, 24f), name, 16, new Color(c.r, c.g, c.b, fade), TextAnchor.MiddleCenter, head: true);
                if (sub != null) HudKit.Label(new Rect(r.x, r.y + 25f, r.width, 18f), sub, 12, new Color(0.8f, 0.82f, 0.85f, fade), TextAnchor.MiddleCenter);
            }
            HudKit.End();
        }
    }
}
