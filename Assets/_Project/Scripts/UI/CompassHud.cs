using Deadfall.Core;
using UnityEngine;

namespace Deadfall.UI
{
    /// <summary>
    /// Heading strip at the top centre of the screen: ticks every 5°, bearings every 15°, N/E/S/W letters, and a
    /// marker on the direction you face. Reads the main camera's yaw, so it works in the FOB and in a run.
    /// </summary>
    public sealed class CompassHud : MonoBehaviour
    {
        private const float Width = 600f, Top = 14f, PxPerDeg = 5f;

        private void OnGUI()
        {
            Camera cam = Camera.main;
            if (cam == null || !cam.isActiveAndEnabled) return;
            HudKit.Begin();
            float yaw = Mathf.Repeat(cam.transform.eulerAngles.y, 360f);
            float cx = HudKit.W * 0.5f, half = Width * 0.5f;

            // Faint backing strip so the ticks read on bright skies.
            HudKit.Round(new Rect(cx - half - 12f, Top - 4f, Width + 24f, 34f), new Color(0f, 0f, 0f, 0.28f), true);

            float span = half / PxPerDeg;
            int first = Mathf.CeilToInt((yaw - span) / 5f) * 5;
            for (int d = first; d <= yaw + span; d += 5)
            {
                float x = cx + (d - yaw) * PxPerDeg;
                float edge = 1f - Mathf.Abs(x - cx) / half;
                float a = Mathf.Clamp01(edge * 3f);
                int bearing = ((d % 360) + 360) % 360;
                bool major = bearing % 15 == 0;
                HudKit.Fill(new Rect(x - 1f, Top, 2f, major ? 10f : 6f), new Color(1f, 1f, 1f, (major ? 0.85f : 0.45f) * a));
                if (!major) continue;
                string label = Cardinal(bearing);
                bool cardinal = label != null;
                if (!cardinal) label = bearing.ToString();
                Color c = bearing == 0 ? HudKit.Accent : Color.white;
                HudKit.Label(new Rect(x - 30f, Top + 9f, 60f, 20f), label, cardinal ? 17 : 12,
                    new Color(c.r, c.g, c.b, (cardinal ? 1f : 0.7f) * a), TextAnchor.MiddleCenter, head: true);
            }

            // Facing marker: a small accent notch under the strip and the exact bearing.
            HudKit.Fill(new Rect(cx - 1f, Top - 4f, 2f, 16f), HudKit.Accent);
            HudKit.Label(new Rect(cx - 30f, Top + 29f, 60f, 18f), Mathf.RoundToInt(yaw) % 360 + "°", 12, HudKit.Accent, TextAnchor.MiddleCenter, head: true);
            HudKit.End();
        }

        private static string Cardinal(int bearing)
        {
            switch (bearing)
            {
                case 0: return "N";
                case 45: return "NE";
                case 90: return "E";
                case 135: return "SE";
                case 180: return "S";
                case 225: return "SW";
                case 270: return "W";
                case 315: return "NW";
                default: return null;
            }
        }
    }
}
