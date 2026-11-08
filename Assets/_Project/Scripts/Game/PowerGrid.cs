using System;
using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Encounters;
using Deadfall.Net;
using Deadfall.Player;
using Deadfall.Rules.Common;
using Deadfall.Rules.Encounters;
using Deadfall.Rules.Engineering;
using Deadfall.Rules.World;
using Deadfall.World;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deadfall.Game
{
    /// <summary>
    /// The map's power grid (<see cref="PowerGridRules"/>). Every peer places the same 2–3 <see cref="GridGenerator"/>s
    /// from the map seed, one per sector on the main route. The host owns their state: random trips during surges, all
    /// of them in a Blackout, and the restart channel when a player stands at a tripped one. It syncs the state to
    /// clients ("df.grid"), feeds capacity to the squad's Arc Fences and Sentries (oldest first), and gates the
    /// power-locked doors' relays. With no generator up in the open sectors the lights go out.
    /// <para>
    /// Only generators in open sectors count: a sector still sealed off powers nothing until the squad opens it.
    /// </para>
    /// </summary>
    public sealed class PowerGrid : MonoBehaviour
    {
        private const string MsgState = "df.grid";
        private const string MsgUse = "df.griduse";
        private const float Range = 3.5f;
        private const float CapacityInterval = 0.5f;
        private const float SyncInterval = 0.25f;

        private readonly List<GridGenerator> _generators = new List<GridGenerator>(PowerGridRules.MaxGenerators);
        private readonly List<int> _rooms = new List<int>(PowerGridRules.MaxGenerators);
        private readonly List<ChannelProgress> _channels = new List<ChannelProgress>(PowerGridRules.MaxGenerators);
        private SessionService _session;
        private MapSession _map;
        private WaveManager _waves;
        private DeterministicRandom _rng = new DeterministicRandom(0x6E1DUL);
        private float _capacityTimer;
        private float _syncTimer;
        private bool _dirty;
        private float _tripAt = -1f;
        private float _surgeClock;
        private bool _wasDark;

        public static PowerGrid Current { get; private set; }

        /// <summary>Host: a generator finished restarting (its index).</summary>
        public event Action<int> GeneratorRestarted;

        public bool HasGenerators => _generators.Count > 0;
        public int GeneratorCount => _generators.Count;
        public IReadOnlyList<GridGenerator> Generators => _generators;
        /// <summary>Rooms holding a generator (other placements avoid them).</summary>
        public IReadOnlyList<int> Rooms => _rooms;

        /// <summary>Generators online in open sectors.</summary>
        public int Online
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _generators.Count; i++) if (_generators[i] != null && _generators[i].Online && Reachable(i)) n++;
                return n;
            }
        }

        /// <summary>True while no generator in the open sectors is up (lights out, relays dead).</summary>
        public bool IsDown => HasGenerators && Online == 0;

        private bool IsAuthority => _session == null || _session.Role != SessionRole.Client;

        // ---------------------------------------------------------------- lifecycle

        private void Awake()
        {
            Current = this;
            _session = SessionService.Instance;
            _session.RegisterHandler(MsgState, OnState);
            _session.RegisterHandler(MsgUse, OnUse);
            _session.PeerConnected += OnPeerConnected;
            MapSession.GridHasPower = () => !IsDown;
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            MapSession.GridHasPower = null;
            BindMap(null);
            BindWaves(null);
            Clear();
            if (_session == null) return;
            _session.UnregisterHandler(MsgState);
            _session.UnregisterHandler(MsgUse);
            _session.PeerConnected -= OnPeerConnected;
        }

        private void BindMap(MapSession m)
        {
            if (_map != null)
            {
                _map.MapBuilt -= OnMapBuilt;
                _map.MapCleared -= Clear;
            }
            _map = m;
            if (_map == null) return;
            _map.MapBuilt += OnMapBuilt;
            _map.MapCleared += Clear;
            if (_map.HasMap && _generators.Count == 0) OnMapBuilt(_map.Map);
        }

        private void BindWaves(WaveManager w)
        {
            if (_waves != null)
            {
                _waves.SurgePlanned -= OnSurgePlanned;
                _waves.PhaseChanged -= OnPhaseChanged;
            }
            _waves = w;
            if (_waves == null) return;
            _waves.SurgePlanned += OnSurgePlanned;
            _waves.PhaseChanged += OnPhaseChanged;
        }

        // ---------------------------------------------------------------- placement (every peer, from the seed)

        private void OnMapBuilt(BuiltMap map)
        {
            Clear();
            if (map == null) return;
            var rng = new DeterministicRandom(map.Layout.Seed ^ 0x9E3779B97F4A7C15UL);
            int sectors = Mathf.Max(1, map.Layout.SectorCount);
            int count = PowerGridRules.GeneratorCount(sectors);
            var candidates = new List<int>(8);
            for (int g = 0; g < count; g++)
            {
                // One per sector; with fewer sectors than generators the extras go to the last one.
                int sector = Mathf.Min(g, sectors - 1);
                candidates.Clear();
                for (int i = 0; i < map.Layout.Rooms.Count; i++)
                {
                    PlacedRoom r = map.Layout.Rooms[i];
                    RoomRole role = r.Template.Role;
                    if (r.Sector != sector || _rooms.Contains(i)) continue;
                    if (role == RoomRole.Corridor || role == RoomRole.Secret || role == RoomRole.Spawn) continue;
                    if (map.Layout.SideDoorTo(i) >= 0) continue; // main route only: never behind a locked side door
                    candidates.Add(i);
                }
                if (candidates.Count == 0) continue;
                int room = candidates[rng.Range(0, candidates.Count)];
                FindSpot(map, room, rng, out Vector3 p, out float yaw);
                _rooms.Add(room);
                _generators.Add(GridGenerator.Create(_generators.Count, p, yaw, transform));
                _channels.Add(new ChannelProgress(PowerGridRules.RestartSeconds));
            }
            _rng = new DeterministicRandom(map.Layout.Seed ^ 0xC0FFEEUL);
            _dirty = true;
            GameLog.Info("Grid", $"{_generators.Count} grid generators placed.");
        }

        private const int PlaceMask = ~(1 << 2);
        private static readonly List<Vector3> Doorways = new List<Vector3>(32);

        /// <summary>
        /// A floor spot in <paramref name="room"/> for a generator: well clear of every doorway and breach-closet
        /// opening (so it never blocks a closet or a way through), not touching walls or props, and on the NavMesh.
        /// Deterministic from the seed, so every peer picks the same spot.
        /// </summary>
        private static void FindSpot(BuiltMap map, int room, DeterministicRandom rng, out Vector3 position, out float yaw)
        {
            Physics.SyncTransforms();
            WallPlacement.Doorways(map, Doorways);
            Vector3 c = map.RoomCenter(room);
            Bounds bounds = map.RoomBounds[room];
            Vector3 fallback = c;
            float fallbackYaw = 0f;
            bool haveFallback = false;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                float a = rng.NextFloat() * Mathf.PI * 2f;
                float radius = attempt == 0 ? 1.8f : rng.NextFloat() * 4f;
                float y = Mathf.Round(rng.NextFloat() * 4f) * 90f;
                Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) continue;
                p = hit.position;
                if (p.x < bounds.min.x || p.x > bounds.max.x || p.z < bounds.min.z || p.z > bounds.max.z) continue;
                if (map.RoomAt(p) != room) continue; // a notch cell of a shaped room (breach closets sit there)
                if (WallPlacement.NearAny(p, Doorways, 3.6f)) continue;
                if (!haveFallback) { fallback = p; fallbackYaw = y; haveFallback = true; }
                // Footprint (1.9 x 1.2 skid) plus a walkway around it must be free of walls and props.
                Quaternion rot = Quaternion.Euler(0f, y, 0f);
                if (Physics.CheckBox(p + Vector3.up * 1f, new Vector3(1.35f, 0.85f, 1.0f), rot, PlaceMask, QueryTriggerInteraction.Ignore)) continue;
                position = p;
                yaw = y;
                return;
            }
            if (!haveFallback && NavMesh.SamplePosition(c, out NavMeshHit h, 4f, NavMesh.AllAreas)) fallback = h.position;
            position = fallback;
            yaw = fallbackYaw;
        }

        private void Clear()
        {
            foreach (GridGenerator g in _generators) if (g != null) Destroy(g.gameObject);
            _generators.Clear();
            _rooms.Clear();
            _channels.Clear();
            _tripAt = -1f;
            if (IsAuthority) EngineeringDirector.Current?.ApplyGridCapacity(int.MaxValue); // no grid: everything runs
            if (_wasDark)
            {
                _wasDark = false;
                BlackoutLighting.SetDark(false);
            }
        }

        private bool Reachable(int i)
        {
            if (_map == null || !_map.HasMap || i < 0 || i >= _rooms.Count) return false;
            int room = _rooms[i];
            return room >= 0 && room < _map.Map.Layout.Rooms.Count && _map.Map.Layout.Rooms[room].Sector < _map.OpenSectors;
        }

        // ---------------------------------------------------------------- commands

        /// <summary>
        /// Host: trips every generator in the open sectors (Blackout). Returns how many are now down there (including
        /// any that had already tripped), which is how many restarts bring the power back.
        /// </summary>
        public int TripAll()
        {
            if (!IsAuthority) return 0;
            int down = 0;
            for (int i = 0; i < _generators.Count; i++)
            {
                if (!Reachable(i) || _generators[i] == null) continue;
                Trip(i, false);
                if (!_generators[i].Online) down++;
            }
            return down;
        }

        /// <summary>Host: brings every generator back (the Blackout ended some other way, or the run reset).</summary>
        public void RestoreAll()
        {
            if (!IsAuthority) return;
            for (int i = 0; i < _generators.Count; i++)
            {
                if (_generators[i] == null || _generators[i].Online) continue;
                _channels[i] = new ChannelProgress(PowerGridRules.RestartSeconds);
                _generators[i].SetState(true, false, 0f);
                _dirty = true;
            }
        }

        /// <summary>Any machine: the local player pressed F on a tripped generator.</summary>
        public void RequestRestart(int index)
        {
            if (IsAuthority)
            {
                StartRestart(index);
                return;
            }
            using (var w = new FastBufferWriter(4, Allocator.Temp))
            {
                w.WriteValueSafe((byte)Mathf.Clamp(index, 0, 255));
                _session.SendToServer(MsgUse, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnUse(ulong sender, FastBufferReader r)
        {
            if (!IsAuthority) return;
            r.ReadValueSafe(out byte index);
            StartRestart(index);
        }

        private void StartRestart(int index)
        {
            if (index < 0 || index >= _generators.Count || _generators[index] == null || _generators[index].Online) return;
            if (!Reachable(index)) return;
            _channels[index].Start();
            _generators[index].SetState(false, true, _channels[index].Value01);
            _dirty = true;
        }

        private bool Trip(int i, bool toast)
        {
            GridGenerator g = _generators[i];
            if (g == null || !g.Online) return false;
            _channels[i] = new ChannelProgress(PowerGridRules.RestartSeconds);
            g.SetState(false, false, 0f);
            _dirty = true;
            GameAudio.Play(SfxId.BarricadeBreak, g.transform.position + Vector3.up, 0.7f, 0.7f);
            if (toast) Toast($"GRID GENERATOR {i + 1} TRIPPED · structures and doors may lose power", new Color(1f, 0.55f, 0.2f));
            return true;
        }

        // ---------------------------------------------------------------- run flow (host)

        private void OnSurgePlanned(SurgePlan plan)
        {
            if (!IsAuthority || plan == null) return;
            _surgeClock = 0f;
            _tripAt = PowerGridRules.RollTrip(_rng, plan.Surge, Online);
        }

        private void OnPhaseChanged(RunPhase from, RunPhase to)
        {
            if (!IsAuthority) return;
            if (to != RunPhase.Surge) _tripAt = -1f;
            if (to == RunPhase.NotStarted || to == RunPhase.Complete || to == RunPhase.Failed) RestoreAll();
        }

        private void Update()
        {
            if (MapSession.Current != _map) BindMap(MapSession.Current);
            if (WaveManager.Current != _waves) BindWaves(WaveManager.Current);
            float dt = Time.deltaTime;

            if (IsAuthority && HasGenerators)
            {
                TickTrip(dt);
                TickRestarts(dt);
                _capacityTimer -= dt;
                if (_capacityTimer <= 0f)
                {
                    _capacityTimer = CapacityInterval;
                    EngineeringDirector.Current?.ApplyGridCapacity(PowerGridRules.Capacity(Online));
                }
                _syncTimer -= dt;
                if (_dirty && _syncTimer <= 0f)
                {
                    _syncTimer = SyncInterval;
                    _dirty = false;
                    Broadcast();
                }
            }

            bool dark = IsDown;
            if (dark != _wasDark)
            {
                _wasDark = dark;
                BlackoutLighting.SetDark(dark);
                Toast(dark ? "GRID DOWN · lights out, fences and sentries offline" : "GRID POWER RESTORED",
                    dark ? new Color(1f, 0.35f, 0.25f) : new Color(0.4f, 1f, 0.5f));
            }
        }

        private void TickTrip(float dt)
        {
            if (_tripAt < 0f || _waves == null || _waves.Phase != RunPhase.Surge) return;
            _surgeClock += dt;
            if (_surgeClock < _tripAt) return;
            _tripAt = -1f;
            if (Online < 2) return; // never the last one outside a Blackout
            int start = _rng.Range(0, _generators.Count);
            for (int k = 0; k < _generators.Count; k++)
            {
                int i = (start + k) % _generators.Count;
                if (Reachable(i) && Trip(i, true)) return;
            }
        }

        private void TickRestarts(float dt)
        {
            for (int i = 0; i < _generators.Count; i++)
            {
                GridGenerator g = _generators[i];
                ChannelProgress c = _channels[i];
                if (g == null || g.Online || !c.Started) continue;
                float before = c.Value01;
                if (c.Tick(dt, PlayerNear(g.transform.position)))
                {
                    _channels[i] = new ChannelProgress(PowerGridRules.RestartSeconds);
                    g.SetState(true, false, 0f);
                    _dirty = true;
                    GameAudio.Play(SfxId.ObjectiveDone, g.transform.position + Vector3.up, 0.8f, 1f);
                    Toast($"GRID GENERATOR {i + 1} BACK ONLINE", new Color(0.4f, 1f, 0.5f));
                    GeneratorRestarted?.Invoke(i);
                    continue;
                }
                g.SetState(false, true, c.Value01);
                if (Mathf.Abs(before - c.Value01) > 0f) _dirty = true;
            }
        }

        private static bool PlayerNear(Vector3 pos)
        {
            IReadOnlyList<ICombatTarget> all = CombatTargets.All;
            for (int i = 0; i < all.Count; i++)
            {
                ICombatTarget t = all[i];
                if (t?.TargetTransform == null || t.Damageable == null || !t.Damageable.IsAlive) continue;
                if (PlayerIdentity.Of(t.TargetTransform.gameObject) == null) continue;
                if ((t.TargetTransform.position - pos).sqrMagnitude <= Range * Range) return true;
            }
            return false;
        }

        /// <summary>Host shows the toast and tells every client to show it too (inside the next state message).</summary>
        private string _pendingToast;
        private Color _pendingToastColor;

        private void Toast(string text, Color color)
        {
            // Every peer derives the "grid down / restored" toast from the synced state itself; only host-side
            // trip and restart lines travel.
            RunToasts.Show(text, color, 3.5f);
            if (IsAuthority && !text.StartsWith("GRID DOWN", StringComparison.Ordinal) && !text.StartsWith("GRID POWER", StringComparison.Ordinal))
            {
                _pendingToast = text;
                _pendingToastColor = color;
                _dirty = true;
            }
        }

        // ---------------------------------------------------------------- sync

        private void OnPeerConnected(ulong clientId)
        {
            if (!IsAuthority || !HasGenerators) return;
            using (var w = new FastBufferWriter(64, Allocator.Temp))
            {
                Write(w, null);
                _session.SendToClient(MsgState, clientId, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void Broadcast()
        {
            string toast = _pendingToast;
            _pendingToast = null;
            if (_session == null || !_session.IsHost || _session.RemotePeerCount == 0) return;
            using (var w = new FastBufferWriter(256, Allocator.Temp))
            {
                Write(w, toast);
                _session.SendToRemoteClients(MsgState, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void Write(FastBufferWriter w, string toast)
        {
            w.WriteValueSafe((byte)_generators.Count);
            for (int i = 0; i < _generators.Count; i++)
            {
                GridGenerator g = _generators[i];
                byte flags = (byte)((g != null && g.Online ? 1 : 0) | (g != null && g.Restarting ? 2 : 0));
                w.WriteValueSafe(flags);
                w.WriteValueSafe((byte)Mathf.RoundToInt((g != null ? g.Progress01 : 0f) * 255f));
            }
            bool hasToast = !string.IsNullOrEmpty(toast);
            w.WriteValueSafe(hasToast ? (byte)1 : (byte)0);
            if (!hasToast) return;
            if (toast.Length > 120) toast = toast.Substring(0, 120);
            w.WriteValueSafe((byte)toast.Length);
            for (int i = 0; i < toast.Length; i++) w.WriteValueSafe((ushort)toast[i]);
            Color32 c = _pendingToastColor;
            w.WriteValueSafe(c.r);
            w.WriteValueSafe(c.g);
            w.WriteValueSafe(c.b);
        }

        private void OnState(ulong sender, FastBufferReader r)
        {
            if (IsAuthority) return;
            r.ReadValueSafe(out byte count);
            for (int i = 0; i < count; i++)
            {
                r.ReadValueSafe(out byte flags);
                r.ReadValueSafe(out byte progress);
                if (i < _generators.Count && _generators[i] != null)
                    _generators[i].SetState((flags & 1) != 0, (flags & 2) != 0, progress / 255f);
            }
            r.ReadValueSafe(out byte hasToast);
            if (hasToast == 0) return;
            r.ReadValueSafe(out byte n);
            var chars = new char[n];
            for (int i = 0; i < n; i++)
            {
                r.ReadValueSafe(out ushort ch);
                chars[i] = (char)ch;
            }
            r.ReadValueSafe(out byte cr);
            r.ReadValueSafe(out byte cg);
            r.ReadValueSafe(out byte cb);
            RunToasts.Show(new string(chars), new Color32(cr, cg, cb, 255), 3.5f);
        }
    }
}
