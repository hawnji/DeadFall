using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Player;
using UnityEngine;

namespace Deadfall.UI
{
    /// <summary>
    /// Temporary IMGUI HUD for Milestone 1: health, ammo, weapon, reload, crosshair, hit markers,
    /// damage flash. Replaced by the UI Toolkit HUD once layouts are designed; it only reads state
    /// and listens to events, so the swap won't touch gameplay code.
    /// </summary>
    public sealed class PrototypeHud : MonoBehaviour
    {
        [SerializeField] private float _hitMarkerSeconds = 0.18f;
        [SerializeField] private float _damageFlashSeconds = 0.35f;

        private PlayerHealth _health;
        private PlayerWeapons _weapons;
        private PlayerLook _look;
        private PlayerCursorLock _cursor;
        private GameObject _player;

        private float _hitMarkerUntil;
        private bool _hitMarkerCrit;
        private bool _hitMarkerKill;
        private float _damageFlashUntil;
        private Vector3 _lastDamageDir;

        private GUIStyle _big, _small, _center;
        private Texture2D _white;

        public void Bind(GameObject player)
        {
            _player = player;
            _health = player.GetComponent<PlayerHealth>();
            _weapons = player.GetComponent<PlayerWeapons>();
            _look = player.GetComponent<PlayerLook>();
            _cursor = player.GetComponent<PlayerCursorLock>();
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

        private void EnsureStyles()
        {
            if (_big != null) return;
            _white = Texture2D.whiteTexture;
            _big = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerRight };
            _big.normal.textColor = Color.white;
            _small = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.UpperLeft };
            _small.normal.textColor = new Color(0.85f, 0.9f, 0.85f);
            _center = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _center.normal.textColor = Color.white;
        }

        private PlayerInteractor _interactor;

        private void OnGUI()
        {
            if (_player == null) return;
            EnsureStyles();
            float w = Screen.width, h = Screen.height;
            float now = Time.unscaledTime;

            // Damage flash (red vignette edge toward the source).
            if (now < _damageFlashUntil)
            {
                float a = (_damageFlashUntil - now) / _damageFlashSeconds * 0.35f;
                DrawRect(new Rect(0, 0, w, h), new Color(0.6f, 0f, 0f, a * 0.4f));
                if (_look != null && _lastDamageDir.sqrMagnitude > 0.01f)
                {
                    Vector3 local = _player.transform.InverseTransformDirection(-_lastDamageDir);
                    float angle = Mathf.Atan2(local.x, local.z);
                    Vector2 c = new Vector2(w * 0.5f, h * 0.5f) + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * (h * 0.22f);
                    DrawRect(new Rect(c.x - 14, c.y - 14, 28, 28), new Color(1f, 0.15f, 0.1f, a * 2f));
                }
            }

            // Interact prompt.
            if (_interactor == null) _interactor = _player.GetComponent<PlayerInteractor>();
            if (_interactor != null && !string.IsNullOrEmpty(_interactor.Prompt))
            {
                GUI.color = _interactor.CanUse ? Color.white : new Color(1f, 1f, 1f, 0.55f);
                GUI.Label(new Rect(0, h * 0.5f + 70, w, 24), _interactor.CanUse ? $"[F] {_interactor.Prompt}" : _interactor.Prompt,
                    new GUIStyle(_small) { alignment = TextAnchor.UpperCenter });
                GUI.color = Color.white;
            }

            // Health bar.
            if (_health != null)
            {
                Rect bg = new Rect(24, h - 52, 260, 22);
                DrawRect(bg, new Color(0f, 0f, 0f, 0.55f));
                float n = Mathf.Clamp01(_health.Normalized);
                Color hc = n > 0.5f ? new Color(0.55f, 0.85f, 0.45f) : n > 0.25f ? new Color(0.95f, 0.75f, 0.25f) : new Color(0.95f, 0.3f, 0.25f);
                DrawRect(new Rect(bg.x + 2, bg.y + 2, (bg.width - 4) * n, bg.height - 4), hc);
                GUI.Label(new Rect(bg.x, bg.y - 22, 260, 22), $"HEALTH {Mathf.CeilToInt(_health.Current)}", _small);
                if (!_health.IsAlive) GUI.Label(new Rect(0, h * 0.35f, w, 40), "YOU DIED", _center);
            }

            // Weapon and ammo.
            if (_weapons != null && _weapons.CurrentDefinition != null)
            {
                WeaponDefinition def = _weapons.CurrentDefinition;
                string ammo = def.Kind == WeaponKind.Firearm ? $"{_weapons.Magazine} / {_weapons.Reserve}" : "MELEE";
                GUI.Label(new Rect(w - 324, h - 72, 300, 44), ammo, _big);
                GUI.Label(new Rect(w - 324, h - 96, 300, 22), $"[{_weapons.CurrentIndex + 1}] {def.DisplayName}{ModsSuffix()}", new GUIStyle(_small) { alignment = TextAnchor.UpperRight });

                if (_weapons.IsReloading)
                {
                    Rect rb = new Rect(w * 0.5f - 60, h * 0.5f + 40, 120, 6);
                    DrawRect(rb, new Color(0, 0, 0, 0.5f));
                    DrawRect(new Rect(rb.x, rb.y, rb.width * _weapons.ReloadProgress, rb.height), Color.white);
                    GUI.Label(new Rect(0, h * 0.5f + 48, w, 24), "RELOADING", new GUIStyle(_small) { alignment = TextAnchor.UpperCenter });
                }
                else if (def.Kind == WeaponKind.Firearm && _weapons.Magazine == 0)
                {
                    GUI.Label(new Rect(0, h * 0.5f + 40, w, 24), _weapons.Reserve > 0 ? "RELOAD [R]" : "NO AMMO", new GUIStyle(_small) { alignment = TextAnchor.UpperCenter });
                }

                DrawCrosshair(w * 0.5f, h * 0.5f, def);
            }

            // Hit marker.
            if (now < _hitMarkerUntil)
            {
                Color mc = _hitMarkerKill ? new Color(1f, 0.3f, 0.25f) : _hitMarkerCrit ? new Color(1f, 0.85f, 0.2f) : Color.white;
                float s = _hitMarkerCrit ? 11f : 8f;
                DrawDiagonalMarker(w * 0.5f, h * 0.5f, s, mc);
            }

            if (_cursor != null && !_cursor.IsLocked && !UiModal.Active)
            {
                DrawRect(new Rect(0, 0, w, h), new Color(0, 0, 0, 0.45f));
                GUI.Label(new Rect(0, h * 0.4f, w, 40), "PAUSED  (Esc to resume)", _center);
            }
        }

        private string ModsSuffix()
        {
            Deadfall.Rules.Combat.WeaponMods m = _weapons.CurrentMods;
            string d = m != null ? m.Describe() : string.Empty;
            return string.IsNullOrEmpty(d) ? string.Empty : $"  ({d})";
        }

        private void DrawCrosshair(float cx, float cy, WeaponDefinition def)
        {
            if (_weapons.AimBlend > 0.9f && def.Kind == WeaponKind.Firearm)
            {
                DrawRect(new Rect(cx - 1.5f, cy - 1.5f, 3, 3), Color.white);
                return;
            }
            float gap = 4f;
            if (def.Kind == WeaponKind.Firearm && _look != null && _look.Camera != null)
            {
                // Convert the spread angle into pixels so the crosshair shows the real cone.
                float vfov = _look.Camera.fieldOfView * Mathf.Deg2Rad;
                float pixelsPerRadian = Screen.height * 0.5f / Mathf.Tan(vfov * 0.5f);
                gap = Mathf.Max(4f, Mathf.Tan(_weapons.CurrentSpread * Mathf.Deg2Rad) * pixelsPerRadian);
            }
            Color c = new Color(1f, 1f, 1f, 0.9f * (1f - _weapons.AimBlend));
            float len = 8f, t = 2f;
            DrawRect(new Rect(cx - t * 0.5f, cy - gap - len, t, len), c);
            DrawRect(new Rect(cx - t * 0.5f, cy + gap, t, len), c);
            DrawRect(new Rect(cx - gap - len, cy - t * 0.5f, len, t), c);
            DrawRect(new Rect(cx + gap, cy - t * 0.5f, len, t), c);
        }

        private void DrawDiagonalMarker(float cx, float cy, float size, Color c)
        {
            Matrix4x4 m = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, new Vector2(cx, cy));
            float t = 2f;
            DrawRect(new Rect(cx - t * 0.5f, cy - size - 6, t, size), c);
            DrawRect(new Rect(cx - t * 0.5f, cy + 6, t, size), c);
            DrawRect(new Rect(cx - size - 6, cy - t * 0.5f, size, t), c);
            DrawRect(new Rect(cx + 6, cy - t * 0.5f, size, t), c);
            GUI.matrix = m;
        }

        private void DrawRect(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = old;
        }
    }
}
