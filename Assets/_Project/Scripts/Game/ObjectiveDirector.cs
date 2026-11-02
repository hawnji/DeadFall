using System;
using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Encounters;
using Deadfall.Enemies;
using Deadfall.Net;
using Deadfall.Rules.Combat;
using Deadfall.Rules.Common;
using Deadfall.Rules.Encounters;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deadfall.Game
{
    /// <summary>
    /// Objectives and random events (M8). The host owns an <see cref="ObjectiveTracker"/> and an
    /// <see cref="EventScheduler"/>, places objective props in open rooms, runs channels, nests and extraction, pays
    /// rewards, and sends the whole state to clients ("df.obj") when it changes. Clients use props through the host
    /// ("df.objuse"). Survival gets optional side objectives every few surges; an Operations run gets its mission's
    /// objectives and ends with a timed extraction defense. Events: Blackout, The Stalker, Emergency Lockdown.
    /// </summary>
    public sealed class ObjectiveDirector : MonoBehaviour
    {
        private const string MsgState = "df.obj";
        private const string MsgUse = "df.objuse";
        private const float SendInterval = 0.25f;
        private const byte NestKind = 100;
        private const float UseRange = 4.5f;

        public static ObjectiveDirector Current { get; private set; }

        private readonly ObjectiveTracker _tracker = new ObjectiveTracker();
        private readonly EventScheduler _events = new EventScheduler();
        private readonly Dictionary<ushort, ObjectiveProp> _props = new Dictionary<ushort, ObjectiveProp>();
        private readonly Dictionary<ushort, ChannelProgress> _channels = new Dictionary<ushort, ChannelProgress>();
        private readonly Dictionary<ushort, ObjectiveNest> _nests = new Dictionary<ushort, ObjectiveNest>();
        private readonly HashSet<Enemy> _nestSpawns = new HashSet<Enemy>();
        private readonly List<Enemy> _born = new List<Enemy>(2);
        private readonly List<int> _rooms = new List<int>(32);
        private readonly List<ushort> _scratch = new List<ushort>(16);
        private SessionService _session;
        private WaveManager _waves;
        private MapSession _map;
        private DeterministicRandom _rng = new DeterministicRandom(1);
        private ushort _nextPropId = NetTargets.ObjectiveBase;
        private ObjectiveKind? _lastSide;
        private bool _dirty;
        private float _nextSend;
        private double _nextNestSpawn;
        private ExtractionZone _zone;
        private HoldTimer _hold;
        private Vector3 _zonePos;
        private MissionTemplate _mission;
        private bool _pendingOperation;

        public IReadOnlyList<Objective> Objectives => _tracker.All;
        public bool IsOperation { get; private set; }
        public string MissionName => _mission != null ? _mission.Name : (IsOperation ? ObjectiveRules.ColdStart.Name : null);
        public RunEventKind ActiveEvent { get; private set; }
        public RunEventKind PendingEvent { get; private set; }
        public bool HasKeycard { get; private set; }
        public bool ExtractionActive => _hold != null;
        public float ExtractionRemaining => _hold != null ? _hold.Remaining : 0f;
        public float ExtractionDuration => _hold != null ? _hold.Duration : 0f;
        public bool ExtractionOccupied { get; private set; }

        /// <summary>Host/offline: an objective was completed (Scrap bonus, stats).</summary>
        public event Action<Objective> ObjectiveCompleted;

        /// <summary>Distance from <paramref name="p"/> to the nearest objective prop, nest or extraction zone (placement checks).</summary>
        public float NearestObjectiveDistance(Vector3 p)
        {
            float best = float.MaxValue;
            foreach (ObjectiveProp prop in _props.Values) if (prop != null) best = Mathf.Min(best, Vector3.Distance(p, prop.transform.position));
            foreach (ObjectiveNest n in _nests.Values) if (n != null && n.IsAlive) best = Mathf.Min(best, Vector3.Distance(p, n.transform.position) - 1f);
            if (_zone != null) best = Mathf.Min(best, Vector3.Distance(p, _zone.transform.position) - ExtractionZone.Radius);
            return best;
        }

        /// <summary>Every machine: a line the HUD shows big (title, subtitle).</summary>
        public event Action<string, string> Announced;

        private bool IsAuthority => _session == null || _session.Role != SessionRole.Client;

        // ---------------------------------------------------------------- lifecycle

        private void Awake()
        {
            Current = this;
            _session = SessionService.Instance;
            _session.RegisterHandler(MsgState, OnState);
            _session.RegisterHandler(MsgUse, OnUse);
            _session.PeerConnected += OnPeerConnected;
            _session.Started += OnSessionStarted;
            _tracker.Finished += OnObjectiveFinished;
            EventBus<EnemyDied>.Subscribe(OnEnemyDied);
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            EventBus<EnemyDied>.Unsubscribe(OnEnemyDied);
            BindWaves(null);
            BindMap(null);
            if (_session == null) return;
            _session.UnregisterHandler(MsgState);
            _session.UnregisterHandler(MsgUse);
            _session.PeerConnected -= OnPeerConnected;
            _session.Started -= OnSessionStarted;
        }

        private void BindWaves(WaveManager w)
        {
            if (_waves != null)
            {
                _waves.PhaseChanged -= OnPhaseChanged;
                _waves.SurgePlanned -= OnSurgePlanned;
            }
            _waves = w;
            if (_waves != null)
            {
                _waves.PhaseChanged += OnPhaseChanged;
                _waves.SurgePlanned += OnSurgePlanned;
            }
        }

        private void BindMap(MapSession m)
        {
            if (_map != null) _map.MapCleared -= OnMapCleared;
            _map = m;
            if (_map != null) _map.MapCleared += OnMapCleared;
        }

        private void OnSessionStarted(SessionRole role)
        {
            if (role == SessionRole.Client) ClearAll(false);
        }

        private void OnMapCleared()
        {
            if (IsAuthority) ClearAll(true);
        }

        // ---------------------------------------------------------------- commands

        /// <summary>Host/offline, on a generated map: starts an Operations run with the given mission.</summary>
        public bool StartOperation(MissionTemplate mission = null)
        {
            if (!IsAuthority || _waves == null || _map == null || !_map.HasMap) return false;
            _mission = mission ?? ObjectiveRules.ColdStart;
            _pendingOperation = true;
            ulong seed = _map.Seed ^ (ulong)DateTime.UtcNow.Ticks;
            if (_waves.StartOperation(seed, _mission.LullSeconds)) return true;
            _pendingOperation = false;
            return false;
        }

        /// <summary>The local player pressed F on a prop.</summary>
        public void RequestUse(ObjectiveProp prop)
        {
            if (prop == null || prop.Done) return;
            if (IsAuthority)
            {
                Use(prop.Id, _session != null ? _session.LocalClientId : 0UL);
                return;
            }
            using (var w = new FastBufferWriter(4, Allocator.Temp))
            {
                w.WriteValueSafe(prop.Id);
                _session.SendToServer(MsgUse, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnUse(ulong sender, FastBufferReader r)
        {
            if (!_session.IsHost) return;
            r.ReadValueSafe(out ushort id);
            if (!_props.TryGetValue(id, out ObjectiveProp p) || p == null) return;
            PlayerIdentity who = PlayerIdentity.Find(sender);
            if (who != null && (who.transform.position - p.transform.position).sqrMagnitude > UseRange * UseRange) return;
            Use(id, sender);
        }

        private void Use(ushort id, ulong who)
        {
            if (!_props.TryGetValue(id, out ObjectiveProp p) || p == null || p.Done) return;
            switch (p.Kind)
            {
                case ObjectivePropKind.Generator:
                case ObjectivePropKind.DataTerminal:
                case ObjectivePropKind.LockdownTerminal:
                    if (_channels.TryGetValue(id, out ChannelProgress c) && !c.Started)
                    {
                        c.Start();
                        p.SetState(c.Value01, true, false);
                    }
                    break;
                case ObjectivePropKind.SampleCase:
                    p.SetState(1f, false, true);
                    _tracker.Advance(p.ObjectiveId);
                    break;
                case ObjectivePropKind.Keycard:
                    p.SetState(1f, false, true);
                    HasKeycard = true;
                    _tracker.Advance(p.ObjectiveId);
                    Say("KEYCARD FOUND", "Open the security locker");
                    break;
                case ObjectivePropKind.SecurityLocker:
                    if (!HasKeycard) return;
                    p.SetState(1f, false, true);
                    HasKeycard = false;
                    _tracker.Advance(p.ObjectiveId);
                    RunEconomy.Current?.SpawnRewardDrop(p.transform.position + p.transform.forward * 1.2f, WeaponTier.Refined);
                    break;
            }
            _dirty = true;
        }

        // ---------------------------------------------------------------- host loop

        private void Update()
        {
            if (WaveManager.Current != _waves) BindWaves(WaveManager.Current);
            if (MapSession.Current != _map) BindMap(MapSession.Current);
            if (!IsAuthority) return;
            _tracker.PruneFinished(); // finished objectives leave the list (the announcement already told everyone)

            float dt = Time.deltaTime;
            TickChannels(dt);
            TickNests();
            TickExtraction(dt);

            if (_session != null && _session.IsHost && _session.RemotePeerCount > 0 && _dirty && Time.unscaledTime >= _nextSend)
            {
                _nextSend = Time.unscaledTime + SendInterval;
                _dirty = false;
                SendState(ulong.MaxValue);
            }
        }

        private void TickChannels(float dt)
        {
            if (_channels.Count == 0) return;
            _scratch.Clear();
            foreach (KeyValuePair<ushort, ChannelProgress> kv in _channels) _scratch.Add(kv.Key);
            foreach (ushort id in _scratch)
            {
                ChannelProgress c = _channels[id];
                if (!c.Started || c.Complete || !_props.TryGetValue(id, out ObjectiveProp p) || p == null) continue;
                float before = c.Value01;
                bool done = c.Tick(dt, PlayerNear(p.transform.position, ObjectiveRules.ChannelRange));
                p.SetState(c.Value01, true, done);
                if (Mathf.Abs(c.Value01 - before) > 0f) _dirty = true;
                if (!done) continue;
                _tracker.Advance(p.ObjectiveId);
                _dirty = true;
            }
        }

        private void TickNests()
        {
            if (_nests.Count == 0 || _waves == null || _waves.Phase != RunPhase.Surge) return;
            double now = Time.timeAsDouble;
            if (now < _nextNestSpawn) return;
            _nextNestSpawn = now + ObjectiveRules.NestSpawnSeconds;
            _nestSpawns.RemoveWhere(e => e == null || !e.IsAlive);
            foreach (ObjectiveNest n in _nests.Values)
            {
                if (n == null || !n.IsAlive || _nestSpawns.Count >= ObjectiveRules.NestMaxAlive) continue;
                // Births count toward the surge, so the squad can't ignore the nest forever.
                _born.Clear();
                _waves.Director.SpawnNear(PrototypeEnemies.Creeper, n.transform.position, 1, 1.5f, 3f, _born);
                foreach (Enemy e in _born) _nestSpawns.Add(e);
            }
        }

        private void TickExtraction(float dt)
        {
            if (_hold == null || _zone == null) return;
            bool inside = _zone.AnyAlivePlayerInside();
            if (inside != ExtractionOccupied)
            {
                ExtractionOccupied = inside;
                _dirty = true;
            }
            int before = Mathf.CeilToInt(_hold.Remaining);
            bool done = _hold.Tick(dt, inside);
            if (Mathf.CeilToInt(_hold.Remaining) != before) _dirty = true;
            if (!done) return;
            Objective o = _tracker.FindActive(ObjectiveKind.Extraction);
            if (o != null) _tracker.Advance(o.Id);
            int bonus = _mission != null ? _mission.CompletionBonus : 0;
            Pay(bonus, "Extraction held");
            string name = MissionName ?? "Operation";
            _hold = null;
            _waves.CompleteRun($"{name}: extraction held, +{bonus} Field Credits each");
            _dirty = true;
        }

        private static bool PlayerNear(Vector3 pos, float range)
        {
            IReadOnlyList<ICombatTarget> all = CombatTargets.All;
            for (int i = 0; i < all.Count; i++)
            {
                ICombatTarget t = all[i];
                if (t?.TargetTransform == null || t.Damageable == null || !t.Damageable.IsAlive) continue;
                if (PlayerIdentity.Of(t.TargetTransform.gameObject) == null) continue;
                if ((t.TargetTransform.position - pos).sqrMagnitude <= range * range) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- run flow (host)

        private void OnPhaseChanged(RunPhase from, RunPhase to)
        {
            if (!IsAuthority) return;
            switch (to)
            {
                case RunPhase.Lull:
                    if (from == RunPhase.NotStarted) BeginRun();
                    else PlanLull();
                    break;
                case RunPhase.Reward:
                    EndEvent();
                    if (_tracker.ExpireAfter(_waves.Surge) > 0) Say("SIDE OBJECTIVE EXPIRED", null);
                    break;
                case RunPhase.NotStarted:
                case RunPhase.Complete:
                case RunPhase.Failed:
                    ClearAll(true);
                    break;
            }
        }

        private void BeginRun()
        {
            ClearAll(false);
            _rng = new DeterministicRandom(_waves.Seed ^ 0x5851F42D4C957F2DUL);
            _events.Reset();
            IsOperation = _pendingOperation && _waves.IsOperation;
            _pendingOperation = false;
            if (IsOperation && _map != null && _map.HasMap)
            {
                if (_mission == null) _mission = ObjectiveRules.ColdStart;
                _map.OpenAllSectors(); // T-47: Operations open the whole map at once
                foreach (MissionStep step in _mission.Steps) AddObjective(step.Kind, step.Required, 0);
                Say(_mission.Name.ToUpperInvariant(), _mission.Briefing);
            }
            else
            {
                _mission = null;
            }
            PlanLull();
            _dirty = true;
        }

        /// <summary>Lull before a surge: maybe a side objective (Survival) and maybe an event for the coming surge.</summary>
        private void PlanLull()
        {
            int upcoming = _waves.Surge;
            bool hasMap = _map != null && _map.HasMap;
            if (!IsOperation && hasMap && ObjectiveRules.OfferSideObjective(upcoming) && !HasActiveSide())
            {
                ObjectiveKind kind = ObjectiveRules.PickSide(_rng, _lastSide);
                _lastSide = kind;
                if (AddObjective(kind, false, upcoming + ObjectiveRules.SideObjectiveSurges - 1) != null)
                    Say("SIDE OBJECTIVE", $"{ObjectiveRules.Title(kind)} · +{ObjectiveRules.Reward(kind)} Field Credits each");
            }
            PendingEvent = _events.Roll(_rng, upcoming, WavePlanner.TypeFor(upcoming), hasMap, hasMap ? _map.OpenSectors : 0);
            if (PendingEvent != RunEventKind.None) Say($"WARNING: {EventScheduler.Name(PendingEvent)} NEXT SURGE", EventScheduler.Hint(PendingEvent));
            _dirty = true;
        }

        private bool HasActiveSide()
        {
            foreach (Objective o in _tracker.All)
                if (!o.IsDone && !o.Required && o.ExpiresAfterSurge > 0) return true;
            return false;
        }

        /// <summary>A surge is starting (before its first spawn): the pending event takes effect.</summary>
        private void OnSurgePlanned(SurgePlan plan)
        {
            if (!IsAuthority || plan == null) return;
            ActiveEvent = PendingEvent;
            PendingEvent = RunEventKind.None;
            switch (ActiveEvent)
            {
                case RunEventKind.Blackout:
                    BlackoutLighting.SetDark(true);
                    if (_tracker.FindActive(ObjectiveKind.RestorePower) == null && _map != null && _map.HasMap)
                        AddObjective(ObjectiveKind.RestorePower, false, plan.Surge);
                    break;
                case RunEventKind.Stalker:
                    plan.Spawns.Insert(0, PrototypeEnemies.Stalker.ContentId);
                    plan.Traits.Insert(0, EnemyTraits.None);
                    plan.HunterId = PrototypeEnemies.Stalker.ContentId;
                    break;
                case RunEventKind.Lockdown:
                    if (_map != null && _map.HasMap)
                    {
                        _map.SetSealed(true);
                        AddObjective(ObjectiveKind.LiftLockdown, false, plan.Surge);
                    }
                    break;
            }
            if (ActiveEvent != RunEventKind.None) Say(EventScheduler.Name(ActiveEvent), EventScheduler.Hint(ActiveEvent));
            _dirty = true;
        }

        private void EndEvent()
        {
            if (ActiveEvent == RunEventKind.None) return;
            if (ActiveEvent == RunEventKind.Blackout) BlackoutLighting.SetDark(false);
            if (ActiveEvent == RunEventKind.Lockdown && _map != null) _map.SetSealed(false);
            ActiveEvent = RunEventKind.None;
            _dirty = true;
        }

        private void OnObjectiveFinished(Objective o)
        {
            if (!IsAuthority) return;
            if (o.Status == ObjectiveStatus.Complete)
            {
                ObjectiveCompleted?.Invoke(o);
                if (o.RewardCredits > 0) Pay(o.RewardCredits, o.Title);
                if (o.Kind != ObjectiveKind.Extraction) Say("OBJECTIVE COMPLETE", o.RewardCredits > 0 ? $"{o.Title} · +{o.RewardCredits} Field Credits each" : o.Title);
                if (o.Kind == ObjectiveKind.RestorePower && ActiveEvent == RunEventKind.Blackout) EndEvent();
                if (o.Kind == ObjectiveKind.LiftLockdown && ActiveEvent == RunEventKind.Lockdown) EndEvent();
            }
            if (o.Kind == ObjectiveKind.Keycard) HasKeycard = false;
            RemovePropsOf(o.Id, o.Status == ObjectiveStatus.Failed);
            if (IsOperation && _hold == null && _tracker.RequiredComplete(ObjectiveKind.Extraction)) BeginExtraction();
            _dirty = true;
        }

        private void BeginExtraction()
        {
            if (_map == null || !_map.HasMap) return;
            int spawnRoom = _map.SpawnRoom;
            _zonePos = spawnRoom >= 0 ? FloorPoint(_map.Map.RoomCenter(spawnRoom)) : _map.PlayerSpawn(0);
            _zone = ExtractionZone.Create(_zonePos, transform);
            _hold = new HoldTimer(_mission != null ? _mission.ExtractionSeconds : 150f);
            AddObjective(ObjectiveKind.Extraction, true, 0);
            _waves.SetLullSeconds(_mission != null ? _mission.ExtractionLullSeconds : 4f);
            Say("EXTRACTION", $"Hold the airlock for {Mathf.RoundToInt(_hold.Duration)} s. The timer only runs with someone inside");
        }

        private void Pay(int amount, string reason)
        {
            if (_waves == null || amount <= 0) return;
            foreach (ulong id in _waves.Players) _waves.AwardCredits(id, amount, reason);
        }

        private void OnEnemyDied(EnemyDied e)
        {
            if (!IsAuthority || e.Definition != PrototypeEnemies.Stalker || ActiveEvent != RunEventKind.Stalker) return;
            RunEconomy.Current?.SpawnRewardDrop(e.Position, WeaponTier.Prototype);
            Say("THE STALKER IS DOWN", "It dropped something rare");
        }

        // ---------------------------------------------------------------- placement (host)

        private Objective AddObjective(ObjectiveKind kind, bool required, int expiresAfter)
        {
            if (kind != ObjectiveKind.Extraction && (_map == null || !_map.HasMap)) return null;
            Objective o = _tracker.Add(kind, ObjectiveRules.Target(kind), required, ObjectiveRules.Reward(kind), expiresAfter);
            switch (kind)
            {
                case ObjectiveKind.RestorePower:
                    for (int i = 0; i < o.Target; i++) PlaceProp(o.Id, ObjectivePropKind.Generator, i, ObjectiveRules.GeneratorSeconds);
                    break;
                case ObjectiveKind.RecoverSamples:
                    for (int i = 0; i < o.Target; i++) PlaceProp(o.Id, ObjectivePropKind.SampleCase, i, 0f);
                    break;
                case ObjectiveKind.RetrieveDataCore:
                    PlaceProp(o.Id, ObjectivePropKind.DataTerminal, 0, ObjectiveRules.TerminalSeconds);
                    break;
                case ObjectiveKind.Keycard:
                    PlaceProp(o.Id, ObjectivePropKind.Keycard, 0, 0f);
                    PlaceProp(o.Id, ObjectivePropKind.SecurityLocker, 1, 0f);
                    break;
                case ObjectiveKind.LiftLockdown:
                    PlaceLockdownTerminal(o.Id);
                    break;
                case ObjectiveKind.DestroyNest:
                {
                    Vector3 pos = PickSpot(0, out _);
                    ushort id = NextId();
                    float scale = _waves != null && _waves.CurrentPlan != null ? _waves.CurrentPlan.HealthScale : 1f;
                    ObjectiveNest n = ObjectiveNest.Create(id, o.Id, pos, ObjectiveRules.NestHealth * scale, transform);
                    n.Destroyed += OnNestDestroyed;
                    _nests[id] = n;
                    break;
                }
            }
            _dirty = true;
            return o;
        }

        private void OnNestDestroyed(ObjectiveNest n)
        {
            _tracker.Advance(n.ObjectiveId);
            ExplosionFx.Spawn(n.NetTargetCenter, 2.5f);
        }

        private void PlaceProp(byte objectiveId, ObjectivePropKind kind, int index, float channelSeconds)
        {
            Vector3 pos = PickSpot(index, out _);
            ushort id = NextId();
            ObjectiveProp p = ObjectiveProp.Create(id, objectiveId, kind, pos, transform);
            p.transform.rotation = Quaternion.Euler(0f, _rng.Range(0, 4) * 90f, 0f);
            _props[id] = p;
            if (channelSeconds > 0f) _channels[id] = new ChannelProgress(channelSeconds);
        }

        /// <summary>Lockdown terminal: in the room the most players stand in, so the sealed squad can reach it.</summary>
        private void PlaceLockdownTerminal(byte objectiveId)
        {
            int best = -1, bestCount = 0;
            var counts = new Dictionary<int, int>();
            foreach (ICombatTarget t in CombatTargets.All)
            {
                if (t?.TargetTransform == null || PlayerIdentity.Of(t.TargetTransform.gameObject) == null) continue;
                int room = _map.Map.RoomAt(t.TargetTransform.position);
                if (room < 0) continue;
                counts.TryGetValue(room, out int c);
                counts[room] = ++c;
                if (c > bestCount)
                {
                    bestCount = c;
                    best = room;
                }
            }
            Vector3 pos = best >= 0 ? SpotInRoom(best) : PickSpot(0, out _);
            ushort id = NextId();
            _props[id] = ObjectiveProp.Create(id, objectiveId, ObjectivePropKind.LockdownTerminal, pos, transform);
            _channels[id] = new ChannelProgress(ObjectiveRules.LockdownHackSeconds);
        }

        /// <summary>A floor point in an open objective room; spreads indexes over different rooms when it can.</summary>
        private Vector3 PickSpot(int index, out int room)
        {
            _map.OpenObjectiveRooms(_rooms);
            if (_rooms.Count == 0) _map.OpenObjectiveRooms(_rooms, includeSpawn: true);
            if (_rooms.Count == 0)
            {
                room = -1;
                return _map.PlayerSpawn(0);
            }
            int start = _rng.Range(0, _rooms.Count);
            room = _rooms[(start + index) % _rooms.Count];
            return SpotInRoom(room);
        }

        private Vector3 SpotInRoom(int room)
        {
            Bounds b = _map.Map.RoomBounds[room];
            for (int attempt = 0; attempt < 10; attempt++)
            {
                var guess = new Vector3(
                    _rng.Range(b.min.x + 1.5f, b.max.x - 1.5f), b.min.y + 0.5f,
                    _rng.Range(b.min.z + 1.5f, b.max.z - 1.5f));
                if (NavMesh.SamplePosition(guess, out NavMeshHit hit, 2f, NavMesh.AllAreas)) return hit.position;
            }
            return FloorPoint(b.center);
        }

        private static Vector3 FloorPoint(Vector3 p) =>
            NavMesh.SamplePosition(p, out NavMeshHit hit, 6f, NavMesh.AllAreas) ? hit.position : p;

        private ushort NextId()
        {
            ushort id = _nextPropId++;
            if (_nextPropId >= NetTargets.StructureBase) _nextPropId = NetTargets.ObjectiveBase;
            return id;
        }

        private void RemovePropsOf(byte objectiveId, bool all)
        {
            _scratch.Clear();
            foreach (KeyValuePair<ushort, ObjectiveProp> kv in _props)
                if (kv.Value == null || (kv.Value.ObjectiveId == objectiveId && (all || kv.Value.Done))) _scratch.Add(kv.Key);
            foreach (ushort id in _scratch)
            {
                if (_props.TryGetValue(id, out ObjectiveProp p) && p != null) Destroy(p.gameObject, all ? 0f : 2f);
                _props.Remove(id);
                _channels.Remove(id);
            }
            _scratch.Clear();
            foreach (KeyValuePair<ushort, ObjectiveNest> kv in _nests)
                if (kv.Value == null || kv.Value.ObjectiveId == objectiveId) _scratch.Add(kv.Key);
            foreach (ushort id in _scratch)
            {
                if (_nests.TryGetValue(id, out ObjectiveNest n) && n != null) Destroy(n.gameObject);
                _nests.Remove(id);
            }
        }

        private void ClearAll(bool notify)
        {
            foreach (ObjectiveProp p in _props.Values) if (p != null) Destroy(p.gameObject);
            foreach (ObjectiveNest n in _nests.Values) if (n != null) Destroy(n.gameObject);
            _props.Clear();
            _channels.Clear();
            _nests.Clear();
            _nestSpawns.Clear();
            _tracker.Clear();
            if (_zone != null) Destroy(_zone.gameObject);
            _zone = null;
            _hold = null;
            ExtractionOccupied = false;
            HasKeycard = false;
            if (ActiveEvent == RunEventKind.Blackout || BlackoutLighting.IsDark) BlackoutLighting.SetDark(false);
            if (_map != null) _map.SetSealed(false);
            ActiveEvent = PendingEvent = RunEventKind.None;
            IsOperation = false;
            if (notify) _dirty = true;
        }

        private void Say(string title, string subtitle)
        {
            Announced?.Invoke(title, subtitle);
            if (_session == null || !_session.IsHost || _session.RemotePeerCount == 0) return;
            _pendingSay.Add((title, subtitle ?? string.Empty));
            _dirty = true;
        }

        private readonly List<(string, string)> _pendingSay = new List<(string, string)>(4);

        // ---------------------------------------------------------------- network

        private void OnPeerConnected(ulong clientId)
        {
            if (_session.IsHost) SendState(clientId);
        }

        private void SendState(ulong onlyTo)
        {
            using (var w = new FastBufferWriter(1024, Allocator.Temp, 8192))
            {
                w.WriteValueSafe(IsOperation ? (byte)1 : (byte)0);
                w.WriteValueSafe((byte)ActiveEvent);
                w.WriteValueSafe((byte)PendingEvent);
                w.WriteValueSafe(HasKeycard ? (byte)1 : (byte)0);
                w.WriteValueSafe(_hold != null ? _hold.Duration : 0f);
                w.WriteValueSafe(_hold != null ? _hold.Elapsed : 0f);
                w.WriteValueSafe(ExtractionOccupied ? (byte)1 : (byte)0);
                w.WriteValueSafe(_zonePos);

                int oc = Mathf.Min(32, _tracker.All.Count);
                w.WriteValueSafe((byte)oc);
                for (int i = 0; i < oc; i++)
                {
                    Objective o = _tracker.All[i];
                    w.WriteValueSafe(o.Id);
                    w.WriteValueSafe((byte)o.Kind);
                    w.WriteValueSafe(o.Required ? (byte)1 : (byte)0);
                    w.WriteValueSafe((byte)o.Status);
                    w.WriteValueSafe((byte)Mathf.Clamp(o.Progress, 0, 255));
                    w.WriteValueSafe((byte)Mathf.Clamp(o.Target, 0, 255));
                    w.WriteValueSafe((ushort)Mathf.Clamp(o.RewardCredits, 0, ushort.MaxValue));
                    w.WriteValueSafe((ushort)Mathf.Clamp(o.ExpiresAfterSurge, 0, ushort.MaxValue));
                }

                int pc = Mathf.Min(64, _props.Count + _nests.Count);
                w.WriteValueSafe((byte)pc);
                int written = 0;
                foreach (ObjectiveProp p in _props.Values)
                {
                    if (p == null || written++ >= pc) continue;
                    w.WriteValueSafe(p.Id);
                    w.WriteValueSafe(p.ObjectiveId);
                    w.WriteValueSafe((byte)p.Kind);
                    w.WriteValueSafe(p.transform.position);
                    w.WriteValueSafe(p.transform.eulerAngles.y);
                    w.WriteValueSafe((byte)Mathf.RoundToInt(p.Progress01 * 255f));
                    w.WriteValueSafe((byte)((p.Started ? 1 : 0) | (p.Done ? 2 : 0)));
                }
                foreach (ObjectiveNest n in _nests.Values)
                {
                    if (n == null || written++ >= pc) continue;
                    w.WriteValueSafe(n.NetTargetId);
                    w.WriteValueSafe(n.ObjectiveId);
                    w.WriteValueSafe(NestKind);
                    w.WriteValueSafe(n.transform.position);
                    w.WriteValueSafe(0f);
                    w.WriteValueSafe((byte)Mathf.CeilToInt(n.Health01 * 255f));
                    w.WriteValueSafe((byte)(n.IsAlive ? 0 : 2));
                }
                // Pad if any entry was null (keeps the count honest for the reader).
                for (; written < pc; written++)
                {
                    w.WriteValueSafe((ushort)0);
                    w.WriteValueSafe((byte)0);
                    w.WriteValueSafe((byte)255);
                    w.WriteValueSafe(Vector3.zero);
                    w.WriteValueSafe(0f);
                    w.WriteValueSafe((byte)0);
                    w.WriteValueSafe((byte)0);
                }

                int sc = onlyTo == ulong.MaxValue ? Mathf.Min(4, _pendingSay.Count) : 0;
                w.WriteValueSafe((byte)sc);
                for (int i = 0; i < sc; i++)
                {
                    WriteText(w, _pendingSay[i].Item1);
                    WriteText(w, _pendingSay[i].Item2);
                }
                if (onlyTo == ulong.MaxValue) _pendingSay.Clear();

                if (onlyTo == ulong.MaxValue) _session.SendToRemoteClients(MsgState, w, NetworkDelivery.ReliableFragmentedSequenced);
                else _session.SendToClient(MsgState, onlyTo, w, NetworkDelivery.ReliableFragmentedSequenced);
            }
        }

        private static string Clip(string s) => string.IsNullOrEmpty(s) ? string.Empty : s.Length > 120 ? s.Substring(0, 120) : s;

        private static void WriteText(FastBufferWriter w, string s)
        {
            s = Clip(s);
            w.WriteValueSafe((byte)s.Length);
            for (int i = 0; i < s.Length; i++) w.WriteValueSafe((ushort)s[i]);
        }

        private static string ReadText(FastBufferReader r)
        {
            r.ReadValueSafe(out byte n);
            var chars = new char[n];
            for (int i = 0; i < n; i++)
            {
                r.ReadValueSafe(out ushort c);
                chars[i] = (char)c;
            }
            return new string(chars);
        }

        private readonly HashSet<ushort> _seen = new HashSet<ushort>();

        private void OnState(ulong sender, FastBufferReader r)
        {
            if (_session.IsHost) return;
            r.ReadValueSafe(out byte op);
            r.ReadValueSafe(out byte activeEvent);
            r.ReadValueSafe(out byte pendingEvent);
            r.ReadValueSafe(out byte keycard);
            r.ReadValueSafe(out float holdDuration);
            r.ReadValueSafe(out float holdElapsed);
            r.ReadValueSafe(out byte occupied);
            r.ReadValueSafe(out Vector3 zonePos);
            IsOperation = op == 1;
            HasKeycard = keycard == 1;
            ExtractionOccupied = occupied == 1;
            ApplyEvent(activeEvent <= (byte)RunEventKind.Lockdown ? (RunEventKind)activeEvent : RunEventKind.None);
            PendingEvent = pendingEvent <= (byte)RunEventKind.Lockdown ? (RunEventKind)pendingEvent : RunEventKind.None;
            if (holdDuration > 0f)
            {
                if (_hold == null || Mathf.Abs(_hold.Duration - holdDuration) > 0.01f) _hold = new HoldTimer(holdDuration);
                _hold.Set(holdElapsed);
                if (_zone == null) _zone = ExtractionZone.Create(zonePos, transform);
            }
            else
            {
                _hold = null;
                if (_zone != null) Destroy(_zone.gameObject);
                _zone = null;
            }

            r.ReadValueSafe(out byte oc);
            var list = new List<Objective>(oc);
            for (int i = 0; i < oc; i++)
            {
                r.ReadValueSafe(out byte id);
                r.ReadValueSafe(out byte kind);
                r.ReadValueSafe(out byte required);
                r.ReadValueSafe(out byte status);
                r.ReadValueSafe(out byte progress);
                r.ReadValueSafe(out byte target);
                r.ReadValueSafe(out ushort reward);
                r.ReadValueSafe(out ushort expires);
                if (kind > (byte)ObjectiveKind.Extraction || status > (byte)ObjectiveStatus.Failed) continue;
                list.Add(new Objective
                {
                    Id = id, Kind = (ObjectiveKind)kind, Required = required == 1, Status = (ObjectiveStatus)status,
                    Progress = progress, Target = Mathf.Max(1, target), RewardCredits = reward, ExpiresAfterSurge = expires,
                });
            }
            _tracker.Replace(list);

            r.ReadValueSafe(out byte pc);
            _seen.Clear();
            for (int i = 0; i < pc; i++)
            {
                r.ReadValueSafe(out ushort id);
                r.ReadValueSafe(out byte objectiveId);
                r.ReadValueSafe(out byte kind);
                r.ReadValueSafe(out Vector3 pos);
                r.ReadValueSafe(out float yaw);
                r.ReadValueSafe(out byte value);
                r.ReadValueSafe(out byte flags);
                if (id == 0 || kind == 255) continue;
                _seen.Add(id);
                if (kind == NestKind)
                {
                    if (!_nests.TryGetValue(id, out ObjectiveNest n) || n == null)
                        _nests[id] = n = ObjectiveNest.Create(id, objectiveId, pos, ObjectiveRules.NestHealth, transform);
                    n.SetHealth01((flags & 2) != 0 ? 0f : value / 255f);
                    continue;
                }
                if (kind > (byte)ObjectivePropKind.LockdownTerminal) continue;
                if (!_props.TryGetValue(id, out ObjectiveProp p) || p == null)
                {
                    _props[id] = p = ObjectiveProp.Create(id, objectiveId, (ObjectivePropKind)kind, pos, transform);
                    p.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                }
                p.SetState(value / 255f, (flags & 1) != 0, (flags & 2) != 0);
            }
            _scratch.Clear();
            foreach (ushort id in _props.Keys) if (!_seen.Contains(id)) _scratch.Add(id);
            foreach (ushort id in _scratch)
            {
                if (_props[id] != null) Destroy(_props[id].gameObject);
                _props.Remove(id);
            }
            _scratch.Clear();
            foreach (ushort id in _nests.Keys) if (!_seen.Contains(id)) _scratch.Add(id);
            foreach (ushort id in _scratch)
            {
                if (_nests[id] != null) Destroy(_nests[id].gameObject);
                _nests.Remove(id);
            }

            r.ReadValueSafe(out byte sc);
            for (int i = 0; i < sc; i++)
            {
                string title = ReadText(r);
                string subtitle = ReadText(r);
                Announced?.Invoke(title, subtitle.Length > 0 ? subtitle : null);
            }
        }

        /// <summary>Client: mirrors the host's active event locally (lights, sealed doors).</summary>
        private void ApplyEvent(RunEventKind kind)
        {
            if (kind == ActiveEvent) return;
            if (ActiveEvent == RunEventKind.Blackout) BlackoutLighting.SetDark(false);
            if (ActiveEvent == RunEventKind.Lockdown && _map != null) _map.SetSealed(false);
            ActiveEvent = kind;
            if (kind == RunEventKind.Blackout) BlackoutLighting.SetDark(true);
            if (kind == RunEventKind.Lockdown && _map != null) _map.SetSealed(true);
        }
    }
}
