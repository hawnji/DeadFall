using System;

namespace Deadfall.Rules.Combat
{
    public enum EnemyState : byte
    {
        /// <summary>No target noticed yet.</summary>
        Idle,
        Chase,
        /// <summary>Melee wind-up: stands still facing the target; the strike lands when it ends.</summary>
        AttackWindup,
        AttackRecover,
        /// <summary>Charge wind-up: plants its feet and locks a direction.</summary>
        ChargeWindup,
        /// <summary>Running in a straight line at charge speed; hits whatever it reaches.</summary>
        Charge,
        Stagger,
        Dead,
    }

    /// <summary>Tunable numbers for one enemy type. Lives in an EnemyDefinition asset.</summary>
    [Serializable]
    public sealed class EnemyStats
    {
        public float MaxHealth = 100f;
        /// <summary>Flat damage reduction 0..1 on every hit (armor plates use the Armor hit zone instead).</summary>
        public float DamageReduction;

        public float ChaseSpeed = 3.2f;
        public float Acceleration = 14f;
        public float TurnSpeedDegrees = 420f;
        /// <summary>Notices a player within this distance (m). Taking damage always alerts it.</summary>
        public float DetectRange = 45f;
        /// <summary>Sideways weave while chasing, in metres (0 = straight line). Makes fast enemies harder to track.</summary>
        public float WeaveAmplitude;
        public float WeaveFrequency = 1.2f;

        public float AttackRange = 1.7f;
        public float AttackDamage = 15f;
        public float AttackWindupSeconds = 0.45f;
        public float AttackRecoverSeconds = 0.65f;
        /// <summary>Total angle in front that the strike covers.</summary>
        public float AttackArcDegrees = 100f;

        /// <summary>Damage within roughly <see cref="StaggerWindowSeconds"/> that causes a stagger. 0 = never staggers.</summary>
        public float StaggerThreshold = 45f;
        public float StaggerWindowSeconds = 1f;
        public float StaggerSeconds = 0.55f;
        /// <summary>After a stagger ends, further staggers are ignored for this long (no stun-locking).</summary>
        public float StaggerImmunitySeconds = 1.25f;

        public bool CanCharge;
        public float ChargeMinRange = 5f;
        public float ChargeMaxRange = 12f;
        public float ChargeWindupSeconds = 0.6f;
        public float ChargeSpeed = 10f;
        public float ChargeSeconds = 1f;
        public float ChargeCooldownSeconds = 7f;
        public float ChargeDamage = 35f;
        /// <summary>Whether damage can stagger it out of a charge wind-up or charge.</summary>
        public bool ChargeCanBeStaggered = true;
    }

    /// <summary>What the brain needs to know about the world this tick.</summary>
    public struct BrainInput
    {
        public double Now;
        public bool HasTarget;
        public float TargetDistance;
    }

    /// <summary>What the brain wants the body to do this tick.</summary>
    public struct BrainOutput
    {
        public EnemyState State;
        /// <summary>Speed to move at (0 = stand still).</summary>
        public float Speed;
        /// <summary>Path toward the target (Chase). False during a charge, which keeps its locked direction.</summary>
        public bool PathToTarget;
        public bool FaceTarget;
        /// <summary>True on the single tick the melee strike lands.</summary>
        public bool Strike;
        /// <summary>True on the tick a charge starts: lock the charge direction now.</summary>
        public bool ChargeStarted;
        /// <summary>True while charging: check for contact every tick.</summary>
        public bool Charging;
    }

    /// <summary>
    /// Builds up from damage and decays over time; crossing the threshold staggers. After a stagger the meter
    /// is ignored for the stagger plus an immunity window, so a squad can't stun-lock an enemy forever.
    /// </summary>
    public sealed class StaggerMeter
    {
        private readonly EnemyStats _stats;
        private float _value;
        private double _lastTime;
        private double _immuneUntil = double.NegativeInfinity;

        public StaggerMeter(EnemyStats stats) => _stats = stats;

        public float Value => _value;

        public void Reset()
        {
            _value = 0f;
            _lastTime = 0;
            _immuneUntil = double.NegativeInfinity;
        }

        /// <summary>Adds damage. Returns true if this hit causes a stagger.</summary>
        public bool Add(float damage, double now)
        {
            if (_stats.StaggerThreshold <= 0f || damage <= 0f) return false;
            float decayPerSecond = _stats.StaggerThreshold / Math.Max(0.05f, _stats.StaggerWindowSeconds);
            _value = Math.Max(0f, _value - (float)(now - _lastTime) * decayPerSecond);
            _lastTime = now;
            if (now < _immuneUntil) return false;
            _value += damage;
            if (_value < _stats.StaggerThreshold) return false;
            _value = 0f;
            _immuneUntil = now + _stats.StaggerSeconds + _stats.StaggerImmunitySeconds;
            return true;
        }
    }

    /// <summary>
    /// Engine-free decision making for one enemy: notice, chase, wind up, strike, recover, charge, stagger, die.
    /// Time-driven (absolute timestamps), so behaviour is the same at any frame rate or tick rate. The body
    /// (Deadfall.Enemies.Enemy) feeds in distance to its target and carries out the output.
    /// </summary>
    public sealed class EnemyBrain
    {
        private readonly EnemyStats _stats;
        private readonly StaggerMeter _stagger;
        private double _stateStart;
        private double _chargeReadyAt;
        private bool _alerted;
        private bool _staggerPending;

        public EnemyBrain(EnemyStats stats)
        {
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
            _stagger = new StaggerMeter(stats);
        }

        public EnemyStats Stats => _stats;
        public EnemyState State { get; private set; } = EnemyState.Idle;
        public double StateStart => _stateStart;
        public bool IsDead => State == EnemyState.Dead;
        public bool IsAlerted => _alerted;

        /// <summary>0..1 progress through the current timed state (wind-ups, recovery, stagger, charge).</summary>
        public float StateProgress(double now)
        {
            float duration = DurationOf(State);
            return duration <= 0f ? 0f : (float)Math.Min(1.0, Math.Max(0.0, (now - _stateStart) / duration));
        }

        public void Reset(double now)
        {
            _stagger.Reset();
            _alerted = false;
            _staggerPending = false;
            // A short delay before the first charge, so freshly spawned chargers don't all leap at once.
            _chargeReadyAt = now + _stats.ChargeCooldownSeconds * 0.5;
            Enter(EnemyState.Idle, now);
        }

        /// <summary>Makes the enemy hunt its target at any distance (wave spawns know where the players are).</summary>
        public void Alert()
        {
            if (!IsDead) _alerted = true;
        }

        /// <summary>Queues a stagger now, if the enemy can be staggered at the moment (ignores the meter).</summary>
        public void ForceStagger(double now)
        {
            if (IsDead) return;
            _alerted = true;
            if (CanBeStaggered()) _staggerPending = true;
        }

        /// <summary>Call when the enemy takes damage. Alerts it and may queue a stagger for the next tick.</summary>
        public void OnDamaged(float amount, double now)
        {
            if (IsDead) return;
            _alerted = true;
            if (_stagger.Add(amount, now) && CanBeStaggered()) _staggerPending = true;
        }

        /// <summary>Call when a charge makes contact; the charge ends in recovery.</summary>
        public void OnChargeHit(double now)
        {
            if (State == EnemyState.Charge) Enter(EnemyState.AttackRecover, now);
        }

        /// <summary>
        /// Co-op clients don't think: they copy the host's state from snapshots. Changing state restarts the
        /// local state clock so wind-up poses still play over the right duration.
        /// </summary>
        public void Mirror(EnemyState state, double now)
        {
            if (state != State) Enter(state, now);
        }

        public void Kill(double now)
        {
            if (!IsDead) Enter(EnemyState.Dead, now);
        }

        public BrainOutput Tick(in BrainInput input)
        {
            double now = input.Now;
            var o = new BrainOutput();
            if (IsDead)
            {
                o.State = EnemyState.Dead;
                return o;
            }

            if (_staggerPending)
            {
                _staggerPending = false;
                Enter(EnemyState.Stagger, now);
            }

            double elapsed = now - _stateStart;
            switch (State)
            {
                case EnemyState.Idle:
                    if (input.HasTarget && (_alerted || input.TargetDistance <= _stats.DetectRange))
                    {
                        _alerted = true;
                        Enter(EnemyState.Chase, now);
                        goto case EnemyState.Chase;
                    }
                    break;

                case EnemyState.Chase:
                    if (!input.HasTarget)
                    {
                        Enter(EnemyState.Idle, now);
                        break;
                    }
                    if (input.TargetDistance <= _stats.AttackRange)
                    {
                        Enter(EnemyState.AttackWindup, now);
                        o.FaceTarget = true;
                        break;
                    }
                    if (_stats.CanCharge && now >= _chargeReadyAt
                        && input.TargetDistance >= _stats.ChargeMinRange && input.TargetDistance <= _stats.ChargeMaxRange)
                    {
                        Enter(EnemyState.ChargeWindup, now);
                        o.FaceTarget = true;
                        break;
                    }
                    o.Speed = _stats.ChaseSpeed;
                    o.PathToTarget = true;
                    o.FaceTarget = true;
                    break;

                case EnemyState.AttackWindup:
                    o.FaceTarget = true;
                    if (elapsed >= _stats.AttackWindupSeconds)
                    {
                        o.Strike = true;
                        Enter(EnemyState.AttackRecover, now);
                    }
                    break;

                case EnemyState.AttackRecover:
                    if (elapsed >= _stats.AttackRecoverSeconds) Enter(EnemyState.Chase, now);
                    break;

                case EnemyState.ChargeWindup:
                    o.FaceTarget = true;
                    if (elapsed >= _stats.ChargeWindupSeconds)
                    {
                        Enter(EnemyState.Charge, now);
                        _chargeReadyAt = now + _stats.ChargeCooldownSeconds;
                        o.ChargeStarted = true;
                        o.Charging = true;
                        o.Speed = _stats.ChargeSpeed;
                    }
                    break;

                case EnemyState.Charge:
                    if (elapsed >= _stats.ChargeSeconds)
                    {
                        Enter(EnemyState.AttackRecover, now);
                        break;
                    }
                    o.Charging = true;
                    o.Speed = _stats.ChargeSpeed;
                    break;

                case EnemyState.Stagger:
                    if (elapsed >= _stats.StaggerSeconds) Enter(_alerted ? EnemyState.Chase : EnemyState.Idle, now);
                    break;
            }

            o.State = State;
            return o;
        }

        private bool CanBeStaggered()
        {
            if (State == EnemyState.Dead) return false;
            if ((State == EnemyState.Charge || State == EnemyState.ChargeWindup) && !_stats.ChargeCanBeStaggered) return false;
            return true;
        }

        private float DurationOf(EnemyState s)
        {
            switch (s)
            {
                case EnemyState.AttackWindup: return _stats.AttackWindupSeconds;
                case EnemyState.AttackRecover: return _stats.AttackRecoverSeconds;
                case EnemyState.ChargeWindup: return _stats.ChargeWindupSeconds;
                case EnemyState.Charge: return _stats.ChargeSeconds;
                case EnemyState.Stagger: return _stats.StaggerSeconds;
                default: return 0f;
            }
        }

        private void Enter(EnemyState s, double now)
        {
            State = s;
            _stateStart = now;
        }
    }
}
