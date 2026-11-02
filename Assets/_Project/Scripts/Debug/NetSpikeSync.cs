using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Enemies;
using Deadfall.Net;
using Deadfall.Player;
using Deadfall.Rules.Combat;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Deadfall.DebugTools
{
    /// <summary>
    /// Milestone 1.5 networking spike for the sandbox. Proves the authority model from ARCHITECTURE.md
    /// with the smallest amount of code:
    /// - player movement is owner-authoritative: each peer sends its own position and view 20 times a second
    ///   and everyone else draws a smoothed proxy;
    /// - hits are client-claimed and host-validated: a client sends a claim, the host checks it, applies
    ///   damage with its own weapon stats, confirms the hit to the shooter and broadcasts target health;
    /// - tracers are cosmetic and relayed.
    /// Everything uses named messages through <see cref="SessionService"/>; no network prefabs (T-19).
    /// Production replication (players as NetworkObjects, enemy snapshots) replaces this in M2/M11.
    /// </summary>
    public sealed class NetSpikeSync : MonoBehaviour, IHitResolver
    {
        private const string MsgPlayerState = "df.ps";
        private const string MsgShots = "df.shots";
        private const string MsgHitClaim = "df.hit";
        private const string MsgHitConfirm = "df.hitok";
        private const string MsgTargets = "df.tgt";
        private const string MsgPlayerLeft = "df.left";
        private const string MsgPlayerDamage = "df.pdmg";
        private const byte TargetDummy = 0;
        private const byte TargetEnemy = 1;
        private const byte TargetWorld = 2;
        private const float EnemyPointSlack = 3f; // enemy copies trail the host's by up to ~150 ms
        private const string QuickMeleeId = "melee.quick";

        private const float StateInterval = 0.05f; // 20 Hz
        private const int MaxShotsPerMessage = 16;
        private const float MaxOriginError = 4f;     // metres between claimed origin and the shooter's last known eye position
        private const float MaxPointError = 2.5f;    // metres between claimed hit point and the target's centre
        private const float ClaimsPerSecond = 60f;   // generous: 9-pellet shotgun or full-auto rifle plus melee
        private const float MaxClaimedOutgoing = 3f; // largest class bonus a claim may carry (Warrior Lunge strike ×3)
        private const float MaxClaimedKnockback = 4f; // metres (Tank Shove)

        private sealed class ClaimBudget
        {
            public float Tokens = ClaimsPerSecond;
            public float LastTime;
        }

        private readonly Dictionary<ulong, RemotePlayerProxy> _proxies = new Dictionary<ulong, RemotePlayerProxy>();
        private readonly Dictionary<ulong, ClaimBudget> _budgets = new Dictionary<ulong, ClaimBudget>();
        private readonly Dictionary<string, WeaponDefinition> _weapons = new Dictionary<string, WeaponDefinition>();
        private readonly List<Vector3> _pendingShots = new List<Vector3>(MaxShotsPerMessage * 2);
        private readonly MeleeStats _quickMelee = new MeleeStats();

        private SessionService _session;
        private GameObject _player;
        private PlayerLook _look;
        private PlayerMotor _motor;
        private PlayerStatus _status;
        private PlayerHealth _health;
        private IHitResolver _offlineResolver;
        private float _nextStateTime;

        public SessionService Session => _session;
        public int ProxyCount => _proxies.Count;

        public void Bind(GameObject player)
        {
            _player = player;
            _look = player.GetComponent<PlayerLook>();
            _motor = player.GetComponent<PlayerMotor>();
            _status = player.GetComponent<PlayerStatus>();
            _health = player.GetComponent<PlayerHealth>();
        }

        private void Awake()
        {
            foreach (WeaponDefinition def in WeaponCatalog.All) _weapons[def.ContentId] = def;
            _session = SessionService.Instance;
        }

        private void OnEnable()
        {
            _session = SessionService.Instance;
            _session.Started += OnSessionStarted;
            _session.Stopped += OnSessionStopped;
            _session.PeerConnected += OnPeerConnected;
            _session.PeerDisconnected += OnPeerDisconnected;
            _session.RegisterHandler(MsgPlayerState, OnPlayerState);
            _session.RegisterHandler(MsgShots, OnShots);
            _session.RegisterHandler(MsgHitClaim, OnHitClaim);
            _session.RegisterHandler(MsgHitConfirm, OnHitConfirm);
            _session.RegisterHandler(MsgTargets, OnTargets);
            _session.RegisterHandler(MsgPlayerLeft, OnPlayerLeft);
            _session.RegisterHandler(MsgPlayerDamage, OnPlayerDamage);
            EventBus<ShotTraced>.Subscribe(OnLocalShot);
            foreach (TrainingDummy d in TrainingDummy.All)
                if (d != null) d.HealthChanged += OnDummyHealthChanged;
        }

        private void OnDisable()
        {
            EventBus<ShotTraced>.Unsubscribe(OnLocalShot);
            foreach (TrainingDummy d in TrainingDummy.All)
                if (d != null) d.HealthChanged -= OnDummyHealthChanged;
            if (_session == null) return;
            _session.Started -= OnSessionStarted;
            _session.Stopped -= OnSessionStopped;
            _session.PeerConnected -= OnPeerConnected;
            _session.PeerDisconnected -= OnPeerDisconnected;
            _session.UnregisterHandler(MsgPlayerState);
            _session.UnregisterHandler(MsgShots);
            _session.UnregisterHandler(MsgHitClaim);
            _session.UnregisterHandler(MsgHitConfirm);
            _session.UnregisterHandler(MsgTargets);
            _session.UnregisterHandler(MsgPlayerLeft);
            _session.UnregisterHandler(MsgPlayerDamage);
            // Leaving the sandbox ends the spike session.
            _session.Stop();
        }

        // ---------------------------------------------------------------- session lifecycle

        private void OnSessionStarted(SessionRole role)
        {
            _offlineResolver = CombatServices.Resolver;
            if (role == SessionRole.Client)
            {
                // Clients stop applying damage themselves: claims go to the host, health comes back.
                ServiceRegistry.Register<IHitResolver>(this);
                foreach (TrainingDummy d in TrainingDummy.All)
                    if (d != null) d.NetworkMirrored = true;
            }
            // The host keeps the LocalHitResolver: its own hits apply directly and dummy health is broadcast.
            _nextStateTime = 0f;
        }

        private void OnSessionStopped()
        {
            ServiceRegistry.Register(_offlineResolver ?? new LocalHitResolver());
            foreach (TrainingDummy d in TrainingDummy.All)
            {
                if (d == null) continue;
                d.NetworkMirrored = false;
                d.SetNetworkHealth(d.MaxHealth);
            }
            foreach (RemotePlayerProxy p in _proxies.Values)
                if (p != null) Destroy(p.gameObject);
            _proxies.Clear();
            _budgets.Clear();
            _pendingShots.Clear();
        }

        private void OnPeerConnected(ulong clientId)
        {
            SendFullTargetState(clientId);
            _nextStateTime = 0f; // send our own state right away
        }

        private void OnPeerDisconnected(ulong clientId)
        {
            RemoveProxy(clientId);
            _budgets.Remove(clientId);
            using (var w = new FastBufferWriter(16, Allocator.Temp))
            {
                w.WriteValueSafe(clientId);
                _session.SendToRemoteClients(MsgPlayerLeft, w, NetworkDelivery.ReliableSequenced);
            }
        }

        // ---------------------------------------------------------------- per frame

        private void Update()
        {
            float dt = Time.deltaTime;
            foreach (RemotePlayerProxy p in _proxies.Values)
                if (p != null) p.Tick(dt);

            if (!CanSend() || _player == null) return;
            if (Time.unscaledTime < _nextStateTime) return;
            _nextStateTime = Time.unscaledTime + StateInterval;
            SendLocalState();
        }

        private void LateUpdate()
        {
            if (_pendingShots.Count == 0) return;
            if (!CanSend())
            {
                _pendingShots.Clear();
                return;
            }
            SendShots(_session.LocalClientId, _pendingShots, ulong.MaxValue);
            _pendingShots.Clear();
        }

        private bool CanSend() => _session != null && (_session.IsHost || _session.IsConnectedClient);

        // ---------------------------------------------------------------- player state

        private void SendLocalState()
        {
            Vector3 pos = _player.transform.position;
            float yaw = _look != null ? _look.Yaw : _player.transform.eulerAngles.y;
            float pitch = _look != null ? _look.Pitch : 0f;
            byte flags = 0;
            if (_motor != null && _motor.IsCrouched) flags |= 1;
            if (_status != null && _status.IsDead) flags |= 2;
            float health01 = _health != null ? _health.Normalized : 1f;
            WriteState(_session.LocalClientId, pos, yaw, pitch, flags, health01, ulong.MaxValue);
        }

        private void WriteState(ulong id, Vector3 pos, float yaw, float pitch, byte flags, float health01, ulong except)
        {
            using (var w = new FastBufferWriter(64, Allocator.Temp))
            {
                w.WriteValueSafe(id);
                w.WriteValueSafe(pos);
                w.WriteValueSafe(yaw);
                w.WriteValueSafe(pitch);
                w.WriteValueSafe(flags);
                w.WriteValueSafe(health01);
                if (_session.IsHost) _session.SendToRemoteClients(MsgPlayerState, w, NetworkDelivery.UnreliableSequenced, except);
                else _session.SendToServer(MsgPlayerState, w, NetworkDelivery.UnreliableSequenced);
            }
        }

        private void OnPlayerState(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out ulong id);
            r.ReadValueSafe(out Vector3 pos);
            r.ReadValueSafe(out float yaw);
            r.ReadValueSafe(out float pitch);
            r.ReadValueSafe(out byte flags);
            r.ReadValueSafe(out float health01);
            if (!IsFinite(pos) || !IsFinite(yaw) || !IsFinite(pitch)) return;

            if (_session.IsHost)
            {
                id = sender; // never trust the id a client writes about itself
                WriteState(id, pos, yaw, pitch, flags, health01, sender); // relay to everyone else
            }
            if (id == _session.LocalClientId) return;
            GetOrCreateProxy(id).SetTarget(pos, yaw, pitch, (flags & 1) != 0, (flags & 2) != 0, health01);
        }

        private void OnPlayerLeft(ulong sender, FastBufferReader r)
        {
            if (_session.IsHost) return;
            r.ReadValueSafe(out ulong id);
            RemoveProxy(id);
        }

        private RemotePlayerProxy GetOrCreateProxy(ulong id)
        {
            if (_proxies.TryGetValue(id, out RemotePlayerProxy proxy) && proxy != null) return proxy;
            proxy = RemotePlayerProxy.Create(id, transform);
            proxy.DamageRequested += OnProxyDamaged;
            _proxies[id] = proxy;
            return proxy;
        }

        private void RemoveProxy(ulong id)
        {
            if (_proxies.TryGetValue(id, out RemotePlayerProxy proxy) && proxy != null) Destroy(proxy.gameObject);
            _proxies.Remove(id);
        }

        // ---------------------------------------------------------------- tracers

        private void OnLocalShot(ShotTraced e)
        {
            if (e.Owner != _player || !CanSend()) return;
            if (_pendingShots.Count >= MaxShotsPerMessage * 2) return;
            _pendingShots.Add(e.From);
            _pendingShots.Add(e.To);
        }

        private void SendShots(ulong id, List<Vector3> points, ulong except)
        {
            int count = Mathf.Min(points.Count / 2, MaxShotsPerMessage);
            if (count == 0) return;
            using (var w = new FastBufferWriter(16 + count * 24, Allocator.Temp))
            {
                w.WriteValueSafe(id);
                w.WriteValueSafe((byte)count);
                for (int i = 0; i < count * 2; i++) w.WriteValueSafe(points[i]);
                if (_session.IsHost) _session.SendToRemoteClients(MsgShots, w, NetworkDelivery.UnreliableSequenced, except);
                else _session.SendToServer(MsgShots, w, NetworkDelivery.UnreliableSequenced);
            }
        }

        private readonly List<Vector3> _relayShots = new List<Vector3>(MaxShotsPerMessage * 2);

        private void OnShots(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out ulong id);
            r.ReadValueSafe(out byte count);
            if (_session.IsHost) id = sender;
            _relayShots.Clear();
            for (int i = 0; i < Mathf.Min(count, (byte)MaxShotsPerMessage); i++)
            {
                r.ReadValueSafe(out Vector3 from);
                r.ReadValueSafe(out Vector3 to);
                if (!IsFinite(from) || !IsFinite(to)) return;
                _relayShots.Add(from);
                _relayShots.Add(to);
            }
            if (_session.IsHost) SendShots(id, _relayShots, sender);
            if (id == _session.LocalClientId) return;
            for (int i = 0; i + 1 < _relayShots.Count; i += 2) TracerPool.Instance.Spawn(_relayShots[i], _relayShots[i + 1]);
        }

        // ---------------------------------------------------------------- hits (client side)

        // ---------------------------------------------------------------- enemy damage to remote players

        /// <summary>Host: a host enemy hit a remote player's proxy; tell that player's machine.</summary>
        private void OnProxyDamaged(RemotePlayerProxy proxy, float amount, Vector3 direction)
        {
            if (!_session.IsHost) return;
            using (var w = new FastBufferWriter(32, Allocator.Temp))
            {
                w.WriteValueSafe(amount);
                w.WriteValueSafe(direction);
                _session.SendToClient(MsgPlayerDamage, proxy.ClientId, w, NetworkDelivery.ReliableSequenced);
            }
        }

        /// <summary>Client: the host says an enemy hit us. Our own health applies it (with our damage reduction).</summary>
        private void OnPlayerDamage(ulong sender, FastBufferReader r)
        {
            if (_session.IsHost || _health == null) return;
            r.ReadValueSafe(out float amount);
            r.ReadValueSafe(out Vector3 direction);
            if (!IsFinite(amount) || amount <= 0f || amount > 1000f || !IsFinite(direction)) return;
            _health.ApplyRawDamage(amount, direction);
        }

        // ---------------------------------------------------------------- hits (client side)

        /// <summary>Client resolver: forwards claims on dummies and enemies to the host. Nothing is applied locally.</summary>
        public void Submit(in HitClaim claim)
        {
            if (!_session.IsConnectedClient) return;
            byte kind;
            ushort id;
            if (claim.Target is TrainingDummy dummy && dummy.NetworkIndex >= 0 && dummy.NetworkIndex <= ushort.MaxValue)
            {
                kind = TargetDummy;
                id = (ushort)dummy.NetworkIndex;
            }
            else if (claim.Target is Enemy enemy && enemy.SlotId != 0)
            {
                kind = TargetEnemy;
                id = enemy.SlotId;
            }
            else if (claim.Target is INetTarget world)
            {
                kind = TargetWorld;
                id = world.NetTargetId;
            }
            else
            {
                return;
            }
            using (var w = new FastBufferWriter(96 + (claim.WeaponId?.Length ?? 0) * 2, Allocator.Temp))
            {
                w.WriteValueSafe(kind);
                w.WriteValueSafe(id);
                w.WriteValueSafe((byte)claim.Zone);
                w.WriteValueSafe(claim.WeakPointMultiplier);
                w.WriteValueSafe(claim.Origin);
                w.WriteValueSafe(claim.Point);
                w.WriteValueSafe(claim.WeaponId ?? string.Empty);
                byte claimFlags = 0;
                if (claim.IsMelee) claimFlags |= 1;
                if (claim.ForceStagger) claimFlags |= 2;
                w.WriteValueSafe(claimFlags);
                w.WriteValueSafe(claim.Request.OutgoingMultiplier);
                w.WriteValueSafe(claim.Knockback);
                _session.SendToServer(MsgHitClaim, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnHitConfirm(ulong sender, FastBufferReader r)
        {
            if (_session.IsHost) return;
            r.ReadValueSafe(out float amount);
            r.ReadValueSafe(out byte flags);
            r.ReadValueSafe(out Vector3 point);
            EventBus<HitConfirmed>.Publish(new HitConfirmed
            {
                Attacker = _player,
                Amount = amount,
                IsCritical = (flags & 1) != 0,
                Killed = (flags & 2) != 0,
                Point = point,
                IsMelee = (flags & 4) != 0,
            });
        }

        // ---------------------------------------------------------------- hits (host side)

        private void OnHitClaim(ulong sender, FastBufferReader r)
        {
            if (!_session.IsHost) return;
            r.ReadValueSafe(out byte kind);
            r.ReadValueSafe(out ushort index);
            r.ReadValueSafe(out byte zoneByte);
            r.ReadValueSafe(out float weak);
            r.ReadValueSafe(out Vector3 origin);
            r.ReadValueSafe(out Vector3 point);
            r.ReadValueSafe(out string weaponId);
            r.ReadValueSafe(out byte claimFlags);
            r.ReadValueSafe(out float outgoing);
            r.ReadValueSafe(out float knockback);

            if (!ConsumeBudget(sender)) { Reject(sender, "rate"); return; }
            IDamageable target;
            Vector3 targetCenter;
            float pointTolerance;
            if (kind == TargetDummy)
            {
                TrainingDummy dummy = TrainingDummy.ByIndex(index);
                if (dummy == null) return;
                target = dummy;
                targetCenter = dummy.transform.position + Vector3.up;
                // Strafing dummies move on each peer's own clock, so allow for their travel.
                pointTolerance = MaxPointError + dummy.StrafeDistance * 2f;
            }
            else if (kind == TargetEnemy)
            {
                Enemy enemy = EnemyManager.Current != null ? EnemyManager.Current.FindBySlot(index) : null;
                if (enemy == null) return; // despawned on the host already
                target = enemy;
                targetCenter = enemy.transform.position + Vector3.up * (enemy.Definition.Height * 0.5f);
                pointTolerance = MaxPointError + EnemyPointSlack;
            }
            else if (kind == TargetWorld)
            {
                INetTarget world = NetTargets.Find(index);
                if (world == null) return; // already gone on the host
                target = world;
                targetCenter = world.NetTargetCenter;
                pointTolerance = MaxPointError + world.NetTargetRadius;
            }
            else
            {
                Reject(sender, "target kind");
                return;
            }
            if (!target.IsAlive) return; // already dead on the host: a normal race, not cheating
            if (!_proxies.TryGetValue(sender, out RemotePlayerProxy shooter) || shooter == null) { Reject(sender, "unknown shooter"); return; }
            if (zoneByte > (byte)HitZone.Armor || !IsFinite(origin) || !IsFinite(point) || !IsFinite(weak) || !IsFinite(outgoing) || !IsFinite(knockback)) { Reject(sender, "malformed"); return; }
            if ((origin - shooter.EyePosition).sqrMagnitude > MaxOriginError * MaxOriginError) { Reject(sender, "origin"); return; }
            if ((point - targetCenter).sqrMagnitude > pointTolerance * pointTolerance) { Reject(sender, "point"); return; }

            var zone = (HitZone)zoneByte;
            weak = Mathf.Clamp(weak, zone == HitZone.Armor ? 0f : 1f, 3f);
            // Class bonuses (Lunge strike) ride on the claim, capped; everything else comes from the host's own weapon stats.
            outgoing = Mathf.Clamp(outgoing, 0f, MaxClaimedOutgoing);
            if (!TryBuildRequest(weaponId, zone, weak, Vector3.Distance(origin, point), outgoing, out DamageRequest request, out bool isMelee)) { Reject(sender, $"weapon '{weaponId}'"); return; }

            HitOutcome outcome = LocalHitResolver.Apply(new HitClaim
            {
                Attacker = shooter.gameObject,
                Target = target,
                Zone = zone,
                WeakPointMultiplier = weak,
                Origin = origin,
                Point = point,
                WeaponId = weaponId,
                Request = request,
                IsMelee = isMelee,
                ForceStagger = isMelee && (claimFlags & 2) != 0,
                Knockback = isMelee ? Mathf.Clamp(knockback, 0f, MaxClaimedKnockback) : 0f,
            });
            if (!outcome.Applied) return;

            using (var w = new FastBufferWriter(32, Allocator.Temp))
            {
                byte flags = 0;
                if (outcome.IsCritical) flags |= 1;
                if (outcome.Killed) flags |= 2;
                if (isMelee) flags |= 4;
                w.WriteValueSafe(outcome.Amount);
                w.WriteValueSafe(flags);
                w.WriteValueSafe(point);
                _session.SendToClient(MsgHitConfirm, sender, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private bool TryBuildRequest(string weaponId, HitZone zone, float weak, float distance, float outgoing,
            out DamageRequest request, out bool isMelee)
        {
            if (weaponId == QuickMeleeId)
            {
                isMelee = true;
                request = DamageRequest.FromMelee(_quickMelee, zone, weak, outgoing);
                return true;
            }
            if (weaponId != null && _weapons.TryGetValue(weaponId, out WeaponDefinition def))
            {
                isMelee = def.Kind == WeaponKind.Melee;
                request = isMelee
                    ? DamageRequest.FromMelee(def.Melee, zone, weak, outgoing)
                    : DamageRequest.FromWeapon(def.Firearm, zone, distance, weak, outgoing);
                return true;
            }
            isMelee = false;
            request = default;
            return false;
        }

        private bool ConsumeBudget(ulong sender)
        {
            if (!_budgets.TryGetValue(sender, out ClaimBudget b))
            {
                b = new ClaimBudget { LastTime = Time.unscaledTime };
                _budgets[sender] = b;
            }
            float now = Time.unscaledTime;
            b.Tokens = Mathf.Min(ClaimsPerSecond, b.Tokens + (now - b.LastTime) * ClaimsPerSecond);
            b.LastTime = now;
            if (b.Tokens < 1f) return false;
            b.Tokens -= 1f;
            return true;
        }

        private static void Reject(ulong sender, string why) =>
            GameLog.Warn("NetSpike", $"Rejected hit claim from player {sender}: {why}");

        // ---------------------------------------------------------------- target health

        private void OnDummyHealthChanged(TrainingDummy dummy)
        {
            if (_session == null || !_session.IsHost || dummy.NetworkIndex < 0) return;
            using (var w = new FastBufferWriter(16, Allocator.Temp))
            {
                w.WriteValueSafe((ushort)1);
                w.WriteValueSafe((ushort)dummy.NetworkIndex);
                w.WriteValueSafe(dummy.Health);
                _session.SendToRemoteClients(MsgTargets, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void SendFullTargetState(ulong clientId)
        {
            IReadOnlyList<TrainingDummy> all = TrainingDummy.All;
            int count = Mathf.Min(all.Count, ushort.MaxValue);
            using (var w = new FastBufferWriter(8 + count * 6, Allocator.Temp))
            {
                w.WriteValueSafe((ushort)count);
                for (int i = 0; i < count; i++)
                {
                    w.WriteValueSafe((ushort)i);
                    w.WriteValueSafe(all[i] != null ? all[i].Health : 0f);
                }
                _session.SendToClient(MsgTargets, clientId, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnTargets(ulong sender, FastBufferReader r)
        {
            if (_session.IsHost) return;
            r.ReadValueSafe(out ushort count);
            for (int i = 0; i < count; i++)
            {
                r.ReadValueSafe(out ushort index);
                r.ReadValueSafe(out float health);
                TrainingDummy d = TrainingDummy.ByIndex(index);
                if (d != null && IsFinite(health)) d.SetNetworkHealth(health);
            }
        }

        private static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        private static bool IsFinite(Vector3 v) => IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
    }
}
