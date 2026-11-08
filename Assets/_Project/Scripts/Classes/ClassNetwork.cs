using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Net;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Deadfall.Classes
{
    /// <summary>
    /// Classes in co-op (T-29). Every player tells the host their class and the host shares the roster. Abilities
    /// that put something in the world (shield, sentry, cluster charge) are created by the host, which deals all
    /// the damage and pulls the aggro; everyone else gets a look-alike copy that is removed when the host's goes.
    /// Offline, everything is simply created locally. Named messages as in T-19.
    /// </summary>
    public sealed class ClassNetwork : MonoBehaviour
    {
        private const string MsgClass = "df.cls";       // client → host: my class index
        private const string MsgRoster = "df.roster";   // host → clients: everyone's class
        private const string MsgRequest = "df.abreq";   // client → host: spawn this for me
        private const string MsgSpawn = "df.absp";      // host → clients: a spawn happened (make a copy)
        private const string MsgGone = "df.abgone";     // host → clients: a spawn is gone
        private const string MsgHealth = "df.abhp";     // host → clients: shield / sentry health
        private const float HealthSendInterval = 0.2f;
        private float _healthTimer;
        private static readonly Dictionary<ushort, byte> SentHealth = new Dictionary<ushort, byte>();
        private const float MaxRequestOriginError = 4f;

        private static ClassNetwork _instance;
        private static readonly Dictionary<ushort, IAbilitySpawn> Spawns = new Dictionary<ushort, IAbilitySpawn>();
        private static ushort _nextId = 1;

        private SessionService _session;
        private PlayerClass _localClass;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            Spawns.Clear();
            SentHealth.Clear();
            _nextId = 1;
        }

        private static SessionService Session => SessionService.Current;
        private static bool IsClient => Session != null && Session.Role == SessionRole.Client;
        private static bool IsHost => Session != null && Session.IsHost;
        private static ulong LocalId => Session != null ? Session.LocalClientId : 0UL;

        public void Bind(PlayerClass localClass)
        {
            if (_localClass != null) _localClass.ClassChanged -= OnLocalClassChanged;
            _localClass = localClass;
            if (_localClass != null)
            {
                _localClass.ClassChanged += OnLocalClassChanged;
                OnLocalClassChanged(_localClass.Class);
            }
        }

        private void OnEnable()
        {
            _instance = this;
            _session = SessionService.Instance;
            _session.Started += OnSessionStarted;
            _session.Stopped += OnSessionStopped;
            _session.PeerConnected += OnPeerConnected;
            _session.PeerDisconnected += OnPeerDisconnected;
            _session.RegisterHandler(MsgClass, OnClassMessage);
            _session.RegisterHandler(MsgRoster, OnRosterMessage);
            _session.RegisterHandler(MsgRequest, OnRequestMessage);
            _session.RegisterHandler(MsgSpawn, OnSpawnMessage);
            _session.RegisterHandler(MsgGone, OnGoneMessage);
            _session.RegisterHandler(MsgHealth, OnHealthMessage);
        }

        private void OnDisable()
        {
            if (_instance == this) _instance = null;
            if (_localClass != null) _localClass.ClassChanged -= OnLocalClassChanged;
            if (_session == null) return;
            _session.Started -= OnSessionStarted;
            _session.Stopped -= OnSessionStopped;
            _session.PeerConnected -= OnPeerConnected;
            _session.PeerDisconnected -= OnPeerDisconnected;
            _session.UnregisterHandler(MsgClass);
            _session.UnregisterHandler(MsgHealth);
            _session.UnregisterHandler(MsgRoster);
            _session.UnregisterHandler(MsgRequest);
            _session.UnregisterHandler(MsgSpawn);
            _session.UnregisterHandler(MsgGone);
        }

        // ---------------------------------------------------------------- roster

        private void OnLocalClassChanged(ClassDefinition c)
        {
            ClassRoster.Set(LocalId, c != null ? c.ContentId : null);
            if (IsClient && _session.IsConnectedClient) SendClass();
            else if (IsHost) BroadcastRoster(ulong.MaxValue);
        }

        private void SendClass()
        {
            using (var w = new FastBufferWriter(4, Allocator.Temp))
            {
                w.WriteValueSafe(ClassIndex(_localClass != null ? _localClass.Class : null));
                _session.SendToServer(MsgClass, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnSessionStarted(SessionRole role)
        {
            // Ids change when joining (offline we were 0), so start the roster over with just ourselves.
            ClassRoster.Clear();
            if (_localClass != null) ClassRoster.Set(LocalId, _localClass.Class != null ? _localClass.Class.ContentId : null);
            if (role == SessionRole.Client) SendClass();
        }

        private void OnSessionStopped()
        {
            ClassRoster.Clear();
            if (_localClass != null && _localClass.Class != null) ClassRoster.Set(0UL, _localClass.Class.ContentId);
            // Copies of the host's spawns go with the session.
            var mirrors = new List<IAbilitySpawn>();
            foreach (IAbilitySpawn s in Spawns.Values)
                if (s is Component c && c != null && IsMirror(s)) mirrors.Add(s);
            foreach (IAbilitySpawn s in mirrors) Destroy(((Component)s).gameObject);
        }

        private void OnPeerConnected(ulong clientId) => BroadcastRoster(clientId);

        private void OnPeerDisconnected(ulong clientId)
        {
            ClassRoster.Remove(clientId);
            BroadcastRoster(ulong.MaxValue);
        }

        private void OnClassMessage(ulong sender, FastBufferReader r)
        {
            if (!IsHost) return;
            r.ReadValueSafe(out byte index);
            ClassRoster.Set(sender, ClassAt(index)?.ContentId);
            BroadcastRoster(ulong.MaxValue);
        }

        private void BroadcastRoster(ulong onlyTo)
        {
            if (!IsHost || _session.RemotePeerCount == 0) return;
            int count = Mathf.Min(16, ClassRoster.All.Count);
            using (var w = new FastBufferWriter(4 + count * 9, Allocator.Temp))
            {
                w.WriteValueSafe((byte)count);
                int n = 0;
                foreach (KeyValuePair<ulong, string> kv in ClassRoster.All)
                {
                    if (n++ >= count) break;
                    w.WriteValueSafe(kv.Key);
                    w.WriteValueSafe(ClassIndex(PrototypeClasses.ById(kv.Value)));
                }
                if (onlyTo == ulong.MaxValue) _session.SendToRemoteClients(MsgRoster, w, NetworkDelivery.ReliableSequenced);
                else _session.SendToClient(MsgRoster, onlyTo, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnRosterMessage(ulong sender, FastBufferReader r)
        {
            if (!IsClient) return;
            r.ReadValueSafe(out byte count);
            var seen = new HashSet<ulong>();
            for (int i = 0; i < count; i++)
            {
                r.ReadValueSafe(out ulong id);
                r.ReadValueSafe(out byte index);
                seen.Add(id);
                if (id == LocalId) continue; // our own entry is ours to set
                ClassRoster.Set(id, ClassAt(index)?.ContentId);
            }
            var stale = new List<ulong>();
            foreach (ulong id in ClassRoster.All.Keys)
                if (id != LocalId && !seen.Contains(id)) stale.Add(id);
            foreach (ulong id in stale) ClassRoster.Remove(id);
        }

        private static byte ClassIndex(ClassDefinition c)
        {
            var all = PrototypeClasses.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] == c) return (byte)i;
            return byte.MaxValue;
        }

        private static ClassDefinition ClassAt(byte index) =>
            index < PrototypeClasses.All.Count ? PrototypeClasses.All[index] : null;

        // ---------------------------------------------------------------- spawns

        /// <summary>
        /// The local player's ability wants <paramref name="kind"/> at <paramref name="position"/> facing
        /// <paramref name="direction"/> (for a charge: thrown from the eye along the aim). Created here offline or
        /// on the host; sent to the host from a client.
        /// </summary>
        public static void RequestSpawn(SpawnKind kind, Vector3 position, Vector3 direction)
        {
            if (IsClient)
            {
                if (_instance == null || !Session.IsConnectedClient) return;
                using (var w = new FastBufferWriter(32, Allocator.Temp))
                {
                    w.WriteValueSafe((byte)kind);
                    w.WriteValueSafe(position);
                    w.WriteValueSafe(direction);
                    Session.SendToServer(MsgRequest, w, NetworkDelivery.ReliableSequenced);
                }
                return;
            }
            PlayerIdentity me = PlayerIdentity.Local;
            SpawnAuthoritative(kind, position, direction, me != null ? me.gameObject : null, LocalId);
        }

        private void OnRequestMessage(ulong sender, FastBufferReader r)
        {
            if (!IsHost) return;
            r.ReadValueSafe(out byte kindByte);
            r.ReadValueSafe(out Vector3 position);
            r.ReadValueSafe(out Vector3 direction);
            if (kindByte > (byte)SpawnKind.ClusterCharge || !Finite(position) || !Finite(direction)) return;
            PlayerIdentity owner = PlayerIdentity.Find(sender);
            if (owner == null) return;
            // The spawn must be near the player who asked for it.
            if ((position - owner.transform.position).sqrMagnitude > (MaxRequestOriginError + 3f) * (MaxRequestOriginError + 3f)) return;
            SpawnAuthoritative((SpawnKind)kindByte, position, direction, owner.gameObject, sender);
        }

        private static void SpawnAuthoritative(SpawnKind kind, Vector3 position, Vector3 direction, GameObject owner, ulong ownerId)
        {
            ushort id = _nextId++;
            if (_nextId == 0) _nextId = 1;
            IAbilitySpawn s = Create(kind, position, direction, owner, ownerId, false, id);
            if (s == null) return;
            if (!IsHost || _instance == null || Session.RemotePeerCount == 0) return;
            using (var w = new FastBufferWriter(48, Allocator.Temp))
            {
                w.WriteValueSafe(id);
                w.WriteValueSafe((byte)kind);
                w.WriteValueSafe(position);
                w.WriteValueSafe(direction);
                w.WriteValueSafe(ownerId);
                Session.SendToRemoteClients(MsgSpawn, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnSpawnMessage(ulong sender, FastBufferReader r)
        {
            if (!IsClient) return;
            r.ReadValueSafe(out ushort id);
            r.ReadValueSafe(out byte kindByte);
            r.ReadValueSafe(out Vector3 position);
            r.ReadValueSafe(out Vector3 direction);
            r.ReadValueSafe(out ulong ownerId);
            if (kindByte > (byte)SpawnKind.ClusterCharge || Spawns.ContainsKey(id)) return;
            PlayerIdentity owner = PlayerIdentity.Find(ownerId);
            Create((SpawnKind)kindByte, position, direction, owner != null ? owner.gameObject : null, ownerId, true, id);
        }

        /// <summary>Host: sends the health of shields and sentries that changed (a few times a second), so mirrors show damage.</summary>
        private void Update()
        {
            if (!IsHost || Session.RemotePeerCount == 0) return;
            _healthTimer -= Time.unscaledDeltaTime;
            if (_healthTimer > 0f) return;
            _healthTimer = HealthSendInterval;
            foreach (KeyValuePair<ushort, IAbilitySpawn> kv in Spawns)
            {
                if (!(kv.Value is IAbilityHealth h) || IsMirror(kv.Value)) continue;
                byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(h.Health01 * 255f), 0, 255);
                if (SentHealth.TryGetValue(kv.Key, out byte last) && last == b) continue;
                SentHealth[kv.Key] = b;
                using (var w = new FastBufferWriter(4, Allocator.Temp))
                {
                    w.WriteValueSafe(kv.Key);
                    w.WriteValueSafe(b);
                    Session.SendToRemoteClients(MsgHealth, w, NetworkDelivery.UnreliableSequenced);
                }
            }
        }

        private void OnHealthMessage(ulong sender, FastBufferReader r)
        {
            if (!IsClient) return;
            r.ReadValueSafe(out ushort id);
            r.ReadValueSafe(out byte health);
            if (Spawns.TryGetValue(id, out IAbilitySpawn s) && s is IAbilityHealth h) h.SetMirrorHealth01(health / 255f);
        }

        private void OnGoneMessage(ulong sender, FastBufferReader r)
        {
            if (!IsClient) return;
            r.ReadValueSafe(out ushort id);
            if (Spawns.TryGetValue(id, out IAbilitySpawn s) && s is Component c && c != null) Destroy(c.gameObject);
        }

        /// <summary>Spawned objects call this from OnDestroy.</summary>
        public static void NotifyGone(IAbilitySpawn s)
        {
            if (s == null || !Spawns.TryGetValue(s.NetId, out IAbilitySpawn known) || !ReferenceEquals(known, s)) return;
            Spawns.Remove(s.NetId);
            SentHealth.Remove(s.NetId);

            // The owner's shield or sentry is gone (broken, destroyed or expired): its ability goes on cooldown now.
            if (s.OwnerClientId == LocalId && !(s is ClusterCharge) && PlayerClass.Local != null) PlayerClass.Local.EndAbilityEarly();

            bool mirror = Mirrors.Remove(s);
            if (mirror || !IsHost || _instance == null || Session.RemotePeerCount == 0) return;
            using (var w = new FastBufferWriter(4, Allocator.Temp))
            {
                w.WriteValueSafe(s.NetId);
                Session.SendToRemoteClients(MsgGone, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private static readonly HashSet<IAbilitySpawn> Mirrors = new HashSet<IAbilitySpawn>();
        private static bool IsMirror(IAbilitySpawn s) => Mirrors.Contains(s);

        /// <summary>Builds the object with numbers from the class data. Mirrors look the same but do nothing.</summary>
        private static IAbilitySpawn Create(SpawnKind kind, Vector3 position, Vector3 direction, GameObject owner, ulong ownerId, bool mirror, ushort id)
        {
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            Quaternion facing = flat.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(flat) : Quaternion.identity;
            IAbilitySpawn s = null;
            switch (kind)
            {
                case SpawnKind.Shield:
                {
                    AbilityDefinition a = AbilityOf("class.tank");
                    s = ShieldWall.Create(position, facing, a.Magnitude, a.Range, a.Secondary > 0f ? a.Secondary : 3f, a.DurationSeconds,
                        owner, ownerId, mirror);
                    break;
                }
                case SpawnKind.Sentry:
                {
                    AbilityDefinition a = AbilityOf("class.engineer");
                    // Magnitude: damage per shot. Secondary: shots per second. Range: range.
                    s = SentryTurret.Create(position, facing, a.DurationSeconds, a.Range > 0f ? a.Range : 22f, a.Magnitude,
                        a.Secondary > 0f ? a.Secondary : 6f, 300f, owner, ownerId, mirror);
                    break;
                }
                case SpawnKind.ClusterCharge:
                {
                    AbilityDefinition a = AbilityOf("class.demolitionist");
                    float self = SelfBlastMultiplier(ownerId);
                    // Magnitude: bomblets. Secondary: main blast damage. Range: main blast radius.
                    s = ClusterCharge.Throw(position, direction * 13f + Vector3.up * 3f, Mathf.RoundToInt(a.Magnitude),
                        a.Secondary > 0f ? a.Secondary : 90f, a.Range > 0f ? a.Range : 4.5f, 45f, 3f, self, owner, ownerId, mirror);
                    break;
                }
            }
            if (s == null) return null;
            s.NetId = id;
            Spawns[id] = s;
            if (mirror) Mirrors.Add(s);
            return s;
        }

        /// <summary>Self-damage multiplier for the thrower: the Demolitionist's Blast Padding cuts it.</summary>
        /// <summary>Share of their own explosive damage a player takes (Blast Padding halves it).</summary>
        public static float SelfBlastMultiplier(ulong ownerId)
        {
            ClassDefinition c = PrototypeClasses.ById(ClassRoster.ClassOf(ownerId));
            if (c == null) return 1f;
            foreach (PassiveDefinition p in c.Passives)
                if (p != null && p.BehaviourId == "self_blast_reduction") return Mathf.Clamp01(1f - p.Magnitude);
            return 1f;
        }

        private static AbilityDefinition AbilityOf(string classId) => PrototypeClasses.ById(classId).Ability;

        private static bool Finite(Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
    }
}
