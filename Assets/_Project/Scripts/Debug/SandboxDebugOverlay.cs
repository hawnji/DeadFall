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
    /// Developer overlay for tuning feel: FPS and frame time, movement state, frame-rate cap hotkeys
    /// (F1 30, F2 60, F3 144, F4 unlimited), damage numbers, respawn (F9), god mode (F10).
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
        private InputAction _f1, _f2, _f3, _f4, _f9, _f10, _help;
        private readonly List<DamageNumber> _numbers = new List<DamageNumber>(32);
        private float _smoothedDt = 1f / 60f;
        private float _worstDt;
        private float _worstResetAt;
        private bool _showHelp = true;
        private GUIStyle _style, _numStyle;
        private SandboxEnemySpawner _spawner;

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
            _help = _map.AddAction("Help", InputActionType.Button, "<Keyboard>/h");
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
            if (_numbers.Count >= 32) _numbers.RemoveAt(0);
            _numbers.Add(new DamageNumber { Position = e.Point, Amount = e.Amount, Crit = e.IsCritical, Until = Time.time + 0.8f });
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _smoothedDt = Mathf.Lerp(_smoothedDt, dt, 0.05f);
            if (Time.unscaledTime > _worstResetAt)
            {
                _worstDt = 0f;
                _worstResetAt = Time.unscaledTime + 2f;
            }
            _worstDt = Mathf.Max(_worstDt, dt);

            if (_f1.WasPressedThisFrame()) SetCap(30);
            if (_f2.WasPressedThisFrame()) SetCap(60);
            if (_f3.WasPressedThisFrame()) SetCap(144);
            if (_f4.WasPressedThisFrame()) SetCap(0);
            if (_help.WasPressedThisFrame()) _showHelp = !_showHelp;
            if (_f9.WasPressedThisFrame() && _player != null)
            {
                _health.Revive();
                _motor.Teleport(_spawnPos, _spawnYaw);
            }
            if (_f10.WasPressedThisFrame() && _health != null) _health.Invulnerable = !_health.Invulnerable;

            for (int i = _numbers.Count - 1; i >= 0; i--)
                if (Time.time > _numbers[i].Until) _numbers.RemoveAt(i);

            // Auto-respawn a few seconds after death so testing never stalls.
            if (_health != null && !_health.IsAlive && _respawnAt < 0f) _respawnAt = Time.time + 3f;
            if (_respawnAt > 0f && Time.time >= _respawnAt)
            {
                _respawnAt = -1f;
                _health.Revive();
                _motor.Teleport(_spawnPos, _spawnYaw);
            }
        }

        private float _respawnAt = -1f;

        private static void SetCap(int cap)
        {
            if (ServiceRegistry.TryGet(out ISettingsService settings))
            {
                settings.Current.FrameRateCap = cap;
                settings.NotifyChanged();
            }
            else
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = cap > 0 ? cap : -1;
            }
        }

        private void OnGUI()
        {
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = 14 };
                _style.normal.textColor = new Color(0.8f, 1f, 0.8f);
                _numStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            }

            int cap = Application.targetFrameRate;
            string capText = cap > 0 ? cap.ToString() : "off";
            string text = $"FPS {1f / Mathf.Max(0.0001f, _smoothedDt):0}  ({_smoothedDt * 1000f:0.0} ms, worst {_worstDt * 1000f:0.0} ms)  cap {capText}";
            if (_motor != null)
                text += $"\nSpeed {_motor.HorizontalSpeed:0.00} m/s  {(_motor.IsGrounded ? "grounded" : "air")}{(_motor.IsSprinting ? "  sprint" : "")}{(_motor.IsCrouched ? "  crouch" : "")}";
            if (_health != null && _health.Invulnerable) text += "\nGOD MODE";
            EnemyManager enemies = EnemyManager.Current;
            if (enemies != null && enemies.ActiveCount > 0)
                text += $"\nEnemies {enemies.AliveCount} / cap {enemies.MaxAlive}  ·  AI {enemies.UpdateMilliseconds:0.00} ms";
            if (_spawner == null) _spawner = GetComponent<SandboxEnemySpawner>();
            if (_spawner != null && !string.IsNullOrEmpty(_spawner.LastMessage)) text += "\n" + _spawner.LastMessage;
            if (_showHelp)
                text += "\n\nWASD move · Mouse look · Shift sprint · Space jump · Ctrl/C crouch\nLMB fire · RMB aim · R reload · V melee · 1-4 / wheel swap · Esc pause" +
                        "\nF1/F2/F3/F4 cap 30/60/144/off · F9 respawn · F10 god mode · H hide help" +
                        "\nF5 Husk · F6 Bolter · F7 Bulwark · F8 horde of 12 · F11 kill all · [ ] enemy cap";
            GUI.Label(new Rect(12, 8, 900, 220), text, _style);

            Camera cam = Camera.main;
            if (cam == null) return;
            for (int i = 0; i < _numbers.Count; i++)
            {
                DamageNumber n = _numbers[i];
                float life = n.Until - Time.time;
                Vector3 world = n.Position + Vector3.up * (0.8f - life) * 0.6f;
                Vector3 sp = cam.WorldToScreenPoint(world);
                if (sp.z <= 0f) continue;
                _numStyle.normal.textColor = n.Crit ? new Color(1f, 0.85f, 0.2f, Mathf.Clamp01(life * 2f)) : new Color(1f, 1f, 1f, Mathf.Clamp01(life * 2f));
                GUI.Label(new Rect(sp.x - 40, Screen.height - sp.y - 12, 80, 24), Mathf.RoundToInt(n.Amount).ToString(), _numStyle);
            }
        }
    }
}
