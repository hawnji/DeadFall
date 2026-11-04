using Deadfall.Core;
using Deadfall.Player;
using UnityEngine;

namespace Deadfall.UI
{
    /// <summary>
    /// The Settings window (M13), opened from the pause screen: volumes, mouse, field of view, frame rate, quality
    /// and window mode. Changes apply live and are saved when the window closes (Done, or Esc back to the game).
    /// </summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        private const string Owner = "settings";
        private static readonly int[] Caps = { 0, 30, 60, 90, 120, 144, 165, 240 };

        private ISettingsService _settings;
        private PlayerCursorLock _cursor;
        private bool _open;

        public bool IsOpen => _open;

        public void Bind(PlayerCursorLock cursor)
        {
            _cursor = cursor;
            ServiceRegistry.TryGet(out _settings);
        }

        public void Open()
        {
            if (_settings == null && !ServiceRegistry.TryGet(out _settings)) return;
            if (!UiModal.TryOpen(Owner)) return;
            _open = true;
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            UiModal.Close(Owner);
            _settings?.Save();
        }

        private void Update()
        {
            // Esc relocks the cursor and resumes; take the window down with it.
            if (_open && _cursor != null && _cursor.IsLocked) Close();
        }

        private void OnDisable() => Close();

        private void OnGUI()
        {
            if (!_open || _settings == null) return;
            GameSettings s = _settings.Current;
            HudKit.Begin();
            GUI.depth = -20;
            Rect c = HudKit.Window(760f, 680f, "SETTINGS", BuildInfo.Label);
            bool changed = false;
            float colW = (c.width - 40f) * 0.5f;
            float lx = c.x, rx = c.x + colW + 40f;
            float y = c.y + 6f;

            HudKit.Section(new Rect(lx, y, colW, 22f), "Audio");
            y += 34f;
            changed |= Volume(lx, ref y, colW, "Master", ref s.MasterVolume);
            changed |= Volume(lx, ref y, colW, "Effects", ref s.EffectsVolume);
            changed |= Volume(lx, ref y, colW, "Music", ref s.AmbienceVolume);
            changed |= Volume(lx, ref y, colW, "Interface", ref s.InterfaceVolume);

            y += 12f;
            HudKit.Section(new Rect(lx, y, colW, 22f), "Controls");
            y += 34f;
            changed |= Number(lx, ref y, colW, "Mouse sensitivity", ref s.MouseSensitivity, 0.1f, 5f, s.MouseSensitivity.ToString("0.00"));
            changed |= Number(lx, ref y, colW, "Aim sensitivity", ref s.AimSensitivityMultiplier, 0.2f, 1.5f, $"{Mathf.RoundToInt(s.AimSensitivityMultiplier * 100f)}%");
            changed |= Toggle(lx, ref y, colW, "Invert look", ref s.InvertY);

            float y2 = c.y + 6f;
            HudKit.Section(new Rect(rx, y2, colW, 22f), "Video");
            y2 += 34f;
            changed |= Number(rx, ref y2, colW, "Field of view", ref s.FieldOfView, 70f, 110f, $"{Mathf.RoundToInt(s.FieldOfView)}°");
            s.FieldOfView = Mathf.Round(s.FieldOfView);
            changed |= Toggle(rx, ref y2, colW, "VSync", ref s.VSync);
            changed |= Cycle(rx, ref y2, colW, "Frame rate cap", s.FrameRateCap > 0 ? $"{s.FrameRateCap} FPS" : "Unlimited", !s.VSync, () =>
            {
                int i = System.Array.IndexOf(Caps, s.FrameRateCap);
                s.FrameRateCap = Caps[(i + 1) % Caps.Length];
            });
            string[] levels = QualitySettings.names;
            int q = s.QualityLevel >= 0 && s.QualityLevel < levels.Length ? s.QualityLevel : QualitySettings.GetQualityLevel();
            changed |= Cycle(rx, ref y2, colW, "Quality", levels.Length > 0 ? levels[Mathf.Clamp(q, 0, levels.Length - 1)] : "Default", levels.Length > 1, () =>
            {
                s.QualityLevel = (q + 1) % levels.Length;
            });
            changed |= Toggle(rx, ref y2, colW, "Windowed", ref s.Windowed);
            changed |= Toggle(rx, ref y2, colW, "Show FPS", ref s.ShowFps);
            changed |= Toggle(lx, ref y, colW, "Damage numbers", ref s.DamageNumbers);
            HudKit.Label(new Rect(rx, y2, colW, 40f), "Window mode applies in builds only. The frame rate cap is used when VSync is off.", 12, HudKit.TextDim, TextAnchor.UpperLeft, wrap: true);

            float by = c.yMax - 44f;
            if (HudKit.Button(new Rect(c.x, by, 200f, 40f), "Reset to defaults"))
            {
                var d = new GameSettings();
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(d), s);
                changed = true;
            }
            if (HudKit.Button(new Rect(c.xMax - 200f, by, 200f, 40f), "Done", on: true)) Close();

            if (changed) _settings.NotifyChanged();
            HudKit.End();
        }

        private static bool Volume(float x, ref float y, float w, string label, ref float value) =>
            Number(x, ref y, w, label, ref value, 0f, 1f, $"{Mathf.RoundToInt(value * 100f)}%");

        private static bool Number(float x, ref float y, float w, string label, ref float value, float min, float max, string shown)
        {
            HudKit.Label(new Rect(x, y, w * 0.42f, 30f), label, 14, HudKit.Text);
            HudKit.Label(new Rect(x + w - 60f, y, 60f, 30f), shown, 14, HudKit.TextDim, TextAnchor.MiddleRight, head: true);
            float before = value;
            value = HudKit.Slider(new Rect(x + w * 0.42f, y + 3f, w * 0.58f - 70f, 24f), value, min, max);
            y += 40f;
            return !Mathf.Approximately(before, value);
        }

        private static bool Toggle(float x, ref float y, float w, string label, ref bool value)
        {
            HudKit.Label(new Rect(x, y, w * 0.6f, 30f), label, 14, HudKit.Text);
            bool clicked = HudKit.Button(new Rect(x + w - 110f, y, 110f, 30f), value ? "On" : "Off", on: value, size: 13);
            if (clicked) value = !value;
            y += 40f;
            return clicked;
        }

        private static bool Cycle(float x, ref float y, float w, string label, string shown, bool enabled, System.Action next)
        {
            HudKit.Label(new Rect(x, y, w * 0.5f, 30f), label, 14, enabled ? HudKit.Text : HudKit.TextDim);
            bool clicked = HudKit.Button(new Rect(x + w - 170f, y, 170f, 30f), shown, enabled: enabled, size: 13);
            if (clicked) next();
            y += 40f;
            return clicked;
        }
    }
}
