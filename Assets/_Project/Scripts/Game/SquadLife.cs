using System.Collections.Generic;
using Deadfall.Classes;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Encounters;
using Deadfall.Net;
using Deadfall.Player;
using Deadfall.Rules.Combat;
using Deadfall.Rules.Encounters;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// M11 squad life (D-01, D-02): teammate revives, the squad's self-revive kits, eliminated players returning at
    /// the end of the next surge (or the next objective in Operations), and the squad-wipe check.
    ///
    /// Health stays owner-authoritative (each machine runs its own bleed-out). Revives and kits go through the host,
    /// which checks them: df.rev (revive claim, reviver → host → downed player), df.reving (a revive in progress, so
    /// the downed player's bleed-out pauses), df.kit / df.kitok (self-revive kit), df.squad (kits left, host → all).
    /// </summary>
    public sealed class SquadLife : MonoBehaviour
    {
        private const string MsgRevive = "df.rev";
        private const string MsgReviving = "df.reving";
        private const string MsgReviveOk = "df.revok";
        private const string MsgKit = "df.kit";
        private const string MsgKitOk = "df.kitok";
        private const string MsgSquad = "df.squad";

        public static SquadLife Current { get; private set; }

        /// <summary>Host/offline: a teammate revive went through (reviver, target). Feeds the run stats.</summary>
        public static event System.Action<ulong, ulong> Revived;

        /// <summary>Most self-revive kits the squad can hold (bought syringes stop here).</summary>
        public const int MaxKits = 6;

        /// <summary>Host/offline: adds one kit to the squad's pool (a syringe bought at an ammo station).</summary>
        public bool HostAddKit()
        {
            if (!IsAuthority || Kits >= MaxKits) return false;
            Kits++;
            SendSquad(ulong.MaxValue);
            return true;
        }

        /// <summary>What the host last saw of each player (for rejoin snapshots and toasts).</summary>
        public struct MemberRecord
        {
            public Vector3 Position;
            public float Yaw;
            public LifeState Life;
            public float Health01;
            public int Downs;
            public float BleedOutLeft;
        }

        private readonly Dictionary<ulong, MemberRecord> _records = new Dictionary<ulong, MemberRecord>();
        private readonly Dictionary<ulong, LifeState> _lastSeen = new Dictionary<ulong, LifeState>();
        private readonly HashSet<ulong> _revivingMe = new HashSet<ulong>();

        private SessionService _session;
        private WaveManager _waves;
        private ObjectiveDirector _objectives;
        private GameObject _player;
        private PlayerHealth _health;
        private PlayerMotor _motor;
        private PlayerWeapons _weapons;
        private PlayerClass _class;
        private PlayerInputReader _input;
        private float _selfReviveStart = -1f;
        private bool _kitPending;

        /// <summary>Self-revive kits the squad has left this run.</summary>
        public int Kits { get; private set; } = DownedRules.TeamSelfReviveKits;
        /// <summary>0..1 while the local player holds Interact to use a kit, else -1.</summary>
        public float SelfReviveProgress { get; private set; } = -1f;
        /// <summary>Someone is reviving the local player right now.</summary>
        public bool BeingRevived => _revivingMe.Count > 0;
        public bool InRun => _waves != null && _waves.IsRunning;
        public PlayerHealth LocalHealth => _health;

        private bool IsAuthority => _session == null || _session.Role != SessionRole.Client;

        public void Bind(GameObject player)
        {
            _player = player;
            _health = player.GetComponent<PlayerHealth>();
            _motor = player.GetComponent<PlayerMotor>();
            _weapons = player.GetComponent<PlayerWeapons>();
            _class = player.GetComponent<PlayerClass>();
            _input = player.GetComponent<PlayerInputReader>();
        }

        public bool TryGetRecord(ulong clientId, out MemberRecord record) => _records.TryGetValue(clientId, out record);

        // ---------------------------------------------------------------- lifecycle

        private void Awake()
        {
            Current = this;
            _session = SessionService.Instance;
            _session.RegisterHandler(MsgRevive, OnReviveClaim);
            _session.RegisterHandler(MsgReviving, OnReviving);
            _session.RegisterHandler(MsgReviveOk, OnReviveOk);
            _session.RegisterHandler(MsgKit, OnKitRequest);
            _session.RegisterHandler(MsgKitOk, OnKitOk);
            _session.RegisterHandler(MsgSquad, OnSquad);
            _session.PeerConnected += OnPeerConnected;
            _session.Started += OnSessionStarted;
            EventBus<PlayerDowned>.Subscribe(OnLocalDowned);
            WaveManager.SquadCanRecover = SquadCanRecover;
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            if (WaveManager.SquadCanRecover == (System.Func<bool>)SquadCanRecover) WaveManager.SquadCanRecover = null;
            EventBus<PlayerDowned>.Unsubscribe(OnLocalDowned);
            if (_waves != null) _waves.PhaseChanged -= OnPhase;
            if (_objectives != null) _objectives.Announced -= OnObjectiveSay;
            if (_session == null) return;
            _session.UnregisterHandler(MsgRevive);
            _session.UnregisterHandler(MsgReviving);
            _session.UnregisterHandler(MsgReviveOk);
            _session.UnregisterHandler(MsgKit);
            _session.UnregisterHandler(MsgKitOk);
            _session.UnregisterHandler(MsgSquad);
            _session.PeerConnected -= OnPeerConnected;
            _session.Started -= OnSessionStarted;
        }

        private bool SquadCanRecover()
        {
            SquadMembers.Count(out int alive, out int downed, out _);
            return !DownedRules.SquadWiped(alive, downed, Kits);
        }

        private void OnSessionStarted(SessionRole role)
        {
            _revivingMe.Clear();
            if (role == SessionRole.Client) Kits = DownedRules.TeamSelfReviveKits; // the host's count arrives next
        }

        private void OnPeerConnected(ulong clientId) => SendSquad(clientId);

        // ---------------------------------------------------------------- per frame

        private void Update()
        {
            if (_waves == null && WaveManager.Current != null) { _waves = WaveManager.Current; _waves.PhaseChanged += OnPhase; }
            if (_objectives == null && ObjectiveDirector.Current != null) { _objectives = ObjectiveDirector.Current; _objectives.Announced += OnObjectiveSay; }

            TrackMembers();
            if (_health == null) return;

            _health.BleedOutPaused = _revivingMe.Count > 0;
            UpdateSelfRevive();
        }

        /// <summary>Records each member's state and announces downs, revives and eliminations.</summary>
        private void TrackMembers()
        {
            IReadOnlyList<ISquadMember> all = SquadMembers.All;
            for (int i = 0; i < all.Count; i++)
            {
                ISquadMember m = all[i];
                if (m == null || (m is Object o && o == null) || m.Body == null) continue;
                ulong id = m.ClientId;
                _records.TryGetValue(id, out MemberRecord rec);
                LifeState before = _lastSeen.TryGetValue(id, out LifeState b) ? b : LifeState.Alive;
                if (!m.Disconnected)
                {
                    rec.Position = m.Body.position;
                    rec.Yaw = m.Body.eulerAngles.y;
                    if (m.Life == LifeState.Downed && before == LifeState.Alive && InRun) rec.Downs++;
                    rec.Life = m.Life;
                    rec.BleedOutLeft = m.Life == LifeState.Downed ? m.BleedOut01 * DownedRules.BleedOutSeconds(Mathf.Max(1, rec.Downs)) : 0f;
                    rec.Health01 = m.Health01;
                    if (m.IsLocalMember && _health != null) rec.Downs = _health.DownCount;
                    _records[id] = rec;
                }
                if (before != m.Life && !m.IsLocalMember && !m.Disconnected && InRun) Announce(id, before, m.Life);
                _lastSeen[id] = m.Life;
            }
        }

        private static void Announce(ulong id, LifeState from, LifeState to)
        {
            if (to == LifeState.Downed) RunToasts.Show($"Player {id} is down. Hold F near them to revive", HudKit.Bad, 4f);
            else if (to == LifeState.Eliminated) RunToasts.Show($"Player {id} bled out. Back after this surge", HudKit.Warn, 4f);
            else if (from == LifeState.Downed) RunToasts.Show($"Player {id} is back up", HudKit.Good);
            else if (from == LifeState.Eliminated) RunToasts.Show($"Player {id} has returned", HudKit.Good);
        }

        private void OnLocalDowned(PlayerDowned e)
        {
            if (e.Player != _player) return;
            _selfReviveStart = -1f;
            if (InRun) GameLog.Info("Squad", $"Down {e.DownNumber}, bleed-out {e.BleedOutSeconds:0} s");
        }

        // ---------------------------------------------------------------- self-revive kits

        private void UpdateSelfRevive()
        {
            SelfReviveProgress = -1f;
            bool canUse = _health.IsDowned && Kits > 0 && !_kitPending;
            bool held = _input != null && _input.Current.InteractHeld;
            if (!canUse || !held)
            {
                _selfReviveStart = -1f;
                return;
            }
            if (_selfReviveStart < 0f) _selfReviveStart = Time.time;
            SelfReviveProgress = Mathf.Clamp01((Time.time - _selfReviveStart) / DownedRules.SelfReviveSeconds);
            if (SelfReviveProgress < 1f) return;
            _selfReviveStart = -1f;
            if (IsAuthority) UseKit(_session != null ? _session.LocalClientId : 0UL, local: true);
            else
            {
                _kitPending = true;
                Invoke(nameof(ClearKitPending), 2f);
                using (var w = new FastBufferWriter(4, Allocator.Temp))
                {
                    w.WriteValueSafe((byte)1);
                    _session.SendToServer(MsgKit, w, NetworkDelivery.ReliableSequenced);
                }
            }
        }

        private void ClearKitPending() => _kitPending = false;

        /// <summary>Host/offline: spends a kit for <paramref name="clientId"/> if they are downed and one is left.</summary>
        private void UseKit(ulong clientId, bool local)
        {
            ISquadMember m = local ? SquadMembers.Local : SquadMembers.Find(clientId);
            if (m == null || !DownedRules.CanSelfRevive(m.Life, Kits)) return;
            Kits--;
            SendSquad(ulong.MaxValue);
            if (local)
            {
                _health.ReviveFromDown(DownedRules.ReviveHealthFraction, selfRevive: true);
                RunToasts.Show($"Self-revive kit used  ·  {Kits} left for the squad", HudKit.Good);
            }
            else
            {
                using (var w = new FastBufferWriter(4, Allocator.Temp))
                {
                    w.WriteValueSafe((byte)Kits);
                    _session.SendToClient(MsgKitOk, clientId, w, NetworkDelivery.ReliableSequenced);
                }
                RunToasts.Show($"Player {clientId} used a self-revive kit  ·  {Kits} left", HudKit.Info);
            }
        }

        private void OnKitRequest(ulong sender, FastBufferReader r)
        {
            if (!_session.IsHost) return;
            UseKit(sender, local: false);
        }

        private void OnKitOk(ulong sender, FastBufferReader r)
        {
            if (_session.IsHost || _health == null) return;
            r.ReadValueSafe(out byte kits);
            Kits = kits;
            _kitPending = false;
            if (_health.ReviveFromDown(DownedRules.ReviveHealthFraction, selfRevive: true))
                RunToasts.Show($"Self-revive kit used  ·  {Kits} left for the squad", HudKit.Good);
        }

        private void SendSquad(ulong onlyTo)
        {
            if (_session == null || !_session.IsHost) return;
            using (var w = new FastBufferWriter(4, Allocator.Temp))
            {
                w.WriteValueSafe((byte)Mathf.Clamp(Kits, 0, 255));
                if (onlyTo == ulong.MaxValue) _session.SendToRemoteClients(MsgSquad, w, NetworkDelivery.ReliableSequenced);
                else _session.SendToClient(MsgSquad, onlyTo, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnSquad(ulong sender, FastBufferReader r)
        {
            if (_session.IsHost) return;
            r.ReadValueSafe(out byte kits);
            Kits = kits;
        }

        // ---------------------------------------------------------------- teammate revives

        /// <summary>The local player finished a revive channel on <paramref name="targetId"/>.</summary>
        public void RequestRevive(ulong targetId)
        {
            if (_health == null || !_health.IsAlive) return;
            if (IsAuthority) HostRevive(_session != null ? _session.LocalClientId : 0UL, targetId);
            else
            {
                using (var w = new FastBufferWriter(16, Allocator.Temp))
                {
                    w.WriteValueSafe(targetId);
                    _session.SendToServer(MsgRevive, w, NetworkDelivery.ReliableSequenced);
                }
            }
        }

        /// <summary>The local player started or stopped holding a revive on <paramref name="targetId"/>.</summary>
        public void SetReviving(ulong targetId, bool active)
        {
            if (_session == null || !_session.IsActive) return;
            using (var w = new FastBufferWriter(16, Allocator.Temp))
            {
                w.WriteValueSafe(targetId);
                w.WriteValueSafe((byte)(active ? 1 : 0));
                if (_session.IsHost) RelayReviving(_session.LocalClientId, targetId, active);
                else _session.SendToServer(MsgReviving, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnReviving(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out ulong targetOrReviver);
            r.ReadValueSafe(out byte active);
            if (_session.IsHost) RelayReviving(sender, targetOrReviver, active != 0);
            else SetRevivingMe(targetOrReviver, active != 0); // host → downed player: the id is the reviver
        }

        private void RelayReviving(ulong reviver, ulong target, bool active)
        {
            if (target == _session.LocalClientId) { SetRevivingMe(reviver, active); return; }
            using (var w = new FastBufferWriter(16, Allocator.Temp))
            {
                w.WriteValueSafe(reviver);
                w.WriteValueSafe((byte)(active ? 1 : 0));
                _session.SendToClient(MsgReviving, target, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void SetRevivingMe(ulong reviver, bool active)
        {
            if (active) _revivingMe.Add(reviver);
            else _revivingMe.Remove(reviver);
        }

        private void OnReviveClaim(ulong sender, FastBufferReader r)
        {
            if (!_session.IsHost) return;
            r.ReadValueSafe(out ulong target);
            HostRevive(sender, target);
        }

        /// <summary>Host/offline: checks a revive claim and stands the target up.</summary>
        private void HostRevive(ulong reviverId, ulong targetId)
        {
            ISquadMember reviver = SquadMembers.Find(reviverId);
            ISquadMember target = SquadMembers.Find(targetId);
            if (reviver == null || target == null || reviver == target || target.Disconnected) return;
            float d = Vector3.Distance(reviver.Body.position, target.Body.position);
            if (!DownedRules.ReviveValid(reviver.Life, target.Life, d))
            {
                GameLog.Warn("Squad", $"Rejected revive {reviverId} → {targetId} ({reviver.Life} → {target.Life}, {d:0.0} m)");
                return;
            }
            Revived?.Invoke(reviverId, targetId);
            if (target.IsLocalMember)
            {
                _revivingMe.Clear();
                if (_health.ReviveFromDown(DownedRules.ReviveHealthFraction, selfRevive: false))
                    RunToasts.Show($"Player {reviverId} revived you", HudKit.Good);
                return;
            }
            using (var w = new FastBufferWriter(16, Allocator.Temp))
            {
                w.WriteValueSafe(reviverId);
                _session.SendToClient(MsgReviveOk, targetId, w, NetworkDelivery.ReliableSequenced);
            }
            if (reviver.IsLocalMember) RunToasts.Show($"Revived player {targetId}", HudKit.Good);
        }

        private void OnReviveOk(ulong sender, FastBufferReader r)
        {
            if (_session.IsHost || _health == null) return;
            r.ReadValueSafe(out ulong reviver);
            _revivingMe.Clear();
            if (_health.ReviveFromDown(DownedRules.ReviveHealthFraction, selfRevive: false))
                RunToasts.Show(reviver == _session.LocalClientId ? "Revived" : $"Player {reviver} revived you", HudKit.Good);
        }

        // ---------------------------------------------------------------- run flow and returns (D-02)

        private void OnPhase(RunPhase from, RunPhase to)
        {
            if (_health == null) return;
            if (from == RunPhase.NotStarted && to != RunPhase.NotStarted && to != RunPhase.Complete && to != RunPhase.Failed)
            {
                // A run starts (or we joined one): fresh life and a full set of squad kits.
                if (to == RunPhase.Lull) _health.ResetLife();
                if (IsAuthority) { Kits = DownedRules.TeamSelfReviveKits; SendSquad(ulong.MaxValue); }
                _records.Clear();
                return;
            }
            if (from == RunPhase.Surge && to == RunPhase.Reward) ReturnIfEliminated("Back in. Surge cleared");
            if (to == RunPhase.Complete || to == RunPhase.NotStarted)
            {
                if (!_health.IsAlive) _health.ResetLife();
                _revivingMe.Clear();
            }
            // A failed run leaves the dead where they fell (no auto revive); they get up when the squad goes back to the FOB.
        }

        private void OnObjectiveSay(string title, string subtitle)
        {
            // Operations checkpoint (D-02). Every peer sees the announcement.
            if (title == "OBJECTIVE COMPLETE") ReturnIfEliminated("Back in. Objective secured");
        }

        /// <summary>Eliminated → back on their feet next to a living teammate with the class's base loadout.</summary>
        private void ReturnIfEliminated(string toast)
        {
            if (_health == null || !DownedRules.ReturnsNow(_health.Life, true, false)) return;
            _health.Revive();
            if (_weapons != null) _weapons.ResetLoadout(RunLoadouts.For(_class != null && _class.Class != null ? _class.Class.ContentId : null));
            ISquadMember buddy = LivingTeammate();
            if (buddy != null && _motor != null)
                _motor.Teleport(buddy.Body.position + buddy.Body.right * 1.2f + Vector3.up * 0.1f, buddy.Body.eulerAngles.y);
            RunToasts.Show(toast, HudKit.Good, 4f);
        }

        /// <summary>A connected teammate who is up, or null.</summary>
        public static ISquadMember LivingTeammate()
        {
            IReadOnlyList<ISquadMember> all = SquadMembers.All;
            for (int i = 0; i < all.Count; i++)
            {
                ISquadMember m = all[i];
                if (m != null && !(m is Object o && o == null) && !m.IsLocalMember && !m.Disconnected && m.Life == LifeState.Alive) return m;
            }
            return null;
        }
    }
}
