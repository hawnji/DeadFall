using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Player;
using UnityEngine;

namespace Deadfall.UI
{
    /// <summary>
    /// Player HUD in the shared <see cref="HudKit"/> style (T-60): health card (bottom left, segmented bar with a
    /// trailing ghost), weapon card (bottom right), outlined crosshair that shows the real spread, hit markers,
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
            if (_interactor != null && !string.IsNullOrEmpty(_interactor.Prompt))
            {
                string text = _interactor.IsHold && _interactor.CanUse ? "Hold  ·  " + _interactor.Prompt : _interactor.Prompt;
                HudKit.Prompt(w * 0.5f, h * 0.5f + 84f, _interactor.CanUse ? "F" : null, text, _interactor.CanUse, _interactor.HoldProgress);
            }

            // Health card (bottom left).
            if (_health != null)
            {
                float n = Mathf.Clamp01(_health.Normalized);
                _ghostHealth = n >= _ghostHealth ? n : Mathf.MoveTowards(_ghostHealth, n, Time.unscaledDeltaTime * 0.35f);
                Color hc = n > 0.5f ? HudKit.Good : n > 0.25f ? HudKit.Warn : HudKit.Bad;
                var card = new Rect(28f, h - 28f - 66f, 340f, 66f);
                HudKit.AccentPanel(card, hc);
                HudKit.Label(new Rect(card.x + 16f, card.y + 6f, 200f, 24f), "HEALTH", 13, HudKit.TextDim, TextAnchor.MiddleLeft, head: true);
                HudKit.Label(new Rect(card.x + 16f, card.y + 2f, card.width - 32f, 32f), Mathf.CeilToInt(_health.Current).ToString(), 28, Color.white, TextAnchor.MiddleRight, head: true);
                HudKit.Bar(new Rect(card.x + 16f, card.y + 40f, card.width - 32f, 14f), n, hc, 10, null, _ghostHealth);
                // Downed and eliminated screens are drawn by the squad HUD (M11).
            }

            // Weapon card (bottom right).
            if (_weapons != null && _weapons.CurrentDefinition != null)
            {
                WeaponDefinition def = _weapons.CurrentDefinition;
                var card = new Rect(w - 28f - 360f, h - 28f - 84f, 360f, 84f);
                HudKit.Panel(card);
                HudKit.KeyChip(card.x + 16f, card.y + 12f, (_weapons.CurrentIndex + 1).ToString(), 22f);
                HudKit.Label(new Rect(card.x + 46f, card.y + 10f, card.width - 62f, 26f), def.DisplayName.ToUpperInvariant(), 17, HudKit.Text, TextAnchor.MiddleLeft, head: true);
                string mods = ModsSuffix();
                if (mods.Length > 0) HudKit.Label(new Rect(card.x + 16f, card.y + 52f, card.width - 170f, 22f), mods, 12, HudKit.Accent, TextAnchor.MiddleLeft);
                if (def.Kind == WeaponKind.Firearm)
                {
                    int mag = _weapons.Magazine;
                    float magMax = Mathf.Max(1, _weapons.MagazineCapacity);
                    Color mc = mag == 0 ? HudKit.Bad : mag <= magMax * 0.25f ? HudKit.Warn : Color.white;
                    float rw = HudKit.TextWidth("/ " + _weapons.Reserve, 18, head: true);
                    HudKit.Label(new Rect(card.x, card.y + 34f, card.width - 16f, 44f), "/ " + _weapons.Reserve, 18, HudKit.TextDim, TextAnchor.LowerRight, head: true);
                    HudKit.Label(new Rect(card.x, card.y + 30f, card.width - 22f - rw, 48f), mag.ToString(), 40, mc, TextAnchor.LowerRight, head: true);
                    HudKit.Bar(new Rect(card.x + 16f, card.yMax - 10f, card.width - 32f, 5f), mag / magMax, new Color(1f, 1f, 1f, 0.75f), 0, new Color(0f, 0f, 0f, 0.4f));
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
                HudKit.Label(new Rect(0f, HudKit.PauseTop, w, 60f), "PAUSED", 48, Color.white, TextAnchor.MiddleCenter, head: true);
                HudKit.Prompt(w * 0.5f, HudKit.PauseTop + 66f, "Esc", "Resume", true);
                float top = HudKit.PauseTop + 112f;
                if (_settingsPanel != null && HudKit.Button(new Rect(w * 0.5f - 206f, top, 412f, 40f), "Settings"))
                    _settingsPanel.Open();
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

        private string ModsSuffix()
        {
            Deadfall.Rules.Combat.WeaponMods m = _weapons.CurrentMods;
            return m != null ? m.Describe() ?? string.Empty : string.Empty;
        }

        private void DrawCrosshair(float cx, float cy, WeaponDefinition def)
        {
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
