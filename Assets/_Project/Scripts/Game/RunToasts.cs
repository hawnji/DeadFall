using System.Collections.Generic;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>Short messages stacked above the ammo counter ("Bought Wasp SMG", "Not enough credits", weapon level ups).</summary>
    public sealed class RunToasts : MonoBehaviour
    {
        private struct Toast { public string Text; public float Until; public Color Color; }
        private static readonly List<Toast> Items = new List<Toast>();
        private GUIStyle _style;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Items.Clear();

        public static void Show(string text, Color? color = null, float seconds = 3f)
        {
            Items.Add(new Toast { Text = text, Until = Time.unscaledTime + seconds, Color = color ?? Color.white });
            if (Items.Count > 5) Items.RemoveAt(0);
        }

        private void OnGUI()
        {
            if (Items.Count == 0) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleRight };
            float now = Time.unscaledTime;
            Items.RemoveAll(t => t.Until < now);
            float y = Screen.height - 140f;
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                Toast t = Items[i];
                Color c = t.Color;
                c.a = Mathf.Clamp01((t.Until - now) / 0.5f);
                GUI.color = c;
                GUI.Label(new Rect(Screen.width - 524f, y, 500f, 22f), t.Text, _style);
                y -= 22f;
            }
            GUI.color = Color.white;
        }
    }
}
