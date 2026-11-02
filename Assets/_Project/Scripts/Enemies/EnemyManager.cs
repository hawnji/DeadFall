using System;
using System.Collections.Generic;
using System.Diagnostics;
using Deadfall.Combat;
using Deadfall.Rules.Combat;
using UnityEngine;
using UnityEngine.AI;

namespace Deadfall.Enemies
{
    /// <summary>
    /// Owns every enemy: spawning from per-type pools, the active cap, and one update loop that ticks them all.
    /// Perception (choosing a target and re-pathing) runs at <see cref="PerceptionHz"/> per enemy, spread across
    /// frames so the cost stays flat; movement and attacks run every frame.
    /// In co-op only the host simulates. On a client the manager is in <see cref="MirrorMode"/>: it keeps
    /// copies of the host's enemies, created and updated from snapshots (see EnemyReplicator).
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
        private readonly Dictionary<ushort, Enemy> _bySlot = new Dictionary<ushort, Enemy>(64);
        private bool _mirrorMode;
        private readonly Stopwatch _timer = new Stopwatch();
        private Transform _poolRoot;
        private ushort _nextSlot = 1;
        private int _perceptionSpread;
        private float _avgMs;

        /// <summary>When set (debug and stress tests), replaces the player-count cap.</summary>
        public int CapOverride { get; set; }
        /// <summary>A mirror that hasn't appeared in a snapshot for this long is removed (lost despawn message).</summary>
        public const float MirrorTimeoutSeconds = 3f;

        /// <summary>
        /// True on co-op clients: no AI runs and enemies only come from <see cref="SpawnMirror"/>. Switching
        /// mode removes every current enemy, since local enemies and host copies can't mix.
        /// </summary>
        public bool MirrorMode
        {
            get => _mirrorMode;
            set
            {
                if (_mirrorMode == value) return;
                DespawnAll();
                _mirrorMode = value;
            }
        }

        /// <summary>Raised when an enemy goes back to its pool (the host tells clients to remove their copy).</summary>
        public event Action<Enemy> Despawned;

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
        /// Returns null at the cap or when there is no NavMesh nearby. <paramref name="healthScale"/> multiplies max
        /// health (surge scaling).
        /// </summary>
        public Enemy Spawn(EnemyDefinition definition, Vector3 position, float yaw, float healthScale = 1f,
            EnemyTraits traits = EnemyTraits.None, SpecimenModifiers bossMods = SpecimenModifiers.None)
        {
            if (definition == null || _mirrorMode || AliveCount >= MaxAlive) return null;
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
            enemy.Activate(hit.position, yaw, now, healthScale, traits, bossMods);
            _bySlot[enemy.SlotId] = enemy;
            // Spread perception ticks evenly across the interval instead of all enemies thinking on one frame.
            enemy.NextPerception = now + (_perceptionSpread++ % 8) / (8f * PerceptionHz);
            _active.Add(enemy);
            AliveCount++;
            return enemy;
        }

        /// <summary>Client: creates the local copy of host enemy <paramref name="slot"/>. Ignores the cap and the NavMesh.</summary>
        public Enemy SpawnMirror(EnemyDefinition definition, ushort slot, Vector3 position, float yaw)
        {
            if (definition == null || !_mirrorMode || slot == 0) return null;
            if (_bySlot.TryGetValue(slot, out Enemy existing) && existing != null) return existing;
            if (!_pools.TryGetValue(definition, out Stack<Enemy> pool))
            {
                pool = new Stack<Enemy>();
                _pools[definition] = pool;
            }
            Enemy enemy = null;
            while (pool.Count > 0 && enemy == null) enemy = pool.Pop();
            if (enemy == null) enemy = Enemy.Create(definition, _poolRoot, position);
            enemy.SlotId = slot;
            enemy.ActivateMirror(position, yaw, Time.timeAsDouble);
            _active.Add(enemy);
            _bySlot[slot] = enemy;
            return enemy;
        }

        /// <summary>The active enemy with this slot id, or null.</summary>
        public Enemy FindBySlot(ushort slot) => _bySlot.TryGetValue(slot, out Enemy e) && e != null ? e : null;

        /// <summary>Removes one enemy by slot (client, when the host says it despawned).</summary>
        public void DespawnSlot(ushort slot)
        {
            Enemy e = FindBySlot(slot);
            if (e == null) return;
            int index = _active.IndexOf(e);
            if (index >= 0) ReturnToPool(index);
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
            if (_mirrorMode)
            {
                TickMirrors();
                return;
            }
            if (_active.Count == 0)
            {
                _avgMs = Mathf.Lerp(_avgMs, 0f, 0.1f);
                return;
            }
            _timer.Restart();
            double now = Time.timeAsDouble;
            float dt = Time.deltaTime;
            float interval = 1f / PerceptionHz;
            int alive = 0;
            ApplyAuras();

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

        private readonly List<Enemy> _auraSources = new List<Enemy>(4);

        /// <summary>Alphas buff the infected around them (not themselves, and buffs from several Alphas don't stack).</summary>
        private void ApplyAuras()
        {
            _auraSources.Clear();
            for (int i = 0; i < _active.Count; i++)
            {
                Enemy e = _active[i];
                if (e != null && e.IsAlive && e.Stats.AuraRadius > 0f) _auraSources.Add(e);
            }
            for (int i = 0; i < _active.Count; i++)
            {
                Enemy e = _active[i];
                if (e == null) continue;
                float speed = 0f, reduction = 0f;
                for (int k = 0; k < _auraSources.Count; k++)
                {
                    Enemy a = _auraSources[k];
                    if (a == e) continue;
                    float r = a.Stats.AuraRadius;
                    if ((a.transform.position - e.transform.position).sqrMagnitude > r * r) continue;
                    speed = Mathf.Max(speed, a.Stats.AuraSpeedBonus);
                    reduction = Mathf.Max(reduction, a.Stats.AuraDamageReduction);
                }
                e.AuraSpeedBonus = speed;
                e.AuraReduction = reduction;
            }
        }

        private void TickMirrors()
        {
            double now = Time.timeAsDouble;
            float dt = Time.deltaTime;
            int alive = 0;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Enemy e = _active[i];
                if (e == null || now - e.LastSnapshotTime > MirrorTimeoutSeconds)
                {
                    ReturnToPool(i);
                    continue;
                }
                e.TickMirror(now, dt);
                if (e.IsAlive) alive++;
            }
            AliveCount = alive;
            _avgMs = 0f;
        }

        private void ReturnToPool(int index)
        {
            Enemy e = _active[index];
            int last = _active.Count - 1;
            _active[index] = _active[last];
            _active.RemoveAt(last);
            if (e == null) return;
            if (_bySlot.TryGetValue(e.SlotId, out Enemy mapped) && mapped == e) _bySlot.Remove(e.SlotId);
            Despawned?.Invoke(e);
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
