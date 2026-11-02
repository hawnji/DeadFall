using System.Collections.Generic;
using Deadfall.Core;
using Deadfall.Rules.Progression;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>Floating facility names over the FOB consoles (greybox signage), greyed while a facility is locked.</summary>
    public sealed class FobLabels : MonoBehaviour
    {
        private static readonly List<FobTerminal> Terminals = new List<FobTerminal>();
        private GUIStyle _style;

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
            if (_style == null) _style = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            PlayerProfile p = ProfileService.Profile;
            int level = p != null ? p.OperatorLevel : 1;
            foreach (FobTerminal t in Terminals)
            {
                if (t == null) continue;
                Vector3 world = t.transform.position + Vector3.up * 2.6f;
                Vector3 to = world - cam.transform.position;
                if (to.sqrMagnitude > 30f * 30f || Vector3.Dot(to, cam.transform.forward) <= 0f) continue;
                Vector3 s = cam.WorldToScreenPoint(world);
                bool open = Facilities.IsOpen(t.Facility, level);
                string text = open ? Facilities.Name(t.Facility) : $"{Facilities.Name(t.Facility)}\n(operator level {Facilities.UnlockLevel(t.Facility)})";
                var r = new Rect(s.x - 120f, Screen.height - s.y - 20f, 240f, 40f);
                GUI.color = new Color(0f, 0f, 0f, 0.7f);
                GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, _style);
                GUI.color = open ? FobBuilder.ColorOf(t.Facility) : new Color(0.6f, 0.6f, 0.6f);
                GUI.Label(r, text, _style);
            }
            GUI.color = Color.white;
        }
    }
}
