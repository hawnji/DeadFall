using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Player;
using UnityEngine;

namespace Deadfall.UI
{
    /// <summary>
    /// Player HUD in the shared <see cref="HudKit"/> style (T-60): health card (bottom left, hex badge and a segmented
    /// bar with a trailing ghost), stacked ammo readout (bottom right), outlined crosshair that shows the real spread, hit markers,
    /// damage vignette and direction marker, low-health pulse, interact prompt (with hold progress), pause overlay.
    /// It only reads state and listens to events, so a later UI Toolkit swap won't touch gameplay code.
    /// </summary>
    public sealed class PrototypeHud : MonoBehaviour
    {
        [SerializeField] private float _hitMarkerSeconds = 0.18f;
        [SerializeField] private float _damageFlashSeconds = 0.35f;

        private PlayerHealth _health;
        private PlayerWeapons _weapons;
        private PlayerLook _look;
        private PlayerCursorLock _cursor;
        private SettingsPanel _settingsPanel;
        private GameObject _player;

        private float _hitMarkerUntil;
        private bool _hitMarkerCrit;
        private bool _hitMarkerKill;
        private float _damageFlashUntil;
        private float _quitConfirmUntil, _hubConfirmUntil;

        /// <summary>Pause screen "Quit to HUB": ends the run and returns to the FOB (set by the bootstrap).</summary>
        public static System.Action QuitToHub;
        /// <summary>Why Quit to HUB is unavailable right now (already at the hub, co-op client), or null.</summary>
        public static System.Func<string> QuitToHubBlocked;
        private Vector3 _lastDamageDir;


        public void Bind(GameObject player)
        {
            _player = player;
            _health = player.GetComponent<PlayerHealth>();
            _weapons = player.GetComponent<PlayerWeapons>();
            _look = player.GetComponent<PlayerLook>();
            _cursor = player.GetComponent<PlayerCursorLock>();
            _settingsPanel = GetComponent<SettingsPanel>();
            if (_settingsPanel == null) _settingsPanel = gameObject.AddComponent<SettingsPanel>();
            _settingsPanel.Bind(_cursor);
        }

        private void OnEnable()
        {
            EventBus<HitConfirmed>.Subscribe(OnHit);
            EventBus<PlayerDamaged>.Subscribe(OnDamaged);
        }

        private void OnDisable()
        {
            EventBus<HitConfirmed>.Unsubscribe(OnHit);
            EventBus<PlayerDamaged>.Unsubscribe(OnDamaged);
        }

        private void OnHit(HitConfirmed e)
        {
            if (e.Attacker != _player) return;
            _hitMarkerUntil = Time.unscaledTime + _hitMarkerSeconds * (e.Killed ? 1.6f : 1f);
            _hitMarkerCrit = e.IsCritical;
            _hitMarkerKill = e.Killed;
        }

        private void OnDamaged(PlayerDamaged e)
        {
            if (e.Player != _player) return;
            _damageFlashUntil = Time.unscaledTime + _damageFlashSeconds;
            _lastDamageDir = e.Direction;
        }

        private PlayerInteractor _interactor;
        private float _ghostHealth = 1f;

        private void OnGUI()
        {
            if (_player == null) return;
            HudKit.Begin();
            float w = HudKit.W, h = HudKit.H;
            float now = Time.unscaledTime;
            if (_weapons != null && _weapons.ScopeView) DrawScope(w, h);

            // Damage: red vignette plus a marker towards the source. Low health pulses the vignette.
            float flash = now < _damageFlashUntil ? (_damageFlashUntil - now) / _damageFlashSeconds : 0f;
            float low = _health != null && _health.IsAlive ? Mathf.InverseLerp(0.35f, 0.1f, _health.Normalized) : 0f;
            float pulse = low * (0.55f + 0.45f * Mathf.Sin(now * 5f));
            HudKit.Vignette(new Color(0.75f, 0.02f, 0.02f, Mathf.Max(flash * 0.75f, pulse * 0.6f)));
            if (flash > 0f && _look != null && _lastDamageDir.sqrMagnitude > 0.01f)
            {
                Vector3 local = _player.transform.InverseTransformDirection(-_lastDamageDir);
                float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                Matrix4x4 m = GUI.matrix;
                var c = new Vector2(w * 0.5f, h * 0.5f);
                GUIUtility.RotateAroundPivot(angle, c * HudKit.Scale);
                HudKit.Round(new Rect(c.x - 46f, c.y - h * 0.2f, 92f, 7f), new Color(1f, 0.2f, 0.15f, flash * 0.9f), true);
                GUI.matrix = m;
            }

            // Interact prompt.
            if (_interactor == null) _interactor = _player.GetComponent<PlayerInteractor>();
            if (_interactor != null && !string.IsNullOrEmpty(_interactor.Prompt)) DrawInteract(w * 0.5f, h * 0.5f + 84f);

            // Health card (bottom left).
            if (_health != null)
            {
                float n = Mathf.Clamp01(_health.Normalized);
                _ghostHealth = n >= _ghostHealth ? n : Mathf.MoveTowards(_ghostHealth, n, Time.unscaledDeltaTime * 0.35f);
                Color hc = n > 0.5f ? HudKit.Good : n > 0.25f ? HudKit.Warn : HudKit.Bad;
                // Hex badge with the number, then a slim angular card with the caption and a segmented bar.
                var card = new Rect(28f, h - 28f - 66f, 340f, 66f);
                var panel = new Rect(card.x + 52f, card.y + 8f, card.width - 52f, 52f);
                HudKit.Panel(panel);
                var hex = new Rect(card.x, card.y, 62f, 66f);
                HudKit.Hex(hex, new Color(HudKit.Glass.r, HudKit.Glass.g, HudKit.Glass.b, 0.95f), hc);
                HudKit.Label(hex, Mathf.CeilToInt(_health.Current).ToString(), 22, Color.white, TextAnchor.MiddleCenter, head: true);
                HudKit.Label(new Rect(panel.x + 20f, panel.y + 3f, 200f, 22f), "HEALTH", 15, HudKit.Text, TextAnchor.MiddleLeft, head: true);
                HudKit.Label(new Rect(panel.x + 20f, panel.y + 3f, panel.width - 34f, 22f), Mathf.RoundToInt(n * 100f) + "%", 15, hc, TextAnchor.MiddleRight, head: true);
                HudKit.Bar(new Rect(panel.x + 20f, panel.y + 28f, panel.width - 34f, 12f), n, hc, 10, null, _ghostHealth);
                // Downed and eliminated screens are drawn by the squad HUD (M11).
            }

            // Weapon card (bottom right).
            if (_weapons != null && _weapons.CurrentDefinition != null)
            {
                WeaponDefinition def = _weapons.CurrentDefinition;
                // Stacked readout: slot and name on top, a big magazine count with the reserve beside it.
                var card = new Rect(w - 28f - 360f, h - 28f - 84f, 360f, 84f);
                HudKit.Panel(card, 0.85f);
                float kw = HudKit.KeyChip(card.x + 16f, card.y + 12f, (_weapons.CurrentIndex + 1).ToString(), 22f);
                HudKit.Label(new Rect(card.x + 24f + kw, card.y + 10f, card.width - 40f - kw, 26f), def.DisplayName.ToUpperInvariant(), 18, HudKit.Text, TextAnchor.MiddleLeft, head: true);
                string mods = ModsSuffix();
                if (mods.Length > 0) HudKit.Label(new Rect(card.x + 16f, card.y + 52f, card.width - 190f, 22f), mods, 12, HudKit.Accent, TextAnchor.MiddleLeft);
                if (def.Kind == WeaponKind.Firearm)
                {
                    int mag = _weapons.Magazine;
                    float magMax = Mathf.Max(1, _weapons.MagazineCapacity);
                    Color mc = mag == 0 ? HudKit.Bad : mag <= magMax * 0.25f ? HudKit.Warn : Color.white;
                    // Reserve column on the far right (caption over number), magazine big to its left.
                    var reserve = new Rect(card.xMax - 16f - 64f, card.y + 34f, 64f, 42f);
                    HudKit.Label(new Rect(reserve.x, reserve.y, reserve.width, 14f), "RESERVE", 11, HudKit.TextDim, TextAnchor.MiddleRight, head: true);
                    HudKit.Label(new Rect(reserve.x, reserve.y + 12f, reserve.width, 30f), _weapons.Reserve.ToString(), 24, HudKit.TextDim, TextAnchor.MiddleRight, head: true);
                    HudKit.Fill(new Rect(reserve.x - 10f, reserve.y + 4f, 2f, 34f), new Color(1f, 1f, 1f, 0.25f));
                    HudKit.Label(new Rect(card.x, card.y + 26f, reserve.x - 18f - card.x, 54f), mag.ToString(), 46, mc, TextAnchor.LowerRight, head: true);
                    HudKit.Bar(new Rect(card.x + 16f, card.yMax - 9f, card.width - 32f, 5f), mag / magMax, new Color(1f, 1f, 1f, 0.75f), 0, new Color(0f, 0f, 0f, 0.4f));
                }
                else
                {
                    HudKit.Label(new Rect(card.x, card.y + 34f, card.width - 18f, 40f), "MELEE", 28, Color.white, TextAnchor.LowerRight, head: true);
                }

                if (_weapons.IsReloading)
                {
                    var rb = new Rect(w * 0.5f - 70f, h * 0.5f + 34f, 140f, 8f);
                    HudKit.Bar(rb, _weapons.ReloadProgress, HudKit.Accent);
                    HudKit.Label(new Rect(0f, rb.yMax + 2f, w, 22f), "RELOADING", 13, HudKit.Text, TextAnchor.UpperCenter, head: true);
                }
                else if (def.Kind == WeaponKind.Firearm && _weapons.Magazine == 0)
                {
                    if (_weapons.Reserve > 0) HudKit.Prompt(w * 0.5f, h * 0.5f + 30f, "R", "Reload", true);
                    else HudKit.Label(new Rect(0f, h * 0.5f + 34f, w, 24f), "NO AMMO", 16, HudKit.Bad, TextAnchor.UpperCenter, head: true);
                }

                DrawCrosshair(w * 0.5f, h * 0.5f, def);
            }

            // Hit marker.
            if (now < _hitMarkerUntil)
            {
                Color mc = _hitMarkerKill ? HudKit.Bad : _hitMarkerCrit ? HudKit.Warn : Color.white;
                float s = _hitMarkerCrit || _hitMarkerKill ? 11f : 8f;
                DrawDiagonalMarker(w * 0.5f, h * 0.5f, s, mc);
            }

            if (_cursor != null && !_cursor.IsLocked && !UiModal.Active)
            {
                HudKit.Backdrop(0.5f);
                // The menu column sits in the middle of the screen: title, Resume, Settings, then the quit row.
                float mid = h * 0.5f;
                HudKit.Label(new Rect(0f, mid - 150f, w, 60f), "PAUSED", 48, Color.white, TextAnchor.MiddleCenter, head: true);
                HudKit.Label(new Rect(0f, mid - 92f, w, 20f), "Esc to resume", 13, HudKit.TextDim, TextAnchor.MiddleCenter, head: true);
                float top = mid - 56f;
                if (HudKit.Button(new Rect(w * 0.5f - 206f, top, 412f, 40f), "Resume", accent: HudKit.Good))
                    _cursor.SetLocked(true);
                if (_settingsPanel != null && HudKit.Button(new Rect(w * 0.5f - 206f, top + 52f, 412f, 40f), "Settings"))
                    _settingsPanel.Open();
                top += 52f;
                // Quit row: two clicks each (the first arms it for a few seconds, so a stray click doesn't end a run).
                var titleRect = new Rect(w * 0.5f - 206f, top + 52f, 200f, 40f);
                var hubRect = new Rect(w * 0.5f + 6f, top + 52f, 200f, 40f);
                string note = null;
                if (TitleScreen.Current != null)
                {
                    bool armed = now < _quitConfirmUntil;
                    if (HudKit.Button(titleRect, armed ? "Click again to quit" : "Quit to title", on: armed, accent: HudKit.Bad))
                    {
                        if (armed) { _quitConfirmUntil = 0f; TitleScreen.Current.ReturnToTitle(); }
                        else { _quitConfirmUntil = now + 3f; _hubConfirmUntil = 0f; }
                    }
                    if (armed) note = "Leaves co-op and ends the current run.";
                }
                if (QuitToHub != null)
                {
                    string blocked = QuitToHubBlocked != null ? QuitToHubBlocked() : null;
                    bool armed = blocked == null && now < _hubConfirmUntil;
                    if (HudKit.Button(hubRect, armed ? "Click again to leave" : "Quit to HUB", on: armed, enabled: blocked == null, accent: HudKit.Warn))
                    {
                        if (armed) { _hubConfirmUntil = 0f; QuitToHub(); }
                        else { _hubConfirmUntil = now + 3f; _quitConfirmUntil = 0f; }
                    }
                    if (armed) note = "Ends the current run and takes the squad back to FOB Halcyon.";
                    else if (blocked != null && hubRect.Contains(Event.current.mousePosition)) note = blocked;
                }
                if (note != null) HudKit.Label(new Rect(0f, top + 100f, w, 22f), note, 13, HudKit.TextDim, TextAnchor.MiddleCenter);
                HudKit.Label(new Rect(w - 320f, h - 30f, 300f, 20f), BuildInfo.Label, 12, HudKit.TextDim, TextAnchor.MiddleRight, head: true);
            }
            HudKit.End();
        }

        /// <summary>
        /// Interact prompt under the crosshair: the action in big caps, then "[F] Use" or "[F] Hold" with the hold
        /// progress as a thin bar under it.
        /// </summary>
        private void DrawInteract(float cx, float y)
        {
            bool can = _interactor.CanUse;
            string title = _interactor.Prompt.ToUpperInvariant();
            HudKit.Label(new Rect(cx - 400f, y, 800f, 28f), title, 22, can ? Color.white : HudKit.TextDim, TextAnchor.MiddleCenter, head: true);
            if (!can) return;
            string verb = _interactor.IsHold ? "Hold" : "Use";
            float vw = HudKit.TextWidth(verb, 16, head: true);
            float kw = Mathf.Max(24f, HudKit.TextWidth("F", 13, head: true) + 12f);
            float x = cx - (kw + 8f + vw) * 0.5f;
            HudKit.KeyChip(x, y + 32f, "F", 24f);
            HudKit.Label(new Rect(x + kw + 8f, y + 32f, vw + 4f, 24f), verb, 16, HudKit.Accent, TextAnchor.MiddleLeft, head: true);
            float p = _interactor.HoldProgress;
            if (p >= 0f && _interactor.IsHold)
            {
                var bar = new Rect(cx - 90f, y + 62f, 180f, 6f);
                HudKit.Bar(bar, p, HudKit.Accent);
            }
        }

        private string ModsSuffix()
        {
            Deadfall.Rules.Combat.WeaponMods m = _weapons.CurrentMods;
            return m != null ? m.Describe() ?? string.Empty : string.Empty;
        }

        private static Texture2D _scopeMask;

        /// <summary>A white disc with soft black outside it (the scope's eyepiece), made once.</summary>
        private static Texture2D ScopeMask()
        {
            if (_scopeMask != null) return _scopeMask;
            const int n = 256;
            _scopeMask = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "ScopeMask" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    // Clear inside, a soft dark edge, solid black outside.
                    float a = Mathf.Clamp01((r - 0.9f) / 0.08f);
                    px[y * n + x] = new Color32(0, 0, 0, (byte)(a * 255f));
                }
            _scopeMask.SetPixels32(px);
            _scopeMask.Apply(false);
            return _scopeMask;
        }

        /// <summary>The scope view while aimed in: black round the eyepiece, fine crosshairs meeting on the aim point.</summary>
        private void DrawScope(float w, float h)
        {
            float d = h * 0.92f;
            var circle = new Rect((w - d) * 0.5f, (h - d) * 0.5f, d, d);
            var black = new Color(0f, 0f, 0f, 1f);
            DrawRect(new Rect(0f, 0f, circle.x + 1f, h), black);
            DrawRect(new Rect(circle.xMax - 1f, 0f, w - circle.xMax + 1f, h), black);
            DrawRect(new Rect(circle.x, 0f, d, circle.y + 1f), black);
            DrawRect(new Rect(circle.x, circle.yMax - 1f, d, h - circle.yMax + 1f), black);
            GUI.DrawTexture(circle, ScopeMask(), ScaleMode.StretchToFill, true);
            float cx = w * 0.5f, cy = h * 0.5f;
            var line = new Color(0f, 0f, 0f, 0.9f);
            // Thick outer posts, thin centre lines, a small gap and a red dot exactly on the aim point.
            DrawRect(new Rect(circle.x, cy - 2f, d * 0.5f - 60f, 4f), line);
            DrawRect(new Rect(cx + 60f, cy - 2f, d * 0.5f - 60f, 4f), line);
            DrawRect(new Rect(cx - 2f, cy + 60f, 4f, d * 0.5f - 60f), line);
            DrawRect(new Rect(cx - 0.5f, circle.y, 1f, d * 0.5f - 6f), line);
            DrawRect(new Rect(cx - 60f, cy - 0.5f, 54f, 1f), line);
            DrawRect(new Rect(cx + 6f, cy - 0.5f, 54f, 1f), line);
            DrawRect(new Rect(cx - 0.5f, cy + 6f, 1f, 54f), line);
            for (int i = 1; i <= 3; i++) DrawRect(new Rect(cx - 5f, cy + 14f * i, 10f, 1f), line); // drop marks
            DrawRect(new Rect(cx - 1.5f, cy - 1.5f, 3f, 3f), new Color(1f, 0.15f, 0.1f, 0.95f));
        }

        private void DrawCrosshair(float cx, float cy, WeaponDefinition def)
        {
            if (_weapons.ScopeView) return; // the scope has its own reticle
            if (_weapons.AimBlend > 0.9f && def.Kind == WeaponKind.Firearm)
            {
                DrawRect(new Rect(cx - 2f, cy - 2f, 4, 4), new Color(0f, 0f, 0f, 0.5f));
                DrawRect(new Rect(cx - 1f, cy - 1f, 2, 2), Color.white);
                return;
            }
            float gap = 4f;
            if (def.Kind == WeaponKind.Firearm && _look != null && _look.Camera != null)
            {
                // Convert the spread angle into pixels so the crosshair shows the real cone.
                float vfov = _look.Camera.fieldOfView * Mathf.Deg2Rad;
                float pixelsPerRadian = Screen.height * 0.5f / Mathf.Tan(vfov * 0.5f);
                gap = Mathf.Max(4f, Mathf.Tan(_weapons.CurrentSpread * Mathf.Deg2Rad) * pixelsPerRadian / HudKit.Scale);
            }
            Color c = new Color(1f, 1f, 1f, 0.92f * (1f - _weapons.AimBlend));
            Color o = new Color(0f, 0f, 0f, 0.45f * (1f - _weapons.AimBlend));
            float len = 9f, t = 2f;
            // Dark outline first so the crosshair reads on bright walls.
            DrawRect(new Rect(cx - t * 0.5f - 1f, cy - gap - len - 1f, t + 2f, len + 2f), o);
            DrawRect(new Rect(cx - t * 0.5f - 1f, cy + gap - 1f, t + 2f, len + 2f), o);
            DrawRect(new Rect(cx - gap - len - 1f, cy - t * 0.5f - 1f, len + 2f, t + 2f), o);
            DrawRect(new Rect(cx + gap - 1f, cy - t * 0.5f - 1f, len + 2f, t + 2f), o);
            DrawRect(new Rect(cx - 1f, cy - 1f, 2f, 2f), c);
            DrawRect(new Rect(cx - t * 0.5f, cy - gap - len, t, len), c);
            DrawRect(new Rect(cx - t * 0.5f, cy + gap, t, len), c);
            DrawRect(new Rect(cx - gap - len, cy - t * 0.5f, len, t), c);
            DrawRect(new Rect(cx + gap, cy - t * 0.5f, len, t), c);
        }

        private void DrawDiagonalMarker(float cx, float cy, float size, Color c)
        {
            Matrix4x4 m = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, new Vector2(cx, cy) * HudKit.Scale);
            float t = 2f;
            DrawRect(new Rect(cx - t * 0.5f, cy - size - 6, t, size), c);
            DrawRect(new Rect(cx - t * 0.5f, cy + 6, t, size), c);
            DrawRect(new Rect(cx - size - 6, cy - t * 0.5f, size, t), c);
            DrawRect(new Rect(cx + 6, cy - t * 0.5f, size, t), c);
            GUI.matrix = m;
        }

        private static void DrawRect(Rect r, Color c) => HudKit.Fill(r, c);
    }
}
