using System;
using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Encounters;
using Deadfall.Net;
using Deadfall.Player;
using Deadfall.Rules.Encounters;
using Deadfall.Rules.World;
using Deadfall.RunSystems;
using Deadfall.World;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// Owns the generated map of a deployment: builds it from a seed (geometry, NavMesh, doors, pickups), feeds its
    /// breach closets to the <see cref="WaveManager"/> as spawn points, runs locked doors (bought with Field Credits
    /// at the door, or opened by a security switch or power relay elsewhere, T-56) and breach barricades (enemies
    /// break boards, players repair them for credits), and keeps clients in step. Only the seed goes over the
    /// network for geometry (T-09): "df.map" (seed, broken vents) on join or regenerate, "df.doors" (open doors and
    /// used switches), "df.breach" (board counts), "df.mapuse" (client asks the host to buy, switch or repair),
    /// "df.sector" (go to spawn), "df.secret" when a vent panel breaks. Seed 0 means "no map" (the sandbox).
    /// </summary>
    public sealed class MapSession : MonoBehaviour
    {
        private const string MsgMap = "df.map";
        private const string MsgSector = "df.sector";
        private const string MsgSecret = "df.secret";
        private const string MsgDoors = "df.doors";
        private const string MsgUse = "df.mapuse";
        private const string MsgBreach = "df.breach";
        public const int SecretStashCredits = 250;
        /// <summary>Credits for putting one board back on a breach (T-56).</summary>
        public const int RepairCredits = 10;
        public const float RepairCooldown = 0.6f;
        /// <summary>Seconds from using a power relay until its door opens.</summary>
        public const float PowerRestoreSeconds = 6f;

        private bool[] _doorOpen = System.Array.Empty<bool>();
        private readonly HashSet<int> _usedSwitches = new HashSet<int>();
        private readonly Dictionary<int, double> _powerAt = new Dictionary<int, double>();
        private readonly Dictionary<ulong, double> _repairReady = new Dictionary<ulong, double>();
        private readonly List<int> _pendingDoors = new List<int>();
        private readonly List<int> _pendingSwitches = new List<int>();
        private readonly List<(int, int)> _pendingBoards = new List<(int, int)>();
        private bool _hasPendingDoors;
        private bool _breachDirty;
        private readonly HashSet<int> _dirtyBreaches = new HashSet<int>();

        private readonly MapBuilder _builder = new MapBuilder();
        private readonly RuntimeNavMesh _navMesh = new RuntimeNavMesh();
        private readonly List<int> _pendingPanels = new List<int>();
        private RoomKit _kit;
        private GraphTemplate _graph;
        private SessionService _session;
        private WaveManager _waves;
        private Transform _lootRoot;
        private ulong _pendingSeed;
        private int _pendingOpen;

        public static MapSession Current { get; private set; }

        /// <summary>Power-locked doors need grid power (set by <see cref="PowerGrid"/>; null means always powered).</summary>
        public static Func<bool> GridHasPower;

        public BuiltMap Map { get; private set; }
        public bool HasMap => Map != null;
        public ulong Seed => Map != null ? Map.Layout.Seed : 0UL;
        public int OpenSectors { get; private set; } = 1;
        public int SectorCount => Map != null ? Map.Layout.SectorCount : 0;
        public RoomKit Kit => _kit;
        /// <summary>The environment of the current (or last) map.</summary>
        public EnvironmentDef Environment { get; private set; } = EnvironmentCatalog.All[0];
        public float LastBuildMilliseconds { get; private set; }
        public float LastBakeMilliseconds { get; private set; }

        /// <summary>M6 hook: builds a Supply Cache at a loot point (none in M5).</summary>
        public Func<MapLootPoint, Transform, GameObject> SupplyCacheFactory { get; set; }

        public event Action<BuiltMap> MapBuilt;
        public event Action MapCleared;
        /// <summary>Sectors open changed: (open count, true if this was an unlock rather than a reset).</summary>
        public event Action<int, bool> SectorsChanged;
        /// <summary>Clients: the host switched world. The bootstrap tears down and calls <see cref="Build"/> (or goes back to the sandbox for 0).</summary>
        public event Action<ulong> RemoteWorldRequested;

        private void Awake()
        {
            Current = this;
            _kit = CinderRidgeKit.Create();
            _graph = CinderRidgeKit.SurvivalGraph();
            _session = SessionService.Instance;
            _session.RegisterHandler(MsgMap, OnMapMessage);
            _session.RegisterHandler(MsgSector, OnSectorMessage);
            _session.RegisterHandler(MsgSecret, OnSecretMessage);
            _session.RegisterHandler(MsgDoors, OnDoorsMessage);
            _session.RegisterHandler(MsgUse, OnUseMessage);
            _session.RegisterHandler(MsgBreach, OnBreachMessage);
            SectorDoor.PromptFor = DoorPrompt;
            SectorDoor.Use = UseDoor;
            DoorSwitch.PromptFor = SwitchPrompt;
            DoorSwitch.Use = UseSwitch;
            BreachBarricade.PromptFor = BreachPrompt;
            BreachBarricade.Use = UseBreach;
            BreachBarricade.BrokenBoard += OnBoardBroken;
            _session.PeerConnected += OnPeerConnected;
            SecretPanel.BrokenByDamage += OnPanelBroken;
            LootNetwork.TakenOnHost += OnLootTaken;
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            SecretPanel.BrokenByDamage -= OnPanelBroken;
            LootNetwork.TakenOnHost -= OnLootTaken;
            BreachBarricade.BrokenBoard -= OnBoardBroken;
            if (_waves != null) _waves.PhaseChanged -= OnPhaseChanged;
            if (_session != null)
            {
                _session.UnregisterHandler(MsgMap);
                _session.UnregisterHandler(MsgSector);
                _session.UnregisterHandler(MsgSecret);
                _session.UnregisterHandler(MsgDoors);
                _session.UnregisterHandler(MsgUse);
                _session.UnregisterHandler(MsgBreach);
                _session.PeerConnected -= OnPeerConnected;
            }
            Clear();
        }

        private void Update()
        {
            // The wave manager may be created after us.
            if (_waves == null && WaveManager.Current != null)
            {
                _waves = WaveManager.Current;
                _waves.PhaseChanged += OnPhaseChanged;
            }
            if (Map == null || !IsAuthority) return;
            // Power relays: the door opens once power is back.
            if (_powerAt.Count > 0)
            {
                int ready = -1;
                foreach (KeyValuePair<int, double> kv in _powerAt) if (Time.timeAsDouble >= kv.Value) { ready = kv.Key; break; }
                if (ready >= 0)
                {
                    _powerAt.Remove(ready);
                    OpenDoor(ready, "POWER RESTORED");
                }
            }
            if (_breachDirty && _session != null && _session.IsHost) SendBreaches(ulong.MaxValue, onlyDirty: true);
            _breachDirty = false;
        }

        private bool IsAuthority => _session == null || _session.Role != SessionRole.Client;

        /// <summary>Builds the map for a seed. On the host this also tells every client.</summary>
        public BuiltMap Build(ulong seed)
        {
            Clear(notifyClients: false);
            float started = Time.realtimeSinceStartup;
            // M12: the environment travels inside the seed.
            Environment = EnvironmentCatalog.ForSeed(seed);
            _kit = Environment.CreateKit();
            _graph = Environment.CreateGraph();
            if (WaveManager.Current != null) WaveManager.Current.PreferredBossId = Environment.BossId;
            Layout layout = LayoutGenerator.Generate(_kit, _graph, seed);
            if (layout == null)
            {
                GameLog.Warn("Map", $"Seed {seed}: no valid layout, even the fallback failed.");
                return null;
            }
            Map = _builder.Build(layout, transform);
            Map.Root.gameObject.AddComponent<MapGizmos>().Bind(Map);
            LastBuildMilliseconds = (Time.realtimeSinceStartup - started) * 1000f;

            // Bake with every doorway open, then add the doors (they carve while closed, T-32).
            Physics.SyncTransforms();
            float bakeStart = Time.realtimeSinceStartup;
            Bounds bake = Map.Bounds;
            bake.Expand(new Vector3(2f, 2f, 2f));
            _navMesh.Build(bake, ~(1 << PlayerFactory.PlayerLayer));
            LastBakeMilliseconds = (Time.realtimeSinceStartup - bakeStart) * 1000f;
            _builder.BuildDoors(Map);

            LootNetwork.ClearAll();
            _lootRoot = new GameObject("Loot").transform;
            _lootRoot.SetParent(Map.Root, false);
            foreach (MapLootPoint p in Map.Loot)
            {
                if (p.Kind == LootKind.SupplyCache)
                {
                    if (SupplyCacheFactory != null) SupplyCacheFactory(p, _lootRoot);
                    continue;
                }
                LootPickup.Create(p.Id, p.Kind, p.Position, p.Yaw, _lootRoot);
            }

            OpenSectors = 1;
            _doorOpen = new bool[layout.Connections.Count];
            _usedSwitches.Clear();
            _powerAt.Clear();
            bool authority = IsAuthority;
            foreach (BreachBarricade b in Map.Breaches) b.Authority = authority;
            ApplySpawnPoints();
            GameLog.Info("Map", $"Seed {seed}: {layout.Rooms.Count} rooms, {layout.SectorCount} sectors, attempt {layout.Attempt}" +
                                $"{(layout.UsedFallback ? " (fallback layout)" : string.Empty)}, built in {LastBuildMilliseconds:0} ms, NavMesh {LastBakeMilliseconds:0} ms.");

            // A client applies what the host sent with the map message.
            if (!IsAuthority && _pendingSeed == seed)
            {
                foreach (int c in _pendingPanels) BreakPanel(c);
                _pendingPanels.Clear();
                ApplyPendingState();
            }
            if (_session != null && _session.IsHost) SendMap(ulong.MaxValue);
            MapBuilt?.Invoke(Map);
            return Map;
        }

        /// <summary>Removes the map. On the host, clients go back to the sandbox too.</summary>
        public void Clear() => Clear(true);

        private void Clear(bool notifyClients)
        {
            bool had = Map != null;
            if (Map != null) _builder.Clear(Map);
            Map = null;
            Sealed = false;
            _doorOpen = System.Array.Empty<bool>();
            _usedSwitches.Clear();
            _powerAt.Clear();
            _navMesh.Clear();
            LootNetwork.ClearAll();
            Deadfall.Enemies.EnemyAttacks.ClearAll();
            OpenSectors = 1;
            if (had)
            {
                if (_waves != null) _waves.ClearSpawnPoints();
                if (notifyClients && _session != null && _session.IsHost) SendMap(ulong.MaxValue);
                MapCleared?.Invoke();
            }
        }

        /// <summary>Player start position for a slot (0 = host or offline).</summary>
        public Vector3 PlayerSpawn(int slot) => Map != null && Map.PlayerSpawns.Count > 0 ? Map.PlayerSpawns[Mathf.Abs(slot) % Map.PlayerSpawns.Count] : Vector3.zero;
        public float PlayerSpawnYaw => Map != null ? Map.PlayerSpawnYaw : 0f;

        /// <summary>The local player's spawn slot: host 0, clients by id.</summary>
        public int LocalSlot => _session == null || !_session.IsActive || _session.IsHost ? 0 : (int)(_session.LocalClientId % 4UL);

        /// <summary>Puts the local player at their spawn point in the airlock.</summary>
        public void MoveLocalPlayerToSpawn()
        {
            PlayerIdentity me = PlayerIdentity.Local;
            if (me == null || Map == null) return;
            var motor = me.GetComponent<PlayerMotor>();
            if (motor != null) motor.Teleport(PlayerSpawn(LocalSlot), PlayerSpawnYaw);
        }

        // ---------------------------------------------------------------- doors and sectors

        /// <summary>Doors opened so far / locked doors on the map.</summary>
        public int DoorsOpened { get { int n = 0; for (int i = 0; i < _doorOpen.Length; i++) if (_doorOpen[i]) n++; return n; } }
        public int DoorCount { get { if (Map == null) return 0; int n = 0; foreach (Connection c in Map.Layout.Connections) if (c.IsDoor) n++; return n; } }
        public bool IsDoorOpen(int connection) => connection >= 0 && connection < _doorOpen.Length && _doorOpen[connection];

        /// <summary>True when the squad can walk into the room: its sector is open and no closed side door is in the way.</summary>
        public bool IsRoomOpen(int room)
        {
            if (Map == null || room < 0 || room >= Map.Layout.Rooms.Count) return false;
            return Map.Layout.Rooms[room].Sector < OpenSectors && Map.Layout.SideDoorTo(room, IsDoorOpen) < 0;
        }

        /// <summary>Doors are free outside a run, so a generated map can be explored from the pause screen (as T-51).</summary>
        private bool DoorsFree => _waves == null || !_waves.IsRunning;
        private ulong LocalId => _session != null ? _session.LocalClientId : 0UL;

        /// <summary>Host/offline: opens every door (Operations runs, T-47).</summary>
        public void OpenAllSectors()
        {
            if (!IsAuthority || Map == null) return;
            for (int i = 0; i < Map.Layout.Connections.Count; i++)
                if (Map.Layout.Connections[i].IsDoor && !_doorOpen[i]) OpenDoor(i, null, sync: false);
            SendDoors(ulong.MaxValue);
        }

        /// <summary>Host/offline: opens the door into the next sector now (debug panel).</summary>
        public void OpenNextSector()
        {
            if (!IsAuthority || Map == null) return;
            for (int i = 0; i < Map.Layout.Connections.Count; i++)
                if (Map.Layout.Connections[i].UnlockSector == OpenSectors && !_doorOpen[i]) { OpenDoor(i, null); return; }
        }

        /// <summary>
        /// Opens a door here (host/offline, and clients applying the host's state): sector doors raise
        /// <see cref="OpenSectors"/>, side doors open their branch. Spawn breaches behind it come into play.
        /// </summary>
        private void OpenDoor(int connection, string announce, bool sync = true)
        {
            if (Map == null || connection < 0 || connection >= _doorOpen.Length || _doorOpen[connection]) return;
            _doorOpen[connection] = true;
            Connection c = Map.Layout.Connections[connection];
            bool sectorDoor = c.Locked;
            if (sectorDoor && c.UnlockSector + 1 > OpenSectors) OpenSectors = c.UnlockSector + 1;
            foreach (SectorDoor d in Map.Doors)
                if (d.ConnectionIndex == connection && !Sealed) d.Open();
            foreach (DoorSwitch sw in Map.Switches)
                if (sw.ConnectionIndex == connection) sw.SetUsed(true);
            ApplySpawnPoints();
            if (announce != null) RunToasts.Show(announce, new Color(0.6f, 0.9f, 1f));
            if (sync && _session != null && _session.IsHost) SendDoors(ulong.MaxValue);
            SectorsChanged?.Invoke(OpenSectors, true);
        }

        /// <summary>Back to a fresh map state: every door shut, switches unused, barricades whole (new run).</summary>
        private void ResetDoorsAndBreaches()
        {
            if (Map == null) return;
            for (int i = 0; i < _doorOpen.Length; i++) _doorOpen[i] = false;
            _usedSwitches.Clear();
            _powerAt.Clear();
            OpenSectors = 1;
            foreach (SectorDoor d in Map.Doors) d.CloseImmediate();
            foreach (DoorSwitch sw in Map.Switches) sw.SetUsed(false);
            foreach (BreachBarricade b in Map.Breaches) b.SetBoards(BreachBarricade.MaxBoards);
            ApplySpawnPoints();
            if (_session != null && _session.IsHost) { SendDoors(ulong.MaxValue); SendBreaches(ulong.MaxValue, onlyDirty: false); }
            SectorsChanged?.Invoke(OpenSectors, false);
        }

        private void ApplySpawnPoints()
        {
            WaveManager waves = _waves != null ? _waves : WaveManager.Current;
            if (waves == null || Map == null) return;
            waves.ClearSpawnPoints();
            foreach (MapSpawnPoint p in Map.EnemySpawns)
                if (IsRoomOpen(p.Room)) waves.AddSpawnPoint(p.Position, p.Room, sheltered: true);
        }

        private void OnPhaseChanged(RunPhase from, RunPhase to)
        {
            if (!IsAuthority || Map == null || _waves == null) return;
            if (from == RunPhase.NotStarted && to == RunPhase.Lull)
            {
                // New run: doors shut, barricades whole, everyone to the airlock.
                ResetDoorsAndBreaches();
                MoveLocalPlayerToSpawn();
                SendRespawnToClients();
            }
        }

        // ---------------------------------------------------------------- use (doors, switches, breaches)

        private enum UseKind : byte { Door = 0, Switch = 1, Repair = 2 }

        private string DoorPrompt(SectorDoor door, GameObject player)
        {
            if (Sealed) return "Sealed by the lockdown";
            switch (door.Lock)
            {
                case DoorLock.Credits:
                    if (DoorsFree) return "Open door (free outside a run)";
                    int credits = _waves != null ? _waves.LocalCredits : 0;
                    return credits >= door.Cost ? $"Open door ({door.Cost} credits)" : $"Open door ({door.Cost} credits, you have {credits})";
                case DoorLock.Switch:
                    return "Locked: find the security switch";
                case DoorLock.Power:
                    if (_usedSwitches.Contains(door.ConnectionIndex)) return "Power restoring...";
                    return GridHasPower != null && !GridHasPower() ? "No grid power: restart a grid generator, then the relay" : "No power: restore the power relay";
                default:
                    return string.Empty;
            }
        }

        private string SwitchPrompt(DoorSwitch sw, GameObject player)
        {
            if (sw.Used || IsDoorOpen(sw.ConnectionIndex)) return string.Empty;
            if (sw.Kind == DoorLock.Power && GridHasPower != null && !GridHasPower()) return "Power relay (the grid is down: restart a generator)";
            return sw.Kind == DoorLock.Power ? "Restore power to a locked door" : "Hit the security switch";
        }

        private string BreachPrompt(BreachBarricade b, GameObject player)
        {
            if (b.Boards >= BreachBarricade.MaxBoards) return string.Empty;
            bool paid = _waves != null && _waves.IsRunning;
            return paid ? $"Repair barricade  +{RepairCredits} each  ·  {b.Boards}/{BreachBarricade.MaxBoards}" : $"Repair barricade  ·  {b.Boards}/{BreachBarricade.MaxBoards}";
        }

        private void UseDoor(SectorDoor door, GameObject player) => Request(UseKind.Door, door.ConnectionIndex);
        private void UseSwitch(DoorSwitch sw, GameObject player) => Request(UseKind.Switch, sw.ConnectionIndex);
        private void UseBreach(BreachBarricade b, GameObject player) => Request(UseKind.Repair, b.Index);

        private void Request(UseKind kind, int index)
        {
            if (IsAuthority) { HandleUse(LocalId, kind, index); return; }
            using (var w = new FastBufferWriter(4, Allocator.Temp))
            {
                w.WriteValueSafe((byte)kind);
                w.WriteValueSafe((ushort)index);
                _session.SendToServer(MsgUse, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnUseMessage(ulong sender, FastBufferReader r)
        {
            if (!IsAuthority) return;
            r.ReadValueSafe(out byte kind);
            r.ReadValueSafe(out ushort index);
            HandleUse(sender, (UseKind)kind, index);
        }

        /// <summary>Host/offline: validates and applies a use from <paramref name="client"/>.</summary>
        private void HandleUse(ulong client, UseKind kind, int index)
        {
            if (Map == null) return;
            var layout = Map.Layout;
            switch (kind)
            {
                case UseKind.Door:
                {
                    if (index < 0 || index >= layout.Connections.Count || _doorOpen[index] || Sealed) return;
                    Connection c = layout.Connections[index];
                    if (c.Lock != DoorLock.Credits) return;
                    if (!DoorsFree && (_waves == null || !_waves.TrySpendCredits(client, c.Cost))) return;
                    OpenDoor(index, c.Locked ? "SECTOR OPEN" : "Door opened");
                    break;
                }
                case UseKind.Switch:
                {
                    if (index < 0 || index >= layout.Connections.Count || _doorOpen[index] || _usedSwitches.Contains(index)) return;
                    Connection c = layout.Connections[index];
                    if (c.Lock == DoorLock.Switch)
                    {
                        _usedSwitches.Add(index);
                        OpenDoor(index, "SECURITY SWITCH: a door has opened");
                    }
                    else if (c.Lock == DoorLock.Power)
                    {
                        if (GridHasPower != null && !GridHasPower()) return; // the relay needs the grid
                        _usedSwitches.Add(index);
                        _powerAt[index] = Time.timeAsDouble + PowerRestoreSeconds;
                        foreach (DoorSwitch sw in Map.Switches) if (sw.ConnectionIndex == index) sw.SetUsed(true);
                        RunToasts.Show($"Power relay engaged: door opens in {PowerRestoreSeconds:0} s", new Color(1f, 0.85f, 0.4f));
                        if (_session != null && _session.IsHost) SendDoors(ulong.MaxValue);
                    }
                    break;
                }
                case UseKind.Repair:
                {
                    if (index < 0 || index >= Map.Breaches.Count) return;
                    double now = Time.timeAsDouble;
                    if (_repairReady.TryGetValue(client, out double ready) && now < ready) return;
                    BreachBarricade b = Map.Breaches[index];
                    if (!b.RepairOne()) return;
                    _repairReady[client] = now + RepairCooldown;
                    if (_waves != null && _waves.IsRunning) _waves.AwardCredits(client, RepairCredits, "Barricade repaired");
                    MarkBreach(index);
                    break;
                }
            }
        }

        private void OnBoardBroken(BreachBarricade b)
        {
            if (Map == null || !Map.Breaches.Contains(b)) return;
            MarkBreach(b.Index);
        }

        private void MarkBreach(int index)
        {
            _dirtyBreaches.Add(index);
            _breachDirty = true;
        }

        /// <summary>True during an Emergency Lockdown: every sector door is shut until the terminal is hacked.</summary>
        public bool Sealed { get; private set; }

        /// <summary>
        /// Seals or unseals every sector door on this machine (each peer calls it from the synced event state). Sealing
        /// closes open doors at once; unsealing reopens the ones whose sector is open.
        /// </summary>
        public void SetSealed(bool sealedDoors)
        {
            if (Map == null || Sealed == sealedDoors) return;
            Sealed = sealedDoors;
            foreach (SectorDoor d in Map.Doors)
            {
                if (sealedDoors) d.CloseImmediate();
                else if (IsDoorOpen(d.ConnectionIndex)) d.Open();
            }
        }

        /// <summary>Rooms in open sectors that suit objective props (not the airlock, corridors or secret rooms).</summary>
        public void OpenObjectiveRooms(System.Collections.Generic.List<int> into, bool includeSpawn = false)
        {
            into.Clear();
            if (Map == null) return;
            var rooms = Map.Layout.Rooms;
            for (int i = 0; i < rooms.Count; i++)
            {
                PlacedRoom r = rooms[i];
                if (!IsRoomOpen(i)) continue;
                RoomRole role = r.Template.Role;
                if (role == RoomRole.Corridor || role == RoomRole.Secret) continue;
                if (role == RoomRole.Spawn && !includeSpawn) continue;
                into.Add(i);
            }
        }

        /// <summary>The airlock (spawn room) index, or -1.</summary>
        public int SpawnRoom
        {
            get
            {
                if (Map == null) return -1;
                var rooms = Map.Layout.Rooms;
                for (int i = 0; i < rooms.Count; i++) if (rooms[i].Template.Role == RoomRole.Spawn) return i;
                return -1;
            }
        }


        // ---------------------------------------------------------------- secrets and loot

        private void OnPanelBroken(SecretPanel panel)
        {
            if (Map == null || !Map.SecretPanels.Contains(panel)) return;
            GameLog.Info("Map", "A secret vent was opened.");
            if (_session != null && _session.IsHost)
            {
                using (var w = new FastBufferWriter(4, Allocator.Temp))
                {
                    w.WriteValueSafe((ushort)panel.ConnectionIndex);
                    _session.SendToRemoteClients(MsgSecret, w, NetworkDelivery.ReliableSequenced);
                }
            }
        }

        private void BreakPanel(int connection)
        {
            if (Map == null) return;
            foreach (SecretPanel p in Map.SecretPanels)
                if (p != null && p.ConnectionIndex == connection) p.Break();
        }

        private void OnLootTaken(LootKind kind, ulong taker)
        {
            if (kind == LootKind.SecretStash && _waves != null) _waves.AwardCredits(taker, SecretStashCredits, "Secret stash");
        }

        // ---------------------------------------------------------------- network

        private void OnPeerConnected(ulong clientId)
        {
            if (!_session.IsHost) return;
            SendMap(clientId);
            SendDoors(clientId);
            SendBreaches(clientId, onlyDirty: false);
            LootNetwork.SendState(clientId);
        }

        private void SendMap(ulong onlyTo)
        {
            var broken = new List<int>();
            if (Map != null)
                foreach (SecretPanel p in Map.SecretPanels) if (p != null && p.IsBroken) broken.Add(p.ConnectionIndex);
            using (var w = new FastBufferWriter(16 + broken.Count * 2, Allocator.Temp))
            {
                w.WriteValueSafe(Seed);
                w.WriteValueSafe((byte)OpenSectors);
                w.WriteValueSafe((byte)broken.Count);
                foreach (int c in broken) w.WriteValueSafe((ushort)c);
                if (onlyTo == ulong.MaxValue) _session.SendToRemoteClients(MsgMap, w, NetworkDelivery.ReliableSequenced);
                else _session.SendToClient(MsgMap, onlyTo, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void SendRespawnToClients()
        {
            if (_session == null || !_session.IsHost) return;
            using (var w = new FastBufferWriter(8, Allocator.Temp))
            {
                w.WriteValueSafe((byte)1);
                w.WriteValueSafe((byte)2); // 2 = "go to your spawn point"
                _session.SendToRemoteClients(MsgSector, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnMapMessage(ulong sender, FastBufferReader r)
        {
            if (_session.Role != SessionRole.Client) return;
            r.ReadValueSafe(out ulong seed);
            r.ReadValueSafe(out byte open);
            r.ReadValueSafe(out byte brokenCount);
            _pendingPanels.Clear();
            for (int i = 0; i < brokenCount; i++)
            {
                r.ReadValueSafe(out ushort c);
                _pendingPanels.Add(c);
            }
            _pendingSeed = seed;
            _pendingOpen = Mathf.Max(1, (int)open);
            if (seed == Seed && Map != null)
            {
                // Same map already built: just catch up.
                foreach (int c in _pendingPanels) BreakPanel(c);
                return;
            }
            if (seed == 0 && Map == null) return;
            RemoteWorldRequested?.Invoke(seed);
        }

        private void OnSectorMessage(ulong sender, FastBufferReader r)
        {
            if (_session.Role != SessionRole.Client) return;
            r.ReadValueSafe(out byte open);
            r.ReadValueSafe(out byte flag);
            if (flag == 2) MoveLocalPlayerToSpawn();
        }

        private void SendDoors(ulong onlyTo)
        {
            if (_session == null || !_session.IsHost || Map == null) return;
            var open = new List<int>();
            for (int i = 0; i < _doorOpen.Length; i++) if (_doorOpen[i]) open.Add(i);
            using (var w = new FastBufferWriter(16 + (open.Count + _usedSwitches.Count) * 2, Allocator.Temp))
            {
                w.WriteValueSafe(Seed);
                w.WriteValueSafe((ushort)open.Count);
                foreach (int i in open) w.WriteValueSafe((ushort)i);
                w.WriteValueSafe((ushort)_usedSwitches.Count);
                foreach (int i in _usedSwitches) w.WriteValueSafe((ushort)i);
                if (onlyTo == ulong.MaxValue) _session.SendToRemoteClients(MsgDoors, w, NetworkDelivery.ReliableSequenced);
                else _session.SendToClient(MsgDoors, onlyTo, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void SendBreaches(ulong onlyTo, bool onlyDirty)
        {
            if (_session == null || !_session.IsHost || Map == null) return;
            var list = new List<int>();
            if (onlyDirty) list.AddRange(_dirtyBreaches);
            else for (int i = 0; i < Map.Breaches.Count; i++) list.Add(i);
            if (onlyDirty) _dirtyBreaches.Clear();
            if (list.Count == 0) return;
            using (var w = new FastBufferWriter(12 + list.Count * 3, Allocator.Temp))
            {
                w.WriteValueSafe(Seed);
                w.WriteValueSafe((ushort)list.Count);
                foreach (int i in list)
                {
                    w.WriteValueSafe((ushort)i);
                    w.WriteValueSafe((byte)Map.Breaches[i].Boards);
                }
                if (onlyTo == ulong.MaxValue) _session.SendToRemoteClients(MsgBreach, w, NetworkDelivery.ReliableSequenced);
                else _session.SendToClient(MsgBreach, onlyTo, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnDoorsMessage(ulong sender, FastBufferReader r)
        {
            if (_session.Role != SessionRole.Client) return;
            r.ReadValueSafe(out ulong seed);
            r.ReadValueSafe(out ushort openCount);
            _pendingDoors.Clear();
            for (int i = 0; i < openCount; i++) { r.ReadValueSafe(out ushort c); _pendingDoors.Add(c); }
            r.ReadValueSafe(out ushort switchCount);
            _pendingSwitches.Clear();
            for (int i = 0; i < switchCount; i++) { r.ReadValueSafe(out ushort c); _pendingSwitches.Add(c); }
            _hasPendingDoors = true;
            if (Map != null && seed == Seed) ApplyPendingState();
        }

        private void OnBreachMessage(ulong sender, FastBufferReader r)
        {
            if (_session.Role != SessionRole.Client) return;
            r.ReadValueSafe(out ulong seed);
            r.ReadValueSafe(out ushort count);
            for (int i = 0; i < count; i++)
            {
                r.ReadValueSafe(out ushort index);
                r.ReadValueSafe(out byte boards);
                _pendingBoards.Add((index, boards));
            }
            if (Map != null && seed == Seed) ApplyPendingState();
        }

        /// <summary>Client: applies door, switch and barricade state from the host (now, or once the map is built).</summary>
        private void ApplyPendingState()
        {
            if (Map == null) return;
            if (_hasPendingDoors)
            {
                _hasPendingDoors = false;
                bool anyClosed = false;
                for (int i = 0; i < _doorOpen.Length; i++) if (_doorOpen[i] && !_pendingDoors.Contains(i)) anyClosed = true;
                if (anyClosed) // the host reset the doors (new run)
                {
                    for (int i = 0; i < _doorOpen.Length; i++) _doorOpen[i] = false;
                    OpenSectors = 1;
                    foreach (SectorDoor d in Map.Doors) d.CloseImmediate();
                    foreach (DoorSwitch sw in Map.Switches) sw.SetUsed(false);
                    SectorsChanged?.Invoke(OpenSectors, false);
                }
                _usedSwitches.Clear();
                foreach (int i in _pendingSwitches)
                {
                    _usedSwitches.Add(i);
                    foreach (DoorSwitch sw in Map.Switches) if (sw.ConnectionIndex == i) sw.SetUsed(true);
                }
                foreach (int i in _pendingDoors) OpenDoor(i, null, sync: false);
            }
            foreach ((int index, int boards) in _pendingBoards)
                if (index >= 0 && index < Map.Breaches.Count)
                {
                    BreachBarricade b = Map.Breaches[index];
                    bool repaired = boards == b.Boards + 1;
                    b.SetBoards(boards);
                    if (repaired) b.PlayNailing();
                }
            _pendingBoards.Clear();
        }

        private void OnSecretMessage(ulong sender, FastBufferReader r)
        {
            if (_session.Role != SessionRole.Client) return;
            r.ReadValueSafe(out ushort c);
            BreakPanel(c);
        }
    }
}
