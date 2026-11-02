using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Combat
{
    /// <summary>
    /// Marks a collider as a hit zone of its parent IDamageable. Put one on each head/body/weak-point collider.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class Hurtbox : MonoBehaviour
    {
        [SerializeField] private HitZone _zone = HitZone.Body;
        [Tooltip("Only used when Zone is WeakPoint.")]
        [SerializeField] private float _weakPointMultiplier = 2.5f;

        private IDamageable _owner;

        public HitZone Zone => _zone;
        public float WeakPointMultiplier => _weakPointMultiplier;

        public IDamageable Owner
        {
            get
            {
                if (_owner == null) _owner = GetComponentInParent<IDamageable>();
                return _owner;
            }
        }

        public void Configure(HitZone zone, float weakPointMultiplier = 2.5f)
        {
            _zone = zone;
            _weakPointMultiplier = weakPointMultiplier;
        }

        /// <summary>Finds the hit zone for a collider: a Hurtbox if present, otherwise the body of a parent damageable.</summary>
        public static bool TryResolve(Collider collider, out IDamageable target, out HitZone zone, out float weakPointMultiplier)
        {
            if (collider.TryGetComponent(out Hurtbox box) && box.Owner != null)
            {
                target = box.Owner;
                zone = box._zone;
                weakPointMultiplier = box._weakPointMultiplier;
                return true;
            }
            target = collider.GetComponentInParent<IDamageable>();
            zone = HitZone.Body;
            weakPointMultiplier = 1f;
            return target != null;
        }
    }
}
