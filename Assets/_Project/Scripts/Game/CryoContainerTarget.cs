using Deadfall.Combat;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// Host side of a cryo container (SealContainers): the infected hunt it while it is unsealed, pulled in from
    /// ObjectiveRules.ContainerAggroRadius, and their hits wear its integrity down. The
    /// <see cref="ObjectiveDirector"/> syncs the integrity and fails the objective when it breaks.
    /// </summary>
    public sealed class CryoContainerTarget : MonoBehaviour, IAggroTarget, IDamageable
    {
        private ObjectiveProp _prop;
        private float _health, _max;
        private float _aggro;

        /// <summary>Raised once when the container breaks.</summary>
        public event System.Action<CryoContainerTarget> Broken;
        /// <summary>Raised on every hit that lands (the director warns the squad).</summary>
        public event System.Action<CryoContainerTarget> Damaged;

        public ObjectiveProp Prop => _prop;
        public Transform TargetTransform => transform;
        public bool IsTargetable => IsAlive && _prop != null && !_prop.Done && isActiveAndEnabled;
        public IDamageable Damageable => this;
        public float AggroRadius => _aggro;
        public bool IsAlive => _health > 0f;
        public float DamageReduction => 0f;
        public float Health01 => _max > 0f ? Mathf.Clamp01(_health / _max) : 0f;

        public static CryoContainerTarget Attach(ObjectiveProp prop, float health, float aggroRadius)
        {
            var t = prop.gameObject.AddComponent<CryoContainerTarget>();
            t._prop = prop;
            t._max = t._health = Mathf.Max(1f, health);
            t._aggro = aggroRadius;
            CombatTargets.Register(t);
            return t;
        }

        public float ApplyDamage(in DamageInfo info)
        {
            if (!IsTargetable || info.Amount <= 0f) return 0f;
            float applied = Mathf.Min(_health, info.Amount);
            _health -= applied;
            _prop.SetHealth01(Health01);
            Damaged?.Invoke(this);
            if (_health <= 0f)
            {
                CombatTargets.Unregister(this);
                Broken?.Invoke(this);
            }
            return applied;
        }

        private void OnDisable() => CombatTargets.Unregister(this);

        private void OnDestroy() => CombatTargets.Unregister(this);
    }
}
