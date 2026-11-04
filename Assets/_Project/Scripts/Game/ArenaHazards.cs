using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Encounters;
using Deadfall.Enemies;
using Deadfall.Net;
using Deadfall.Player;
using Deadfall.Rules.Combat;
using Deadfall.Rules.Common;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deadfall.Game
{
    /// <summary>
    /// Boss arena hazards: while a Specimen or the Broodmother is enraged (<see cref="ArenaHazardRules"/>), the host
    /// picks spots under the players near it and tells every peer ("df.arena"). Each peer shows the telegraph ring,
    /// burst and burning pool and damages only its own player, so health stays owner-authoritative. The host hatches a
    /// Creeper from each of the Broodmother's ruptures.
    /// </summary>
    public sealed class ArenaHazards : MonoBehaviour
    {
        private const string Msg = "df.arena";

        private readonly Dictionary<Enemy, double> _nextVolley = new Dictionary<Enemy, double>();
        private readonly List<Transform> _targets = new List<Transform>(4);
        private readonly List<Enemy> _gone = new List<Enemy>(2);
        private SessionService _session;
        private DeterministicRandom _rng = new DeterministicRandom(0xA9E4AUL);

        private bool IsAuthority => _session == null || _session.Role != SessionRole.Client;

        private void Awake()
        {
            _session = SessionService.Instance;
            _session.RegisterHandler(Msg, OnMessage);
        }

        private void OnDestroy()
        {
            if (_session != null) _session.UnregisterHandler(Msg);
        }

        private void Update()
        {
            if (!IsAuthority) return;
            EnemyManager em = EnemyManager.Current;
            if (em == null) return;
            double now = Time.timeAsDouble;
            IReadOnlyList<Enemy> all = em.Active;
            for (int i = 0; i < all.Count; i++)
            {
                Enemy boss = all[i];
                if (boss == null || !boss.IsAlive || boss.Definition == null || !boss.Definition.IsBoss) continue;
                float interval = ArenaHazardRules.Interval(boss.Phase);
                if (interval <= 0f) continue;
                if (!_nextVolley.TryGetValue(boss, out double at))
                {
                    _nextVolley[boss] = now + 2.5; // a breath after the phase change roar
                    continue;
                }
                if (now < at) continue;
                _nextVolley[boss] = now + interval;
                Volley(boss);
            }
            // Forget bosses that died or were pooled.
            _gone.Clear();
            foreach (KeyValuePair<Enemy, double> kv in _nextVolley)
                if (kv.Key == null || !kv.Key.IsAlive) _gone.Add(kv.Key);
            foreach (Enemy e in _gone) _nextVolley.Remove(e);
        }

        private void Volley(Enemy boss)
        {
            _targets.Clear();
            IReadOnlyList<ICombatTarget> all = CombatTargets.All;
            float rangeSq = ArenaHazardRules.Range * ArenaHazardRules.Range;
            for (int i = 0; i < all.Count; i++)
            {
                ICombatTarget t = all[i];
                if (t?.TargetTransform == null || t.Damageable == null || !t.Damageable.IsAlive) continue;
                if (PlayerIdentity.Of(t.TargetTransform.gameObject) == null) continue;
                if ((t.TargetTransform.position - boss.transform.position).sqrMagnitude > rangeSq) continue;
                _targets.Add(t.TargetTransform);
            }
            int count = ArenaHazardRules.Count(boss.Phase, _targets.Count);
            bool brood = boss.Definition.IsBrood;
            for (int n = 0; n < count && _targets.Count > 0; n++)
            {
                Transform target = _targets[n % _targets.Count];
                float a = _rng.Range(0f, Mathf.PI * 2f);
                float d = n < _targets.Count ? _rng.Range(0f, ArenaHazardRules.Scatter) : _rng.Range(2.5f, 4.5f); // extras land beside them
                Vector3 p = target.position + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                if (NavMesh.SamplePosition(p, out NavMeshHit hit, 2f, NavMesh.AllAreas)) p = hit.position;
                Spawn(p, brood, true);
                Send(p, brood);
            }
        }

        private void Send(Vector3 p, bool brood)
        {
            if (_session == null || !_session.IsHost || _session.RemotePeerCount == 0) return;
            using (var w = new FastBufferWriter(16, Allocator.Temp))
            {
                w.WriteValueSafe(p);
                w.WriteValueSafe(brood ? (byte)1 : (byte)0);
                _session.SendToRemoteClients(Msg, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnMessage(ulong sender, FastBufferReader r)
        {
            if (_session.IsHost) return;
            r.ReadValueSafe(out Vector3 p);
            r.ReadValueSafe(out byte brood);
            Spawn(p, brood == 1, false);
        }

        private void Spawn(Vector3 p, bool brood, bool authority)
        {
            var go = new GameObject(brood ? "Brood rupture" : "Acid rupture");
            go.transform.SetParent(transform, false);
            go.transform.position = p;
            go.AddComponent<RuptureZone>().Init(brood, authority);
        }
    }
}
