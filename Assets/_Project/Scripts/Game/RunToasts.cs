using System.Collections.Generic;
using Deadfall.Core;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>Short messages stacked above the ammo counter ("Bought Wasp SMG", "Not enough credits", weapon level ups).</summary>
    public sealed class RunToasts : MonoBehaviour
    {
        private struct Toast { public string Text; public float Until; public Color Color; }
        private static readonly List<Toast> Items = new List<Toast>();

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
            HudKit.Begin();
            float now = Time.unscaledTime;
            Items.RemoveAll(t => t.Until < now);
            // Stacked pills above the weapon card (bottom right).
            float y = HudKit.H - 28f - 84f - 14f - 34f;
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                Toast t = Items[i];
                float a = Mathf.Clamp01((t.Until - now) / 0.5f);
                float tw = HudKit.TextWidth(t.Text, 14) + 34f;
                var r = new Rect(HudKit.W - 28f - tw, y, tw, 30f);
                HudKit.AccentPanel(r, t.Color, a);
                HudKit.Label(new Rect(r.x + 16f, r.y, r.width - 24f, r.height), t.Text, 14, new Color(t.Color.r, t.Color.g, t.Color.b, a), TextAnchor.MiddleLeft);
                y -= 36f;
            }
            HudKit.End();
        }
    }
}
