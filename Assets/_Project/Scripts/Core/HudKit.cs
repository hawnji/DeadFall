using System.Collections.Generic;
using UnityEngine;

namespace Deadfall.Core
{
    /// <summary>
    /// Shared look for every IMGUI screen (T-60): resolution scaling (layouts are authored at 1080p and scaled with
    /// <see cref="GUI.matrix"/>), a dark glass palette with one amber accent, rounded panels, bars, key chips,
    /// prompts, buttons, windows and a <see cref="Skin"/> for GUILayout menus. OS fonts are used when present
    /// (Bahnschrift / Segoe UI on Windows), with Unity's built-in font as the fallback.
    /// Call <see cref="Begin"/> at the top of OnGUI and <see cref="End"/> at the bottom, then lay out in
    /// <see cref="W"/> x <see cref="H"/> instead of Screen.width/height.
    /// </summary>
    public static class HudKit
    {
        public static readonly Color Accent = new Color(1f, 0.67f, 0.24f);
        public static readonly Color AccentDim = new Color(0.62f, 0.4f, 0.13f);
        public static readonly Color Glass = new Color(0.05f, 0.06f, 0.075f, 0.8f);
        public static readonly Color GlassLight = new Color(0.13f, 0.145f, 0.17f, 0.92f);
        public static readonly Color Track = new Color(0f, 0f, 0f, 0.55f);
        public static readonly Color Text = new Color(0.94f, 0.95f, 0.96f);
        public static readonly Color TextDim = new Color(0.64f, 0.68f, 0.73f);
        public static readonly Color Good = new Color(0.44f, 0.9f, 0.56f);
        public static readonly Color Warn = new Color(1f, 0.76f, 0.3f);
        public static readonly Color Bad = new Color(1f, 0.36f, 0.32f);
        public static readonly Color Info = new Color(0.42f, 0.8f, 1f);

        /// <summary>Pixels per layout unit this frame.</summary>
        public static float Scale { get; private set; } = 1f;
        /// <summary>Layout width and height (Screen size divided by <see cref="Scale"/>).</summary>
        public static float W { get; private set; } = 1920f;
        public static float H { get; private set; } = 1080f;

        private static Matrix4x4 _saved;
        private static Texture2D _round, _roundSmall, _ring, _vignette;
        private static GUIStyle _roundStyle, _roundSmallStyle, _ringStyle;
        private static readonly Dictionary<int, GUIStyle> Styles = new Dictionary<int, GUIStyle>();
        private static Font _body, _head;
        private static bool _fontsTried;
        private static GUISkin _skin;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Styles.Clear();
            _skin = null;
            _roundStyle = _roundSmallStyle = _ringStyle = null;
        }

        // ------------------------------------------------------------------------------- frame

        /// <summary>Starts a scaled IMGUI frame (pair with <see cref="End"/>).</summary>
        public static void Begin()
        {
            _saved = GUI.matrix;
            Scale = Mathf.Clamp(Screen.height / 1080f, 0.72f, 4f);
            W = Screen.width / Scale;
            H = Screen.height / Scale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(Scale, Scale, 1f));
        }

        public static void End()
        {
            GUI.matrix = _saved;
            GUI.color = Color.white;
            GUI.enabled = true;
        }

        /// <summary>Screen pixel point (e.g. from WorldToScreenPoint, y up) to layout units (y down).</summary>
        public static Vector2 FromScreen(Vector3 screen) => new Vector2(screen.x / Scale, (Screen.height - screen.y) / Scale);

        // ------------------------------------------------------------------------------- text

        public static Font BodyFont { get { LoadFonts(); return _body; } }
        public static Font HeadFont { get { LoadFonts(); return _head; } }

        private static void LoadFonts()
        {
            if (_fontsTried) return;
            _fontsTried = true;
            try
            {
                string[] installed = Font.GetOSInstalledFontNames();
                _body = Pick(installed, "Segoe UI", "Roboto", "Helvetica Neue", "Noto Sans", "Arial");
                _head = Pick(installed, "Bahnschrift", "Segoe UI Semibold", "Roboto Condensed", "DIN Alternate", "Arial Narrow") ?? _body;
            }
            catch (System.Exception)
            {
                _body = _head = null; // platform without OS fonts: Unity's default font
            }
        }

        private static Font Pick(string[] installed, params string[] names)
        {
            foreach (string n in names)
                if (System.Array.IndexOf(installed, n) >= 0) return Font.CreateDynamicFontFromOSFont(n, 16);
            return null;
        }

        /// <summary>A cached label style (white text: colour it with <see cref="GUI.color"/> or <see cref="Label"/>).</summary>
        public static GUIStyle Style(int size, TextAnchor anchor = TextAnchor.MiddleLeft, bool head = false, bool bold = false, bool wrap = false)
        {
            int key = size | ((int)anchor << 8) | (head ? 1 << 13 : 0) | (bold ? 1 << 14 : 0) | (wrap ? 1 << 15 : 0);
            if (Styles.TryGetValue(key, out GUIStyle s) && s != null) return s;
            s = new GUIStyle(GUI.skin.label)
            {
                fontSize = size, alignment = anchor, wordWrap = wrap, richText = true, clipping = TextClipping.Overflow,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0),
            };
            Font f = head ? HeadFont : BodyFont;
            if (f != null) s.font = f;
            s.normal.textColor = Color.white;
            Styles[key] = s;
            return s;
        }

        /// <summary>Text with a soft drop shadow.</summary>
        public static void Label(Rect r, string text, int size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft, bool head = false,
            bool bold = false, bool wrap = false, bool shadow = true)
        {
            if (string.IsNullOrEmpty(text)) return;
            GUIStyle s = Style(size, anchor, head, bold, wrap);
            Color old = GUI.color;
            if (shadow)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.65f * color.a * old.a);
                GUI.Label(new Rect(r.x + 1f, r.y + 1.5f, r.width, r.height), text, s);
            }
            GUI.color = new Color(color.r, color.g, color.b, color.a * old.a);
            GUI.Label(r, text, s);
            GUI.color = old;
        }

        public static float TextWidth(string text, int size, bool head = false, bool bold = false) =>
            string.IsNullOrEmpty(text) ? 0f : Style(size, TextAnchor.MiddleLeft, head, bold).CalcSize(new GUIContent(text)).x;

        // ------------------------------------------------------------------------------- shapes

        public static void Fill(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = new Color(c.r, c.g, c.b, c.a * old.a);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        /// <summary>Rounded rectangle (radius 8, or 4 when <paramref name="small"/>).</summary>
        public static void Round(Rect r, Color c, bool small = false)
        {
            if (Event.current.type != EventType.Repaint) return;
            EnsureTextures();
            Color old = GUI.color;
            GUI.color = new Color(c.r, c.g, c.b, c.a * old.a);
            (small || r.height < 18f ? _roundSmallStyle : _roundStyle).Draw(r, false, false, false, false);
            GUI.color = old;
        }

        /// <summary>A thin rounded outline.</summary>
        public static void Outline(Rect r, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            EnsureTextures();
            Color old = GUI.color;
            GUI.color = new Color(c.r, c.g, c.b, c.a * old.a);
            _ringStyle.Draw(r, false, false, false, false);
            GUI.color = old;
        }

        /// <summary>Dark glass panel with a faint edge.</summary>
        public static void Panel(Rect r, float alpha = 1f)
        {
            Round(new Rect(r.x, r.y + 3f, r.width, r.height), new Color(0f, 0f, 0f, 0.25f * alpha));
            Round(r, new Color(Glass.r, Glass.g, Glass.b, Glass.a * alpha));
            Outline(r, new Color(1f, 1f, 1f, 0.07f * alpha));
        }

        /// <summary><see cref="Panel"/> with a coloured strip on the left.</summary>
        public static void AccentPanel(Rect r, Color accent, float alpha = 1f)
        {
            Panel(r, alpha);
            Round(new Rect(r.x, r.y + 6f, 3f, r.height - 12f), new Color(accent.r, accent.g, accent.b, accent.a * alpha), true);
        }

        /// <summary>A full-screen radial vignette in <paramref name="c"/> (damage, low health).</summary>
        public static void Vignette(Color c)
        {
            if (c.a <= 0.001f) return;
            EnsureTextures();
            Color old = GUI.color;
            GUI.color = new Color(c.r, c.g, c.b, c.a * old.a);
            GUI.DrawTexture(new Rect(0f, 0f, W, H), _vignette, ScaleMode.StretchToFill);
            GUI.color = old;
        }

        /// <summary>Darkens the whole screen behind a menu.</summary>
        public static void Backdrop(float alpha = 0.55f) => Fill(new Rect(0f, 0f, W, H), new Color(0.01f, 0.012f, 0.02f, alpha));

        // ------------------------------------------------------------------------------- widgets

        /// <summary>Rounded progress bar; <paramref name="segments"/> &gt; 1 draws dividers.</summary>
        public static void Bar(Rect r, float t, Color fill, int segments = 0, Color? back = null, float ghost = -1f)
        {
            t = Mathf.Clamp01(t);
            Round(r, back ?? Track, true);
            var inner = new Rect(r.x + 2f, r.y + 2f, r.width - 4f, r.height - 4f);
            if (ghost > t) Round(new Rect(inner.x, inner.y, Mathf.Max(inner.height, inner.width * Mathf.Clamp01(ghost)), inner.height), new Color(1f, 1f, 1f, 0.35f), true);
            if (t > 0f)
            {
                float fw = Mathf.Max(Mathf.Min(inner.height, inner.width), inner.width * t);
                Round(new Rect(inner.x, inner.y, fw, inner.height), fill, true);
                Fill(new Rect(inner.x + 3f, inner.y + 1f, Mathf.Max(0f, fw - 6f), Mathf.Max(1f, inner.height * 0.28f)), new Color(1f, 1f, 1f, 0.18f)); // sheen
            }
            if (segments > 1)
                for (int i = 1; i < segments; i++)
                    Fill(new Rect(inner.x + inner.width * i / segments - 1f, inner.y, 2f, inner.height), new Color(0f, 0f, 0f, 0.55f));
        }

        /// <summary>A keyboard key cap ("F", "Q", "Esc").</summary>
        public static float KeyChip(float x, float y, string key, float height = 24f, bool enabled = true)
        {
            float w = Mathf.Max(height, TextWidth(key, Mathf.RoundToInt(height * 0.55f), head: true) + 12f);
            var r = new Rect(x, y, w, height);
            Round(new Rect(r.x, r.y + 2f, r.width, r.height), new Color(0f, 0f, 0f, 0.5f), true);
            Round(r, enabled ? new Color(0.93f, 0.94f, 0.95f) : new Color(0.5f, 0.52f, 0.55f), true);
            Label(r, key, Mathf.RoundToInt(height * 0.55f), new Color(0.08f, 0.09f, 0.1f), TextAnchor.MiddleCenter, head: true, shadow: false);
            return w;
        }

        /// <summary>
        /// Centred "[F] Do the thing" pill. <paramref name="progress"/> in 0..1 draws a fill along the bottom (hold
        /// actions); negative hides it. Returns the pill rect.
        /// </summary>
        public static Rect Prompt(float centerX, float y, string key, string text, bool enabled = true, float progress = -1f)
        {
            const int size = 17;
            float tw = TextWidth(text, size, head: true);
            float kw = string.IsNullOrEmpty(key) ? 0f : Mathf.Max(26f, TextWidth(key, 14, head: true) + 12f);
            float w = tw + (kw > 0f ? kw + 12f : 0f) + 28f;
            var r = new Rect(centerX - w * 0.5f, y, w, 40f);
            Panel(r, enabled ? 1f : 0.7f);
            float x = r.x + 14f;
            if (kw > 0f) x += KeyChip(x, r.y + 7f, key, 26f, enabled) + 12f;
            Label(new Rect(x, r.y, tw + 4f, r.height), text, size, enabled ? Text : TextDim, TextAnchor.MiddleLeft, head: true);
            if (progress >= 0f)
                Round(new Rect(r.x + 8f, r.yMax - 5f, (r.width - 16f) * Mathf.Clamp01(progress), 3f), Accent, true);
            return r;
        }

        /// <summary>Flat rounded button with hover; returns true when clicked (and enabled).</summary>
        public static bool Button(Rect r, string text, bool on = false, bool enabled = true, int size = 15, Color? accent = null)
        {
            Color a = accent ?? Accent;
            bool hover = enabled && r.Contains(Event.current.mousePosition);
            Color bg = on ? new Color(a.r * 0.55f, a.g * 0.55f, a.b * 0.55f, 0.95f) : hover ? new Color(0.21f, 0.235f, 0.27f, 0.95f) : GlassLight;
            if (!enabled) bg.a *= 0.5f;
            Round(r, bg);
            if (on || hover) Outline(r, new Color(a.r, a.g, a.b, on ? 0.9f : 0.45f));
            Label(r, text, size, enabled ? (on ? Color.white : Text) : TextDim, TextAnchor.MiddleCenter, head: true);
            bool clicked = GUI.Button(r, GUIContent.none, GUIStyle.none);
            if (clicked) GameAudio.Play2D(enabled ? Deadfall.Rules.Common.SfxId.UiClick : Deadfall.Rules.Common.SfxId.UiDeny);
            return clicked && enabled;
        }

        /// <summary>
        /// A centred menu window: backdrop, panel, title bar with an accent rule and optional right-hand text.
        /// Returns the content rect inside the padding.
        /// </summary>
        public static Rect Window(float width, float height, string title, string right = null, Color? accent = null, bool backdrop = true)
        {
            if (backdrop) Backdrop();
            width = Mathf.Min(width, W - 32f);
            height = Mathf.Min(height, H - 32f);
            var r = new Rect((W - width) * 0.5f, (H - height) * 0.5f, width, height);
            Round(new Rect(r.x - 2f, r.y + 6f, r.width + 4f, r.height + 4f), new Color(0f, 0f, 0f, 0.35f));
            Round(r, new Color(0.065f, 0.075f, 0.09f, 0.97f));
            Outline(r, new Color(1f, 1f, 1f, 0.08f));
            Color a = accent ?? Accent;
            Label(new Rect(r.x + 22f, r.y + 10f, r.width - 44f, 36f), title, 24, Text, TextAnchor.MiddleLeft, head: true);
            if (!string.IsNullOrEmpty(right)) Label(new Rect(r.x + 22f, r.y + 10f, r.width - 44f, 36f), right, 15, TextDim, TextAnchor.MiddleRight, head: true);
            Fill(new Rect(r.x + 22f, r.y + 52f, 56f, 3f), a);
            Fill(new Rect(r.x + 82f, r.y + 53f, r.width - 104f, 1f), new Color(1f, 1f, 1f, 0.08f));
            return new Rect(r.x + 22f, r.y + 66f, r.width - 44f, r.height - 82f);
        }

        /// <summary>A draggable rounded slider (settings). Returns the new value; click or drag anywhere on the track.</summary>
        public static float Slider(Rect r, float value, float min, float max, Color? fill = null)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            Event e = Event.current;
            float Pick() => Mathf.Lerp(min, max, Mathf.InverseLerp(r.x + 6f, r.xMax - 6f, e.mousePosition.x));
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (r.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        value = Pick();
                        GUI.changed = true;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        value = Pick();
                        GUI.changed = true;
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
                case EventType.Repaint:
                    float t = Mathf.InverseLerp(min, max, value);
                    var track = new Rect(r.x, r.y + r.height * 0.5f - 4f, r.width, 8f);
                    Round(track, Track, true);
                    Round(new Rect(track.x, track.y, Mathf.Max(8f, track.width * t), track.height), fill ?? Accent, true);
                    float kx = Mathf.Lerp(r.x + 6f, r.xMax - 6f, t);
                    bool hot = GUIUtility.hotControl == id || r.Contains(e.mousePosition);
                    Round(new Rect(kx - 8f, r.y + r.height * 0.5f - 8f, 16f, 16f), hot ? Color.white : Text, true);
                    break;
            }
            return Mathf.Clamp(value, Mathf.Min(min, max), Mathf.Max(min, max));
        }

        /// <summary>Small caps section heading with a rule.</summary>
        public static void Section(Rect r, string text, Color? color = null)
        {
            Label(new Rect(r.x, r.y, r.width, r.height), text.ToUpperInvariant(), 13, color ?? Accent, TextAnchor.MiddleLeft, head: true);
            float tw = TextWidth(text.ToUpperInvariant(), 13, head: true);
            Fill(new Rect(r.x + tw + 10f, r.y + r.height * 0.5f, Mathf.Max(0f, r.width - tw - 10f), 1f), new Color(1f, 1f, 1f, 0.08f));
        }

        // ------------------------------------------------------------------------------- GUILayout skin

        /// <summary>
        /// A GUISkin for GUILayout menus in the same style (labels, buttons, boxes, text fields, scroll bars).
        /// Must be read inside OnGUI. Set it with <c>GUI.skin = HudKit.Skin</c> after <see cref="Begin"/>.
        /// </summary>
        public static GUISkin Skin
        {
            get
            {
                if (_skin != null) return _skin;
                EnsureTextures();
                GUISkin baseSkin = GUI.skin;
                _skin = Object.Instantiate(baseSkin);
                _skin.hideFlags = HideFlags.HideAndDontSave;
                _skin.name = "HudKit";
                Font body = BodyFont, head = HeadFont;
                if (body != null) _skin.font = body;

                _skin.label = new GUIStyle(baseSkin.label) { fontSize = 14, wordWrap = true, richText = true };
                if (body != null) _skin.label.font = body;
                _skin.label.normal.textColor = Text;

                _skin.button = new GUIStyle(baseSkin.button)
                {
                    fontSize = 14, border = new RectOffset(6, 6, 6, 6), padding = new RectOffset(12, 12, 6, 6), margin = new RectOffset(3, 3, 3, 3),
                    alignment = TextAnchor.MiddleCenter, wordWrap = true,
                };
                if (head != null) _skin.button.font = head;
                SetState(_skin.button.normal, Tex(new Color(0.16f, 0.18f, 0.21f), 4), Text);
                SetState(_skin.button.hover, Tex(new Color(0.23f, 0.26f, 0.3f), 4), Color.white);
                SetState(_skin.button.active, Tex(new Color(0.86f, 0.56f, 0.18f), 4), new Color(0.08f, 0.08f, 0.08f));
                SetState(_skin.button.focused, Tex(new Color(0.16f, 0.18f, 0.21f), 4), Text);
                SetState(_skin.button.onNormal, Tex(new Color(0.55f, 0.36f, 0.12f), 4), Color.white);
                SetState(_skin.button.onHover, Tex(new Color(0.64f, 0.42f, 0.14f), 4), Color.white);
                SetState(_skin.button.onActive, Tex(new Color(0.86f, 0.56f, 0.18f), 4), Color.white);

                _skin.box = new GUIStyle(baseSkin.box) { border = new RectOffset(8, 8, 8, 8), padding = new RectOffset(12, 12, 9, 9), margin = new RectOffset(2, 2, 4, 4) };
                SetState(_skin.box.normal, Tex(new Color(0.11f, 0.125f, 0.15f, 0.95f), 6), Text);

                _skin.textField = new GUIStyle(baseSkin.textField) { fontSize = 14, border = new RectOffset(6, 6, 6, 6), padding = new RectOffset(8, 8, 5, 5) };
                if (body != null) _skin.textField.font = body;
                SetState(_skin.textField.normal, Tex(new Color(0.03f, 0.035f, 0.045f), 4), Text);
                SetState(_skin.textField.hover, Tex(new Color(0.05f, 0.055f, 0.07f), 4), Text);
                SetState(_skin.textField.focused, Tex(new Color(0.06f, 0.065f, 0.08f), 4), Color.white);

                _skin.scrollView = new GUIStyle(baseSkin.scrollView);
                _skin.scrollView.normal.background = null;
                _skin.verticalScrollbar = new GUIStyle(baseSkin.verticalScrollbar) { fixedWidth = 8f, border = new RectOffset(4, 4, 4, 4), margin = new RectOffset(6, 2, 2, 2) };
                _skin.verticalScrollbar.normal.background = Tex(new Color(0f, 0f, 0f, 0.35f), 3);
                _skin.verticalScrollbarThumb = new GUIStyle(baseSkin.verticalScrollbarThumb) { fixedWidth = 8f, border = new RectOffset(4, 4, 4, 4) };
                _skin.verticalScrollbarThumb.normal.background = Tex(new Color(0.42f, 0.45f, 0.5f), 3);
                _skin.verticalScrollbarUpButton = GUIStyle.none;
                _skin.verticalScrollbarDownButton = GUIStyle.none;
                _skin.horizontalScrollbar = new GUIStyle(baseSkin.horizontalScrollbar) { fixedHeight = 8f, border = new RectOffset(4, 4, 4, 4) };
                _skin.horizontalScrollbar.normal.background = Tex(new Color(0f, 0f, 0f, 0.35f), 3);
                _skin.horizontalScrollbarThumb = new GUIStyle(baseSkin.horizontalScrollbarThumb) { fixedHeight = 8f, border = new RectOffset(4, 4, 4, 4) };
                _skin.horizontalScrollbarThumb.normal.background = Tex(new Color(0.42f, 0.45f, 0.5f), 3);
                _skin.horizontalScrollbarLeftButton = GUIStyle.none;
                _skin.horizontalScrollbarRightButton = GUIStyle.none;
                return _skin;
            }
        }

        private static void SetState(GUIStyleState s, Texture2D bg, Color text)
        {
            s.background = bg;
            s.textColor = text;
        }

        // ------------------------------------------------------------------------------- textures

        private static void EnsureTextures()
        {
            if (_roundStyle != null && _round != null) return;
            _round = MakeRound(32, 8f, 0f);
            _roundSmall = MakeRound(16, 4f, 0f);
            _ring = MakeRound(32, 8f, 1.4f);
            _vignette = MakeVignette(128);
            _roundStyle = new GUIStyle { border = new RectOffset(9, 9, 9, 9) };
            _roundStyle.normal.background = _round;
            _roundSmallStyle = new GUIStyle { border = new RectOffset(5, 5, 5, 5) };
            _roundSmallStyle.normal.background = _roundSmall;
            _ringStyle = new GUIStyle { border = new RectOffset(9, 9, 9, 9) };
            _ringStyle.normal.background = _ring;
        }

        /// <summary>A rounded-rect texture pre-tinted with <paramref name="c"/> (for GUISkin states, which can't be tinted).</summary>
        private static Texture2D Tex(Color c, float radius)
        {
            int size = Mathf.CeilToInt(radius * 2f + 4f);
            Texture2D t = MakeRound(size, radius, 0f);
            Color32[] px = t.GetPixels32();
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color(c.r, c.g, c.b, c.a * px[i].a / 255f);
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        /// <summary>White rounded rectangle (or ring when <paramref name="ring"/> &gt; 0) with anti-aliased edges.</summary>
        private static Texture2D MakeRound(int size, float radius, float ring)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Signed distance to a rounded box filling the texture.
                    float px0 = Mathf.Abs(x + 0.5f - half) - (half - radius);
                    float py0 = Mathf.Abs(y + 0.5f - half) - (half - radius);
                    float outside = new Vector2(Mathf.Max(px0, 0f), Mathf.Max(py0, 0f)).magnitude + Mathf.Min(Mathf.Max(px0, py0), 0f) - radius;
                    float a = Mathf.Clamp01(0.5f - outside);
                    if (ring > 0f) a *= Mathf.Clamp01(outside + ring + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            t.SetPixels32(px);
            t.Apply(false, false); // stays readable: Tex() tints a copy of these pixels
            return t;
        }

        private static Texture2D MakeVignette(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, d));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }
    }
}
