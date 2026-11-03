using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Player;
using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// M11 spectating: while the local player is eliminated the camera follows a living teammate from over the
    /// shoulder (click to switch). The first-person viewmodel is hidden meanwhile, and the camera goes back to the
    /// player's head when they return. Runs after the look and weapon scripts so it has the last word.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class SpectatorCamera : MonoBehaviour
    {
        private static readonly Vector3 Offset = new Vector3(0.45f, 0.55f, -2.6f);
        private const float Sharpness = 10f;

        public static SpectatorCamera Current { get; private set; }

        private PlayerHealth _health;
        private PlayerLook _look;
        private PlayerInputReader _input;
        private Transform _cam;
        private Vector3 _homePos;
        private Quaternion _homeRot;
        private bool _active;
        private int _index;
        private readonly List<Renderer> _hidden = new List<Renderer>();
        private readonly List<ISquadMember> _candidates = new List<ISquadMember>(4);
        private static readonly RaycastHit[] Hits = new RaycastHit[8];

        public ISquadMember Target { get; private set; }

        public void Bind(GameObject player)
        {
            _health = player.GetComponent<PlayerHealth>();
            _look = player.GetComponent<PlayerLook>();
            _input = player.GetComponent<PlayerInputReader>();
        }

        private void Awake() => Current = this;
        private void OnDestroy()
        {
            if (Current == this) Current = null;
            Stop();
        }

        private void LateUpdate()
        {
            bool want = _health != null && _health.IsEliminated;
            if (want && !_active) Begin();
            else if (!want && _active) Stop();
            if (!_active) return;

            Collect();
            if (_candidates.Count == 0) { Target = null; return; }
            if (_input != null && _input.Current.FirePressed) _index++;
            _index = (_index % _candidates.Count + _candidates.Count) % _candidates.Count;
            Target = _candidates[_index];

            Transform body = Target.Body;
            Vector3 pivot = Target.Eye;
            Vector3 want3 = body.position + body.rotation * Offset + Vector3.up * 1.1f;
            // Keep the camera on this side of walls.
            Vector3 dir = want3 - pivot;
            float dist = dir.magnitude;
            int n = Physics.SphereCastNonAlloc(pivot, 0.2f, dir / Mathf.Max(0.001f, dist), Hits, dist, ~0, QueryTriggerInteraction.Ignore);
            float best = dist;
            for (int i = 0; i < n; i++)
            {
                if (Hits[i].collider == null || Hits[i].collider.transform.IsChildOf(_health.transform)) continue;
                if (Hits[i].distance > 0f && Hits[i].distance < best) best = Hits[i].distance;
            }
            Vector3 pos = pivot + dir.normalized * Mathf.Max(0.3f, best - 0.1f);
            float k = 1f - Mathf.Exp(-Sharpness * Time.deltaTime);
            _cam.position = Vector3.Lerp(_cam.position, pos, k);
            Quaternion rot = Quaternion.LookRotation(pivot + body.forward * 3f - _cam.position, Vector3.up);
            _cam.rotation = Quaternion.Slerp(_cam.rotation, rot, k);
        }

        private void Collect()
        {
            _candidates.Clear();
            IReadOnlyList<ISquadMember> all = SquadMembers.All;
            for (int i = 0; i < all.Count; i++)
            {
                ISquadMember m = all[i];
                if (m == null || (m is Object o && o == null) || m.IsLocalMember || m.Disconnected || m.Body == null) continue;
                if (m.Life != LifeState.Eliminated) _candidates.Add(m);
            }
            _candidates.Sort((a, b) => a.ClientId.CompareTo(b.ClientId));
        }

        private void Begin()
        {
            Camera cam = _look != null ? _look.Camera : Camera.main;
            if (cam == null) return;
            _cam = cam.transform;
            _homePos = _cam.localPosition;
            _homeRot = _cam.localRotation;
            _hidden.Clear();
            foreach (Renderer r in _cam.GetComponentsInChildren<Renderer>())
                if (r.enabled) { r.enabled = false; _hidden.Add(r); }
            _active = true;
        }

        private void Stop()
        {
            if (!_active) return;
            _active = false;
            Target = null;
            if (_cam != null)
            {
                _cam.localPosition = _homePos;
                _cam.localRotation = _homeRot;
            }
            foreach (Renderer r in _hidden)
                if (r != null) r.enabled = true;
            _hidden.Clear();
        }
    }
}
