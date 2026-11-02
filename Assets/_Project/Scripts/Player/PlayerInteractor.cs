using Deadfall.Core;
using UnityEngine;

namespace Deadfall.Player
{
    /// <summary>
    /// Looks for an <see cref="IInteractable"/> under the crosshair (2.6 m ray, then a small sphere so small
    /// pickups are easy to hit) and uses it on Interact. The HUD reads <see cref="Prompt"/>.
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

        private void Awake()
        {
            _input = GetComponent<PlayerInputReader>();
            _look = GetComponent<PlayerLook>();
            _health = GetComponent<PlayerHealth>();
        }

        private void Update()
        {
            Current = Find();
            Prompt = Current != null ? Current.InteractPrompt(gameObject) ?? string.Empty : string.Empty;
            CanUse = Current != null && Current.CanInteract(gameObject);
            if (CanUse && _input != null && _input.Current.InteractPressed && (_health == null || _health.IsAlive))
                Current.Interact(gameObject);
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
