using System;
using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Net;
using Deadfall.Rules.World;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Deadfall.RunSystems
{
    /// <summary>
    /// Host-authoritative pickups. A client asks the host to take a pickup ("df.lootreq"); the host checks it's
    /// still there, marks it taken and tells everyone ("df.loottaken", with who took it); only the taker applies
    /// the item. Offline and on the host it happens at once. Pickup ids come from the map layout, so they match
    /// on every peer. <see cref="SendState"/> brings a late joiner up to date after it builds the map.
    /// </summary>
    public sealed class LootNetwork : MonoBehaviour
    {
        private const string MsgRequest = "df.lootreq";
        private const string MsgTaken = "df.loottaken";
        private const string MsgState = "df.lootstate";

        private static readonly Dictionary<int, LootPickup> Pickups = new Dictionary<int, LootPickup>();
        private static readonly HashSet<int> TakenIds = new HashSet<int>();
        private static LootNetwork _instance;
        private SessionService _session;

        /// <summary>Host/offline: a pickup was taken (kind, taker client id). Used for credit rewards.</summary>
        public static event Action<LootKind, ulong> TakenOnHost;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Pickups.Clear();
            TakenIds.Clear();
            _instance = null;
            TakenOnHost = null;
        }

        private void Awake()
        {
            _instance = this;
            _session = SessionService.Instance;
            _session.RegisterHandler(MsgRequest, OnRequest);
            _session.RegisterHandler(MsgTaken, OnTaken);
            _session.RegisterHandler(MsgState, OnState);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (_session == null) return;
            _session.UnregisterHandler(MsgRequest);
            _session.UnregisterHandler(MsgTaken);
            _session.UnregisterHandler(MsgState);
        }

        public static void Register(LootPickup p)
        {
            if (p == null) return;
            Pickups[p.Id] = p;
            if (TakenIds.Contains(p.Id)) p.MarkTaken();
        }

        public static void Unregister(LootPickup p)
        {
            if (p != null && Pickups.TryGetValue(p.Id, out LootPickup cur) && cur == p) Pickups.Remove(p.Id);
        }

        /// <summary>Forget everything (new map).</summary>
        public static void ClearAll()
        {
            Pickups.Clear();
            TakenIds.Clear();
        }

        private static bool IsClient => _instance != null && _instance._session.IsConnectedClient;
        private static bool IsHost => _instance != null && _instance._session.IsHost;

        public static void RequestTake(LootPickup p)
        {
            if (p == null || p.Taken) return;
            if (IsClient)
            {
                using (var w = new FastBufferWriter(8, Allocator.Temp))
                {
                    w.WriteValueSafe((ushort)p.Id);
                    _instance._session.SendToServer(MsgRequest, w, NetworkDelivery.ReliableSequenced);
                }
                return;
            }
            ulong me = _instance != null ? _instance._session.LocalClientId : 0UL;
            TakeAuthoritative(p.Id, me);
        }

        private static void TakeAuthoritative(int id, ulong taker)
        {
            if (TakenIds.Contains(id) || !Pickups.TryGetValue(id, out LootPickup p) || p == null) return;
            LootKind kind = p.Kind;
            TakenIds.Add(id);
            p.MarkTaken();
            ulong me = _instance != null ? _instance._session.LocalClientId : 0UL;
            if (taker == me || !IsHost) LootPickup.ApplyTo(PlayerIdentity.Local != null ? PlayerIdentity.Local.gameObject : null, kind);
            TakenOnHost?.Invoke(kind, taker);
            if (IsHost)
            {
                using (var w = new FastBufferWriter(16, Allocator.Temp))
                {
                    w.WriteValueSafe((ushort)id);
                    w.WriteValueSafe(taker);
                    w.WriteValueSafe((byte)kind);
                    _instance._session.SendToRemoteClients(MsgTaken, w, NetworkDelivery.ReliableSequenced);
                }
            }
        }

        private void OnRequest(ulong sender, FastBufferReader r)
        {
            if (!_session.IsHost) return;
            r.ReadValueSafe(out ushort id);
            if (!Pickups.TryGetValue(id, out LootPickup p) || p == null) return;
            // The taker must be near it (generous: lag and the 2.6 m reach).
            PlayerIdentity who = PlayerIdentity.Find(sender);
            if (who != null && (who.transform.position - p.transform.position).sqrMagnitude > 6f * 6f) return;
            TakeAuthoritative(id, sender);
        }

        private void OnTaken(ulong sender, FastBufferReader r)
        {
            if (_session.IsHost) return;
            r.ReadValueSafe(out ushort id);
            r.ReadValueSafe(out ulong taker);
            r.ReadValueSafe(out byte kind);
            TakenIds.Add(id);
            if (Pickups.TryGetValue(id, out LootPickup p) && p != null) p.MarkTaken();
            if (taker == _session.LocalClientId && PlayerIdentity.Local != null)
                LootPickup.ApplyTo(PlayerIdentity.Local.gameObject, (LootKind)kind);
        }

        /// <summary>Host: sends the taken list to one peer (after its map message).</summary>
        public static void SendState(ulong clientId)
        {
            if (!IsHost) return;
            using (var w = new FastBufferWriter(8 + TakenIds.Count * 2, Allocator.Temp))
            {
                w.WriteValueSafe((ushort)TakenIds.Count);
                foreach (int id in TakenIds) w.WriteValueSafe((ushort)id);
                _instance._session.SendToClient(MsgState, clientId, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnState(ulong sender, FastBufferReader r)
        {
            if (_session.IsHost) return;
            r.ReadValueSafe(out ushort count);
            for (int i = 0; i < count; i++)
            {
                r.ReadValueSafe(out ushort id);
                TakenIds.Add(id);
                if (Pickups.TryGetValue(id, out LootPickup p) && p != null) p.MarkTaken();
            }
        }
    }
}
