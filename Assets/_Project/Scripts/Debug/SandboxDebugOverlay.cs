using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Enemies;
using Deadfall.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deadfall.DebugTools
{
    /// <summary>
    /// Developer helpers: frame-rate cap hotkeys (F1 30, F2 60, F3 144, F4 unlimited), damage numbers, respawn (F9),
    /// god mode (F10) and the optional FPS counter from Settings. The on-screen dev stats readout was removed.
    /// </summary>
    public sealed class SandboxDebugOverlay : MonoBehaviour
    {
        private struct DamageNumber
        {
            public Vector3 Position;
            public float Amount;
            public bool Crit;
            public float Until;
        }

        private GameObject _player;
        private PlayerMotor _motor;
        private PlayerHealth _health;
        private Vector3 _spawnPos;
        private float _spawnYaw;
        private InputActionMap _map;
        private InputAction _f1, _f2, _f3, _f4, _f9, _f10;
        private readonly List<DamageNumber> _numbers = new List<DamageNumber>(32);
        private float _smoothedDt = 1f / 60f;

        public void Bind(GameObject player, Vector3 spawnPos, float spawnYaw)
        {
            _player = player;
            _motor = player.GetComponent<PlayerMotor>();
            _health = player.GetComponent<PlayerHealth>();
            _spawnPos = spawnPos;
            _spawnYaw = spawnYaw;
        }

        private void Awake()
        {
            _map = new InputActionMap("Debug");
            _f1 = _map.AddAction("Cap30", InputActionType.Button, "<Keyboard>/f1");
            _f2 = _map.AddAction("Cap60", InputActionType.Button, "<Keyboard>/f2");
            _f3 = _map.AddAction("Cap144", InputActionType.Button, "<Keyboard>/f3");
            _f4 = _map.AddAction("Uncapped", InputActionType.Button, "<Keyboard>/f4");
            _f9 = _map.AddAction("Respawn", InputActionType.Button, "<Keyboard>/f9");
            _f10 = _map.AddAction("God", InputActionType.Button, "<Keyboard>/f10");
            _map.Enable();
        }

        private void OnEnable()
        {
            _map?.Enable();
            EventBus<HitConfirmed>.Subscribe(OnHit);
        }

        private void OnDisable()
        {
            _map?.Disable();
            EventBus<HitConfirmed>.Unsubscribe(OnHit);
        }

        private void OnDestroy() => _map?.Dispose();

        private void OnHit(HitConfirmed e)
        {
            GameSettings gs = Settings;
            if (gs != null && !gs.DamageNumbers) return;
            if (_numbers.Count >= 32) _numbers.RemoveAt(0);
            _numbers.Add(new DamageNumber { Position = e.Point, Amount = e.Amount, Crit = e.IsCritical, Until = Time.time + 0.8f });
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _smoothedDt = Mathf.Lerp(_smoothedDt, dt, 0.05f);

            if (BuildInfo.DevTools) DevKeys();

            TickNumbers();

            // Outside a run, get back up a few seconds after going down so testing never stalls. During a run the
            // squad rules decide (revives, kits, return after the surge: M11).
            bool inRun = Deadfall.Encounters.WaveManager.Current != null && Deadfall.Encounters.WaveManager.Current.IsRunning;
            if (inRun) _respawnAt = -1f;
            else if (_health != null && !_health.IsAlive && _respawnAt < 0f) _respawnAt = Time.time + 3f;
            if (_respawnAt > 0f && Time.time >= _respawnAt)
            {
                // Get up where you fell. This used to teleport to the firing range spawn, which also fired after a
                // failed run and could pull you off the FOB (or out of a map) onto the original test range.
                _respawnAt = -1f;
                if (!_health.IsAlive) _health.Revive();
            }
        }

        private float _respawnAt = -1f;

        private void DevKeys()
        {
            if (_f1.WasPressedThisFrame()) SetCap(30);
            if (_f2.WasPressedThisFrame()) SetCap(60);
            if (_f3.WasPressedThisFrame()) SetCap(144);
            if (_f4.WasPressedThisFrame()) SetCap(0);
            if (_f9.WasPressedThisFrame() && _player != null)
            {
                _health.Revive();
                _motor.Teleport(_spawnPos, _spawnYaw);
            }
            if (_f10.WasPressedThisFrame() && _health != null) _health.Invulnerable = !_health.Invulnerable;
        }

        private void TickNumbers()
        {
            for (int i = _numbers.Count - 1; i >= 0; i--)
                if (Time.time > _numbers[i].Until) _numbers.RemoveAt(i);
        }

        private static GameSettings Settings => ServiceRegistry.TryGet(out ISettingsService s) ? s.Current : null;

        private static void SetCap(int cap)
        {
            if (ServiceRegistry.TryGet(out ISettingsService settings))
            {
                // A manual cap is a frame-rate test, so it turns VSync off.
                settings.Current.VSync = false;
                settings.Current.FrameRateCap = cap;
                settings.NotifyChanged();
                settings.Save();
            }
            else
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = cap > 0 ? cap : -1;
            }
        }

        private void OnGUI()
        {
            HudKit.Begin();
            DrawGui();
            HudKit.End();
        }

        private void DrawGui()
        {
            // The old dev readout (frame time, speed, enemies, waves, network) is gone; only Settings' FPS counter is left.
            GameSettings gs = Settings;
            if (gs != null && gs.ShowFps)
                HudKit.Label(new Rect(HudKit.W - 140f, 8f, 130f, 20f), $"{1f / Mathf.Max(0.0001f, _smoothedDt):0} FPS", 12, HudKit.TextDim, TextAnchor.MiddleRight, head: true);
            DrawNumbers();
        }

        private void DrawNumbers()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            for (int i = 0; i < _numbers.Count; i++)
            {
                DamageNumber n = _numbers[i];
                float life = n.Until - Time.time;
                Vector3 world = n.Position + Vector3.up * (0.8f - life) * 0.6f;
                Vector3 sp = cam.WorldToScreenPoint(world);
                if (sp.z <= 0f) continue;
                Vector2 p = HudKit.FromScreen(sp);
                float a = Mathf.Clamp01(life * 2f);
                Color c = n.Crit ? new Color(HudKit.Warn.r, HudKit.Warn.g, HudKit.Warn.b, a) : new Color(1f, 1f, 1f, a);
                HudKit.Label(new Rect(p.x - 50f, p.y - 14f, 100f, 28f), Mathf.RoundToInt(n.Amount).ToString(), n.Crit ? 22 : 17, c, TextAnchor.MiddleCenter, head: true);
            }
        }
    }
}
