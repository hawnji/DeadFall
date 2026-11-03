using System.Collections.Generic;
using Deadfall.Core;
using UnityEngine;

namespace Deadfall.Player
{
    /// <summary>
    /// Looks for an <see cref="IInteractable"/> under the crosshair (2.6 m ray, then a small sphere so small
    /// pickups are easy to hit) and uses it on Interact. When nothing is aimed at, the closest
    /// <see cref="INearbyInteractable"/> in reach is used instead (barricades). <see cref="IHoldInteractable"/>s
    /// repeat while Interact is held. The HUD reads <see cref="Prompt"/>, <see cref="IsHold"/> and <see cref="HoldProgress"/>.
    /// </summary>
    public sealed class PlayerInteractor : MonoBehaviour
    {
        public const float Range = 2.6f;
        private static readonly RaycastHit[] Hits = new RaycastHit[8];

        private PlayerInputReader _input;
        private PlayerLook _look;
        private PlayerHealth _health;

        public IInteractable Current { get; private set; }
        public string Prompt { get; private set; } = string.Empty;
        public bool CanUse { get; private set; }
        /// <summary>The current target repeats while F is held.</summary>
        public bool IsHold { get; private set; }
        /// <summary>0..1 towards the next repeat while holding, or -1.</summary>
        public float HoldProgress { get; private set; } = -1f;

        private float _nextRepeat;
        private float _lastUse;

        private void Awake()
        {
            _input = GetComponent<PlayerInputReader>();
            _look = GetComponent<PlayerLook>();
            _health = GetComponent<PlayerHealth>();
        }

        private void Update()
        {
            IInteractable previous = Current;
            Current = Find() ?? FindNearby();
            Prompt = Current != null ? Current.InteractPrompt(gameObject) ?? string.Empty : string.Empty;
            CanUse = Current != null && Current.CanInteract(gameObject);
            var hold = Current as IHoldInteractable;
            IsHold = hold != null;
            if (Current != previous) _nextRepeat = 0f;

            bool alive = _health == null || _health.IsAlive;
            bool pressed = _input != null && _input.Current.InteractPressed;
            bool held = _input != null && _input.Current.InteractHeld;
            float now = Time.time;
            if (CanUse && alive && (pressed || (hold != null && held && now >= _nextRepeat)))
            {
                Current.Interact(gameObject);
                _lastUse = now;
                _nextRepeat = now + (hold != null ? Mathf.Max(0.1f, hold.RepeatSeconds) : 0f);
            }
            HoldProgress = hold != null && held && CanUse && alive
                ? Mathf.Clamp01((now - _lastUse) / Mathf.Max(0.1f, hold.RepeatSeconds))
                : -1f;
        }

        /// <summary>Closest usable <see cref="INearbyInteractable"/> in reach (one that can't be used only if none can).</summary>
        private IInteractable FindNearby()
        {
            IReadOnlyList<INearbyInteractable> all = NearbyInteractables.All;
            if (all.Count == 0) return null;
            Vector3 feet = transform.position;
            INearbyInteractable best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < all.Count; i++)
            {
                INearbyInteractable n = all[i];
                if (n == null || (n is Object o && o == null) || !n.InReach(feet, out float d)) continue;
                float score = d + (n.CanInteract(gameObject) ? 0f : 100f);
                if (score < bestScore) { bestScore = score; best = n; }
            }
            return best;
        }

        private IInteractable Find()
        {
            Camera cam = _look != null ? _look.Camera : null;
            if (cam == null) return null;
            Vector3 origin = cam.transform.position, dir = cam.transform.forward;
            IInteractable best = Pick(Physics.RaycastNonAlloc(origin, dir, Hits, Range, ~0, QueryTriggerInteraction.Collide));
            return best ?? Pick(Physics.SphereCastNonAlloc(origin, 0.3f, dir, Hits, Range, ~0, QueryTriggerInteraction.Collide));
        }

        /// <summary>Nearest interactable among the hits, unless a solid non-interactable collider is closer.</summary>
        private IInteractable Pick(int count)
        {
            IInteractable best = null;
            float bestDistance = float.MaxValue, wallDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Collider c = Hits[i].collider;
                if (c == null || c.transform.IsChildOf(transform)) continue;
                float d = Hits[i].distance;
                var it = c.GetComponentInParent<IInteractable>();
                if (it != null) { if (d < bestDistance) { best = it; bestDistance = d; } }
                else if (!c.isTrigger && d < wallDistance && d > 0f) wallDistance = d;
            }
            return best != null && bestDistance <= wallDistance + 0.05f ? best : null;
        }
    }
}
