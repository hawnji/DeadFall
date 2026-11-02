using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Encounters;
using Deadfall.Net;
using Deadfall.Rules.Common;
using Deadfall.Rules.Encounters;
using Deadfall.Rules.World;
using Deadfall.RunSystems;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deadfall.Game
{
    /// <summary>
    /// Supply surges (GAME_DESIGN §8): when one starts, the host drops ammo crates and medkits on the NavMesh
    /// 8-20 m from the squad and tells clients where ("df.supply"). They are ordinary host-authoritative loot
    /// pickups, so only one player gets each. Crates stay until taken, the next supply surge, or the run ends.
    /// </summary>
    public sealed class SupplyDrops : MonoBehaviour
    {
        private const string Msg = "df.supply";
        /// <summary>Pickup ids 50000+ never clash with map loot (room*16+slot stays far below).</summary>
        public const int FirstId = 50000;

        private readonly List<LootPickup> _crates = new List<LootPickup>(8);
        private readonly List<Vector3> _players = new List<Vector3>(4);
        private SessionService _session;
        private WaveManager _waves;

        private void Awake()
        {
            _session = SessionService.Instance;
            _session.RegisterHandler(Msg, OnMessage);
        }

        private void OnDestroy()
        {
            if (_session != null) _session.UnregisterHandler(Msg);
            Bind(null);
        }

        private void Update()
        {
            if (WaveManager.Current != _waves) Bind(WaveManager.Current);
        }

        private void Bind(WaveManager w)
        {
            if (_waves != null)
            {
                _waves.SurgePlanned -= OnSurgePlanned;
                _waves.PhaseChanged -= OnPhaseChanged;
            }
            _waves = w;
            if (_waves != null)
            {
                _waves.SurgePlanned += OnSurgePlanned;
                _waves.PhaseChanged += OnPhaseChanged;
            }
        }

        private void OnPhaseChanged(RunPhase from, RunPhase to)
        {
            if (to == RunPhase.NotStarted || to == RunPhase.Complete || to == RunPhase.Failed) Clear();
        }

        private void OnSurgePlanned(SurgePlan plan)
        {
            if (plan == null || plan.SupplyCrates <= 0) return;
            Clear();
            CollectPlayers();
            if (_players.Count == 0) return;
            var rng = new DeterministicRandom(_waves.Seed ^ ((ulong)plan.Surge * 0xA24BAED4963EE407UL));
            var ids = new List<ushort>();
            var kinds = new List<byte>();
            var spots = new List<Vector3>();
            for (int i = 0; i < plan.SupplyCrates; i++)
            {
                if (!TryPickSpot(rng, out Vector3 pos)) continue;
                ushort id = (ushort)(FirstId + (plan.Surge % 64) * 8 + i);
                LootKind kind = i % 2 == 0 ? LootKind.AmmoCrate : LootKind.Medkit;
                Spawn(id, kind, pos);
                ids.Add(id);
                kinds.Add((byte)kind);
                spots.Add(pos);
            }
            if (ids.Count == 0) return;
            RunToasts.Show($"{ids.Count} supply crates dropped nearby", new Color(0.6f, 0.85f, 1f));
            if (_session == null || !_session.IsHost || _session.RemotePeerCount == 0) return;
            using (var w = new FastBufferWriter(4 + ids.Count * 16, Allocator.Temp))
            {
                w.WriteValueSafe((byte)ids.Count);
                for (int i = 0; i < ids.Count; i++)
                {
                    w.WriteValueSafe(ids[i]);
                    w.WriteValueSafe(kinds[i]);
                    w.WriteValueSafe(spots[i]);
                }
                _session.SendToRemoteClients(Msg, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private bool TryPickSpot(DeterministicRandom rng, out Vector3 pos)
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                Vector3 around = _players[rng.Range(0, _players.Count)];
                float angle = rng.Range(0f, Mathf.PI * 2f);
                float dist = rng.Range(8f, 20f);
                Vector3 guess = around + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
                if (!NavMesh.SamplePosition(guess, out NavMeshHit hit, 4f, NavMesh.AllAreas)) continue;
                pos = hit.position;
                return true;
            }
            pos = default;
            return false;
        }

        private void OnMessage(ulong sender, FastBufferReader r)
        {
            if (_session.IsHost) return;
            Clear();
            r.ReadValueSafe(out byte count);
            for (int i = 0; i < count; i++)
            {
                r.ReadValueSafe(out ushort id);
                r.ReadValueSafe(out byte kind);
                r.ReadValueSafe(out Vector3 pos);
                if (id < FirstId || kind > (byte)LootKind.SecretStash) continue;
                Spawn(id, (LootKind)kind, pos);
            }
            if (count > 0) RunToasts.Show($"{count} supply crates dropped nearby", new Color(0.6f, 0.85f, 1f));
        }

        private void Spawn(ushort id, LootKind kind, Vector3 pos) => _crates.Add(LootPickup.Create(id, kind, pos, 0f, transform));

        private void Clear()
        {
            foreach (LootPickup p in _crates)
                if (p != null) Destroy(p.gameObject);
            _crates.Clear();
        }

        private void CollectPlayers()
        {
            _players.Clear();
            IReadOnlyList<ICombatTarget> all = CombatTargets.All;
            for (int i = 0; i < all.Count; i++)
            {
                ICombatTarget t = all[i];
                if (t?.TargetTransform == null || PlayerIdentity.Of(t.TargetTransform.gameObject) == null) continue;
                _players.Add(t.TargetTransform.position);
            }
        }
    }
}
