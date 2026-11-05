using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Enemies;
using Deadfall.Rules.World;
using Deadfall.World;
using UnityEngine;
using UnityEngine.AI;

namespace Deadfall.Game
{
    /// <summary>
    /// Title screen attract mode: builds a random map (random seed and environment), opens every sector and lets a
    /// horde of infected chase an invisible lure from room to room while a chase camera follows behind. Different
    /// every time the game opens. Everything is torn down when the player picks an option on the title.
    /// </summary>
    public sealed class AttractMode : MonoBehaviour, ITitleBackdrop
    {
        private const float LureSpeed = 3.1f;
        private const int Horde = 16;
        private const float TopUpEvery = 2.5f;
        private const float CameraHeight = 3.1f;
        private const float CameraBack = 6.5f;
        private const int Mask = ~(1 << 2);

        private MapSession _map;
        private ulong _seed;
        private bool _running;
        private Lure _lure;
        private readonly List<Vector3> _path = new List<Vector3>(32);
        private int _corner;
        private float _pauseUntil;
        private float _nextTopUp;
        private Vector3 _heading = Vector3.forward;
        private Vector3 _camVel;
        private float _orbit;
        private System.Random _rng;
        private NavMeshPath _navPath;

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
            _seed = ((ulong)(uint)_rng.Next() << 32) | (uint)_rng.Next();
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

            // The lure starts in the spawn room and the horde pours out of the closets after it.
            Vector3 start = built.PlayerSpawns.Count > 0 ? built.PlayerSpawns[0] : built.RoomCenter(0);
            if (NavMesh.SamplePosition(start, out NavMeshHit hit, 4f, NavMesh.AllAreas)) start = hit.position;
            var go = new GameObject("[AttractLure]");
            go.transform.position = start;
            _lure = go.AddComponent<Lure>();
            _path.Clear();
            _corner = 0;
            _pauseUntil = Time.time + 2.5f;
            _nextTopUp = 0f;
            _camVel = Vector3.zero;
            _orbit = (float)_rng.NextDouble() * 360f;
            _running = true;
            return true;
        }

        public void End()
        {
            if (!_running) return;
            _running = false;
            if (_lure != null) Destroy(_lure.gameObject);
            _lure = null;
            EnemyManager em = EnemyManager.Current;
            if (em != null) em.DespawnAll();
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

        private void Update()
        {
            if (!_running || _lure == null || _map == null || !_map.HasMap) return;
            MoveLure();
            if (Time.time >= _nextTopUp)
            {
                _nextTopUp = Time.time + TopUpEvery;
                TopUp();
            }
        }

        // ---------------------------------------------------------------- lure

        private void MoveLure()
        {
            if (Time.time < _pauseUntil) return;
            if (_corner >= _path.Count)
            {
                if (!PickDestination()) { _pauseUntil = Time.time + 1f; return; }
            }
            Vector3 pos = _lure.transform.position;
            Vector3 target = _path[_corner];
            Vector3 to = target - pos;
            float step = LureSpeed * Time.deltaTime;
            if (to.magnitude <= step)
            {
                _lure.transform.position = target;
                _corner++;
                if (_corner >= _path.Count) _pauseUntil = Time.time + 1.5f + (float)_rng.NextDouble() * 2f; // let the horde catch up
                return;
            }
            Vector3 dir = to.normalized;
            _lure.transform.position = pos + dir * step;
            Vector3 flat = new Vector3(dir.x, 0f, dir.z);
            if (flat.sqrMagnitude > 0.01f) _heading = Vector3.Slerp(_heading, flat.normalized, Time.deltaTime * 1.5f).normalized;
        }

        /// <summary>A path to a random room the lure can walk to (not secrets, not the room it's in).</summary>
        private bool PickDestination()
        {
            BuiltMap m = _map.Map;
            int rooms = m.Layout.Rooms.Count;
            Vector3 from = _lure.transform.position;
            int here = m.RoomAt(from);
            for (int attempt = 0; attempt < 10; attempt++)
            {
                int room = _rng.Next(rooms);
                if (room == here || m.Layout.Rooms[room].Template.Role == RoomRole.Secret) continue;
                if (!NavMesh.SamplePosition(m.RoomCenter(room), out NavMeshHit hit, 3f, NavMesh.AllAreas)) continue;
                if (!NavMesh.CalculatePath(from, hit.position, NavMesh.AllAreas, _navPath) || _navPath.status != NavMeshPathStatus.PathComplete) continue;
                Vector3[] corners = _navPath.corners;
                if (corners.Length < 2) continue;
                _path.Clear();
                for (int i = 1; i < corners.Length; i++) _path.Add(corners[i]);
                _corner = 0;
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- horde

        private void TopUp()
        {
            EnemyManager em = EnemyManager.Current;
            BuiltMap m = _map.Map;
            if (em == null || m.EnemySpawns.Count == 0) return;
            int want = Horde - em.AliveCount;
            Vector3 lure = _lure.transform.position;
            for (int n = 0; n < Mathf.Min(want, 3); n++)
            {
                // A closet near the lure, so the action stays in front of the camera.
                Vector3 best = m.EnemySpawns[_rng.Next(m.EnemySpawns.Count)].Position;
                float bestSq = (best - lure).sqrMagnitude;
                for (int k = 0; k < 4; k++)
                {
                    Vector3 p = m.EnemySpawns[_rng.Next(m.EnemySpawns.Count)].Position;
                    float sq = (p - lure).sqrMagnitude;
                    if (sq < bestSq) { best = p; bestSq = sq; }
                }
                double roll = _rng.NextDouble();
                EnemyDefinition def = roll < 0.75 ? PrototypeEnemies.Husk : roll < 0.93 ? PrototypeEnemies.Bolter : PrototypeEnemies.Bulwark;
                em.Spawn(def, best, (float)_rng.NextDouble() * 360f);
            }
        }

        // ---------------------------------------------------------------- camera

        public void Place(Camera camera, float dt)
        {
            if (camera == null || _lure == null) return;
            Vector3 focus = _lure.transform.position + Vector3.up * 1.1f;
            bool resting = Time.time < _pauseUntil;
            // While the lure waits, swing slowly around it to show the horde arriving.
            _orbit += dt * (resting ? 14f : 0f);
            Vector3 back = resting ? Quaternion.Euler(0f, _orbit, 0f) * Vector3.back : -_heading;
            if (!resting) _orbit = Quaternion.LookRotation(-back).eulerAngles.y;
            Vector3 eye = _lure.transform.position + Vector3.up * 1.6f;
            Vector3 desired = _lure.transform.position + back * CameraBack + Vector3.up * CameraHeight;
            Vector3 d = desired - eye;
            if (Physics.SphereCast(eye, 0.3f, d.normalized, out RaycastHit hit, d.magnitude, Mask, QueryTriggerInteraction.Ignore))
                desired = eye + d.normalized * Mathf.Max(0.8f, hit.distance - 0.2f);
            Vector3 pos = Vector3.SmoothDamp(camera.transform.position, desired, ref _camVel, 0.6f, 20f, Mathf.Max(0.0001f, dt));
            if ((camera.transform.position - desired).sqrMagnitude > 40f * 40f) pos = desired; // first frame
            pos = KeepInside(eye, pos);
            Quaternion look = Quaternion.LookRotation(focus - pos);
            camera.transform.SetPositionAndRotation(pos, Quaternion.Slerp(camera.transform.rotation, look, 1f - Mathf.Exp(-dt * 4f)));
        }

        /// <summary>
        /// Keeps the camera in the play area: under the ceiling, with a clear line back to the lure (smoothing can drag it
        /// round a corner and through a wall), and inside a room cell rather than out in the void around the map.
        /// </summary>
        private Vector3 KeepInside(Vector3 eye, Vector3 pos)
        {
            float floor = _lure.transform.position.y;
            float ceiling = floor + Mathf.Max(2f, (_map.Kit != null ? _map.Kit.WallHeight : 4f) - 0.6f);
            pos.y = Mathf.Clamp(pos.y, floor + 1.2f, ceiling);
            Vector3 d = pos - eye;
            float len = d.magnitude;
            if (len > 0.01f && Physics.SphereCast(eye, 0.25f, d / len, out RaycastHit hit, len, Mask, QueryTriggerInteraction.Ignore))
            {
                pos = eye + d / len * Mathf.Max(0.5f, hit.distance - 0.15f);
                _camVel = Vector3.zero;
            }
            BuiltMap m = _map.Map;
            for (int i = 0; i < 8 && m.RoomAt(pos) < 0; i++)
            {
                pos = Vector3.Lerp(pos, eye, 0.35f);
                _camVel = Vector3.zero;
            }
            return pos;
        }

        /// <summary>The invisible thing the horde chases: a target every enemy prefers over the (far away) player.</summary>
        private sealed class Lure : MonoBehaviour, IAggroTarget, IDamageable
        {
            public Transform TargetTransform => transform;
            public bool IsTargetable => true;
            public IDamageable Damageable => this;
            public float AggroRadius => 1000f;
            public bool IsAlive => true;
            public float DamageReduction => 1f;
            public float ApplyDamage(in DamageInfo info) => 0f;
            private void OnEnable() => CombatTargets.Register(this);
            private void OnDisable() => CombatTargets.Unregister(this);
        }
    }
}
