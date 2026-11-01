using Deadfall.Player;
using UnityEngine;

namespace Deadfall.DebugTools
{
    /// <summary>Box area that damages, heals, or refills ammo for players inside it. Polls with OverlapBox (no rigidbodies needed).</summary>
    public sealed class SandboxZone : MonoBehaviour
    {
        public enum ZoneKind { Damage, Heal, Ammo }

        [SerializeField] private ZoneKind _kind = ZoneKind.Damage;
        [SerializeField] private float _amountPerTick = 10f;
        [SerializeField] private float _tickSeconds = 0.5f;
        [SerializeField] private Vector3 _size = new Vector3(3f, 2f, 3f);

        private readonly Collider[] _hits = new Collider[8];
        private float _nextTick;

        public void Configure(ZoneKind kind, Vector3 size, float amountPerTick)
        {
            _kind = kind;
            _size = size;
            _amountPerTick = amountPerTick;
        }

        private void Update()
        {
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + _tickSeconds;
            Vector3 center = transform.position + Vector3.up * (_size.y * 0.5f);
            int count = Physics.OverlapBoxNonAlloc(center, _size * 0.5f, _hits, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (!_hits[i].TryGetComponent(out PlayerHealth health)) continue;
                switch (_kind)
                {
                    case ZoneKind.Damage: health.ApplyRawDamage(_amountPerTick, Vector3.down, gameObject); break;
                    case ZoneKind.Heal: health.Heal(_amountPerTick); break;
                    case ZoneKind.Ammo:
                        if (_hits[i].TryGetComponent(out PlayerWeapons weapons)) weapons.RefillAllAmmo();
                        break;
                }
            }
        }
    }
}
