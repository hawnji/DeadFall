using System.Collections.Generic;
using System.Diagnostics;
using Deadfall.Combat;
using UnityEngine;
using UnityEngine.AI;

namespace Deadfall.Enemies
{
    /// <summary>
    /// Owns every enemy: spawning from per-type pools, the active cap, and one update loop that ticks them all.
    /// Perception (choosing a target and re-pathing) runs at <see cref="PerceptionHz"/> per enemy, spread across
    /// frames so the cost stays flat; movement and attacks run every frame. Only the host runs this in co-op.
    /// </summary>
    [DefaultExecutionOrder(20)]
    public sealed class EnemyManager : MonoBehaviour
    {
        public const float PerceptionHz = 8f;
        /// <summary>D-19: 24 enemies alive solo, +8 per extra player (48 at four players).</summary>
        public const int BaseCap = 24;
        public const int CapPerExtraPlayer = 8;

        private static EnemyManager _instance;

        private readonly List<Enemy> _active = new List<Enemy>(64);
        private readonly Dictionary<EnemyDefinition, Stack<Enemy>> _pools = new Dictionary<EnemyDefinition, Stack<Enemy>>();
        private readonly Stopwatch _timer = new Stopwatch();
        private Transform _poolRoot;
        private ushort _nextSlot = 1;
        private int _perceptionSpread;
        private float _avgMs;

        /// <summary>When set (debug and stress tests), replaces the player-count cap.</summary>
        public int CapOverride { get; set; }
        /// <summary>False on co-op clients: the host simulates and clients show its snapshots.</summary>
        public bool SimulationEnabled { get; set; } = true;

        public int MaxAlive => CapOverride > 0 ? CapOverride : BaseCap + CapPerExtraPlayer * Mathf.Max(0, TargetCount() - 1);
        public int AliveCount { get; private set; }
        public int ActiveCount => _active.Count;
        public IReadOnlyList<Enemy> Active => _active;
        /// <summary>Smoothed cost of the enemy update loop in milliseconds (AI, movement commands, attacks).</summary>
        public float UpdateMilliseconds => _avgMs;

        public static EnemyManager Instance
        {
            get
            {
                if (_instance == null) _instance = new GameObject("[EnemyManager]").AddComponent<EnemyManager>();
                return _instance;
            }
        }

        /// <summary>The manager if one exists, without creating it.</summary>
        public static EnemyManager Current => _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            _poolRoot = transform;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        /// <summary>
        /// Spawns an enemy at the nearest NavMesh point within 3 m of <paramref name="position"/>.
        /// Returns null at the cap or when there is no NavMesh nearby.
        /// </summary>
        public Enemy Spawn(EnemyDefinition definition, Vector3 position, float yaw)
        {
            if (definition == null || AliveCount >= MaxAlive) return null;
            if (!NavMesh.SamplePosition(position, out NavMeshHit hit, 3f, NavMesh.AllAreas)) return null;

            if (!_pools.TryGetValue(definition, out Stack<Enemy> pool))
            {
                pool = new Stack<Enemy>();
                _pools[definition] = pool;
            }
            Enemy enemy = null;
            while (pool.Count > 0 && enemy == null) enemy = pool.Pop();
            if (enemy == null) enemy = Enemy.Create(definition, _poolRoot, hit.position);

            double now = Time.timeAsDouble;
            enemy.SlotId = _nextSlot++;
            if (_nextSlot == 0) _nextSlot = 1; // 0 means "none" on the wire
            enemy.Activate(hit.position, yaw, now);
            // Spread perception ticks evenly across the interval instead of all enemies thinking on one frame.
            enemy.NextPerception = now + (_perceptionSpread++ % 8) / (8f * PerceptionHz);
            _active.Add(enemy);
            AliveCount++;
            return enemy;
        }

        /// <summary>Returns every enemy to its pool immediately.</summary>
        public void DespawnAll()
        {
            for (int i = _active.Count - 1; i >= 0; i--) ReturnToPool(i);
            AliveCount = 0;
        }

        /// <summary>Kills every living enemy (they play their death and despawn normally).</summary>
        public void KillAll()
        {
            for (int i = 0; i < _active.Count; i++) _active[i].Kill();
        }

        private void Update()
        {
            if (!SimulationEnabled || _active.Count == 0)
            {
                _avgMs = Mathf.Lerp(_avgMs, 0f, 0.1f);
                return;
            }
            _timer.Restart();
            double now = Time.timeAsDouble;
            float dt = Time.deltaTime;
            float interval = 1f / PerceptionHz;
            int alive = 0;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Enemy e = _active[i];
                if (e == null)
                {
                    _active.RemoveAt(i);
                    continue;
                }
                if (e.ReadyToDespawn(now))
                {
                    ReturnToPool(i);
                    continue;
                }
                bool perceive = false;
                if (e.IsAlive && now >= e.NextPerception)
                {
                    perceive = true;
                    e.NextPerception += interval;
                    if (e.NextPerception < now) e.NextPerception = now + interval; // after a hitch, don't burst
                }
                e.Tick(now, dt, perceive);
                if (e.IsAlive) alive++;
            }
            AliveCount = alive;

            _timer.Stop();
            _avgMs = Mathf.Lerp(_avgMs, (float)_timer.Elapsed.TotalMilliseconds, 0.1f);
        }

        private void ReturnToPool(int index)
        {
            Enemy e = _active[index];
            int last = _active.Count - 1;
            _active[index] = _active[last];
            _active.RemoveAt(last);
            if (e == null) return;
            e.Deactivate();
            if (_pools.TryGetValue(e.Definition, out Stack<Enemy> pool)) pool.Push(e);
        }

        private static int TargetCount()
        {
            int n = 0;
            IReadOnlyList<ICombatTarget> all = CombatTargets.All;
            for (int i = 0; i < all.Count; i++) if (all[i] != null) n++;
            return n;
        }
    }
}
