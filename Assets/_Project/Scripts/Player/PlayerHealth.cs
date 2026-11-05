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

    /// <summary>The player bled out (eliminated, D-02).</summary>
    public struct PlayerDied
    {
        public GameObject Player;
        public GameObject Killer;
    }

    /// <summary>The player hit 0 HP and is down (D-01).</summary>
    public struct PlayerDowned
    {
        public GameObject Player;
        public GameObject Killer;
        public int DownNumber;
        public float BleedOutSeconds;
    }

    /// <summary>The player got back up (teammate revive, self-revive kit, return after a surge, or debug).</summary>
    public struct PlayerRevived
    {
        public GameObject Player;
        public bool SelfRevive;
    }

    /// <summary>
    /// Player health using the engine-free HealthModel, with the M11 down/revive cycle (D-01, D-02):
    /// at 0 HP the player is <see cref="LifeState.Downed"/> (crawls with a sidearm, bleeds out over 30/20/10 s),
    /// a teammate or a self-revive kit brings them back, and bleeding out eliminates them until the next surge ends.
    /// Health stays owner-authoritative: this machine runs the timer and others see the state in df.ps.
    /// </summary>
    public sealed class PlayerHealth : MonoBehaviour, IDamageable, ICombatTarget, ISquadMember
    {
        [SerializeField] private HealthStats _stats = new HealthStats();
        [Tooltip("Class or difficulty damage reduction 0..1.")]
        [SerializeField] private float _damageReduction;

        private HealthModel _model;
        private PlayerStatus _status;

        public float Current => _model.Current;
        public float Max => _model.Max;
        public float Normalized => _model.Normalized;
        /// <summary>Up and fighting (not downed or eliminated). Enemies only target living players.</summary>
        public bool IsAlive => Life == LifeState.Alive;
        public LifeState Life { get; private set; } = LifeState.Alive;
        public bool IsDowned => Life == LifeState.Downed;
        public bool IsEliminated => Life == LifeState.Eliminated;
        /// <summary>Downs so far this run (bleed-out shortens with each one).</summary>
        public int DownCount { get; private set; }
        public float BleedOutLeft { get; private set; }
        public float BleedOutTotal { get; private set; } = 1f;
        public float BleedOut01 => IsDowned ? Mathf.Clamp01(BleedOutLeft / Mathf.Max(0.01f, BleedOutTotal)) : 0f;
        /// <summary>Pauses the bleed-out (someone is reviving this player).</summary>
        public bool BleedOutPaused { get; set; }

        // ISquadMember
        public ulong ClientId => _identity != null ? _identity.ClientId : 0UL;
        public bool IsLocalMember => true;
        public float Health01 => Normalized;
        public bool Disconnected => false;
        public Transform Body => transform;
        public Vector3 Eye => transform.position + Vector3.up * (IsAlive ? 1.6f : 0.5f);
        private PlayerIdentity _identity;
        /// <summary>Base reduction combined with <see cref="BonusDamageReduction"/> (never reaches 100%).</summary>
        public float DamageReduction => 1f - (1f - Mathf.Clamp01(_damageReduction)) * (1f - Mathf.Clamp01(BonusDamageReduction)) * (1f - Mathf.Clamp(ModDamageReduction, 0f, MaxModDamageReduction));
        /// <summary>Cap on Field Mod + skill damage reduction (T-77: was 60%, which let a Tank with Guard pass 70%).</summary>
        public const float MaxModDamageReduction = 0.45f;
        /// <summary>Damage reduction from Field Mods and skills (separate from class passives, capped at 45%).</summary>
        public float ModDamageReduction { get; set; }
        /// <summary>Extra reduction from class passives and abilities, set by whoever owns it each frame.</summary>
        public float BonusDamageReduction { get; set; }
        public bool Invulnerable { get; set; }

        public Transform TargetTransform => transform;
        public bool IsTargetable => IsAlive && isActiveAndEnabled;
        public IDamageable Damageable => this;

        private void Awake()
        {
            _model = new HealthModel(_stats);
            _status = GetComponent<PlayerStatus>();
            _identity = GetComponent<PlayerIdentity>();
        }

        private void OnEnable()
        {
            CombatTargets.Register(this);
            SquadMembers.Register(this);
        }

        private void OnDisable()
        {
            CombatTargets.Unregister(this);
            SquadMembers.Unregister(this);
        }

        private void Update()
        {
            if (_identity == null) _identity = GetComponent<PlayerIdentity>();
            if (IsDowned)
            {
                if (!BleedOutPaused && !Invulnerable) BleedOutLeft -= Time.deltaTime;
                if (BleedOutLeft <= 0f) Eliminate(null);
                return;
            }
            if (IsAlive) _model.Tick(Time.timeAsDouble, Time.deltaTime);
        }

        public float ApplyDamage(in DamageInfo info)
        {
            if (Invulnerable || !IsAlive) return 0f;
            float applied = _model.ApplyDamage(info.Amount * DamageModifiers.For(info, gameObject), Time.timeAsDouble);
            if (applied <= 0f) return 0f;

            EventBus<PlayerDamaged>.Publish(new PlayerDamaged
            {
                Player = gameObject,
                Amount = applied,
                Direction = info.Direction,
                HealthNormalized = _model.Normalized,
            });

            if (_model.IsDead) GoDown(info.Source);
            return applied;
        }

        /// <summary>Convenience for environmental damage (hazards, falls, test volumes).</summary>
        public float ApplyRawDamage(float amount, Vector3 direction, GameObject source = null)
        {
            var info = new DamageInfo { Amount = amount * (1f - DamageReduction), Direction = direction, Source = source, Zone = HitZone.Body };
            return ApplyDamage(info);
        }

        public float Heal(float amount) => IsAlive ? _model.Heal(amount) : 0f;

        /// <summary>Extra max health (Field Mods, skills).</summary>
        public void SetBonusMaxHealth(float bonus) => _model?.SetBonusMax(bonus);

        private void GoDown(GameObject killer)
        {
            DownCount++;
            BleedOutTotal = DownedRules.BleedOutSeconds(DownCount);
            BleedOutLeft = BleedOutTotal;
            BleedOutPaused = false;
            SetLife(LifeState.Downed);
            EventBus<PlayerDowned>.Publish(new PlayerDowned { Player = gameObject, Killer = killer, DownNumber = DownCount, BleedOutSeconds = BleedOutTotal });
        }

        /// <summary>Bleeds out now (timer ran out, or the run says so).</summary>
        public void Eliminate(GameObject killer)
        {
            if (IsEliminated) return;
            _model.SetCurrent(0f);
            BleedOutLeft = 0f;
            SetLife(LifeState.Eliminated);
            EventBus<PlayerDied>.Publish(new PlayerDied { Player = gameObject, Killer = killer });
        }

        /// <summary>Stands a downed player up with <paramref name="healthFraction"/> of max health (D-01).</summary>
        public bool ReviveFromDown(float healthFraction, bool selfRevive)
        {
            if (!IsDowned) return false;
            _model.SetCurrent(Mathf.Max(1f, Max * Mathf.Clamp01(healthFraction)));
            SetLife(LifeState.Alive);
            EventBus<PlayerRevived>.Publish(new PlayerRevived { Player = gameObject, SelfRevive = selfRevive });
            return true;
        }

        /// <summary>Full health and up, keeping this run's down count (return after a surge, D-02).</summary>
        public void Revive()
        {
            bool wasUp = IsAlive;
            _model.ResetToFull();
            SetLife(LifeState.Alive);
            if (!wasUp) EventBus<PlayerRevived>.Publish(new PlayerRevived { Player = gameObject });
        }

        /// <summary>Full health, up, and the down count cleared (a new run).</summary>
        public void ResetLife()
        {
            Revive();
            DownCount = 0;
        }

        /// <summary>Rejoin (D-16): puts back the state the host remembered.</summary>
        public void Restore(LifeState life, float health01, int downs, float bleedOutLeft)
        {
            DownCount = Mathf.Max(0, downs);
            if (life == LifeState.Alive)
            {
                _model.SetCurrent(Mathf.Max(1f, Max * Mathf.Clamp01(health01)));
                SetLife(LifeState.Alive);
                return;
            }
            _model.SetCurrent(0f);
            BleedOutTotal = DownedRules.BleedOutSeconds(Mathf.Max(1, DownCount));
            BleedOutLeft = Mathf.Clamp(bleedOutLeft, 1f, BleedOutTotal);
            SetLife(life);
        }

        private void SetLife(LifeState life)
        {
            Life = life;
            if (life != LifeState.Downed) BleedOutPaused = false;
            if (_status == null) return;
            _status.IsDowned = life == LifeState.Downed;
            _status.IsDead = life == LifeState.Eliminated;
        }
    }
}
