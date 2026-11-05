using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Encounters;
using Deadfall.Net;
using Deadfall.Player;
using Deadfall.Rules.Combat;
using Deadfall.Rules.Common;
using Deadfall.Rules.Encounters;
using Deadfall.Rules.World;
using Deadfall.World;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deadfall.Game
{
    /// <summary>
    /// Runs the Gun Dispenser in Survival and Endless runs (not Operations). The host owns it: it takes the Field
    /// Credits, rolls the weapon (<see cref="GunDispenserRules"/>), times the spin and the offer, and after a few
    /// uses sometimes moves it to another open sector (with a refund). Every peer gets the state over "df.gund" and
    /// shows the same <see cref="GunDispenser"/>; pressing F sends "df.gunduse". A screen marker points the way to it.
    /// </summary>
    public sealed class GunDispenserDirector : MonoBehaviour
    {
        private const string MsgState = "df.gund";
        private const string MsgUse = "df.gunduse";
        private const float MarkerRange = 80f;

        private struct Spot
        {
            public Vector3 Position;
            public float Yaw;
            public int Sector;
        }

        private readonly List<Spot> _spots = new List<Spot>(8);
        private readonly List<int> _candidates = new List<int>(16);
        private SessionService _session;
        private WaveManager _waves;
        private MapSession _map;
        private GunDispenser _dispenser;
        private DeterministicRandom _rng = new DeterministicRandom(0x6D15UL);
        private int _spot = -1;
        private int _uses;
        private ulong _buyer;
        private WeaponDefinition _offer;
        private WeaponMods _offerMods;
        private float _timer;
        private GunDispenser.Mode _mode = GunDispenser.Mode.Hidden;

        public static GunDispenserDirector Current { get; private set; }
        public GunDispenser Dispenser => _dispenser;

        private bool IsAuthority => _session == null || _session.Role != SessionRole.Client;

        // ---------------------------------------------------------------- lifecycle

        private void Awake()
        {
            Current = this;
            _session = SessionService.Instance;
            _session.RegisterHandler(MsgState, OnState);
            _session.RegisterHandler(MsgUse, OnUse);
            _session.PeerConnected += OnPeerConnected;
            _dispenser = GunDispenser.Create(transform);
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            BindWaves(null);
            BindMap(null);
            if (_session == null) return;
            _session.UnregisterHandler(MsgState);
            _session.UnregisterHandler(MsgUse);
            _session.PeerConnected -= OnPeerConnected;
        }

        private void BindWaves(WaveManager w)
        {
            if (_waves != null) _waves.PhaseChanged -= OnPhaseChanged;
            _waves = w;
            if (_waves != null) _waves.PhaseChanged += OnPhaseChanged;
        }

        private void BindMap(MapSession m)
        {
            if (_map != null) _map.MapCleared -= OnMapCleared;
            _map = m;
            if (_map != null) _map.MapCleared += OnMapCleared;
        }

        private void OnMapCleared()
        {
            _spots.Clear();
            if (IsAuthority) Hide();
        }

        private void OnPhaseChanged(RunPhase from, RunPhase to)
        {
            if (!IsAuthority) return;
            if (from == RunPhase.NotStarted && to == RunPhase.Lull) BeginRun();
            else if (to == RunPhase.NotStarted || to == RunPhase.Complete || to == RunPhase.Failed) Hide();
        }

        /// <summary>Host: a Survival or Endless run started on a generated map; the dispenser appears in the first sector.</summary>
        private void BeginRun()
        {
            Hide();
            if (_waves == null || _waves.IsOperation || _map == null || !_map.HasMap) return;
            BuildSpots();
            if (_spots.Count == 0) return;
            _rng = new DeterministicRandom(_waves.Seed ^ 0x47554E44UL);
            int first = PickSpot(-1);
            if (first < 0) return;
            _spot = first;
            _uses = 0;
            SetMode(GunDispenser.Mode.Idle, 0f);
        }

        /// <summary>
        /// One spot per sector, in a main-route room away from the grid generators: backed against a solid stretch
        /// of wall, facing into the room, and well clear of doorways, breach closets, switches, vents and loot so it
        /// never blocks a way through.
        /// </summary>
        private void BuildSpots()
        {
            _spots.Clear();
            BuiltMap map = _map.Map;
            var rng = new DeterministicRandom(map.Layout.Seed ^ 0x5D15A5E7UL);
            IReadOnlyList<int> taken = PowerGrid.Current != null ? PowerGrid.Current.Rooms : null;
            var rooms = new List<int>(8);
            for (int sector = 0; sector < map.Layout.SectorCount; sector++)
            {
                rooms.Clear();
                for (int i = 0; i < map.Layout.Rooms.Count; i++)
                {
                    PlacedRoom r = map.Layout.Rooms[i];
                    RoomRole role = r.Template.Role;
                    if (r.Sector != sector || (role != RoomRole.Room && role != RoomRole.Objective && role != RoomRole.Spawn)) continue;
                    if (map.Layout.SideDoorTo(i) >= 0) continue;
                    if (taken != null && Contains(taken, i)) continue;
                    rooms.Add(i);
                }
                // Prefer a real room over the airlock.
                if (rooms.Count > 1) rooms.RemoveAll(i => map.Layout.Rooms[i].Template.Role == RoomRole.Spawn);
                if (rooms.Count == 0) continue;
                // Shuffle, then take the first room with a good wall spot.
                for (int i = rooms.Count - 1; i > 0; i--)
                {
                    int j = rng.Range(0, i + 1);
                    (rooms[i], rooms[j]) = (rooms[j], rooms[i]);
                }
                bool placed = false;
                foreach (int room in rooms)
                {
                    if (!WallSpot(map, room, rng, out Vector3 pos, out float yaw)) continue;
                    _spots.Add(new Spot { Position = pos, Yaw = yaw, Sector = sector });
                    placed = true;
                    break;
                }
                if (!placed) GameLog.Warn("Guns", $"No wall spot for the Gun Dispenser in sector {sector}.");
            }
        }

        /// <summary>Half the cabinet's footprint (1.5 m wide, 0.9 m deep) plus the roof band's overhang.</summary>
        private const float HalfWidth = 0.8f;
        private const float HalfDepth = 0.5f;
        /// <summary>Gap between the wall and the cabinet's back.</summary>
        private const float WallGap = 0.06f;
        /// <summary>Minimum distance from a doorway or breach centre (half a doorway, the cabinet, and a walkway).</summary>
        private const float DoorClearance = 3.2f;
        /// <summary>Minimum distance from switches, vents, loot and secret panels.</summary>
        private const float PropClearance = 1.8f;

        private readonly List<Vector3> _avoidDoors = new List<Vector3>(16);
        private readonly List<Vector3> _avoidProps = new List<Vector3>(32);
        private readonly List<Vector3> _wallSpots = new List<Vector3>(32);
        private readonly List<float> _wallYaws = new List<float>(32);

        /// <summary>A spot against a wall of <paramref name="room"/>, or false when none is clear.</summary>
        private bool WallSpot(BuiltMap map, int room, DeterministicRandom rng, out Vector3 pos, out float yaw)
        {
            pos = default;
            yaw = 0f;
            Layout layout = map.Layout;
            PlacedRoom r = layout.Rooms[room];
            float cs = map.CellSize;
            float floorY = map.RoomBounds[room].min.y;

            // Doorways and breach openings of this room (any room's, really: neighbours share the wall).
            _avoidDoors.Clear();
            for (int i = 0; i < layout.Rooms.Count; i++)
            {
                PlacedRoom other = layout.Rooms[i];
                for (int s = 0; s < other.Template.Sockets.Count; s++)
                    if (layout.IsConnected(i, s, out _)) _avoidDoors.Add(Edge(other.SocketCell(s), other.SocketFacing(s), cs));
            }
            foreach (Breach br in layout.Breaches) _avoidDoors.Add(Edge(br.Inner, br.Facing, cs));
            _avoidProps.Clear();
            foreach (DoorSwitch sw in map.Switches) if (sw != null) _avoidProps.Add(sw.transform.position);
            foreach (SecretPanel sp in map.SecretPanels) if (sp != null) _avoidProps.Add(sp.transform.position);
            foreach (CrawlVent v in map.CrawlVents) _avoidProps.Add(v.Position);
            foreach (MapLootPoint l in map.Loot) _avoidProps.Add(l.Position);
            foreach (SectorDoor d in map.Doors) if (d != null) _avoidDoors.Add(d.transform.position);

            _wallSpots.Clear();
            _wallYaws.Clear();
            foreach (Cell c in r.Cells())
            {
                for (int d = 0; d < 4; d++)
                {
                    Dir dir = (Dir)d;
                    if (r.Contains(c.Step(dir))) continue; // not an outer wall of the room
                    Vector3 edge = Edge(c, dir, cs);
                    var outward = new Vector3(dir.Dx(), 0f, dir.Dz());
                    var guess = edge - outward * (HalfDepth + WallGap);
                    if (Near(guess, _avoidDoors, DoorClearance) || Near(guess, _avoidProps, PropClearance)) continue;
                    if (!Fits(guess, outward, floorY, out Vector3 floor)) continue;
                    _wallSpots.Add(floor);
                    _wallYaws.Add(Quaternion.LookRotation(-outward).eulerAngles.y);
                }
            }
            if (_wallSpots.Count == 0) return false;
            int pick = rng.Range(0, _wallSpots.Count);
            pos = _wallSpots[pick];
            yaw = _wallYaws[pick];
            return true;
        }

        /// <summary>
        /// The cabinet fits at <paramref name="at"/> with its back to the wall in <paramref name="outward"/>: floor
        /// under it, solid wall behind its whole width, nothing in its footprint, and a clear standing area in front
        /// that is on the NavMesh.
        /// </summary>
        private static bool Fits(Vector3 at, Vector3 outward, float floorY, out Vector3 floor)
        {
            floor = default;
            Vector3? f = FloorPoint(at, floorY);
            if (f == null) return false;
            floor = f.Value;
            const int mask = ~(1 << 2);
            Vector3 side = Vector3.Cross(Vector3.up, outward);
            // Wall right behind the back, at both sides and the middle (no doorway or gap behind it).
            for (int i = -1; i <= 1; i++)
            {
                Vector3 from = floor + Vector3.up * 1.2f + side * (i * (HalfWidth - 0.1f));
                if (!Physics.Raycast(from, outward, out RaycastHit hit, HalfDepth + WallGap + 0.35f, mask, QueryTriggerInteraction.Ignore)) return false;
                if (Vector3.Dot(hit.normal, -outward) < 0.9f) return false; // a corner or a prop, not the flat wall
            }
            Quaternion rot = Quaternion.LookRotation(-outward);
            // Footprint (shrunk a touch so the wall itself and the floor don't count).
            if (Physics.CheckBox(floor + Vector3.up * 1.2f, new Vector3(HalfWidth - 0.05f, 1f, HalfDepth - 0.05f), rot, mask, QueryTriggerInteraction.Ignore))
                return false;
            // A clear place to stand and use it.
            Vector3 front = floor - outward * (HalfDepth + 0.9f);
            if (Physics.CheckBox(front + Vector3.up * 1f, new Vector3(HalfWidth, 0.8f, 0.6f), rot, mask, QueryTriggerInteraction.Ignore))
                return false;
            if (!NavMesh.SamplePosition(front, out NavMeshHit hitNav, 0.6f, NavMesh.AllAreas)) return false;
            return Mathf.Abs(hitNav.position.y - floor.y) <= 0.3f;
        }

        private static Vector3 Edge(Cell c, Dir facing, float cs) =>
            new Vector3((c.X + 0.5f + facing.Dx() * 0.5f) * cs, 0f, (c.Z + 0.5f + facing.Dz() * 0.5f) * cs);

        private static bool Near(Vector3 p, List<Vector3> points, float distance)
        {
            float sq = distance * distance;
            foreach (Vector3 q in points)
            {
                float dx = p.x - q.x, dz = p.z - q.z;
                if (dx * dx + dz * dz < sq) return true;
            }
            return false;
        }

        /// <summary>The floor under <paramref name="at"/> (a ray down from 1.5 m above the room's floor level), or null.</summary>
        private static Vector3? FloorPoint(Vector3 at, float floorY)
        {
            var from = new Vector3(at.x, floorY + 1.5f, at.z);
            if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, 3f, ~(1 << 2), QueryTriggerInteraction.Ignore)) return null;
            if (hit.point.y > floorY + 0.5f) return null; // landed on furniture
            return hit.point;
        }

        private static bool Contains(IReadOnlyList<int> list, int v)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == v) return true;
            return false;
        }

        /// <summary>A spot in an open sector other than <paramref name="except"/>, or -1.</summary>
        private int PickSpot(int except)
        {
            _candidates.Clear();
            int open = _map != null ? _map.OpenSectors : 1;
            for (int i = 0; i < _spots.Count; i++)
                if (i != except && _spots[i].Sector < open) _candidates.Add(i);
            return _candidates.Count > 0 ? _candidates[_rng.Range(0, _candidates.Count)] : -1;
        }

        private bool CanMove() => PickableCount() > 0;

        private int PickableCount()
        {
            int open = _map != null ? _map.OpenSectors : 1, n = 0;
            for (int i = 0; i < _spots.Count; i++) if (i != _spot && _spots[i].Sector < open) n++;
            return n;
        }

        private void Hide()
        {
            _spot = -1;
            _offer = null;
            _offerMods = null;
            SetMode(GunDispenser.Mode.Hidden, 0f);
        }

        // ---------------------------------------------------------------- use

        /// <summary>The local player pressed F on the dispenser.</summary>
        public void RequestUse()
        {
            if (IsAuthority)
            {
                HandleUse(_session != null ? _session.LocalClientId : 0UL);
                return;
            }
            using (var w = new FastBufferWriter(4, Allocator.Temp))
            {
                w.WriteValueSafe((byte)1);
                _session.SendToServer(MsgUse, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnUse(ulong sender, FastBufferReader r)
        {
            if (!_session.IsHost) return;
            HandleUse(sender);
        }

        private void HandleUse(ulong who)
        {
            if (_spot < 0 || _waves == null || !_waves.IsRunning) return;
            PlayerIdentity p = PlayerIdentity.Find(who);
            Vector3 at = _spots[_spot].Position;
            if (p != null && (p.transform.position - at).sqrMagnitude > GunDispenserRules.UseRange * GunDispenserRules.UseRange * 2.25f) return;
            RunEconomy economy = RunEconomy.Current;
            switch (_mode)
            {
                case GunDispenser.Mode.Idle:
                    if (!_waves.TrySpendCredits(who, GunDispenserRules.Price))
                    {
                        economy?.Notify(who, "Not enough Field Credits");
                        return;
                    }
                    _buyer = who;
                    _uses++;
                    RollResult(p);
                    SetMode(GunDispenser.Mode.Spinning, GunDispenserRules.SpinSeconds);
                    GameAudio.Play(SfxId.UiConfirm, at + Vector3.up * 1.4f);
                    break;
                case GunDispenser.Mode.Offering:
                    if (who != _buyer || _offer == null) return;
                    economy?.GiveWeapon(who, _offer, _offerMods);
                    _offer = null;
                    _offerMods = null;
                    SetMode(GunDispenser.Mode.Idle, 0f);
                    break;
            }
        }

        /// <summary>Host: decides this pull now (a weapon, or "moving" with a refund) so the spin can't be gamed.</summary>
        private void RollResult(PlayerIdentity buyer)
        {
            if (GunDispenserRules.RollMove(_rng, _uses - 1, CanMove()))
            {
                _offer = null;
                _offerMods = null;
                return;
            }
            var guns = new List<int>(WeaponCatalog.All.Count);
            for (int i = 0; i < WeaponCatalog.All.Count; i++)
                if (WeaponCatalog.All[i] != null && WeaponCatalog.All[i].Kind != WeaponKind.Melee) guns.Add(i);
            int exclude = -1;
            PlayerWeapons weapons = buyer != null ? buyer.GetComponent<PlayerWeapons>() : null;
            if (weapons != null && weapons.CurrentDefinition != null) exclude = WeaponCatalog.IndexOf(weapons.CurrentDefinition);
            int index = GunDispenserRules.PickIndex(_rng, guns, exclude);
            _offer = WeaponCatalog.At(index);
            WeaponTier tier = GunDispenserRules.RollTier(_rng, _waves != null ? _waves.Surge : 1);
            _offerMods = _offer != null ? LootRules.RollMods(_rng, tier, _offer.Category) : null;
        }

        // ---------------------------------------------------------------- host loop

        private void Update()
        {
            if (WaveManager.Current != _waves) BindWaves(WaveManager.Current);
            if (MapSession.Current != _map) BindMap(MapSession.Current);
            if (!IsAuthority || _mode == GunDispenser.Mode.Hidden || _mode == GunDispenser.Mode.Idle) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            switch (_mode)
            {
                case GunDispenser.Mode.Spinning:
                    if (_offer != null)
                    {
                        SetMode(GunDispenser.Mode.Offering, GunDispenserRules.OfferSeconds);
                        GameAudio.Play(SfxId.Pickup, _spots[_spot].Position + Vector3.up * 1.4f);
                    }
                    else
                    {
                        _waves?.AwardCredits(_buyer, GunDispenserRules.Price, "Gun Dispenser refund");
                        SetMode(GunDispenser.Mode.Leaving, GunDispenserRules.LeaveSeconds);
                        Toast("The Gun Dispenser is moving. Pull refunded", new Color(1f, 0.6f, 0.3f));
                        GameAudio.Play(SfxId.UiDeny, _spots[_spot].Position + Vector3.up * 1.4f);
                    }
                    break;
                case GunDispenser.Mode.Offering:
                    _offer = null; // not taken in time
                    _offerMods = null;
                    SetMode(GunDispenser.Mode.Idle, 0f);
                    break;
                case GunDispenser.Mode.Leaving:
                {
                    int next = PickSpot(_spot);
                    if (next >= 0) _spot = next;
                    _uses = 0;
                    SetMode(GunDispenser.Mode.Idle, 0f);
                    Toast($"The Gun Dispenser reappeared in sector {_spots[_spot].Sector + 1}", new Color(1f, 0.8f, 0.35f));
                    break;
                }
            }
        }

        private void SetMode(GunDispenser.Mode mode, float seconds)
        {
            _mode = mode;
            _timer = seconds;
            Spot s = _spot >= 0 && _spot < _spots.Count ? _spots[_spot] : default;
            _dispenser.Apply(mode, s.Position, s.Yaw, _buyer, mode == GunDispenser.Mode.Offering ? _offer : null,
                mode == GunDispenser.Mode.Offering ? _offerMods : null, seconds);
            Broadcast(ulong.MaxValue, null);
        }

        private void Toast(string text, Color color)
        {
            RunToasts.Show(text, color, 4f);
            Broadcast(ulong.MaxValue, text);
        }

        // ---------------------------------------------------------------- sync

        private void OnPeerConnected(ulong clientId)
        {
            if (IsAuthority) Broadcast(clientId, null);
        }

        private void Broadcast(ulong onlyTo, string toast)
        {
            if (_session == null || !_session.IsHost || _session.RemotePeerCount == 0) return;
            byte[] mods = _mode == GunDispenser.Mode.Offering && _offerMods != null ? _offerMods.Encode() : new byte[0];
            toast = toast ?? string.Empty;
            if (toast.Length > 120) toast = toast.Substring(0, 120);
            using (var w = new FastBufferWriter(64 + mods.Length + toast.Length * 2, Allocator.Temp))
            {
                Spot s = _spot >= 0 && _spot < _spots.Count ? _spots[_spot] : default;
                w.WriteValueSafe((byte)_mode);
                w.WriteValueSafe(s.Position);
                w.WriteValueSafe(s.Yaw);
                w.WriteValueSafe(_buyer);
                w.WriteValueSafe(_mode == GunDispenser.Mode.Offering && _offer != null ? (byte)WeaponCatalog.IndexOf(_offer) : (byte)255);
                w.WriteValueSafe((byte)mods.Length);
                foreach (byte b in mods) w.WriteValueSafe(b);
                w.WriteValueSafe(Mathf.Max(0f, _timer));
                w.WriteValueSafe((byte)toast.Length);
                for (int i = 0; i < toast.Length; i++) w.WriteValueSafe((ushort)toast[i]);
                if (onlyTo == ulong.MaxValue) _session.SendToRemoteClients(MsgState, w, NetworkDelivery.ReliableSequenced);
                else _session.SendToClient(MsgState, onlyTo, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnState(ulong sender, FastBufferReader r)
        {
            if (IsAuthority) return;
            r.ReadValueSafe(out byte mode);
            r.ReadValueSafe(out Vector3 pos);
            r.ReadValueSafe(out float yaw);
            r.ReadValueSafe(out ulong buyer);
            r.ReadValueSafe(out byte weapon);
            r.ReadValueSafe(out byte modsLen);
            var bytes = new byte[modsLen];
            for (int i = 0; i < modsLen; i++) r.ReadValueSafe(out bytes[i]);
            r.ReadValueSafe(out float timeLeft);
            r.ReadValueSafe(out byte toastLen);
            var chars = new char[toastLen];
            for (int i = 0; i < toastLen; i++)
            {
                r.ReadValueSafe(out ushort ch);
                chars[i] = (char)ch;
            }
            if (mode > (byte)GunDispenser.Mode.Leaving) return;
            _mode = (GunDispenser.Mode)mode;
            WeaponDefinition def = weapon == 255 ? null : WeaponCatalog.At(weapon);
            _dispenser.Apply(_mode, pos, yaw, buyer, def, modsLen > 0 ? WeaponMods.Decode(bytes) : null, timeLeft);
            if (toastLen > 0) RunToasts.Show(new string(chars), new Color(1f, 0.75f, 0.35f), 4f);
        }

        // ---------------------------------------------------------------- marker

        private void OnGUI()
        {
            if (UiModal.Active || _dispenser == null || _dispenser.State == GunDispenser.Mode.Hidden) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 world = _dispenser.transform.position + Vector3.up * 2.8f;
            Vector3 to = world - cam.transform.position;
            float dist = to.magnitude;
            if (dist > MarkerRange || dist < 3f || Vector3.Dot(to, cam.transform.forward) <= 0f) return;
            HudKit.Begin();
            Vector2 s = HudKit.FromScreen(cam.WorldToScreenPoint(world));
            float fade = Mathf.Clamp01((MarkerRange - dist) / 15f);
            string label = $"GUN DISPENSER  {dist:0} m";
            float w = HudKit.TextWidth(label, 12, head: true) + 20f;
            var rect = new Rect(s.x - w * 0.5f, s.y - 12f, w, 22f);
            HudKit.Panel(rect, 0.7f * fade);
            Color c = _dispenser.State == GunDispenser.Mode.Leaving ? new Color(1f, 0.4f, 0.3f) : new Color(1f, 0.75f, 0.3f);
            HudKit.Label(rect, label, 12, new Color(c.r, c.g, c.b, fade), TextAnchor.MiddleCenter, head: true);
            HudKit.End();
        }
    }
}
