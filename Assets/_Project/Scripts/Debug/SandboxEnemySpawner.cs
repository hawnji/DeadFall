using Deadfall.Encounters;
using Deadfall.Enemies;
using Deadfall.Net;
using Deadfall.Rules.Combat;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace Deadfall.DebugTools
{
    /// <summary>
    /// Sandbox hotkeys for the M2 enemies: F5 Husk, F6 Bolter, F7 Bulwark, F8 a mixed group of 12, F11 kill all,
    /// [ and ] lower/raise the alive cap in steps of 24 for stress tests. Enemies appear on the NavMesh
    /// 18-35 m from the player in a random direction.
    /// M3: N starts (or stops) a survival run on the host, G toggles ready during a lull.
    /// M7: F12 spawns the next advanced enemy (Creeper, Pyre, Retcher, Alpha, Goliath, Stalker, Specimen Zero);
    /// hold Ctrl for a random mutation variant.
    /// </summary>
    public sealed class SandboxEnemySpawner : MonoBehaviour
    {
        private const float MinDistance = 18f;
        private const float MaxDistance = 35f;
        private static readonly Vector2 WorldMin = new Vector2(-43f, -43f);
        private static readonly Vector2 WorldMax = new Vector2(43f, 83f);

        private Transform _player;
        private InputActionMap _map;
        private InputAction _husk, _bolter, _bulwark, _horde, _clear, _capDown, _capUp, _run, _ready, _advanced, _ctrl;
        private int _advancedIndex;
        private static readonly EnemyTraits[] DebugTraits =
            { EnemyTraits.Regenerating, EnemyTraits.Splitting, EnemyTraits.Hardened, EnemyTraits.Swift };

        public string LastMessage { get; private set; } = string.Empty;
        private float _messageUntil;

        public void Bind(GameObject player) => _player = player != null ? player.transform : null;

        private void Awake()
        {
            _map = new InputActionMap("EnemyDebug");
            _husk = _map.AddAction("SpawnHusk", InputActionType.Button, "<Keyboard>/f5");
            _bolter = _map.AddAction("SpawnBolter", InputActionType.Button, "<Keyboard>/f6");
            _bulwark = _map.AddAction("SpawnBulwark", InputActionType.Button, "<Keyboard>/f7");
            _horde = _map.AddAction("SpawnHorde", InputActionType.Button, "<Keyboard>/f8");
            _advanced = _map.AddAction("SpawnAdvanced", InputActionType.Button, "<Keyboard>/f12");
            _ctrl = _map.AddAction("VariantModifier", InputActionType.Button, "<Keyboard>/ctrl");
            _clear = _map.AddAction("KillAll", InputActionType.Button, "<Keyboard>/f11");
            _capDown = _map.AddAction("CapDown", InputActionType.Button, "<Keyboard>/leftBracket");
            _capUp = _map.AddAction("CapUp", InputActionType.Button, "<Keyboard>/rightBracket");
            _run = _map.AddAction("SurvivalRun", InputActionType.Button, "<Keyboard>/n");
            _ready = _map.AddAction("Ready", InputActionType.Button, "<Keyboard>/g");
            _map.Enable();
        }

        private void OnEnable() => _map?.Enable();
        private void OnDisable() => _map?.Disable();

        private void Update()
        {
            if (Time.time > _messageUntil) LastMessage = string.Empty;
            if (_player == null) return;
            UpdateWaveKeys();

            bool any = _husk.WasPressedThisFrame() || _bolter.WasPressedThisFrame() || _bulwark.WasPressedThisFrame()
                       || _horde.WasPressedThisFrame() || _clear.WasPressedThisFrame() || _advanced.WasPressedThisFrame();
            if (any && SessionService.Instance.Role == SessionRole.Client)
            {
                Say("Enemies are controlled by the host.");
                return;
            }

            EnemyManager m = EnemyManager.Instance;
            if (_husk.WasPressedThisFrame()) SpawnMany(PrototypeEnemies.Husk, 1);
            if (_bolter.WasPressedThisFrame()) SpawnMany(PrototypeEnemies.Bolter, 1);
            if (_bulwark.WasPressedThisFrame()) SpawnMany(PrototypeEnemies.Bulwark, 1);
            if (_horde.WasPressedThisFrame())
            {
                SpawnMany(PrototypeEnemies.Husk, 8);
                SpawnMany(PrototypeEnemies.Bolter, 3);
                SpawnMany(PrototypeEnemies.Bulwark, 1);
            }
            if (_clear.WasPressedThisFrame()) m.KillAll();
            if (_advanced.WasPressedThisFrame()) SpawnAdvanced();

            if (_capUp.WasPressedThisFrame()) SetCap(m, Mathf.Min(240, m.MaxAlive + 24));
            if (_capDown.WasPressedThisFrame()) SetCap(m, Mathf.Max(24, m.MaxAlive - 24));
        }

        private void UpdateWaveKeys()
        {
            WaveManager waves = WaveManager.Current;
            if (waves == null) return;
            if (_ready.WasPressedThisFrame()) waves.ToggleReady();
            if (!_run.WasPressedThisFrame()) return;
            if (!waves.IsAuthority)
            {
                Say("The host starts and stops runs.");
                return;
            }
            if (waves.IsRunning)
            {
                waves.StopRun();
                Say("Survival run stopped.");
            }
            else waves.StartRun();
        }

        private void SetCap(EnemyManager m, int cap)
        {
            m.CapOverride = cap;
            Say($"Enemy cap {cap}");
        }

        private void SpawnAdvanced()
        {
            var all = PrototypeEnemies.All;
            const int first = 3; // after Husk, Bolter, Bulwark
            EnemyDefinition def = all[first + _advancedIndex % (all.Count - first)];
            _advancedIndex++;
            bool ctrl = _ctrl.IsPressed();
            EnemyTraits traits = ctrl && (def.Role == EnemyRole.Regular || def.Role == EnemyRole.Elite)
                ? DebugTraits[Random.Range(0, DebugTraits.Length)] : EnemyTraits.None;
            if (!def.CanSplit && traits == EnemyTraits.Splitting) traits = EnemyTraits.Hardened;
            if (!TrySpawnPoint(out Vector3 point))
            {
                Say("No room to spawn (is the NavMesh built?)");
                return;
            }
            float yaw = Quaternion.LookRotation(_player.position - point).eulerAngles.y;
            Enemy e = EnemyManager.Instance.Spawn(def, point, yaw, 1f, traits);
            if (e != null) e.Alert();
            Say(e == null ? "Enemy cap reached." : $"Spawned {def.DisplayName}{(traits != EnemyTraits.None ? " (" + VariantRules.Describe(traits) + ")" : string.Empty)}");
        }

        private void SpawnMany(EnemyDefinition def, int count)
        {
            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                if (TrySpawnPoint(out Vector3 point))
                {
                    float yaw = Quaternion.LookRotation(_player.position - point).eulerAngles.y;
                    if (EnemyManager.Instance.Spawn(def, point, yaw) != null) spawned++;
                }
            }
            if (spawned < count)
                Say(EnemyManager.Instance.AliveCount >= EnemyManager.Instance.MaxAlive
                    ? $"Enemy cap reached ({EnemyManager.Instance.MaxAlive}). Press ] to raise it."
                    : "No room to spawn (is the NavMesh built?)");
        }

        private bool TrySpawnPoint(out Vector3 point)
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                float angle = Random.value * Mathf.PI * 2f;
                float dist = Random.Range(MinDistance, MaxDistance);
                Vector3 p = _player.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dist;
                p.x = Mathf.Clamp(p.x, WorldMin.x, WorldMax.x);
                p.z = Mathf.Clamp(p.z, WorldMin.y, WorldMax.y);
                p.y = 0.5f;
                if (NavMesh.SamplePosition(p, out NavMeshHit hit, 2f, NavMesh.AllAreas) && hit.position.y < 0.6f)
                {
                    point = hit.position; // ground level only, not on top of boxes or ramps
                    return true;
                }
            }
            point = default;
            return false;
        }

        private void Say(string message)
        {
            LastMessage = message;
            _messageUntil = Time.time + 2.5f;
        }
    }
}
