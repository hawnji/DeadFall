using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Enemies;
using Deadfall.Rules.Common;
using Deadfall.Rules.Encounters;
using UnityEngine;

namespace Deadfall.Encounters
{
    /// <summary>A place enemies can appear. Points sharing a group form one arrival direction.</summary>
    public struct SpawnPoint
    {
        public Vector3 Position;
        public int Group;
    }

    /// <summary>
    /// Turns a <see cref="SurgePlan"/> into enemies over time (host only). Keeps the surge's alive count under
    /// its concurrency, spawns out of sight and at least 15 m from every player, and limits arrivals to the
    /// plan's number of directions. It only tracks the enemies it spawned, so debug spawns never count toward a surge.
    /// </summary>
    public sealed class SpawnDirector
    {
        public const float MinPlayerDistance = 15f;
        /// <summary>Fallback when no point is 15 m away (small open areas): still never right next to a player.</summary>
        public const float CloseMinPlayerDistance = 7f;
        public const float SpawnInterval = 0.4f;
        /// <summary>When far below concurrency, up to this many spawn per interval.</summary>
        public const int MaxBurst = 3;
        /// <summary>Spawn attempts for one enemy before it is skipped (no NavMesh near any point).</summary>
        public const int MaxAttempts = 25;
        private const float EyeHeight = 1.6f;
        private const float ChestHeight = 1.2f;

        private readonly List<SpawnPoint> _points = new List<SpawnPoint>();
        private readonly List<int> _groups = new List<int>();
        private readonly List<int> _activeGroups = new List<int>();
        private readonly HashSet<Enemy> _alive = new HashSet<Enemy>();
        private readonly List<Vector3> _players = new List<Vector3>(4);
        private readonly List<int> _candidates = new List<int>(32);
        private readonly List<float> _scores = new List<float>(32);
        private readonly Dictionary<string, EnemyDefinition> _definitions = new Dictionary<string, EnemyDefinition>();
        private readonly List<Enemy> _cullBuffer = new List<Enemy>();

        private SurgePlan _plan;
        private DeterministicRandom _rng = new DeterministicRandom(1);
        private int _next;
        private int _attempts;
        private int _groupCursor;
        private double _nextSpawnAt;

        public IReadOnlyList<SpawnPoint> Points => _points;
        public int GroupCount => _groups.Count;
        public int Alive => _alive.Count;
        public bool AllSpawned => _plan == null || _next >= _plan.Spawns.Count;
        /// <summary>Enemies of this surge not yet dead: still to spawn plus alive.</summary>
        public int Remaining => (_plan == null ? 0 : Mathf.Max(0, _plan.Spawns.Count - _next)) + _alive.Count;
        /// <summary>Enemies dropped because no spawn point worked (shown on the debug overlay).</summary>
        public int Skipped { get; private set; }
        /// <summary>The groups (directions) used by the current surge.</summary>
        public IReadOnlyList<int> ActiveGroups => _activeGroups;

        public SpawnDirector(IEnumerable<EnemyDefinition> definitions)
        {
            foreach (EnemyDefinition d in definitions)
                if (d != null) _definitions[d.ContentId] = d;
        }

        public void AddPoint(Vector3 position, int group)
        {
            _points.Add(new SpawnPoint { Position = position, Group = group });
            if (!_groups.Contains(group)) _groups.Add(group);
        }

        public void ClearPoints()
        {
            _points.Clear();
            _groups.Clear();
        }

        public bool Owns(Enemy e) => e != null && _alive.Contains(e);

        /// <summary>Stops tracking an enemy (it died or despawned). Returns true if it was one of ours.</summary>
        public bool Forget(Enemy e) => e != null && _alive.Remove(e);

        public void Begin(SurgePlan plan, ulong seed, double now)
        {
            _plan = plan;
            _next = 0;
            _attempts = 0;
            _groupCursor = 0;
            Skipped = 0;
            _nextSpawnAt = now;
            _rng = new DeterministicRandom(seed ^ ((ulong)plan.Surge * 0xD1B54A32D192ED03UL));

            // Pick this surge's arrival directions.
            _activeGroups.Clear();
            _activeGroups.AddRange(_groups);
            for (int i = _activeGroups.Count - 1; i > 0; i--)
            {
                int j = _rng.Range(0, i + 1);
                (_activeGroups[i], _activeGroups[j]) = (_activeGroups[j], _activeGroups[i]);
            }
            int keep = Mathf.Clamp(plan.Directions, 1, Mathf.Max(1, _activeGroups.Count));
            if (_activeGroups.Count > keep) _activeGroups.RemoveRange(keep, _activeGroups.Count - keep);
        }

        /// <summary>Ends the surge's spawning; enemies already alive are left alone.</summary>
        public void Stop() => _plan = null;

        /// <summary>Kills every enemy this director spawned (deadlock guard, run reset). No credits are paid.</summary>
        public void KillAll()
        {
            _cullBuffer.Clear();
            _cullBuffer.AddRange(_alive);
            _alive.Clear();
            foreach (Enemy e in _cullBuffer)
                if (e != null && e.IsAlive) e.Kill();
            _cullBuffer.Clear();
        }

        public void Tick(double now)
        {
            if (_plan == null || AllSpawned || now < _nextSpawnAt) return;
            EnemyManager manager = EnemyManager.Current;
            if (manager == null || _points.Count == 0) return;
            CollectPlayers();
            if (_players.Count == 0) return; // nobody to fight; wait

            _nextSpawnAt = now + SpawnInterval;
            int room = Mathf.Min(_plan.Concurrency - _alive.Count, manager.MaxAlive - manager.AliveCount);
            int burst = Mathf.Clamp(room / 8 + 1, 0, MaxBurst);
            for (int i = 0; i < burst && room > 0 && !AllSpawned; i++)
            {
                if (!TrySpawnNext(manager)) break;
                room--;
            }
        }

        private bool TrySpawnNext(EnemyManager manager)
        {
            string id = _plan.Spawns[_next];
            if (!_definitions.TryGetValue(id, out EnemyDefinition def))
            {
                GameLog.Warn("Waves", $"unknown enemy '{id}' in plan, skipped.");
                SkipCurrent();
                return true;
            }

            int group = _activeGroups.Count > 0 ? _activeGroups[_groupCursor++ % _activeGroups.Count] : int.MinValue;
            int point = PickPoint(group, MinPlayerDistance);
            if (point < 0) point = PickPoint(int.MinValue, MinPlayerDistance); // this direction has nothing usable; try any
            // Small maps (the first sector of a generated map): accept closer points rather than skipping enemies.
            if (point < 0) point = PickPoint(int.MinValue, CloseMinPlayerDistance);
            Enemy enemy = null;
            if (point >= 0)
            {
                Vector3 pos = _points[point].Position;
                Vector3 toPlayer = NearestPlayer(pos) - pos;
                float yaw = toPlayer.sqrMagnitude > 0.01f ? Mathf.Atan2(toPlayer.x, toPlayer.z) * Mathf.Rad2Deg : 0f;
                enemy = manager.Spawn(def, pos, yaw, _plan.HealthScale);
            }
            if (enemy == null)
            {
                if (++_attempts >= MaxAttempts)
                {
                    GameLog.Warn("Waves", $"could not place '{id}' after {MaxAttempts} tries, skipped.");
                    SkipCurrent();
                }
                return false;
            }
            enemy.Alert(); // surge enemies come for the squad, however far away they spawn
            _alive.Add(enemy);
            _next++;
            _attempts = 0;
            return true;
        }

        private void SkipCurrent()
        {
            _next++;
            _attempts = 0;
            Skipped++;
        }

        /// <summary>
        /// Best point in <paramref name="group"/> (any group for int.MinValue): far enough from every player,
        /// hidden from them if possible, and otherwise as close to the action as allowed. A little randomness
        /// among the top three keeps enemies from queueing on a single spot.
        /// </summary>
        private int PickPoint(int group, float minDistance)
        {
            _candidates.Clear();
            _scores.Clear();
            float minSq = minDistance * minDistance;
            for (int i = 0; i < _points.Count; i++)
            {
                SpawnPoint p = _points[i];
                if (group != int.MinValue && p.Group != group) continue;
                float nearestSq = float.MaxValue;
                bool seen = false;
                for (int k = 0; k < _players.Count; k++)
                {
                    float sq = (_players[k] - p.Position).sqrMagnitude;
                    if (sq < nearestSq) nearestSq = sq;
                }
                if (nearestSq < minSq) continue;
                for (int k = 0; k < _players.Count && !seen; k++)
                    seen = !Physics.Linecast(_players[k] + Vector3.up * EyeHeight, p.Position + Vector3.up * ChestHeight,
                        ~(1 << 2), QueryTriggerInteraction.Ignore);
                // Lower is better: distance, plus a large penalty for being in plain view.
                _candidates.Add(i);
                _scores.Add(Mathf.Sqrt(nearestSq) + (seen ? 1000f : 0f));
            }
            if (_candidates.Count == 0) return -1;

            // Partial selection sort for the best three.
            int top = Mathf.Min(3, _candidates.Count);
            for (int a = 0; a < top; a++)
            {
                int best = a;
                for (int b = a + 1; b < _candidates.Count; b++)
                    if (_scores[b] < _scores[best]) best = b;
                (_scores[a], _scores[best]) = (_scores[best], _scores[a]);
                (_candidates[a], _candidates[best]) = (_candidates[best], _candidates[a]);
            }
            // Only pick among the top entries that share the best one's visibility.
            bool bestSeen = _scores[0] >= 1000f;
            int usable = 1;
            while (usable < top && (_scores[usable] >= 1000f) == bestSeen) usable++;
            return _candidates[_rng.Range(0, usable)];
        }

        private void CollectPlayers()
        {
            _players.Clear();
            IReadOnlyList<ICombatTarget> targets = CombatTargets.All;
            for (int i = 0; i < targets.Count; i++)
            {
                ICombatTarget t = targets[i];
                if (t == null || !t.IsTargetable || t.TargetTransform == null) continue;
                _players.Add(t.TargetTransform.position);
            }
        }

        private Vector3 NearestPlayer(Vector3 from)
        {
            Vector3 best = from;
            float bestSq = float.MaxValue;
            foreach (Vector3 p in _players)
            {
                float sq = (p - from).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = p;
                }
            }
            return best;
        }
    }
}
