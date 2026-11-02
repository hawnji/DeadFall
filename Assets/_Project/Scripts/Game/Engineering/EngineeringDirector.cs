using System;
using System.Collections.Generic;
using Deadfall.Classes;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Encounters;
using Deadfall.Enemies;
using Deadfall.Net;
using Deadfall.Rules.Encounters;
using Deadfall.Rules.Engineering;
using Deadfall.World;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deadfall.Game
{
    /// <summary>
    /// Engineering (M9), host-authoritative. Owns the squad's shared Scrap, every structure, and the build, repair,
    /// refill, upgrade, dismantle and drone-mode actions. Clients ask ("df.build", "df.sact"); the host validates with
    /// <see cref="PlacementRules"/> and sends the full structure list and Scrap ("df.eng") when something changes.
    /// Outside a run building is free so the sandbox can be used to try structures.
    /// </summary>
    public sealed class EngineeringDirector : MonoBehaviour
    {
        private const string MsgBuild = "df.build";
        private const string MsgAction = "df.sact";
        private const string MsgState = "df.eng";
        private const string MsgTell = "df.engmsg";
        private const float SendInterval = 0.25f;
        public const string EngineerClassId = "class.engineer";
        public const string DemolitionistClassId = "class.demolitionist";
        /// <summary>Demolition Crew doctrine: a Demolitionist's blast repairs structures it covers by this fraction.</summary>
        public const float DemolitionCrewRepair = 0.1f;

        public static EngineeringDirector Current { get; private set; }

        private readonly Dictionary<ushort, Structure> _structures = new Dictionary<ushort, Structure>();
        private readonly ScrapLedger _scrap = new ScrapLedger();
        private readonly List<ushort> _scratch = new List<ushort>(16);
        private readonly HashSet<ushort> _seen = new HashSet<ushort>();
        private SessionService _session;
        private WaveManager _waves;
        private ObjectiveDirector _objectives;
        private ushort _nextId = NetTargets.StructureBase;
        private bool _dirty;
        private float _nextSend;

        public int Scrap => _scrap.Amount;
        public IReadOnlyCollection<Structure> Structures => _structures.Values;
        /// <summary>No cost outside a run (sandbox testing).</summary>
        public bool Free => _waves == null || !_waves.IsRunning;
        public bool LocalIsEngineer => IsEngineer(LocalId);
        public ulong LocalId => _session != null ? _session.LocalClientId : 0UL;
        /// <summary>Every machine: a short message for the local player (denied build, not enough Scrap...).</summary>
        public event Action<string> LocalMessage;

        private bool IsAuthority => _session == null || _session.Role != SessionRole.Client;

        public static bool IsEngineer(ulong clientId) => ClassRoster.ClassOf(clientId) == EngineerClassId;

        // ---------------------------------------------------------------- lifecycle

        private void Awake()
        {
            Current = this;
            _session = SessionService.Instance;
            _session.RegisterHandler(MsgBuild, OnBuildMessage);
            _session.RegisterHandler(MsgAction, OnActionMessage);
            _session.RegisterHandler(MsgState, OnState);
            _session.RegisterHandler(MsgTell, OnTell);
            _session.PeerConnected += OnPeerConnected;
            _session.PeerDisconnected += OnPeerDisconnected;
            _session.Started += OnSessionStarted;
            EventBus<EnemyDied>.Subscribe(OnEnemyDied);
            EventBus<ExplosionHappened>.Subscribe(OnExplosion);
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            EventBus<EnemyDied>.Unsubscribe(OnEnemyDied);
            EventBus<ExplosionHappened>.Unsubscribe(OnExplosion);
            BindWaves(null);
            BindObjectives(null);
            if (_session == null) return;
            _session.UnregisterHandler(MsgBuild);
            _session.UnregisterHandler(MsgAction);
            _session.UnregisterHandler(MsgState);
            _session.UnregisterHandler(MsgTell);
            _session.PeerConnected -= OnPeerConnected;
            _session.PeerDisconnected -= OnPeerDisconnected;
            _session.Started -= OnSessionStarted;
        }

        private void BindWaves(WaveManager w)
        {
            if (_waves != null) _waves.PhaseChanged -= OnPhaseChanged;
            _waves = w;
            if (_waves != null) _waves.PhaseChanged += OnPhaseChanged;
        }

        private void BindObjectives(ObjectiveDirector o)
        {
            if (_objectives != null) _objectives.ObjectiveCompleted -= OnObjectiveCompleted;
            _objectives = o;
            if (_objectives != null) _objectives.ObjectiveCompleted += OnObjectiveCompleted;
        }

        private void OnSessionStarted(SessionRole role)
        {
            if (role == SessionRole.Client) ClearAll();
        }

        private void OnPeerConnected(ulong id)
        {
            if (_session.IsHost) SendState(id);
        }

        private void OnPeerDisconnected(ulong id)
        {
            if (!IsAuthority) return;
            // A leaver's drone has nobody to follow; their other structures stay for the squad.
            _scratch.Clear();
            foreach (Structure s in _structures.Values)
                if (s != null && s.OwnerClientId == id && s.Kind == StructureKind.Drone) _scratch.Add(s.Id);
            foreach (ushort sid in _scratch) DestroyStructure(_structures[sid], false);
        }

        public void MarkDirty() => _dirty = true;

        private void Update()
        {
            if (WaveManager.Current != _waves) BindWaves(WaveManager.Current);
            if (ObjectiveDirector.Current != _objectives) BindObjectives(ObjectiveDirector.Current);
            if (_session == null || !_session.IsHost || _session.RemotePeerCount == 0 || !_dirty || Time.unscaledTime < _nextSend) return;
            _nextSend = Time.unscaledTime + SendInterval;
            _dirty = false;
            SendState(ulong.MaxValue);
        }

        // ---------------------------------------------------------------- scrap (host)

        private void OnPhaseChanged(RunPhase from, RunPhase to)
        {
            if (!IsAuthority) return;
            if (from == RunPhase.NotStarted && to == RunPhase.Lull)
            {
                ClearAll();
                _scrap.Set(ScrapRules.StartingScrap);
            }
            else if (to == RunPhase.Reward) _scrap.Add(ScrapRules.PerSurgeCleared);
            else if (to == RunPhase.NotStarted || to == RunPhase.Complete || to == RunPhase.Failed) ClearAll();
            _dirty = true;
        }

        private void OnEnemyDied(EnemyDied e)
        {
            if (!IsAuthority || e.Definition == null || _waves == null || !_waves.IsRunning) return;
            PlayerIdentity killer = PlayerIdentity.Of(e.Killer);
            _scrap.Add(ScrapRules.KillScrap(e.Definition.ThreatCost, killer != null && IsEngineer(killer.ClientId)));
            _dirty = true;
        }

        private void OnObjectiveCompleted(Objective o)
        {
            if (!IsAuthority || _waves == null || !_waves.IsRunning) return;
            _scrap.Add(ScrapRules.PerObjective);
            _dirty = true;
        }

        /// <summary>Demolition Crew (Engineer + Demolitionist in the squad): a Demolitionist's blast patches structures.</summary>
        private void OnExplosion(ExplosionHappened e)
        {
            if (!IsAuthority || _structures.Count == 0) return;
            PlayerIdentity who = PlayerIdentity.Of(e.Source);
            if (who == null || ClassRoster.ClassOf(who.ClientId) != DemolitionistClassId || !SquadHasEngineerOtherThan(who.ClientId)) return;
            float r2 = e.Radius * e.Radius;
            foreach (Structure s in _structures.Values)
            {
                if (s == null || !s.IsAlive || (s.transform.position - e.Position).sqrMagnitude > r2) continue;
                s.Heal(s.MaxHealth * DemolitionCrewRepair);
                _dirty = true;
            }
        }

        private static bool SquadHasEngineerOtherThan(ulong id)
        {
            foreach (KeyValuePair<ulong, string> kv in ClassRoster.All)
                if (kv.Key != id && kv.Value == EngineerClassId) return true;
            return false;
        }

        // ---------------------------------------------------------------- queries

        public int CountOwned(ulong owner, StructureKind kind)
        {
            int n = 0;
            foreach (Structure s in _structures.Values) if (s != null && s.OwnerClientId == owner && s.Kind == kind) n++;
            return n;
        }

        public bool IsMine(Structure s) => s != null && s.OwnerClientId == LocalId;

        /// <summary>Scrap to upgrade (free outside runs), or -1 at max tier or when the local player can't upgrade it.</summary>
        public int UpgradeCostFor(Structure s)
        {
            if (s == null) return -1;
            int cost = ScrapRules.UpgradeCost(s.Def, s.Tier);
            if (cost < 0) return -1;
            if (s.Def.EngineerOnly && !LocalIsEngineer) return -1;
            return Free ? 0 : cost;
        }

        public Structure Find(ushort id) => _structures.TryGetValue(id, out Structure s) && s != null ? s : null;

        /// <summary>Measures and validates a placement for <paramref name="builder"/> (both the build ghost and the host use it).</summary>
        public PlacementError Check(StructureKind kind, Vector3 position, float yaw, ulong builder, Vector3 builderPosition)
        {
            PlacementQuery q = PlacementProbe.Measure(kind, position, yaw, builderPosition);
            q.Engineer = IsEngineer(builder);
            q.Free = Free;
            q.Scrap = _scrap.Amount;
            q.OwnedOfKind = CountOwned(builder, kind);
            q.SquadTotal = _structures.Count;
            return PlacementRules.Validate(q);
        }

        // ---------------------------------------------------------------- requests

        /// <summary>Local player: build <paramref name="kind"/> here (drones ignore the position).</summary>
        public void RequestBuild(StructureKind kind, Vector3 position, float yaw)
        {
            if (IsAuthority)
            {
                PlayerIdentity me = PlayerIdentity.Local;
                TryBuild(LocalId, me != null ? me.transform.position : position, kind, position, yaw);
                return;
            }
            using (var w = new FastBufferWriter(24, Allocator.Temp))
            {
                w.WriteValueSafe((byte)kind);
                w.WriteValueSafe(position);
                w.WriteValueSafe(yaw);
                _session.SendToServer(MsgBuild, w, NetworkDelivery.ReliableSequenced);
            }
        }

        public void RequestAction(Structure s, StructureAction action)
        {
            if (s == null) return;
            if (IsAuthority)
            {
                DoAction(LocalId, s.Id, action);
                return;
            }
            using (var w = new FastBufferWriter(8, Allocator.Temp))
            {
                w.WriteValueSafe(s.Id);
                w.WriteValueSafe((byte)action);
                _session.SendToServer(MsgAction, w, NetworkDelivery.ReliableSequenced);
            }
        }

        /// <summary>Local player: switches their drone between attack and repair.</summary>
        public void ToggleMyDrone()
        {
            foreach (Structure s in _structures.Values)
                if (s != null && s.Kind == StructureKind.Drone && s.OwnerClientId == LocalId)
                {
                    RequestAction(s, StructureAction.ToggleMode);
                    return;
                }
        }

        private void OnBuildMessage(ulong sender, FastBufferReader r)
        {
            if (!_session.IsHost) return;
            r.ReadValueSafe(out byte kind);
            r.ReadValueSafe(out Vector3 pos);
            r.ReadValueSafe(out float yaw);
            if (kind > (byte)StructureKind.Barricade) return;
            PlayerIdentity who = PlayerIdentity.Find(sender);
            if (who == null) return;
            TryBuild(sender, who.transform.position, (StructureKind)kind, pos, yaw);
        }

        private void OnActionMessage(ulong sender, FastBufferReader r)
        {
            if (!_session.IsHost) return;
            r.ReadValueSafe(out ushort id);
            r.ReadValueSafe(out byte action);
            if (action > (byte)StructureAction.ToggleMode) return;
            Structure s = Find(id);
            PlayerIdentity who = PlayerIdentity.Find(sender);
            if (s == null || who == null) return;
            if (s.Kind != StructureKind.Drone && (who.transform.position - s.transform.position).sqrMagnitude > 6f * 6f) return;
            DoAction(sender, id, (StructureAction)action);
        }

        private void TryBuild(ulong builder, Vector3 builderPos, StructureKind kind, Vector3 pos, float yaw)
        {
            if (kind == StructureKind.Drone) pos = builderPos + Vector3.up * 2f;
            PlacementError err = Check(kind, pos, yaw, builder, builderPos);
            if (err != PlacementError.Ok)
            {
                Tell(builder, PlacementRules.Explain(err));
                return;
            }
            bool engineer = IsEngineer(builder);
            if (!Free && !_scrap.TrySpend(ScrapRules.BuildCost(StructureCatalog.Of(kind), engineer)))
            {
                Tell(builder, PlacementRules.Explain(PlacementError.NotEnoughScrap));
                return;
            }
            Spawn(NextId(), kind, builder, engineer, pos, yaw, false);
            _dirty = true;
        }

        private void DoAction(ulong who, ushort id, StructureAction action)
        {
            Structure s = Find(id);
            if (s == null) return;
            bool engineer = IsEngineer(who);
            switch (action)
            {
                case StructureAction.Repair:
                {
                    float amount = RepairRules.Amount(s.Health, s.MaxHealth, engineer);
                    if (amount <= 0f) return;
                    if (!Free && !_scrap.TrySpend(RepairRules.Cost(amount, engineer))) { Tell(who, "Not enough Scrap to repair"); return; }
                    s.Heal(amount);
                    break;
                }
                case StructureAction.Refill:
                    if (s.SupplyMax <= 0f || s.Supply >= s.SupplyMax) return;
                    if (!Free && !_scrap.TrySpend(s.Def.RefillCost)) { Tell(who, "Not enough Scrap to refill"); return; }
                    s.Refill();
                    break;
                case StructureAction.Upgrade:
                {
                    int cost = ScrapRules.UpgradeCost(s.Def, s.Tier);
                    if (cost < 0) return;
                    if (s.Def.EngineerOnly && !engineer) { Tell(who, "Only Engineers can upgrade this"); return; }
                    if (!Free && !_scrap.TrySpend(cost)) { Tell(who, $"Upgrade needs {cost} Scrap"); return; }
                    s.SetTier(s.Tier + 1, true);
                    break;
                }
                case StructureAction.Dismantle:
                    if (s.OwnerClientId != who) return;
                    if (!Free) _scrap.Add(ScrapRules.Refund(s.Def, s.Tier, s.OwnerIsEngineer));
                    DestroyStructure(s, false);
                    break;
                case StructureAction.ToggleMode:
                    if (s.Kind != StructureKind.Drone || s.OwnerClientId != who) return;
                    s.Mode = s.Mode == DroneMode.Attack ? DroneMode.Repair : DroneMode.Attack;
                    Tell(who, s.Mode == DroneMode.Attack ? "Drone: attack mode" : "Drone: repair mode");
                    break;
            }
            _dirty = true;
        }

        private void Tell(ulong who, string text)
        {
            if (who == LocalId)
            {
                LocalMessage?.Invoke(text);
                return;
            }
            if (_session == null || !_session.IsHost) return;
            string t = text.Length > 80 ? text.Substring(0, 80) : text;
            using (var w = new FastBufferWriter(4 + t.Length * 2, Allocator.Temp))
            {
                w.WriteValueSafe((byte)t.Length);
                for (int i = 0; i < t.Length; i++) w.WriteValueSafe((ushort)t[i]);
                _session.SendToClient(MsgTell, who, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnTell(ulong sender, FastBufferReader r)
        {
            if (_session.IsHost) return;
            r.ReadValueSafe(out byte n);
            var chars = new char[n];
            for (int i = 0; i < n; i++)
            {
                r.ReadValueSafe(out ushort c);
                chars[i] = (char)c;
            }
            LocalMessage?.Invoke(new string(chars));
        }

        // ---------------------------------------------------------------- spawning

        private ushort NextId()
        {
            for (int i = 0; i < 0x4000; i++)
            {
                ushort id = _nextId++;
                if (_nextId == 0 || _nextId < NetTargets.StructureBase) _nextId = NetTargets.StructureBase;
                if (!_structures.ContainsKey(id)) return id;
            }
            return NetTargets.StructureBase;
        }

        private Structure Spawn(ushort id, StructureKind kind, ulong owner, bool engineer, Vector3 pos, float yaw, bool mirror)
        {
            var go = new GameObject($"{kind} #{id}");
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            Structure s = kind switch
            {
                StructureKind.Sentry => go.AddComponent<SentryStructure>(),
                StructureKind.Drone => go.AddComponent<DroneStructure>(),
                StructureKind.ArcFence => go.AddComponent<ArcFenceStructure>(),
                _ => go.AddComponent<BarricadeStructure>(),
            };
            s.Init(id, kind, owner, engineer, mirror);
            _structures[id] = s;
            return s;
        }

        /// <summary>Removes a structure (destroyed by enemies when <paramref name="destroyed"/>, else dismantled).</summary>
        public void DestroyStructure(Structure s, bool destroyed)
        {
            if (s == null) return;
            _structures.Remove(s.Id);
            if (destroyed) ExplosionFx.Spawn(s.transform.position + Vector3.up * 0.6f, 1.2f);
            Destroy(s.gameObject);
            _dirty = true;
        }

        private void ClearAll()
        {
            foreach (Structure s in _structures.Values) if (s != null) Destroy(s.gameObject);
            _structures.Clear();
            _dirty = true;
        }

        // ---------------------------------------------------------------- network

        private void SendState(ulong onlyTo)
        {
            int count = Mathf.Min(64, _structures.Count);
            using (var w = new FastBufferWriter(16 + count * 40, Allocator.Temp))
            {
                w.WriteValueSafe((ushort)_scrap.Amount);
                w.WriteValueSafe((byte)count);
                int written = 0;
                foreach (Structure s in _structures.Values)
                {
                    if (written >= count) break;
                    if (s == null) continue;
                    written++;
                    w.WriteValueSafe(s.Id);
                    w.WriteValueSafe((byte)s.Kind);
                    w.WriteValueSafe(s.OwnerClientId);
                    w.WriteValueSafe(s.OwnerIsEngineer ? (byte)1 : (byte)0);
                    w.WriteValueSafe(s.transform.position);
                    w.WriteValueSafe(s.transform.eulerAngles.y);
                    w.WriteValueSafe((byte)s.Tier);
                    w.WriteValueSafe((byte)Mathf.CeilToInt(s.Health01 * 255f));
                    w.WriteValueSafe((byte)Mathf.RoundToInt(s.Supply01 * 255f));
                    w.WriteValueSafe((byte)s.Mode);
                }
                for (; written < count; written++)
                {
                    w.WriteValueSafe((ushort)0);
                    w.WriteValueSafe((byte)255);
                    w.WriteValueSafe(0UL);
                    w.WriteValueSafe((byte)0);
                    w.WriteValueSafe(Vector3.zero);
                    w.WriteValueSafe(0f);
                    w.WriteValueSafe((byte)0);
                    w.WriteValueSafe((byte)0);
                    w.WriteValueSafe((byte)0);
                    w.WriteValueSafe((byte)0);
                }
                if (onlyTo == ulong.MaxValue) _session.SendToRemoteClients(MsgState, w, NetworkDelivery.ReliableSequenced);
                else _session.SendToClient(MsgState, onlyTo, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnState(ulong sender, FastBufferReader r)
        {
            if (_session.IsHost) return;
            r.ReadValueSafe(out ushort scrap);
            r.ReadValueSafe(out byte count);
            _scrap.Set(scrap);
            _seen.Clear();
            for (int i = 0; i < count; i++)
            {
                r.ReadValueSafe(out ushort id);
                r.ReadValueSafe(out byte kind);
                r.ReadValueSafe(out ulong owner);
                r.ReadValueSafe(out byte engineer);
                r.ReadValueSafe(out Vector3 pos);
                r.ReadValueSafe(out float yaw);
                r.ReadValueSafe(out byte tier);
                r.ReadValueSafe(out byte health);
                r.ReadValueSafe(out byte supply);
                r.ReadValueSafe(out byte mode);
                if (id == 0 || kind > (byte)StructureKind.Barricade) continue;
                _seen.Add(id);
                Structure s = Find(id);
                if (s == null) s = Spawn(id, (StructureKind)kind, owner, engineer == 1, pos, yaw, true);
                else if (s.Kind != StructureKind.Drone) s.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
                s.ApplySynced(tier, health / 255f, supply / 255f, mode == 1 ? DroneMode.Repair : DroneMode.Attack);
            }
            _scratch.Clear();
            foreach (ushort id in _structures.Keys) if (!_seen.Contains(id)) _scratch.Add(id);
            foreach (ushort id in _scratch)
            {
                Structure s = _structures[id];
                _structures.Remove(id);
                if (s != null)
                {
                    ExplosionFx.Spawn(s.transform.position + Vector3.up * 0.6f, 1f);
                    Destroy(s.gameObject);
                }
            }
        }
    }

    /// <summary>Measures a candidate placement in the world (ground, clearance, distances) for <see cref="PlacementRules"/>.</summary>
    public static class PlacementProbe
    {
        private static readonly Collider[] Overlap = new Collider[8];

        public static Vector3 HalfExtents(StructureKind kind) => kind switch
        {
            StructureKind.Sentry => new Vector3(0.45f, 0.65f, 0.45f),
            StructureKind.ArcFence => new Vector3(1.7f, 0.7f, 0.2f),
            StructureKind.Barricade => new Vector3(BarricadeStructure.Width * 0.5f, BarricadeStructure.Height * 0.5f, 0.25f),
            _ => new Vector3(0.3f, 0.3f, 0.3f),
        };

        public static PlacementQuery Measure(StructureKind kind, Vector3 pos, float yaw, Vector3 builder)
        {
            var q = new PlacementQuery
            {
                Kind = kind,
                DistanceFromBuilder = Vector3.Distance(new Vector3(pos.x, 0f, pos.z), new Vector3(builder.x, 0f, builder.z)),
                NearestEnemySpawn = float.MaxValue,
                NearestObjective = float.MaxValue,
                NearestDoor = float.MaxValue,
                NearestPlayerSpawn = float.MaxValue,
            };
            if (kind == StructureKind.Drone) return q;

            q.GroundAngle = Physics.Raycast(pos + Vector3.up * 0.5f, Vector3.down, out RaycastHit ground, 1.5f, ~0, QueryTriggerInteraction.Ignore)
                ? Vector3.Angle(ground.normal, Vector3.up) : 90f;
            Vector3 half = HalfExtents(kind);
            int n = Physics.OverlapBoxNonAlloc(pos + Vector3.up * (half.y + 0.12f), half, Overlap, Quaternion.Euler(0f, yaw, 0f), ~0, QueryTriggerInteraction.Ignore);
            q.Clear = n == 0;
            q.OnNavMesh = NavMesh.SamplePosition(pos, out NavMeshHit hit, 0.6f, NavMesh.AllAreas);

            MapSession map = MapSession.Current;
            if (map != null && map.HasMap)
            {
                foreach (MapSpawnPoint sp in map.Map.EnemySpawns) q.NearestEnemySpawn = Mathf.Min(q.NearestEnemySpawn, Vector3.Distance(pos, sp.Position));
                foreach (SectorDoor d in map.Map.Doors) if (d != null) q.NearestDoor = Mathf.Min(q.NearestDoor, Vector3.Distance(pos, d.transform.position));
                foreach (Vector3 ps in map.Map.PlayerSpawns) q.NearestPlayerSpawn = Mathf.Min(q.NearestPlayerSpawn, Vector3.Distance(pos, ps));
            }
            else if (WaveManager.Current != null)
            {
                foreach (SpawnPoint sp in WaveManager.Current.Director.Points) q.NearestEnemySpawn = Mathf.Min(q.NearestEnemySpawn, Vector3.Distance(pos, sp.Position));
            }
            if (ObjectiveDirector.Current != null) q.NearestObjective = ObjectiveDirector.Current.NearestObjectiveDistance(pos);
            return q;
        }
    }
}
