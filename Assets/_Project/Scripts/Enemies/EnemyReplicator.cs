using System.Collections.Generic;
using Deadfall.Net;
using Deadfall.Rules.Combat;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Deadfall.Enemies
{
    /// <summary>
    /// The compact enemy snapshot channel (ARCHITECTURE §7, DECISIONS T-08). The host runs all AI and sends
    /// every enemy 12 times a second as 12 bytes: slot id, type, position in 2 cm steps, yaw in 256 steps,
    /// state, health %. Clients keep pooled copies (EnemyManager mirror mode) and smooth toward each snapshot.
    /// Snapshots are unreliable (a lost one is replaced 83 ms later); despawns are reliable.
    /// 48 enemies cost about 7 KB/s per client.
    /// </summary>
    public sealed class EnemyReplicator : MonoBehaviour
    {
        private const string MsgSnapshot = "df.en";
        private const string MsgDespawn = "df.engone";
        private const float SendInterval = 1f / 12f;
        private const int BytesPerEnemy = 12;
        /// <summary>Keeps each unreliable message under one packet (about 1.1 KB).</summary>
        private const int MaxPerMessage = 90;
        private const float PositionStep = 0.02f;

        private readonly List<ushort> _despawned = new List<ushort>(32);
        private SessionService _session;
        private EnemyManager _manager;
        private float _nextSend;

        private void OnEnable()
        {
            _session = SessionService.Instance;
            _manager = EnemyManager.Instance;
            _session.Started += OnSessionStarted;
            _session.Stopped += OnSessionStopped;
            _session.RegisterHandler(MsgSnapshot, OnSnapshot);
            _session.RegisterHandler(MsgDespawn, OnDespawnMessage);
            _manager.Despawned += OnEnemyDespawned;
            if (_session.IsActive) OnSessionStarted(_session.Role);
        }

        private void OnDisable()
        {
            if (_manager != null) _manager.Despawned -= OnEnemyDespawned;
            if (_session == null) return;
            _session.Started -= OnSessionStarted;
            _session.Stopped -= OnSessionStopped;
            _session.UnregisterHandler(MsgSnapshot);
            _session.UnregisterHandler(MsgDespawn);
        }

        private void OnSessionStarted(SessionRole role)
        {
            if (_manager != null) _manager.MirrorMode = role == SessionRole.Client;
            _despawned.Clear();
        }

        private void OnSessionStopped()
        {
            if (_manager != null) _manager.MirrorMode = false;
            _despawned.Clear();
        }

        private void OnEnemyDespawned(Enemy e)
        {
            if (_session != null && _session.IsHost && e.SlotId != 0) _despawned.Add(e.SlotId);
        }

        // ---------------------------------------------------------------- host: send

        private void LateUpdate()
        {
            if (_session == null || !_session.IsHost || _manager == null) return;
            if (_session.RemotePeerCount == 0)
            {
                _despawned.Clear();
                return;
            }
            if (_despawned.Count > 0) SendDespawns();
            if (Time.unscaledTime < _nextSend) return;
            _nextSend = Time.unscaledTime + SendInterval;
            SendSnapshots();
        }

        private void SendSnapshots()
        {
            IReadOnlyList<Enemy> active = _manager.Active;
            int total = active.Count;
            for (int start = 0; start < total; start += MaxPerMessage)
            {
                int count = Mathf.Min(MaxPerMessage, total - start);
                using (var w = new FastBufferWriter(8 + count * BytesPerEnemy, Allocator.Temp))
                {
                    w.WriteValueSafe((ushort)count);
                    for (int i = start; i < start + count; i++) WriteEnemy(w, active[i]);
                    _session.SendToRemoteClients(MsgSnapshot, w, NetworkDelivery.UnreliableSequenced);
                }
            }
        }

        private static void WriteEnemy(FastBufferWriter w, Enemy e)
        {
            Vector3 p = e.transform.position;
            w.WriteValueSafe(e.SlotId);
            w.WriteValueSafe((byte)EnemyCatalog.IndexOf(e.Definition));
            w.WriteValueSafe(Quantize(p.x));
            w.WriteValueSafe(Quantize(p.y));
            w.WriteValueSafe(Quantize(p.z));
            w.WriteValueSafe((byte)Mathf.RoundToInt(Mathf.Repeat(e.transform.eulerAngles.y, 360f) / 360f * 256f));
            w.WriteValueSafe((byte)e.State);
            w.WriteValueSafe((byte)Mathf.CeilToInt(e.Health01 * 255f)); // ceil: a living enemy never reads as 0
        }

        private void SendDespawns()
        {
            int count = Mathf.Min(_despawned.Count, 500);
            using (var w = new FastBufferWriter(4 + count * 2, Allocator.Temp))
            {
                w.WriteValueSafe((ushort)count);
                for (int i = 0; i < count; i++) w.WriteValueSafe(_despawned[i]);
                _session.SendToRemoteClients(MsgDespawn, w, NetworkDelivery.ReliableSequenced);
            }
            _despawned.RemoveRange(0, count);
        }

        // ---------------------------------------------------------------- client: receive

        private void OnSnapshot(ulong sender, FastBufferReader r)
        {
            if (_session.IsHost || _manager == null || !_manager.MirrorMode) return;
            r.ReadValueSafe(out ushort count);
            double now = Time.timeAsDouble;
            for (int i = 0; i < count; i++)
            {
                r.ReadValueSafe(out ushort slot);
                r.ReadValueSafe(out byte type);
                r.ReadValueSafe(out short x);
                r.ReadValueSafe(out short y);
                r.ReadValueSafe(out short z);
                r.ReadValueSafe(out byte yawByte);
                r.ReadValueSafe(out byte stateByte);
                r.ReadValueSafe(out byte hp);
                if (slot == 0 || stateByte > (byte)EnemyState.Dead) continue;

                var pos = new Vector3(x * PositionStep, y * PositionStep, z * PositionStep);
                float yaw = yawByte * (360f / 256f);
                Enemy e = _manager.FindBySlot(slot);
                if (e == null)
                {
                    var state = (EnemyState)stateByte;
                    if (state == EnemyState.Dead) continue; // don't create corpses we never saw alive
                    EnemyDefinition def = EnemyCatalog.ByIndex(type);
                    if (def == null) continue;
                    e = _manager.SpawnMirror(def, slot, pos, yaw);
                    if (e == null) continue;
                }
                e.ApplySnapshot(pos, yaw, (EnemyState)stateByte, hp / 255f, now);
            }
        }

        private void OnDespawnMessage(ulong sender, FastBufferReader r)
        {
            if (_session.IsHost || _manager == null) return;
            r.ReadValueSafe(out ushort count);
            for (int i = 0; i < count; i++)
            {
                r.ReadValueSafe(out ushort slot);
                _manager.DespawnSlot(slot);
            }
        }

        private static short Quantize(float v) => (short)Mathf.Clamp(Mathf.RoundToInt(v / PositionStep), short.MinValue, short.MaxValue);
    }

    /// <summary>
    /// Stable numbering of enemy types for the network. Until enemy definitions are assets with a registry
    /// (M3/M6), this is the prototype list in a fixed order; append new types, never reorder.
    /// </summary>
    public static class EnemyCatalog
    {
        public static int IndexOf(EnemyDefinition def)
        {
            IReadOnlyList<EnemyDefinition> all = PrototypeEnemies.All;
            for (int i = 0; i < all.Count; i++) if (all[i] == def) return i;
            return 255;
        }

        public static EnemyDefinition ByIndex(int index)
        {
            IReadOnlyList<EnemyDefinition> all = PrototypeEnemies.All;
            return index >= 0 && index < all.Count ? all[index] : null;
        }
    }
}
