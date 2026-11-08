using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Encounters;
using Deadfall.Net;
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
    /// Escort mode (<see cref="EscortRules"/>). The host plots the cart's route along the map's main route (spawn
    /// room → each doorway → the far room), rolls it along the NavMesh while players push it, forces every door on
    /// the way open (sector doors also heal it a little), and ends the run when it arrives or breaks. Every peer gets
    /// the cart's state ten times a second and draws the escort panel and an on-screen marker.
    /// </summary>
    public sealed class EscortDirector : MonoBehaviour
    {
        private const string MsgState = "df.escort";
        private const float SendInterval = 0.1f;

        private struct Waypoint
        {
            public Vector3 Position;
            /// <summary>Door connection here, or -1 for a room centre / open doorway.</summary>
            public int Connection;
            public bool Sector;
        }

        public static EscortDirector Current { get; private set; }

        private SessionService _session;
        private WaveManager _waves;
        private MapSession _map;
        private EscortCart _cart;
        private readonly List<Waypoint> _route = new List<Waypoint>(32);
        private readonly List<float> _cumulative = new List<float>(32);
        private float _routeLength;
        private int _next;
        private NavMeshPath _path; // created in Awake: Unity forbids NavMeshPath in field initializers
        private readonly List<Vector3> _corners = new List<Vector3>(16);
        private int _corner;
        private double _doorUntil;
        private bool _ended;
        private float _sendTimer, _hitToastTimer;
        private float _progress;
        private EscortRules.Status _status = EscortRules.Status.Waiting;

        // Client mirror.
        private Vector3 _mirrorPos;
        private float _mirrorYaw;

        private bool IsAuthority => _session == null || _session.Role != SessionRole.Client;
        public EscortCart Cart => _cart;
        public float Progress01 => _progress;

        private void Awake()
        {
            Current = this;
            _path = new NavMeshPath();
            _session = SessionService.Instance;
            _session.RegisterHandler(MsgState, OnState);
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            Bind(null, null);
            if (_session != null) _session.UnregisterHandler(MsgState);
        }

        private void Bind(WaveManager w, MapSession m)
        {
            if (_waves != w)
            {
                if (_waves != null) _waves.PhaseChanged -= OnPhase;
                _waves = w;
                if (_waves != null) _waves.PhaseChanged += OnPhase;
            }
            if (_map != m)
            {
                if (_map != null) _map.MapCleared -= Clear;
                _map = m;
                if (_map != null) _map.MapCleared += Clear;
            }
        }

        private void OnPhase(RunPhase from, RunPhase to)
        {
            if (to == RunPhase.NotStarted) { Clear(); return; }
            if (!IsAuthority || from != RunPhase.NotStarted || _waves == null || !_waves.IsEscort) return;
            Begin();
        }

        private void Clear()
        {
            if (_cart != null) Destroy(_cart.gameObject);
            _cart = null;
            _route.Clear();
            _cumulative.Clear();
            _corners.Clear();
            _ended = false;
            _progress = 0f;
            _status = EscortRules.Status.Waiting;
        }

        // ---------------------------------------------------------------- host: route and cart

        private void Begin()
        {
            Clear();
            if (_map == null || !_map.HasMap) return;
            BuiltMap map = _map.Map;
            if (!BuildRoute(map) || _route.Count < 2)
            {
                GameLog.Warn("Escort", "No route for the cart on this map.");
                return;
            }
            // The cart starts a few metres along the route from the spawn room's centre.
            Vector3 start = _route[0].Position;
            Vector3 dir = _route[1].Position - start;
            dir.y = 0f;
            float yaw = dir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(dir).eulerAngles.y : 0f;
            int players = _waves != null ? Mathf.Max(1, _waves.PlayerCount) : 1;
            _cart = EscortCart.Create(start, yaw, EscortRules.MaxHealth(players), false, map.Root != null ? map.Root : transform);
            _cart.Damaged += OnCartDamaged;
            _cart.Destroyed += OnCartDestroyed;
            _next = 1;
            _corners.Clear();
            RunToasts.Show("Escort the reactor cart: stay within its ring to push it", new Color(0.4f, 0.95f, 1f), 5f);
            Send();
        }

        /// <summary>Spawn room → main route rooms → the boss room (or the deepest room), through each connecting doorway.</summary>
        private bool BuildRoute(BuiltMap map)
        {
            Layout layout = map.Layout;
            int dest = -1;
            PlacedRoom target = layout.FirstWithRole(RoomRole.Boss) ?? layout.FirstWithRole(RoomRole.Objective);
            if (target != null) dest = target.Index;
            if (dest < 0)
            {
                int deepest = 0, depth = 0;
                for (int i = 0; i < layout.Rooms.Count; i++)
                {
                    int d = 0;
                    for (int r = i; r > 0 && d < 999; r = layout.Rooms[r].Parent) d++;
                    if (d > depth && layout.Rooms[i].Template.Role != RoomRole.Secret) { depth = d; deepest = i; }
                }
                dest = deepest;
            }
            var parents = new List<int>(layout.Rooms.Count);
            foreach (PlacedRoom r in layout.Rooms) parents.Add(r.Parent);
            List<int> rooms = EscortRules.Route(parents, dest);
            if (rooms.Count < 2) return false;

            _route.Add(new Waypoint { Position = Snap(map.RoomCenter(rooms[0])), Connection = -1 });
            for (int i = 1; i < rooms.Count; i++)
            {
                int a = rooms[i - 1], b = rooms[i];
                for (int ci = 0; ci < layout.Connections.Count; ci++)
                {
                    Connection c = layout.Connections[ci];
                    if (!((c.RoomA == a && c.RoomB == b) || (c.RoomA == b && c.RoomB == a))) continue;
                    PlacedRoom ra = layout.Rooms[a];
                    int socket = c.RoomA == a ? c.SocketA : c.SocketB;
                    Cell cell = ra.SocketCell(socket);
                    Dir facing = ra.SocketFacing(socket);
                    float cs = map.CellSize;
                    var door = new Vector3((cell.X + 0.5f + facing.Dx() * 0.5f) * cs, map.RoomCenter(a).y, (cell.Z + 0.5f + facing.Dz() * 0.5f) * cs);
                    _route.Add(new Waypoint { Position = Snap(door), Connection = c.IsDoor ? ci : -1, Sector = c.Locked });
                    break;
                }
                _route.Add(new Waypoint { Position = Snap(map.RoomCenter(b)), Connection = -1 });
            }
            _cumulative.Clear();
            _routeLength = 0f;
            _cumulative.Add(0f);
            for (int i = 1; i < _route.Count; i++)
            {
                _routeLength += Vector3.Distance(_route[i - 1].Position, _route[i].Position);
                _cumulative.Add(_routeLength);
            }
            return true;
        }

        private static Vector3 Snap(Vector3 p) => NavMesh.SamplePosition(p, out NavMeshHit hit, 4f, NavMesh.AllAreas) ? hit.position : p;

        private void OnCartDamaged(EscortCart c)
        {
            if (_hitToastTimer > 0f) return;
            _hitToastTimer = 8f;
            RunToasts.Show("The cart is under attack!", new Color(1f, 0.5f, 0.35f));
        }

        private void OnCartDestroyed(EscortCart c)
        {
            if (_ended) return;
            _ended = true;
            _status = EscortRules.Status.Destroyed;
            GameAudio.Play(SfxId.Explosion, c.transform.position + Vector3.up);
            Send();
            if (_waves != null) _waves.FailRun("The cart was destroyed");
        }

        private void Update()
        {
            if (_waves != WaveManager.Current || _map != MapSession.Current) Bind(WaveManager.Current, MapSession.Current);
            _hitToastTimer -= Time.deltaTime;
            if (_cart == null) return;
            if (!IsAuthority)
            {
                // Mirror: glide to the host's position.
                Transform t = _cart.transform;
                if ((t.position - _mirrorPos).sqrMagnitude > 25f) t.position = _mirrorPos;
                else t.position = Vector3.Lerp(t.position, _mirrorPos, 1f - Mathf.Exp(-10f * Time.deltaTime));
                t.rotation = Quaternion.Slerp(t.rotation, Quaternion.Euler(0f, _mirrorYaw, 0f), 1f - Mathf.Exp(-8f * Time.deltaTime));
                _cart.Status = _status;
                return;
            }
            if (!_ended && _waves != null && _waves.IsRunning) Tick(Time.deltaTime);
            _cart.Status = _status;
            _sendTimer -= Time.deltaTime;
            if (_sendTimer <= 0f)
            {
                _sendTimer = SendInterval;
                Send();
            }
        }

        private void Tick(float dt)
        {
            Vector3 pos = _cart.transform.position;
            if (_next >= _route.Count)
            {
                Deliver();
                return;
            }

            // Who's pushing, and is it contested?
            int pushers = 0;
            bool blocked = false;
            Vector3 ahead = pos + _cart.transform.forward * 1.7f;
            IReadOnlyList<ISquadMember> squad = SquadMembers.All;
            for (int i = 0; i < squad.Count; i++)
            {
                ISquadMember m = squad[i];
                if (m == null || m.Body == null || m.Disconnected || m.Life != LifeState.Alive) continue;
                Vector3 d = m.Body.position - pos;
                d.y = 0f;
                if (d.sqrMagnitude <= EscortRules.PushRadius * EscortRules.PushRadius) pushers++;
                Vector3 a = m.Body.position - ahead;
                a.y = 0f;
                if (a.sqrMagnitude < 1.1f * 1.1f) blocked = true;
            }
            int infected = 0;
            IReadOnlyList<IHostile> hostiles = Hostiles.All;
            for (int i = 0; i < hostiles.Count; i++)
            {
                IHostile h = hostiles[i];
                if (h == null || !h.IsAlive) continue;
                Vector3 d = h.AimPoint - pos;
                d.y = 0f;
                if (d.sqrMagnitude <= EscortRules.ContestRadius * EscortRules.ContestRadius) infected++;
            }
            _status = EscortRules.Evaluate(pushers, infected);

            // Doors: stop short of a closed one, force it, wait for it to slide open.
            Waypoint wp = _route[_next];
            double now = Time.timeAsDouble;
            if (now < _doorUntil)
            {
                _status = EscortRules.Status.Door;
                UpdateProgress(pos);
                return;
            }
            bool closedDoor = wp.Connection >= 0 && !_map.IsDoorOpen(wp.Connection);
            if (closedDoor && Flat(wp.Position - pos).magnitude <= 2.2f)
            {
                if (_status != EscortRules.Status.Moving) { UpdateProgress(pos); return; } // someone has to be there to get it through
                if (_map.ForceOpenDoor(wp.Connection, wp.Sector ? "SECTOR OPEN: the cart forced the door" : "The cart forced the door"))
                {
                    if (wp.Sector)
                    {
                        _cart.Heal(EscortRules.CheckpointHeal);
                        RunToasts.Show($"Checkpoint: the cart is patched up (+{Mathf.RoundToInt(EscortRules.CheckpointHeal * 100f)}%)", HudKit.Good);
                    }
                    _doorUntil = now + EscortRules.DoorSeconds;
                    _corners.Clear();
                    _status = EscortRules.Status.Door;
                }
                else _status = EscortRules.Status.Blocked; // sealed by a lockdown: wait it out
                UpdateProgress(pos);
                return;
            }
            if (_status == EscortRules.Status.Moving && blocked) _status = EscortRules.Status.Blocked;
            if (_status != EscortRules.Status.Moving)
            {
                UpdateProgress(pos);
                return;
            }

            // Roll along the NavMesh towards the next waypoint.
            if (_corners.Count == 0 || _corner >= _corners.Count) Plot(pos, closedDoor ? wp.Position + Flat(pos - wp.Position).normalized * 2f : wp.Position);
            float step = EscortRules.Speed(pushers) * dt;
            while (step > 0f && _corner < _corners.Count)
            {
                Vector3 to = _corners[_corner];
                Vector3 delta = to - pos;
                float dist = delta.magnitude;
                if (dist <= step)
                {
                    pos = to;
                    step -= dist;
                    _corner++;
                    continue;
                }
                pos += delta / dist * step;
                step = 0f;
            }
            Vector3 move = Flat(pos - _cart.transform.position);
            _cart.transform.position = pos;
            if (move.sqrMagnitude > 0.000001f)
                _cart.transform.rotation = Quaternion.RotateTowards(_cart.transform.rotation, Quaternion.LookRotation(move), 90f * dt);
            if (Flat(wp.Position - pos).magnitude <= 0.6f || (_corner >= _corners.Count && !closedDoor))
            {
                _next++;
                _corners.Clear();
            }
            UpdateProgress(pos);
        }

        private void Plot(Vector3 from, Vector3 to)
        {
            _corners.Clear();
            _corner = 0;
            if (_path == null) _path = new NavMeshPath();
            if (NavMesh.CalculatePath(from, to, NavMesh.AllAreas, _path) && _path.status != NavMeshPathStatus.PathInvalid)
            {
                Vector3[] cs = _path.corners;
                for (int i = 1; i < cs.Length; i++) _corners.Add(cs[i]);
            }
            if (_corners.Count == 0) _corners.Add(to);
        }

        private void UpdateProgress(Vector3 pos)
        {
            if (_routeLength <= 0f || _next <= 0) return;
            int seg = Mathf.Clamp(_next, 1, _route.Count - 1);
            float segLen = _cumulative[seg] - _cumulative[seg - 1];
            float left = Mathf.Min(segLen, Vector3.Distance(pos, _route[seg].Position));
            _progress = Mathf.Clamp01(Mathf.Max(_progress, (_cumulative[seg - 1] + segLen - left) / _routeLength));
        }

        private void Deliver()
        {
            if (_ended) return;
            _ended = true;
            _progress = 1f;
            _status = EscortRules.Status.Delivered;
            Send();
            if (_waves != null) _waves.CompleteRun("The cart made it through");
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        // ---------------------------------------------------------------- network

        private void Send()
        {
            if (_session == null || !_session.IsHost || _session.RemotePeerCount == 0) return;
            using (var w = new FastBufferWriter(40, Allocator.Temp))
            {
                bool has = _cart != null;
                w.WriteValueSafe((byte)(has ? 1 : 0));
                if (has)
                {
                    w.WriteValueSafe(_cart.transform.position);
                    w.WriteValueSafe(_cart.transform.eulerAngles.y);
                    w.WriteValueSafe((byte)Mathf.RoundToInt(_cart.Health01 * 255f));
                    w.WriteValueSafe((byte)Mathf.RoundToInt(_progress * 255f));
                    w.WriteValueSafe((byte)_status);
                }
                _session.SendToRemoteClients(MsgState, w, NetworkDelivery.UnreliableSequenced);
            }
        }

        private void OnState(ulong sender, FastBufferReader r)
        {
            if (IsAuthority) return;
            r.ReadValueSafe(out byte has);
            if (has == 0)
            {
                Clear();
                return;
            }
            r.ReadValueSafe(out Vector3 pos);
            r.ReadValueSafe(out float yaw);
            r.ReadValueSafe(out byte health);
            r.ReadValueSafe(out byte progress);
            r.ReadValueSafe(out byte status);
            _mirrorPos = pos;
            _mirrorYaw = yaw;
            _progress = progress / 255f;
            _status = status <= (byte)EscortRules.Status.Destroyed ? (EscortRules.Status)status : EscortRules.Status.Waiting;
            if (_cart == null && _map != null && _map.HasMap)
                _cart = EscortCart.Create(pos, yaw, 1f, true, _map.Map.Root != null ? _map.Map.Root : transform);
            if (_cart != null) _cart.SetHealth01(health / 255f);
        }

        // ---------------------------------------------------------------- HUD

        private void OnGUI()
        {
            if (_cart == null || _waves == null || !_waves.IsEscort || _waves.Phase == RunPhase.NotStarted) return;
            HudKit.Begin();
            float w = HudKit.W;
            // Panel under the compass: cart health and route progress, with what the cart is doing.
            var box = new Rect(w * 0.5f - 200f, 64f, 400f, 78f);
            HudKit.Panel(box, 0.9f);
            float h = _cart.Health01;
            Color hc = h > 0.5f ? HudKit.Good : h > 0.25f ? HudKit.Warn : HudKit.Bad;
            HudKit.Label(new Rect(box.x + 16f, box.y + 4f, 120f, 18f), "ESCORT", 13, HudKit.Accent, TextAnchor.MiddleLeft, head: true);
            HudKit.Label(new Rect(box.x + 16f, box.y + 4f, box.width - 32f, 18f), $"CART {Mathf.CeilToInt(h * 100f)}%  ·  ROUTE {Mathf.FloorToInt(_progress * 100f)}%", 13, Color.white, TextAnchor.MiddleRight, head: true);
            HudKit.Bar(new Rect(box.x + 16f, box.y + 24f, box.width - 32f, 10f), h, hc, 10);
            HudKit.Bar(new Rect(box.x + 16f, box.y + 38f, box.width - 32f, 6f), _progress, HudKit.Info);
            Color sc = _status == EscortRules.Status.Moving || _status == EscortRules.Status.Door || _status == EscortRules.Status.Delivered ? HudKit.Good
                : _status == EscortRules.Status.Waiting ? HudKit.Warn : HudKit.Bad;
            HudKit.Label(new Rect(box.x, box.y + 50f, box.width, 22f), EscortRules.Describe(_status), 14, sc, TextAnchor.MiddleCenter, head: true);

            // Marker over the cart when it's in front of the camera.
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 world = _cart.transform.position + Vector3.up * 2.1f;
                Vector3 sp = cam.WorldToScreenPoint(world);
                if (sp.z > 0f)
                {
                    Vector2 p = HudKit.FromScreen(sp);
                    float dist = Vector3.Distance(cam.transform.position, _cart.transform.position);
                    if (dist > 5f)
                    {
                        HudKit.Hex(new Rect(p.x - 11f, p.y - 12f, 22f, 24f), new Color(0f, 0f, 0f, 0.45f), hc);
                        HudKit.Label(new Rect(p.x - 60f, p.y + 12f, 120f, 18f), $"CART  {Mathf.RoundToInt(dist)} m", 12, Color.white, TextAnchor.MiddleCenter, head: true);
                    }
                }
            }
            HudKit.End();
        }
    }
}
