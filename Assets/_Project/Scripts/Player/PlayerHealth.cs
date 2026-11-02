using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Player
{
    public struct PlayerDamaged
    {
        public GameObject Player;
        public float Amount;
        public Vector3 Direction;
        public float HealthNormalized;
    }

    public struct PlayerDied
    {
        public GameObject Player;
        public GameObject Killer;
    }

    /// <summary>
    /// Player health using the engine-free HealthModel. Downed and revive states arrive with
    /// multiplayer rules (D-01, D-02); in M1 death simply disables control until Revive is called.
    /// </summary>
    public sealed class PlayerHealth : MonoBehaviour, IDamageable, ICombatTarget
    {
        [SerializeField] private HealthStats _stats = new HealthStats();
        [Tooltip("Class or difficulty damage reduction 0..1.")]
        [SerializeField] private float _damageReduction;

        private HealthModel _model;
        private PlayerStatus _status;

        public float Current => _model.Current;
        public float Max => _model.Max;
        public float Normalized => _model.Normalized;
        public bool IsAlive => !_model.IsDead;
        public float DamageReduction => _damageReduction;
        public bool Invulnerable { get; set; }

        public Transform TargetTransform => transform;
        public bool IsTargetable => IsAlive && isActiveAndEnabled;
        public IDamageable Damageable => this;

        private void Awake()
        {
            _model = new HealthModel(_stats);
            _status = GetComponent<PlayerStatus>();
        }

        private void OnEnable() => CombatTargets.Register(this);
        private void OnDisable() => CombatTargets.Unregister(this);

        private void Update() => _model.Tick(Time.timeAsDouble, Time.deltaTime);

        public float ApplyDamage(in DamageInfo info)
        {
            if (Invulnerable || !IsAlive) return 0f;
            float applied = _model.ApplyDamage(info.Amount, Time.timeAsDouble);
            if (applied <= 0f) return 0f;

            EventBus<PlayerDamaged>.Publish(new PlayerDamaged
            {
                Player = gameObject,
                Amount = applied,
                Direction = info.Direction,
                HealthNormalized = _model.Normalized,
            });

            if (_model.IsDead)
            {
                if (_status != null) _status.IsDead = true;
                EventBus<PlayerDied>.Publish(new PlayerDied { Player = gameObject, Killer = info.Source });
            }
            return applied;
        }

        /// <summary>Convenience for environmental damage (hazards, falls, test volumes).</summary>
        public float ApplyRawDamage(float amount, Vector3 direction, GameObject source = null)
        {
            var info = new DamageInfo { Amount = amount * (1f - Mathf.Clamp01(_damageReduction)), Direction = direction, Source = source, Zone = HitZone.Body };
            return ApplyDamage(info);
        }

        public float Heal(float amount) => _model.Heal(amount);

        public void Revive()
        {
            _model.ResetToFull();
            if (_status != null) _status.IsDead = false;
        }
    }
}
