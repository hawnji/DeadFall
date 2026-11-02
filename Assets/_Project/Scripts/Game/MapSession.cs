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
    /// enemy spawn points to the <see cref="WaveManager"/>, opens sectors as surges are cleared
    /// (<see cref="SectorSchedule"/>), and keeps clients in step. Only the seed goes over the network (T-09):
    /// "df.map" (seed, open sectors, broken vents) on join or regenerate, "df.sector" on unlocks, "df.secret" when a
    /// vent panel breaks. Seed 0 means "no map" (the sandbox).
    /// </summary>
    public sealed class MapSession : MonoBehaviour
    {
        private const string MsgMap = "df.map";
        private const string MsgSector = "df.sector";
        private const string MsgSecret = "df.secret";
        public const int SecretStashCredits = 250;

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

        public BuiltMap Map { get; private set; }
        public bool HasMap => Map != null;
        public ulong Seed => Map != null ? Map.Layout.Seed : 0UL;
        public int OpenSectors { get; private set; } = 1;
        public int SectorCount => Map != null ? Map.Layout.SectorCount : 0;
        public RoomKit Kit => _kit;
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
            _session.PeerConnected += OnPeerConnected;
            SecretPanel.BrokenByDamage += OnPanelBroken;
            LootNetwork.TakenOnHost += OnLootTaken;
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            SecretPanel.BrokenByDamage -= OnPanelBroken;
            LootNetwork.TakenOnHost -= OnLootTaken;
            if (_waves != null) _waves.PhaseChanged -= OnPhaseChanged;
            if (_session != null)
            {
                _session.UnregisterHandler(MsgMap);
                _session.UnregisterHandler(MsgSector);
                _session.UnregisterHandler(MsgSecret);
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
        }

        private bool IsAuthority => _session == null || _session.Role != SessionRole.Client;

        /// <summary>Builds the map for a seed. On the host this also tells every client.</summary>
        public BuiltMap Build(ulong seed)
        {
            Clear(notifyClients: false);
            float started = Time.realtimeSinceStartup;
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
            ApplySpawnPoints();
            GameLog.Info("Map", $"Seed {seed}: {layout.Rooms.Count} rooms, {layout.SectorCount} sectors, attempt {layout.Attempt}" +
                                $"{(layout.UsedFallback ? " (fallback layout)" : string.Empty)}, built in {LastBuildMilliseconds:0} ms, NavMesh {LastBakeMilliseconds:0} ms.");

            // A client applies what the host sent with the map message.
            if (!IsAuthority && _pendingSeed == seed)
            {
                foreach (int c in _pendingPanels) BreakPanel(c);
                _pendingPanels.Clear();
                if (_pendingOpen > 1) SetOpen(_pendingOpen, false);
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

        // ---------------------------------------------------------------- sectors

        /// <summary>Host/offline: opens every sector (Operations runs, T-47).</summary>
        public void OpenAllSectors()
        {
            if (!IsAuthority || Map == null || OpenSectors >= SectorCount) return;
            SetOpen(SectorCount, true);
        }

        /// <summary>Host/offline: opens the next sector now (debug panel).</summary>
        public void OpenNextSector()
        {
            if (!IsAuthority || Map == null || OpenSectors >= SectorCount) return;
            SetOpen(OpenSectors + 1, true);
        }

        private void SetOpen(int open, bool unlock)
        {
            if (Map == null) return;
            open = Mathf.Clamp(open, 1, Mathf.Max(1, SectorCount));
            bool reset = open < OpenSectors;
            OpenSectors = open;
            foreach (SectorDoor d in Map.Doors)
            {
                if (d.Sector < open && !Sealed) d.Open();
                else if (reset) d.CloseImmediate();
            }
            ApplySpawnPoints();
            if (_session != null && _session.IsHost)
            {
                using (var w = new FastBufferWriter(8, Allocator.Temp))
                {
                    w.WriteValueSafe((byte)open);
                    w.WriteValueSafe(unlock ? (byte)1 : (byte)0);
                    _session.SendToRemoteClients(MsgSector, w, NetworkDelivery.ReliableSequenced);
                }
            }
            if (unlock) GameLog.Info("Map", $"Sector {open - 1} unlocked.");
            SectorsChanged?.Invoke(open, unlock);
        }

        private void ApplySpawnPoints()
        {
            WaveManager waves = _waves != null ? _waves : WaveManager.Current;
            if (waves == null || Map == null) return;
            waves.ClearSpawnPoints();
            foreach (MapSpawnPoint p in Map.EnemySpawns)
                if (p.Sector < OpenSectors) waves.AddSpawnPoint(p.Position, p.Room);
        }

        private void OnPhaseChanged(RunPhase from, RunPhase to)
        {
            if (!IsAuthority || Map == null || _waves == null) return;
            if (from == RunPhase.NotStarted && to == RunPhase.Lull)
            {
                // New run: back to the first sector, everyone to the airlock.
                if (OpenSectors > 1) SetOpen(1, false);
                MoveLocalPlayerToSpawn();
                SendRespawnToClients();
                return;
            }
            if (to == RunPhase.Reward)
            {
                int open = SectorSchedule.OpenSectors(_waves.Surge, SectorCount);
                if (open > OpenSectors) SetOpen(open, true);
            }
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
                else if (d.Sector < OpenSectors) d.Open();
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
                if (r.Sector >= OpenSectors) continue;
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

        /// <summary>Surge after which the next sector opens, or 0 if all are open.</summary>
        public int NextUnlockSurge => Map != null && OpenSectors < SectorCount ? SectorSchedule.UnlockAfterSurge(OpenSectors) : 0;

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
                if (_pendingOpen != OpenSectors) SetOpen(_pendingOpen, false);
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
            if (flag == 2) { MoveLocalPlayerToSpawn(); return; }
            SetOpen(open, flag == 1);
        }

        private void OnSecretMessage(ulong sender, FastBufferReader r)
        {
            if (_session.Role != SessionRole.Client) return;
            r.ReadValueSafe(out ushort c);
            BreakPanel(c);
        }
    }
}
