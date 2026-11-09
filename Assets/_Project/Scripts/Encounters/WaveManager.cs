using System;
using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Enemies;
using Deadfall.Net;
using Deadfall.Rules.Combat;
using Deadfall.Rules.Encounters;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Deadfall.Encounters
{
    /// <summary>A line of text the HUD shows big for a moment ("SURGE 5", "SURGE CLEARED").</summary>
    public struct WaveAnnouncement
    {
        public string Title;
        public string Subtitle;
    }

    /// <summary>
    /// Runs survival surges. On the host (or offline) it owns the <see cref="SurgeRun"/> state machine, plans
    /// each surge with <see cref="WavePlanner"/>, feeds the <see cref="SpawnDirector"/>, and pays Field Credits.
    /// Clients get a small state message ("df.wave") and only display it; they send ready votes to the host.
    /// </summary>
    public sealed class WaveManager : MonoBehaviour
    {
        private const string MsgState = "df.wave";
        private const string MsgReady = "df.waveready";
        private const float StateInterval = 0.5f;

        [SerializeField] private DifficultySettings _difficulty = new DifficultySettings();
        [SerializeField] private SurgeRunSettings _runSettings = new SurgeRunSettings();

        private static WaveManager _current;

        private SurgeRun _run;
        private int _defaultFinalSurge;
        private float _defaultLullSeconds;
        private bool _operation;
        private WavePlanner _planner;
        private SpawnDirector _director;
        private readonly CreditLedger _credits = new CreditLedger();
        private readonly List<ulong> _players = new List<ulong>(4);
        private readonly HashSet<ulong> _ready = new HashSet<ulong>();
        private SurgePlan _plan;
        private ulong _seed;
        /// <summary>Survival/Endless lulls between surges last a random 10-25 s (no ready-up); Operations keep their own.</summary>
        public const float RandomLullMin = 10f, RandomLullMax = 25f;
        /// <summary>The "SURGE N INCOMING" call comes this long before the lull ends.</summary>
        public const float IncomingCallSeconds = 3f;
        private int _incomingCalled;
        private SessionService _session;
        private float _nextStateSend;
        private bool _stateDirty;

        // What clients display (and what the host displays too, kept in step each frame).
        private RunPhase _phase = RunPhase.NotStarted;
        private int _surge;
        private SurgeType _type;
        private int _directions;
        private double _phaseEndsAt;
        private int _remaining;
        private int _readyCount;
        private int _playerCount = 1;
        private bool _localReady;

        public static WaveManager Current => _current;

        /// <summary>Events (Red Sky) scale kill credits on the host. Reset by the objective director when the event ends.</summary>
        public static float EventCreditMultiplier = 1f;
        /// <summary>Double Credits power-up: kill credits are multiplied by this (host). Set by the power-up director.</summary>
        public static float PowerUpCreditMultiplier = 1f;

        public RunPhase Phase => _phase;
        public int Surge => _surge;
        public SurgeType SurgeType => _type;
        public int Directions => _directions;
        public int FinalSurge => _runSettings.FinalSurge;
        /// <summary>Host/offline: true while an Operations mission runs (no final surge; objectives end it).</summary>
        public bool IsOperation => _operation;
        /// <summary>Endless run (no final surge, escalation modifiers from surge 21). Synced to clients.</summary>
        public bool IsEndless => _endless;
        private bool _endless;
        /// <summary>Gauntlet run (<see cref="GauntletRules"/>): six hard surges, fixed lulls, no shops. Synced to clients.</summary>
        public bool IsGauntlet => _gauntlet;
        private bool _gauntlet;
        /// <summary>Escort run (<see cref="EscortRules"/>): surges until the cart is delivered or destroyed. Synced to clients.</summary>
        public bool IsEscort => _escort;
        private bool _escort;
        /// <summary>The environment's boss for Specimen surges (set when a map is built; null for any boss).</summary>
        public string PreferredBossId { get => _planner != null ? _planner.PreferredBossId : null; set { if (_planner != null) _planner.PreferredBossId = value; } }
        public float TimeLeft => Mathf.Max(0f, (float)(_phaseEndsAt - Time.timeAsDouble));
        public int EnemiesRemaining => _remaining;
        public int ReadyCount => _readyCount;
        public int PlayerCount => _playerCount;
        public bool LocalReady => _localReady;
        public bool IsRunning => _phase == RunPhase.Lull || _phase == RunPhase.Surge || _phase == RunPhase.Reward;
        /// <summary>True when this machine runs the waves (offline or host).</summary>
        public bool IsAuthority => _session == null || _session.Role != SessionRole.Client;
        public ulong Seed => _seed;
        public SpawnDirector Director => _director;
        public SurgePlan CurrentPlan => _plan;
        public int LocalCredits => _credits.Get(LocalId);
        public int CreditsOf(ulong clientId) => _credits.Get(clientId);
        /// <summary>Host/offline: client ids of the players in the run (local first).</summary>
        public IReadOnlyList<ulong> Players
        {
            get
            {
                if (IsAuthority) RefreshPlayers();
                return _players;
            }
        }

        /// <summary>Phase changes, raised on every machine (clients derive them from host messages).</summary>
        public event Action<RunPhase, RunPhase> PhaseChanged;
        /// <summary>Host/offline: a player spent Field Credits (run stats).</summary>
        public event Action<ulong, int> CreditsSpent;
        public event Action<WaveAnnouncement> Announced;
        /// <summary>The local player earned credits (amount, reason).</summary>
        public event Action<int, string> LocalCreditsEarned;
        /// <summary>Host/offline: a surge was planned and is starting (supply drops, boss music, telemetry).</summary>
        public event Action<SurgePlan> SurgePlanned;
        /// <summary>Host/offline: a Specimen died (the bounty is already paid).</summary>
        public event Action<EnemyDied> SpecimenDefeated;

        private ulong LocalId => _session != null ? _session.LocalClientId : 0UL;

        private void Awake()
        {
            _current = this;
            _run = new SurgeRun(_runSettings);
            _defaultFinalSurge = _runSettings.FinalSurge;
            _defaultLullSeconds = _runSettings.LullSeconds;
            _planner = new WavePlanner(new DifficultyCurve(_difficulty), BuildOptions(PrototypeEnemies.All));
            _director = new SpawnDirector(PrototypeEnemies.All);
        }

        private void OnDestroy()
        {
            if (_current == this) _current = null;
        }

        private static List<EnemyOption> BuildOptions(IReadOnlyList<EnemyDefinition> defs)
        {
            var options = new List<EnemyOption>(defs.Count);
            foreach (EnemyDefinition d in defs)
                if (d != null) options.Add(new EnemyOption(d.ContentId, d.ThreatCost, d.UnlockSurge, d.SpawnWeight, d.MaxBudgetShare, d.Role, d.CanSplit));
            return options;
        }

        private void OnEnable()
        {
            EventBus<EnemyDied>.Subscribe(OnEnemyDied);
            Enemy.SummonRequested += OnSummonRequested;
            _session = SessionService.Instance;
            _session.Started += OnSessionStarted;
            _session.Stopped += OnSessionStopped;
            _session.PeerConnected += OnPeerConnected;
            _session.PeerDisconnected += OnPeerDisconnected;
            _session.RegisterHandler(MsgState, OnStateMessage);
            _session.RegisterHandler(MsgReady, OnReadyMessage);
            EnemyManager.Instance.Despawned += OnEnemyDespawned;
        }

        private void OnDisable()
        {
            EventBus<EnemyDied>.Unsubscribe(OnEnemyDied);
            Enemy.SummonRequested -= OnSummonRequested;
            if (EnemyManager.Current != null) EnemyManager.Current.Despawned -= OnEnemyDespawned;
            if (_session == null) return;
            _session.Started -= OnSessionStarted;
            _session.Stopped -= OnSessionStopped;
            _session.PeerConnected -= OnPeerConnected;
            _session.PeerDisconnected -= OnPeerDisconnected;
            _session.UnregisterHandler(MsgState);
            _session.UnregisterHandler(MsgReady);
        }

        // ---------------------------------------------------------------- setup

        public void AddSpawnPoint(Vector3 position, int group, bool sheltered = false, Deadfall.Combat.IBlocker blocker = null) =>
            _director.AddPoint(position, group, sheltered, blocker);

        /// <summary>Host/offline: gives Field Credits to a player (secret stashes, objectives). Synced to clients.</summary>
        public void AwardCredits(ulong clientId, int amount, string reason)
        {
            if (!IsAuthority || amount <= 0) return;
            _credits.Add(clientId, amount);
            if (clientId == LocalId) LocalCreditsEarned?.Invoke(amount, reason);
            _stateDirty = true;
        }

        /// <summary>Host/offline: spends a player's Field Credits (Supply Caches, sector doors, structures). False if short.</summary>
        public bool TrySpendCredits(ulong clientId, int amount)
        {
            if (!IsAuthority) return false;
            if (amount <= 0) return true;
            if (!_credits.TrySpend(clientId, amount)) return false;
            _stateDirty = true;
            CreditsSpent?.Invoke(clientId, amount);
            return true;
        }

        public void ClearSpawnPoints() => _director.ClearPoints();

        // ---------------------------------------------------------------- commands

        /// <summary>
        /// Host/offline: starts an Operations run: no final surge, lulls on a timer, and the objective director ends it
        /// with <see cref="CompleteRun"/> when extraction is held.
        /// </summary>
        public bool StartOperation(ulong seed, float lullSeconds)
        {
            if (!StartRunWith(seed, () =>
            {
                _operation = true;
                _runSettings.FinalSurge = 0;
                _runSettings.LullSeconds = Mathf.Max(3f, lullSeconds);
            })) return false;
            GameLog.Info("Waves", "Operations run: surges continue until extraction.");
            return true;
        }

        /// <summary>Host/offline: starts an Endless run (M12, D-15): no final surge, escalation from surge 21.</summary>
        public bool StartEndless(ulong seed = 0)
        {
            if (!StartRunWith(seed, () =>
            {
                _endless = true;
                _planner.Endless = true;
                _runSettings.FinalSurge = 0;
            })) return false;
            _stateDirty = true;
            GameLog.Info("Waves", "Endless run: no final surge.");
            return true;
        }

        /// <summary>Host/offline: starts a Gauntlet run (six surges played as 5-10, fixed short lulls).</summary>
        public bool StartGauntlet(ulong seed = 0)
        {
            if (!StartRunWith(seed, () =>
            {
                _gauntlet = true;
                _runSettings.FinalSurge = GauntletRules.Surges;
                _runSettings.LullSeconds = GauntletRules.LullSeconds;
            })) return false;
            _stateDirty = true;
            GameLog.Info("Waves", $"Gauntlet run: {GauntletRules.Surges} surges.");
            return true;
        }

        /// <summary>Host/offline: starts an Escort run: no final surge, short lulls; the escort director ends it.</summary>
        public bool StartEscort(ulong seed = 0)
        {
            if (!StartRunWith(seed, () =>
            {
                _escort = true;
                _runSettings.FinalSurge = 0;
                _runSettings.LullSeconds = EscortRules.LullSeconds;
            })) return false;
            _stateDirty = true;
            GameLog.Info("Waves", "Escort run: surges continue until the cart is delivered or destroyed.");
            return true;
        }

        /// <summary>Host/offline: lull length for the rest of the run (extraction pressure).</summary>
        public void SetLullSeconds(float seconds)
        {
            if (!IsAuthority) return;
            _runSettings.LullSeconds = Mathf.Max(1f, seconds);
            _run.ShortenLull(Time.timeAsDouble, _runSettings.LullSeconds);
        }

        /// <summary>Host/offline: ends the run as a win now (Operations extraction).</summary>
        public void CompleteRun(string subtitle = null)
        {
            if (!IsAuthority || !_run.IsRunning) return;
            _director.Stop();
            _director.KillAll();
            _run.Complete();
            _completeSubtitle = subtitle;
            SyncFromRun();
        }

        private string _completeSubtitle;
        private string _failSubtitle;

        /// <summary>Host/offline: ends the run as a loss now (Operations: a required objective failed).</summary>
        public void FailRun(string subtitle)
        {
            if (!IsAuthority || !_run.IsRunning) return;
            _director.Stop();
            _director.KillAll();
            _run.Fail();
            _failSubtitle = string.IsNullOrEmpty(subtitle) ? "A required objective failed" : subtitle;
            SyncFromRun();
        }

        /// <summary>Host/offline: starts a new run (ending any current one). Seed 0 picks a random seed.</summary>
        public bool StartRun(ulong seed = 0) => StartRunWith(seed, null);

        /// <summary>
        /// Starts a run with its mode set up by <paramref name="configure"/> before the first phase change goes out, so
        /// listeners (the escort cart, the Gun Dispenser, ...) already see Escort / Gauntlet / Endless / Operation.
        /// </summary>
        private bool StartRunWith(ulong seed, Action configure)
        {
            if (!IsAuthority) return false;
            StopRun();
            _operation = false;
            _endless = false;
            _gauntlet = false;
            _escort = false;
            _planner.Endless = false;
            _completeSubtitle = null;
            _failSubtitle = null;
            _runSettings.FinalSurge = _defaultFinalSurge;
            _runSettings.LullSeconds = _defaultLullSeconds;
            _seed = seed != 0 ? seed : (ulong)DateTime.UtcNow.Ticks;
            _credits.Clear();
            RefreshPlayers();
            configure?.Invoke();
            _run.Start(Time.timeAsDouble);
            GameLog.Info("Waves", $"Run started, seed {_seed}, final surge {(_run.IsEndless ? "none" : _runSettings.FinalSurge.ToString())}.");
            SyncFromRun();
            return true;
        }

        /// <summary>Host/offline: ends the run without a result and removes the surge's enemies.</summary>
        public void StopRun()
        {
            if (!IsAuthority) return;
            _director.Stop();
            _director.KillAll();
            EnemyAttacks.ClearAll();
            _run.Abort();
            _operation = false;
            _gauntlet = false;
            _escort = false;
            _runSettings.FinalSurge = _defaultFinalSurge;
            _runSettings.LullSeconds = _defaultLullSeconds;
            _plan = null;
            Enemy.RoundSpeedScale = 1f;
            _ready.Clear();
            SyncFromRun();
        }

        /// <summary>The local player toggles ready during a lull. When everyone is ready the surge starts.</summary>
        public void ToggleReady()
        {
            if (_phase != RunPhase.Lull) return;
            _localReady = !_localReady;
            if (IsAuthority)
            {
                SetReady(LocalId, _localReady);
                return;
            }
            using (var w = new FastBufferWriter(4, Allocator.Temp))
            {
                w.WriteValueSafe(_localReady ? (byte)1 : (byte)0);
                _session.SendToServer(MsgReady, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void SetReady(ulong id, bool ready)
        {
            if (_run.Phase != RunPhase.Lull) return;
            if (ready) _ready.Add(id);
            else _ready.Remove(id);
            _stateDirty = true;
        }

        // ---------------------------------------------------------------- host loop

        private void Update()
        {
            TickIncomingCall();
            if (!IsAuthority || !_run.IsRunning) return;
            double now = Time.timeAsDouble;
            RefreshPlayers();
            _director.Tick(now);

            SurgeRunEvents events = _run.Tick(new SurgeRunInput
            {
                Now = now,
                AllSpawned = _director.AllSpawned,
                Alive = _director.Alive,
                AnyPlayerAlive = AnyPlayerAlive() || (SquadCanRecover != null && SquadCanRecover()),
                AllReady = AllReady(),
            });
            if (events != SurgeRunEvents.None) HandleEvents(events, now);
            SyncFromRun();
        }

        private void LateUpdate()
        {
            if (_session == null || !_session.IsHost || _session.RemotePeerCount == 0) return;
            if (_phase == RunPhase.NotStarted && !_stateDirty) return;
            if (!_stateDirty && Time.unscaledTime < _nextStateSend) return;
            _nextStateSend = Time.unscaledTime + StateInterval;
            _stateDirty = false;
            SendState(ulong.MaxValue);
        }

        private void HandleEvents(SurgeRunEvents e, double now)
        {
            if ((e & SurgeRunEvents.SurgeStarted) != 0)
            {
                _ready.Clear();
                _plan = _planner.Plan(_gauntlet ? GauntletRules.PlannedSurge(_run.Surge) : _run.Surge, Mathf.Max(1, _players.Count), _seed);
                // Endless "Relentless": shorter lulls after this surge.
                if (_gauntlet) _runSettings.LullSeconds = GauntletRules.LullSeconds;
                else if (_escort) _runSettings.LullSeconds = EscortRules.LullSeconds;
                else if (!_operation)
                {
                    // Next lull: a random 10-25 s from the run seed (Endless "Relentless" still trims it).
                    var rng = new System.Random(unchecked((int)(_seed ^ ((ulong)_run.Surge * 0x9E3779B97F4A7C15UL))));
                    float lull = RandomLullMin + (float)rng.NextDouble() * (RandomLullMax - RandomLullMin);
                    if (_endless) lull *= EndlessRules.LullMultiplier(_run.Surge);
                    _runSettings.LullSeconds = Mathf.Max(5f, lull);
                }
                _director.Begin(_plan, _seed, now);
                Enemy.RoundSpeedScale = _plan.SpeedScale;
                SurgePlanned?.Invoke(_plan);
                GameLog.Info("Waves", $"Surge {_plan.Surge} ({_plan.Type}): {_plan.Spawns.Count} enemies, budget {_plan.Budget:0.#}, " +
                                      $"concurrency {_plan.Concurrency}, directions {_director.ActiveGroups.Count}, health x{_plan.HealthScale:0.00}, speed x{_plan.SpeedScale:0.00}.");
            }
            if ((e & SurgeRunEvents.CullStragglers) != 0)
            {
                GameLog.Info("Waves", $"No kills for {_runSettings.StragglerTimeoutSeconds:0} s: removing {_director.Alive} stuck enemies.");
                _director.KillAll();
            }
            if ((e & SurgeRunEvents.SurgeCleared) != 0)
            {
                _director.Stop();
                int bonus = _run.ClearBonus(_run.Surge);
                foreach (ulong id in _players) _credits.Add(id, bonus);
                if (_players.Contains(LocalId)) LocalCreditsEarned?.Invoke(bonus, "Surge cleared");
            }
            if ((e & SurgeRunEvents.LullStarted) != 0) _ready.Clear();
            if ((e & SurgeRunEvents.RunFailed) != 0)
            {
                // Squad wiped: clear the horde as FailRun does. Left alive, it kept killing a player who got back up
                // after the results (the hub's auto-respawn), over and over, most noticeably at the end of Endless.
                _director.Stop();
                _director.KillAll();
                EnemyManager.Current?.DespawnAll();
            }
            _stateDirty = true;
        }

        private void OnEnemyDied(EnemyDied e)
        {
            if (!IsAuthority || !_director.Forget(e.Enemy)) return;
            _run.NotifyKill(Time.timeAsDouble);
            EnemyTraits traits = e.Enemy != null ? e.Enemy.Traits : EnemyTraits.None;
            if ((traits & EnemyTraits.Splitting) != 0)
                _director.SpawnNear(PrototypeEnemies.Creeper, e.Position, VariantRules.SplitCount, 0.6f, 1.6f);
            if (e.Definition != null && e.Definition.IsBoss)
            {
                foreach (ulong id in _players) _credits.Add(id, SpecimenRules.Bounty);
                if (_players.Contains(LocalId)) LocalCreditsEarned?.Invoke(SpecimenRules.Bounty, $"{e.Definition.DisplayName} destroyed");
                Announced?.Invoke(new WaveAnnouncement { Title = "SPECIMEN DOWN", Subtitle = $"+{SpecimenRules.Bounty} Field Credits each" });
                SpecimenDefeated?.Invoke(e);
                _stateDirty = true;
            }
            PlayerIdentity killer = PlayerIdentity.Of(e.Killer);
            if (killer == null || e.Definition == null) return;
            int amount = Mathf.RoundToInt(_run.KillCredits(e.Definition.ThreatCost * VariantRules.CreditMultiplier(traits)) * EventCreditMultiplier * PowerUpCreditMultiplier);
            _credits.Add(killer.ClientId, amount);
            if (killer.IsLocal) LocalCreditsEarned?.Invoke(amount, e.Definition.DisplayName);
            _stateDirty = true;
        }

        /// <summary>A Specimen in its Rampage phase calls infected to its side: mostly Husks, every fourth a Bolter.</summary>
        private void OnSummonRequested(Enemy boss, int count)
        {
            if (!IsAuthority || boss == null || _run.Phase != RunPhase.Surge) return;
            if (boss.Definition != null && boss.Definition.IsBrood)
            {
                // Her brood: Creepers, with a Retcher in every five.
                int spitters = count / 5;
                _director.SpawnNear(PrototypeEnemies.Creeper, boss.transform.position, count - spitters, 2.5f, 6f);
                if (spitters > 0) _director.SpawnNear(PrototypeEnemies.Retcher, boss.transform.position, spitters, 4f, 8f);
                return;
            }
            if (boss.Definition != null && boss.Definition.BossKind == SpecimenKind.Orderly)
            {
                // The wards empty out: Husks, with a Pyre in every four.
                int pyres = count / 4;
                _director.SpawnNear(PrototypeEnemies.Husk, boss.transform.position, count - pyres, 3f, 7f);
                if (pyres > 0) _director.SpawnNear(PrototypeEnemies.Pyre, boss.transform.position, pyres, 4f, 8f);
                return;
            }
            int bolters = count / 4;
            _director.SpawnNear(PrototypeEnemies.Husk, boss.transform.position, count - bolters, 3f, 7f);
            if (bolters > 0) _director.SpawnNear(PrototypeEnemies.Bolter, boss.transform.position, bolters, 3f, 7f);
        }

        private void OnEnemyDespawned(Enemy e)
        {
            // Pooled without dying (DespawnAll): it no longer counts toward the surge.
            if (IsAuthority) _director.Forget(e);
        }

        private void RefreshPlayers()
        {
            _players.Clear();
            _players.Add(LocalId);
            IReadOnlyList<ICombatTarget> targets = CombatTargets.All;
            for (int i = 0; i < targets.Count; i++)
            {
                ICombatTarget t = targets[i];
                if (t?.TargetTransform == null) continue;
                PlayerIdentity id = PlayerIdentity.Of(t.TargetTransform.gameObject);
                if (id != null && !id.IsLocal && !_players.Contains(id.ClientId)) _players.Add(id.ClientId);
            }
        }

        /// <summary>
        /// M11: true while nobody is up but the squad can still come back (downed players with self-revive kits).
        /// Set by the squad-life system; keeps the run from failing as soon as the last player goes down.
        /// </summary>
        public static Func<bool> SquadCanRecover;

        /// <summary>Host: a player rejoined under a new id (D-16); their Field Credits move with them.</summary>
        public void TransferCredits(ulong fromClientId, ulong toClientId)
        {
            if (!IsAuthority || fromClientId == toClientId) return;
            int amount = _credits.Get(fromClientId);
            if (amount <= 0) return;
            _credits.Set(fromClientId, 0);
            _credits.Add(toClientId, amount);
            _stateDirty = true;
        }

        private static bool AnyPlayerAlive()
        {
            IReadOnlyList<ICombatTarget> targets = CombatTargets.All;
            for (int i = 0; i < targets.Count; i++)
            {
                ICombatTarget t = targets[i];
                if (t?.TargetTransform == null || t.Damageable == null) continue;
                if (PlayerIdentity.Of(t.TargetTransform.gameObject) != null && t.Damageable.IsAlive) return true;
            }
            return false;
        }

        private bool AllReady()
        {
            if (_run.Phase != RunPhase.Lull || _players.Count == 0) return false;
            foreach (ulong id in _players)
                if (!_ready.Contains(id)) return false;
            return true;
        }

        /// <summary>Copies the authoritative run into the display fields and raises phase events.</summary>
        private void SyncFromRun()
        {
            double now = Time.timeAsDouble;
            int remaining = _run.Phase == RunPhase.Surge ? _director.Remaining : 0;
            int planned = _gauntlet ? GauntletRules.PlannedSurge(_run.Surge) : Mathf.Max(1, _run.Surge);
            SurgeType type = _plan != null && _plan.Surge == planned ? _plan.Type : WavePlanner.TypeFor(planned);
            int directions = _plan != null && _plan.Surge == planned ? _director.ActiveGroups.Count : 0;
            ApplyState(_run.Phase, _run.Surge, type, directions, now + _run.TimeLeft(now), remaining,
                CountReady(), Mathf.Max(1, _players.Count));
            if (_run.Phase != RunPhase.Lull) _localReady = false;
        }

        private int CountReady()
        {
            int n = 0;
            foreach (ulong id in _players)
                if (_ready.Contains(id)) n++;
            return n;
        }

        private void ApplyState(RunPhase phase, int surge, SurgeType type, int directions, double endsAt, int remaining, int ready, int players)
        {
            RunPhase old = _phase;
            int oldSurge = _surge;
            _phase = phase;
            _surge = surge;
            Enemy.DodgeSurge = surge;
            _type = type;
            _directions = directions;
            _phaseEndsAt = endsAt;
            _remaining = remaining;
            _readyCount = ready;
            _playerCount = players;
            if (old != phase || oldSurge != surge)
            {
                _localReady = false;
                _stateDirty = true;
                PhaseChanged?.Invoke(old, phase);
                Announce(phase);
            }
        }

        /// <summary>Every machine: calls "SURGE N INCOMING" (with the surge's type) just before the lull ends.</summary>
        private void TickIncomingCall()
        {
            if (_phase != RunPhase.Lull || _incomingCalled == _surge || TimeLeft > IncomingCallSeconds) return;
            _incomingCalled = _surge;
            Announce(RunPhase.Surge);
        }

        private void Announce(RunPhase phase)
        {
            string title = null, subtitle = null;
            switch (phase)
            {
                case RunPhase.Lull:
                    // Between surges there's no pop-up; only the start of a run (and Operations' objective lulls) say so.
                    if (_surge == 1) _incomingCalled = 0;
                    if (_surge == 1) title = _operation ? "OPERATION STARTED" : _gauntlet ? "GAUNTLET" : _escort ? "ESCORT" : "DEPLOYMENT STARTED";
                    else if (_operation) title = "LULL";
                    if (title != null)
                        subtitle = _operation ? $"Surge {_surge} in {Mathf.CeilToInt(TimeLeft)} s · work the objectives" : _gauntlet ? $"{GauntletRules.Surges} surges · the clock starts with the first" : _escort ? "Push the reactor cart to the far end of the map" : "Get ready";
                    break;
                case RunPhase.Surge:
                    // Called once, just before the surge (see Update); a surge that starts without that call says it now.
                    if (_incomingCalled == _surge && _phase == RunPhase.Surge) return;
                    _incomingCalled = _surge;
                    title = $"SURGE {_surge} INCOMING";
                    subtitle = _type switch
                    {
                        SurgeType.Swarm => "Swarm · a flood of weak infected",
                        SurgeType.Siege => "Siege · they're coming from every side",
                        SurgeType.Elite => "Elite · Alphas lead the pack, mutations everywhere",
                        SurgeType.Hunt => "Hunt · something is tracking you. Stay together",
                        SurgeType.Supply => "Supply · crates are dropping into the area",
                        SurgeType.Specimen => PreferredBossId == "enemy.broodmother" ? "Specimen · the Broodmother has woken"
                            : PreferredBossId == PrototypeEnemies.OrderlyId ? "Specimen · the Orderly is making its rounds" : "Specimen · the test subject is loose",
                        _ => !_endless && _surge == FinalSurge ? "Final surge" : null,
                    };
                    if (_endless && EndlessRules.Steps(_surge) > 0)
                    {
                        string mods = EndlessRules.Describe(_surge);
                        if (EndlessRules.StepsUpAt(_surge)) title = $"SURGE {_surge}  ·  ESCALATION";
                        subtitle = subtitle != null ? subtitle + "  ·  " + mods : mods;
                    }
                    break;
                case RunPhase.Reward:
                    title = $"SURGE {_surge} CLEARED";
                    subtitle = $"+{_run.ClearBonus(_surge)} Field Credits";
                    break;
                case RunPhase.Complete:
                    title = _escort ? "CART DELIVERED" : _completeSubtitle != null ? "OPERATION COMPLETE" : "DEPLOYMENT COMPLETE";
                    subtitle = _completeSubtitle ?? $"All {_surge} surges cleared";
                    break;
                case RunPhase.Failed:
                    title = _failSubtitle != null ? "MISSION FAILED" : "SQUAD WIPED";
                    subtitle = _failSubtitle ?? $"Fell on surge {_surge}";
                    break;
            }
            if (title != null) Announced?.Invoke(new WaveAnnouncement { Title = title, Subtitle = subtitle });
        }

        // ---------------------------------------------------------------- network

        private void OnSessionStarted(SessionRole role)
        {
            if (role != SessionRole.Client) return;
            // Joining someone else's game: whatever ran here locally stops; the host's state takes over.
            _director.Stop();
            _director.KillAll();
            _run.Abort();
            _plan = null;
            Enemy.RoundSpeedScale = 1f;
            _credits.Clear();
            _ready.Clear();
            ApplyState(RunPhase.NotStarted, 0, SurgeType.Standard, 0, 0, 0, 0, 1);
        }

        private void OnSessionStopped()
        {
            // A client that leaves keeps nothing from the host's run. The host's run simply carries on offline.
            if (_run.IsRunning || _run.Phase != RunPhase.NotStarted) return;
            _credits.Clear();
            ApplyState(RunPhase.NotStarted, 0, SurgeType.Standard, 0, 0, 0, 0, 1);
        }

        private void OnPeerConnected(ulong clientId)
        {
            if (!_session.IsHost) return;
            _stateDirty = true;
        }

        private void OnPeerDisconnected(ulong clientId)
        {
            _ready.Remove(clientId);
            _stateDirty = true;
        }

        private void SendState(ulong onlyTo)
        {
            double now = Time.timeAsDouble;
            using (var w = new FastBufferWriter(64 + _credits.All.Count * 12, Allocator.Temp))
            {
                w.WriteValueSafe((byte)_phase);
                w.WriteValueSafe((ushort)Mathf.Clamp(_surge, 0, ushort.MaxValue));
                w.WriteValueSafe((byte)_type);
                w.WriteValueSafe((byte)Mathf.Clamp(_directions, 0, 255));
                w.WriteValueSafe((float)Math.Max(0.0, _phaseEndsAt - now));
                w.WriteValueSafe((ushort)Mathf.Clamp(_remaining, 0, ushort.MaxValue));
                w.WriteValueSafe((byte)Mathf.Clamp(_readyCount, 0, 255));
                w.WriteValueSafe((byte)Mathf.Clamp(_playerCount, 0, 255));
                int count = Mathf.Min(255, _credits.All.Count);
                w.WriteValueSafe((byte)count);
                int written = 0;
                foreach (KeyValuePair<ulong, int> kv in _credits.All)
                {
                    if (written++ >= count) break;
                    w.WriteValueSafe(kv.Key);
                    w.WriteValueSafe(kv.Value);
                }
                w.WriteValueSafe((byte)((_endless ? 1 : 0) | (_failSubtitle != null ? 2 : 0) | (_completeSubtitle != null ? 4 : 0) | (_gauntlet ? 8 : 0) | (_escort ? 16 : 0)));
                if (onlyTo == ulong.MaxValue) _session.SendToRemoteClients(MsgState, w, NetworkDelivery.ReliableSequenced);
                else _session.SendToClient(MsgState, onlyTo, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnStateMessage(ulong sender, FastBufferReader r)
        {
            if (_session.Role != SessionRole.Client) return;
            r.ReadValueSafe(out byte phase);
            r.ReadValueSafe(out ushort surge);
            r.ReadValueSafe(out byte type);
            r.ReadValueSafe(out byte directions);
            r.ReadValueSafe(out float timeLeft);
            r.ReadValueSafe(out ushort remaining);
            r.ReadValueSafe(out byte ready);
            r.ReadValueSafe(out byte players);
            r.ReadValueSafe(out byte count);
            ulong me = LocalId;
            int before = _credits.Get(me);
            for (int i = 0; i < count; i++)
            {
                r.ReadValueSafe(out ulong id);
                r.ReadValueSafe(out int credits);
                _credits.Set(id, credits);
            }
            r.ReadValueSafe(out byte flags);
            _endless = (flags & 1) != 0;
            _gauntlet = (flags & 8) != 0;
            _escort = (flags & 16) != 0;
            // Clients don't get the host's wording, but the end screen title should still say how the run ended.
            if ((flags & 2) == 0) _failSubtitle = null;
            else if (_failSubtitle == null) _failSubtitle = _escort ? "The cart was destroyed" : "A required objective failed";
            if ((flags & 4) == 0) _completeSubtitle = null;
            else if (_completeSubtitle == null) _completeSubtitle = _escort ? "The cart made it through" : "Extraction held";
            if (phase > (byte)RunPhase.Failed || type > (byte)SurgeType.Specimen) return;
            int gained = _credits.Get(me) - before;
            if (gained > 0) LocalCreditsEarned?.Invoke(gained, null);
            ApplyState((RunPhase)phase, surge, (SurgeType)type, directions, Time.timeAsDouble + Mathf.Max(0f, timeLeft),
                remaining, ready, players);
        }

        private void OnReadyMessage(ulong sender, FastBufferReader r)
        {
            if (!_session.IsHost) return;
            r.ReadValueSafe(out byte ready);
            SetReady(sender, ready != 0);
        }
    }
}
