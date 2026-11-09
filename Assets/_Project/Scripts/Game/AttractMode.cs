using System.Collections.Generic;
using Deadfall.Core;
using Deadfall.Enemies;
using Deadfall.Rules.Combat;
using Deadfall.Rules.World;
using Deadfall.World;
using UnityEngine;
using UnityEngine.AI;

namespace Deadfall.Game
{
    /// <summary>
    /// Title screen attract mode: builds a random map (random seed and environment), opens every door and glides a
    /// camera through it on one continuous route, room to room through the open doorways, past infected standing
    /// dormant where they were placed. The route is a closed tour of the room tree, so the camera never stops, jumps
    /// or resets; it just keeps going. Everything is torn down when the player picks an option on the title.
    /// </summary>
    public sealed class AttractMode : MonoBehaviour, ITitleBackdrop
    {
        private const float Speed = 1.9f;          // metres per second along the route
        private const float EyeHeight = 1.7f;
        private const float LookAhead = 6f;        // the camera looks at the route this far ahead
        private const float Step = 0.5f;           // route resampling
        private const int Extras = 40;             // dormant infected placed along the tour
        private const float ClearOfRoute = 2.6f;   // they never stand where the camera passes

        private MapSession _map;
        private ulong _seed;
        private bool _running;
        private readonly List<Vector3> _route = new List<Vector3>(4096);
        private float _routeLength;
        private float _travel;
        private Quaternion _look = Quaternion.identity;
        private bool _placedOnce;
        private System.Random _rng;
        private NavMeshPath _navPath;
        private int _savedCap;

        private System.Func<ulong, bool> _enterMap;
        private System.Action _exitMap;
        private GameObject _player;
        private readonly List<Renderer> _hidden = new List<Renderer>(16);

        /// <param name="enterMap">Swaps the base world for the map of a seed (the same path a real game takes, so the
        /// base's own NavMesh and props are gone and the map's lighting applies).</param>
        /// <param name="exitMap">Back to base with the player at the FOB.</param>
        /// <param name="player">The local player, hidden while the attract map runs.</param>
        public void Bind(MapSession map, System.Func<ulong, bool> enterMap, System.Action exitMap, GameObject player)
        {
            _map = map;
            _enterMap = enterMap;
            _exitMap = exitMap;
            _player = player;
        }

        public bool Begin()
        {
            if (_map == null || _running || _map.HasMap) return false;
            _rng = new System.Random(System.Environment.TickCount);
            // A plain environment seed: the full Survival layout, never the short Gauntlet or Escort shapes.
            ulong raw = ((ulong)(uint)_rng.Next() << 32) | (uint)_rng.Next();
            _seed = EnvironmentCatalog.Encode(raw, _rng.Next(EnvironmentCatalog.All.Length));
            try
            {
                if (_enterMap != null ? !_enterMap(_seed) : _map.Build(_seed) == null) return false;
            }
            catch (System.Exception e) { Debug.LogException(e); return false; }
            BuiltMap built = _map.Map;
            if (built == null) return false;
            HidePlayer(true);
            _map.OpenAllSectors();
            _navPath = new NavMeshPath();
            BuildRoute(built);
            _travel = 0f;
            _placedOnce = false;
            PlaceExtras(built);
            _running = true;
            return true;
        }

        public void End()
        {
            if (!_running) return;
            _running = false;
            EnemyManager em = EnemyManager.Current;
            if (em != null)
            {
                em.DespawnAll();
                em.CapOverride = _savedCap;
            }
            _route.Clear();
            HidePlayer(false);
            // Leave a map that arrived from a host alone (joining a game replaces ours).
            if (_map != null && _map.HasMap && _map.Seed == _seed)
            {
                if (_exitMap != null) _exitMap();
                else _map.Clear();
            }
        }

        /// <summary>The player stands in the attract map (frozen behind the title); keep their body out of shot.</summary>
        private void HidePlayer(bool hide)
        {
            if (hide)
            {
                _hidden.Clear();
                if (_player == null) return;
                foreach (Renderer r in _player.GetComponentsInChildren<Renderer>())
                    if (r.enabled) { r.enabled = false; _hidden.Add(r); }
                return;
            }
            foreach (Renderer r in _hidden) if (r != null) r.enabled = true;
            _hidden.Clear();
        }

        private void OnDestroy() => End();

        // ---------------------------------------------------------------- route

        /// <summary>
        /// A closed walk over the room tree (depth first, back up through each parent), walked on the NavMesh so it
        /// goes through the doorways. Resampled evenly and lightly rounded so the camera glides round corners.
        /// </summary>
        private void BuildRoute(BuiltMap m)
        {
            _route.Clear();
            Layout layout = m.Layout;
            int n = layout.Rooms.Count;
            var children = new List<int>[n];
            for (int i = 0; i < n; i++) children[i] = new List<int>();
            for (int i = 0; i < n; i++)
            {
                int p = layout.Rooms[i].Parent;
                if (p >= 0 && p < n && layout.Rooms[i].Template.Role != RoomRole.Secret) children[p].Add(i);
            }
            // Shuffle the visiting order so every title screen takes a different tour.
            foreach (List<int> c in children)
                for (int i = c.Count - 1; i > 0; i--) { int j = _rng.Next(i + 1); (c[i], c[j]) = (c[j], c[i]); }

            var walk = new List<Vector3>(1024);
            Vector3 start = Floor(m.RoomCenter(0));
            walk.Add(start);
            Visit(m, 0, start, children, walk, 0);
            if (walk.Count < 2)
            {
                // Nowhere to go: a slow circle round the spawn room.
                for (int i = 0; i <= 48; i++)
                {
                    float a = i * Mathf.PI * 2f / 48f;
                    walk.Add(start + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 2.5f);
                }
            }

            // Even spacing, then two rounds of corner cutting (small at this spacing: it never leaves the NavMesh corridor).
            var even = new List<Vector3>(4096);
            even.Add(walk[0]);
            float carry = 0f;
            for (int i = 1; i < walk.Count; i++)
            {
                Vector3 a = walk[i - 1], b = walk[i];
                float len = Vector3.Distance(a, b);
                float d = Step - carry;
                while (d <= len)
                {
                    even.Add(Vector3.Lerp(a, b, d / len));
                    d += Step;
                }
                carry = len - (d - Step);
            }
            List<Vector3> smooth = even;
            for (int pass = 0; pass < 2; pass++) smooth = Chaikin(smooth);
            // Lift to eye height.
            for (int i = 0; i < smooth.Count; i++) _route.Add(smooth[i] + Vector3.up * EyeHeight);
            _routeLength = 0f;
            for (int i = 1; i < _route.Count; i++) _routeLength += Vector3.Distance(_route[i - 1], _route[i]);
            _routeLength += Vector3.Distance(_route[_route.Count - 1], _route[0]);
        }

        private void Visit(BuiltMap m, int room, Vector3 at, List<int>[] children, List<Vector3> walk, int depth)
        {
            if (depth > 64) return;
            foreach (int child in children[room])
            {
                Vector3 there = Floor(m.RoomCenter(child));
                if (!AppendPath(at, there, walk)) continue;      // unreachable (sealed): skip that branch
                Visit(m, child, there, children, walk, depth + 1);
                AppendPath(there, at, walk);                      // and back to this room
            }
        }

        private bool AppendPath(Vector3 from, Vector3 to, List<Vector3> walk)
        {
            if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, _navPath) || _navPath.status != NavMeshPathStatus.PathComplete) return false;
            Vector3[] corners = _navPath.corners;
            for (int i = 1; i < corners.Length; i++) walk.Add(corners[i]);
            return true;
        }

        private static List<Vector3> Chaikin(List<Vector3> pts)
        {
            var o = new List<Vector3>(pts.Count * 2);
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = pts[i], b = pts[(i + 1) % n]; // closed loop
                o.Add(Vector3.Lerp(a, b, 0.25f));
                o.Add(Vector3.Lerp(a, b, 0.75f));
            }
            return o;
        }

        private static Vector3 Floor(Vector3 p) =>
            NavMesh.SamplePosition(p, out NavMeshHit hit, 3f, NavMesh.AllAreas) ? hit.position : p;

        /// <summary>Point on the closed route <paramref name="distance"/> metres from its start.</summary>
        private Vector3 PointAt(float distance)
        {
            int n = _route.Count;
            if (n == 0) return Vector3.zero;
            if (n == 1 || _routeLength <= 0.01f) return _route[0];
            // Evenly spaced after resampling, so index by distance directly.
            float seg = _routeLength / n;
            float f = Mathf.Repeat(distance, _routeLength) / seg;
            int i = Mathf.FloorToInt(f) % n;
            return Vector3.Lerp(_route[i], _route[(i + 1) % n], f - Mathf.Floor(f));
        }

        // ---------------------------------------------------------------- extras

        /// <summary>Dormant infected spread through the rooms the tour passes, standing clear of the camera's line.</summary>
        private void PlaceExtras(BuiltMap m)
        {
            EnemyManager em = EnemyManager.Current;
            if (em == null || _route.Count == 0) return;
            _savedCap = em.CapOverride;
            em.CapOverride = Mathf.Max(em.CapOverride, Extras + 4);
            int rooms = m.Layout.Rooms.Count;
            int placed = 0;
            for (int attempt = 0; attempt < Extras * 8 && placed < Extras; attempt++)
            {
                int room = _rng.Next(rooms);
                if (m.Layout.Rooms[room].Template.Role == RoomRole.Secret) continue;
                Bounds b = m.RoomBounds[room];
                var guess = new Vector3(Mathf.Lerp(b.min.x + 1f, b.max.x - 1f, (float)_rng.NextDouble()), b.min.y + 0.5f,
                                        Mathf.Lerp(b.min.z + 1f, b.max.z - 1f, (float)_rng.NextDouble()));
                if (m.RoomAt(guess) != room) continue;
                if (!NavMesh.SamplePosition(guess, out NavMeshHit hit, 1.5f, NavMesh.AllAreas) || m.RoomAt(hit.position) != room) continue;
                if (NearRoute(hit.position)) continue;
                double roll = _rng.NextDouble();
                EnemyDefinition def = roll < 0.62 ? PrototypeEnemies.Husk : roll < 0.76 ? PrototypeEnemies.Bolter
                    : roll < 0.86 ? PrototypeEnemies.Creeper : roll < 0.94 ? PrototypeEnemies.Bulwark : PrototypeEnemies.Pyre;
                Enemy e = em.Spawn(def, hit.position, (float)_rng.NextDouble() * 360f, 1f, EnemyTraits.None, SpecimenModifiers.None, 1.5f);
                if (e == null) continue;
                e.Dormant = true;
                placed++;
            }
        }

        private bool NearRoute(Vector3 p)
        {
            float sq = ClearOfRoute * ClearOfRoute;
            for (int i = 0; i < _route.Count; i += 2)
            {
                Vector3 r = _route[i];
                float dx = r.x - p.x, dz = r.z - p.z;
                if (dx * dx + dz * dz < sq && Mathf.Abs(r.y - EyeHeight - p.y) < 2.5f) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- camera

        public void Place(Camera camera, float dt)
        {
            // Joining a session clears the attract map while the title is still up: nothing to film then.
            if (camera == null || !_running || _map == null || !_map.HasMap || _map.Map == null || _route.Count == 0) return;
            dt = Mathf.Clamp(dt, 0f, 0.1f); // a hitch never jumps the camera along the route
            _travel += Speed * dt;
            if (_travel > _routeLength) _travel -= _routeLength; // seamless: the tour is a closed loop
            Vector3 pos = PointAt(_travel);
            // Look at a point a little ahead (averaged over a few samples so turns ease in), slightly downward.
            Vector3 ahead = (PointAt(_travel + LookAhead * 0.6f) + PointAt(_travel + LookAhead) + PointAt(_travel + LookAhead * 1.4f)) / 3f;
            Vector3 dir = ahead - pos;
            dir.y -= 0.25f;
            if (dir.sqrMagnitude < 0.01f) dir = camera.transform.forward;
            Quaternion want = Quaternion.LookRotation(dir.normalized, Vector3.up);
            _look = _placedOnce ? Quaternion.Slerp(_look, want, 1f - Mathf.Exp(-dt * 2.2f)) : want;
            _placedOnce = true;
            camera.transform.SetPositionAndRotation(pos, _look);
        }
    }
}
