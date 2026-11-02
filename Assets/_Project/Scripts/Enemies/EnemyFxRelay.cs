using Deadfall.Net;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Deadfall.Enemies
{
    /// <summary>
    /// Replays enemy attack visuals (acid globs, pools, Pyre blasts) on co-op clients ("df.efx"). Damage stays on the
    /// host; clients see the same arc and pool so they can dodge. About 30 bytes per effect, unreliable.
    /// </summary>
    public sealed class EnemyFxRelay : MonoBehaviour
    {
        private const string Msg = "df.efx";
        private SessionService _session;

        private void OnEnable()
        {
            _session = SessionService.Instance;
            _session.RegisterHandler(Msg, OnFx);
            EnemyAttacks.FxPlayed += OnFxPlayed;
        }

        private void OnDisable()
        {
            EnemyAttacks.FxPlayed -= OnFxPlayed;
            if (_session != null) _session.UnregisterHandler(Msg);
        }

        private void OnFxPlayed(EnemyFxKind kind, Vector3 a, Vector3 b, float radius, float seconds)
        {
            if (_session == null || !_session.IsHost || _session.RemotePeerCount == 0) return;
            using (var w = new FastBufferWriter(40, Allocator.Temp))
            {
                w.WriteValueSafe((byte)kind);
                w.WriteValueSafe(a);
                w.WriteValueSafe(b);
                w.WriteValueSafe(radius);
                w.WriteValueSafe(seconds);
                _session.SendToRemoteClients(Msg, w, NetworkDelivery.Unreliable);
            }
        }

        private void OnFx(ulong sender, FastBufferReader r)
        {
            if (_session.IsHost) return;
            r.ReadValueSafe(out byte kind);
            r.ReadValueSafe(out Vector3 a);
            r.ReadValueSafe(out Vector3 b);
            r.ReadValueSafe(out float radius);
            r.ReadValueSafe(out float seconds);
            if (kind > (byte)EnemyFxKind.Blast) return;
            EnemyAttacks.PlayRemote((EnemyFxKind)kind, a, b, Mathf.Clamp(radius, 0f, 10f), Mathf.Clamp(seconds, 0f, 15f));
        }
    }
}
