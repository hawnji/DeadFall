using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Rules.Combat;
using UnityEngine;
using UnityEngine.AI;

namespace Deadfall.Enemies
{
    /// <summary>Raised once when an enemy dies.</summary>
    public struct EnemyDied
    {
        public Enemy Enemy;
        public EnemyDefinition Definition;
        public GameObject Killer;
        public Vector3 Position;
    }

    /// <summary>
    /// One pooled enemy body: health, hurtboxes, NavMesh movement, and carrying out what its EnemyBrain decides.
    /// It has no Update of its own; EnemyManager ticks every enemy from one loop and time-slices perception.
    /// Placeholder visuals are built from primitives; the real models replace the Visual child only.
    /// </summary>
    public sealed class Enemy : MonoBehaviour, IDamageable
    {
        private const float CorpseSeconds = 4f;
        private const float StrikeRangeSlack = 0.5f;
        private const float ChargeContactSlack = 0.9f;

        private static readonly Dictionary<Color, Material> SharedMaterials = new Dictionary<Color, Material>();

        private EnemyDefinition _definition;
        private EnemyBrain _brain;
        private NavMeshAgent _agent;
        private Transform _visual;
        private readonly List<Collider> _hurtColliders = new List<Collider>(4);
        private ICombatTarget _target;
        private float _targetDistance;
        private Vector3 _chargeDirection;
        private bool _chargeLanded;
        private float _weavePhase;
        private float _health;
        private float _maxHealth = 1f;
        private double _diedAt;
        private bool _mirrored;
        private Vector3 _mirrorPos;
        private float _mirrorYaw;
        private double _lastSnapshot;
        private bool _moving;

        public EnemyDefinition Definition => _definition;
        public EnemyBrain Brain => _brain;
        public EnemyState State => _brain.State;
        public float Health => _health;
        /// <summary>Max health after the surge's health scale (GAME_DESIGN §8).</summary>
        public float MaxHealth => _maxHealth;
        public bool IsAlive => _health > 0f;
        public float DamageReduction => _definition.Stats.DamageReduction;
        /// <summary>Per-session slot id used by the snapshot channel (assigned by EnemyManager).</summary>
        public ushort SlotId { get; internal set; }
        internal double NextPerception { get; set; }
        internal bool ReadyToDespawn(double now) => !IsAlive && now >= _diedAt + CorpseSeconds;
        /// <summary>True on a co-op client: this body copies the host's enemy and never decides anything.</summary>
        public bool IsMirror => _mirrored;
        internal double LastSnapshotTime => _lastSnapshot;
        /// <summary>Health as a fraction 0..1 (what snapshots carry).</summary>
        public float Health01 => _maxHealth > 0f ? Mathf.Clamp01(_health / _maxHealth) : 0f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => SharedMaterials.Clear();

        // ---------------------------------------------------------------- construction (pooled)

        internal static Enemy Create(EnemyDefinition def, Transform parent, Vector3 position)
        {
            var root = new GameObject(def.DisplayName);
            root.SetActive(false);
            root.transform.SetParent(parent, false);
            root.transform.position = position;

            var enemy = root.AddComponent<Enemy>();
            enemy._definition = def;
            enemy._brain = new EnemyBrain(def.Stats);
            enemy._weavePhase = Random.value * Mathf.PI * 2f;

            NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
            agent.radius = def.Radius;
            agent.height = def.Height;
            agent.baseOffset = 0f;
            agent.speed = def.Stats.ChaseSpeed;
            agent.acceleration = def.Stats.Acceleration;
            agent.angularSpeed = def.Stats.TurnSpeedDegrees;
            agent.updateRotation = false; // facing is driven by the brain (wind-ups face the target)
            agent.autoBraking = false;
            agent.stoppingDistance = 0f;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            agent.avoidancePriority = def.BodyType == EnemyBodyType.Plated ? 20 : Random.Range(40, 70);
            enemy._agent = agent;

            enemy.BuildVisual();
            return enemy;
        }

        private void BuildVisual()
        {
            _visual = new GameObject("Visual").transform;
            _visual.SetParent(transform, false);
            EnemyDefinition d = _definition;
            float h = d.Height, r = d.Radius;
            Color skin = d.Color;
            Color dark = skin * 0.6f;
            dark.a = 1f;

            switch (d.BodyType)
            {
                case EnemyBodyType.Lean:
                {
                    Transform torso = Part(PrimitiveType.Capsule, "Body", new Vector3(0f, h * 0.45f, 0f), new Vector3(r * 2f, h * 0.38f, r * 1.8f), skin, HitZone.Body);
                    torso.localRotation = Quaternion.Euler(18f, 0f, 0f); // hunched sprinter
                    Part(PrimitiveType.Sphere, "Head", new Vector3(0f, h * 0.86f, h * 0.12f), Vector3.one * 0.28f, dark, HitZone.Head);
                    Part(PrimitiveType.Cube, "ArmL", new Vector3(-r - 0.06f, h * 0.55f, 0.3f), new Vector3(0.08f, 0.08f, 0.6f), skin, null);
                    Part(PrimitiveType.Cube, "ArmR", new Vector3(r + 0.06f, h * 0.55f, 0.3f), new Vector3(0.08f, 0.08f, 0.6f), skin, null);
                    break;
                }
                case EnemyBodyType.Plated:
                {
                    Part(PrimitiveType.Capsule, "Body", new Vector3(0f, h * 0.45f, 0f), new Vector3(r * 2f, h * 0.42f, r * 1.6f), skin, HitZone.Body);
                    Part(PrimitiveType.Sphere, "Head", new Vector3(0f, h * 0.88f, 0.08f), Vector3.one * 0.32f, dark, HitZone.Head);
                    // Front plate: covers the torso from the front. Shots from ahead hit it before the body.
                    Part(PrimitiveType.Cube, "ArmorPlate", new Vector3(0f, h * 0.5f, r * 0.95f), new Vector3(r * 2.3f, h * 0.55f, 0.12f),
                        new Color(0.22f, 0.23f, 0.26f), HitZone.Armor, d.ArmorMultiplier);
                    // Glowing growth on the back: the reward for flanking.
                    Part(PrimitiveType.Cube, "BackGrowth", new Vector3(0f, h * 0.6f, -r * 0.95f), new Vector3(r * 1.1f, h * 0.25f, 0.25f),
                        new Color(0.95f, 0.55f, 0.15f), HitZone.WeakPoint, d.WeakPointMultiplier);
                    Part(PrimitiveType.Cube, "ArmL", new Vector3(-r - 0.12f, h * 0.5f, 0.25f), new Vector3(0.2f, 0.2f, 0.75f), skin, null);
                    Part(PrimitiveType.Cube, "ArmR", new Vector3(r + 0.12f, h * 0.5f, 0.25f), new Vector3(0.2f, 0.2f, 0.75f), skin, null);
                    break;
                }
                default:
                {
                    Part(PrimitiveType.Capsule, "Body", new Vector3(0f, h * 0.42f, 0f), new Vector3(r * 2f, h * 0.4f, r * 1.6f), skin, HitZone.Body);
                    Part(PrimitiveType.Sphere, "Head", new Vector3(0f, h * 0.89f, 0.05f), Vector3.one * 0.3f, dark, HitZone.Head);
                    // Arms reaching forward: the classic shamble, and it shows which way it's facing.
                    Part(PrimitiveType.Cube, "ArmL", new Vector3(-r - 0.06f, h * 0.68f, 0.32f), new Vector3(0.09f, 0.09f, 0.65f), skin, null);
                    Part(PrimitiveType.Cube, "ArmR", new Vector3(r + 0.06f, h * 0.68f, 0.32f), new Vector3(0.09f, 0.09f, 0.65f), skin, null);
                    break;
                }
            }
        }

        private Transform Part(PrimitiveType type, string partName, Vector3 localPos, Vector3 scale, Color color, HitZone? zone, float multiplier = 1f)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = partName;
            go.transform.SetParent(_visual, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = MaterialFor(color);
            Collider col = go.GetComponent<Collider>();
            if (zone.HasValue)
            {
                go.AddComponent<Hurtbox>().Configure(zone.Value, multiplier);
                _hurtColliders.Add(col);
            }
            else
            {
                Destroy(col);
            }
            return go.transform;
        }

        private static Material MaterialFor(Color c)
        {
            if (!SharedMaterials.TryGetValue(c, out Material m) || m == null)
            {
                m = RuntimeMaterials.CreateLit(c);
                SharedMaterials[c] = m;
            }
            return m;
        }

        // ---------------------------------------------------------------- pooling

        internal void Activate(Vector3 position, float yaw, double now, float healthScale = 1f)
        {
            _maxHealth = _definition.Stats.MaxHealth * Mathf.Max(0.01f, healthScale);
            _mirrored = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            gameObject.SetActive(true);
            _agent.enabled = true;
            _agent.Warp(position);
            _agent.isStopped = true;
            _moving = false;
            _health = _maxHealth;
            _target = null;
            _targetDistance = 0f;
            foreach (Collider c in _hurtColliders) c.enabled = true;
            _visual.localRotation = Quaternion.identity;
            _visual.localScale = Vector3.one;
            _brain.Reset(now);
        }

        /// <summary>Activates as a client-side copy of a host enemy: no agent, no AI, health from snapshots.</summary>
        internal void ActivateMirror(Vector3 position, float yaw, double now)
        {
            _mirrored = true;
            _agent.enabled = false; // disabled before activation so it never looks for a NavMesh
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            gameObject.SetActive(true);
            _mirrorPos = position;
            _mirrorYaw = yaw;
            _lastSnapshot = now;
            _moving = false;
            _maxHealth = _definition.Stats.MaxHealth; // clients only see health as a fraction
            _health = _maxHealth;
            _target = null;
            foreach (Collider c in _hurtColliders) c.enabled = true;
            _visual.localRotation = Quaternion.identity;
            _visual.localScale = Vector3.one;
            _brain.Reset(now);
        }

        /// <summary>Applies one snapshot entry from the host.</summary>
        internal void ApplySnapshot(Vector3 position, float yaw, EnemyState state, float health01, double now)
        {
            _lastSnapshot = now;
            _mirrorPos = position;
            _mirrorYaw = yaw;
            if (state == EnemyState.Dead || health01 <= 0f)
            {
                if (IsAlive)
                {
                    _health = 0f;
                    _diedAt = now;
                    _brain.Kill(now);
                    foreach (Collider c in _hurtColliders) c.enabled = false;
                }
                return;
            }
            _health = Mathf.Max(0.01f, health01 * _maxHealth);
            _brain.Mirror(state, now);
        }

        internal void TickMirror(double now, float dt)
        {
            if (!IsAlive)
            {
                float t = Mathf.Clamp01((float)(now - _diedAt) / 0.35f);
                _visual.localRotation = Quaternion.Euler(-85f * t, 0f, 0f);
                return;
            }
            // Smoothly chase the latest snapshot; snap if it jumped (spawned, teleported, or long gap).
            if ((transform.position - _mirrorPos).sqrMagnitude > 25f) transform.position = _mirrorPos;
            else transform.position = Vector3.Lerp(transform.position, _mirrorPos, 1f - Mathf.Exp(-14f * dt));
            Quaternion want = Quaternion.Euler(0f, _mirrorYaw, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, want, 1f - Mathf.Exp(-16f * dt));
            Pose(now);
        }

        internal void Deactivate()
        {
            if (_agent.enabled && _agent.isOnNavMesh) _agent.ResetPath();
            gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- damage

        public float ApplyDamage(in DamageInfo info)
        {
            if (!IsAlive || _mirrored) return 0f; // clients never apply damage; the host does
            float applied = Mathf.Min(_health, info.Amount);
            if (applied <= 0f) return 0f;
            _health -= applied;
            double now = Time.timeAsDouble;
            _brain.OnDamaged(applied, now);
            if (_health <= 0f) Die(info.Source, now);
            return applied;
        }

        /// <summary>Hunts the nearest player from any distance instead of waiting for one to come within detect range.</summary>
        public void Alert()
        {
            if (IsAlive && !_mirrored) _brain.Alert();
        }

        /// <summary>Kills instantly (debug and scripted events).</summary>
        public void Kill(GameObject killer = null)
        {
            if (!IsAlive || _mirrored) return;
            _health = 0f;
            Die(killer, Time.timeAsDouble);
        }

        private void Die(GameObject killer, double now)
        {
            _health = 0f;
            _diedAt = now;
            _brain.Kill(now);
            foreach (Collider c in _hurtColliders) c.enabled = false; // corpses don't soak bullets
            if (_agent.enabled)
            {
                if (_agent.isOnNavMesh) _agent.isStopped = true;
                _agent.enabled = false; // stop avoidance so the living walk through
            }
            EventBus<EnemyDied>.Publish(new EnemyDied { Enemy = this, Definition = _definition, Killer = killer, Position = transform.position });
        }

        // ---------------------------------------------------------------- per tick (called by EnemyManager)

        internal void Tick(double now, float dt, bool perceive)
        {
            if (!IsAlive)
            {
                float t = Mathf.Clamp01((float)(now - _diedAt) / 0.35f);
                _visual.localRotation = Quaternion.Euler(-85f * t, 0f, 0f);
                return;
            }

            if (perceive || (_target != null && !_target.IsTargetable))
                _target = CombatTargets.Nearest(transform.position, out _targetDistance);
            bool hasTarget = _target != null && _target.IsTargetable;
            Vector3 targetPos = hasTarget ? _target.TargetTransform.position : transform.position;
            if (hasTarget) _targetDistance = Vector3.Distance(transform.position, targetPos);

            BrainOutput o = _brain.Tick(new BrainInput { Now = now, HasTarget = hasTarget, TargetDistance = _targetDistance });
            EnemyStats s = _definition.Stats;
            // Off the NavMesh (e.g. spawned in a bad spot): think and attack, but don't drive the agent.
            bool onMesh = _agent.isOnNavMesh;

            if (o.ChargeStarted && onMesh)
            {
                Vector3 dir = Flat(targetPos - transform.position);
                _chargeDirection = dir.sqrMagnitude > 0.001f ? dir.normalized : transform.forward;
                _chargeLanded = false;
                Move(s.ChargeSpeed, s.Acceleration * 4f);
                _agent.SetDestination(transform.position + _chargeDirection * s.ChargeSpeed * s.ChargeSeconds);
            }

            if (o.Charging)
            {
                if (!_chargeLanded && hasTarget && _targetDistance <= _definition.Radius + ChargeContactSlack)
                {
                    _chargeLanded = true;
                    DealDamage(s.ChargeDamage, targetPos);
                    _brain.OnChargeHit(now);
                    if (onMesh) Stop();
                }
            }
            else if (!onMesh)
            {
                _moving = false;
            }
            else if (o.PathToTarget && hasTarget)
            {
                Move(o.Speed, s.Acceleration);
                if (perceive || !_agent.hasPath) _agent.SetDestination(targetPos + Weave(targetPos, now, s));
            }
            else
            {
                Stop();
            }

            if (o.Strike && hasTarget && _targetDistance <= s.AttackRange + StrikeRangeSlack
                && Vector3.Angle(transform.forward, Flat(targetPos - transform.position)) <= s.AttackArcDegrees * 0.5f)
                DealDamage(s.AttackDamage, targetPos);

            Face(o, hasTarget, targetPos, dt, s);
            Pose(now);
        }

        private void Move(float speed, float acceleration)
        {
            _agent.speed = speed;
            _agent.acceleration = acceleration;
            if (!_moving)
            {
                _agent.isStopped = false;
                _moving = true;
            }
        }

        private void Stop()
        {
            if (!_moving) return;
            _moving = false;
            _agent.isStopped = true;
            _agent.velocity = Vector3.zero;
        }

        private Vector3 Weave(Vector3 targetPos, double now, EnemyStats s)
        {
            if (s.WeaveAmplitude <= 0f) return Vector3.zero;
            Vector3 to = Flat(targetPos - transform.position);
            float dist = to.magnitude;
            if (dist < 0.01f) return Vector3.zero;
            Vector3 side = Vector3.Cross(Vector3.up, to / dist);
            float fade = Mathf.Clamp01((dist - 3f) / 6f); // run straight in the last few metres
            return side * (Mathf.Sin((float)now * s.WeaveFrequency * Mathf.PI * 2f + _weavePhase) * s.WeaveAmplitude * fade);
        }

        private void DealDamage(float amount, Vector3 targetPos)
        {
            IDamageable victim = _target?.Damageable;
            if (victim == null || !victim.IsAlive) return;
            victim.ApplyDamage(new DamageInfo
            {
                Amount = amount * (1f - Mathf.Clamp01(victim.DamageReduction)),
                Zone = HitZone.Body,
                Point = targetPos + Vector3.up,
                Direction = (targetPos - transform.position).normalized,
                Source = gameObject,
                WeaponId = _definition.ContentId,
            });
        }

        private void Face(in BrainOutput o, bool hasTarget, Vector3 targetPos, float dt, EnemyStats s)
        {
            Vector3 look;
            if (o.Charging) look = _chargeDirection;
            else if (o.FaceTarget && hasTarget) look = Flat(targetPos - transform.position);
            else if (_moving) look = Flat(_agent.velocity);
            else return;
            if (look.sqrMagnitude < 0.0001f) return;
            Quaternion want = Quaternion.LookRotation(look.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, s.TurnSpeedDegrees * dt);
        }

        /// <summary>Placeholder body language so wind-ups and staggers read clearly without animations.</summary>
        private void Pose(double now)
        {
            float p = _brain.StateProgress(now);
            Quaternion rot = Quaternion.identity;
            Vector3 scale = Vector3.one;
            switch (_brain.State)
            {
                case EnemyState.AttackWindup: rot = Quaternion.Euler(-20f * p, 0f, 0f); break;             // rear back
                case EnemyState.AttackRecover: rot = Quaternion.Euler(Mathf.Lerp(20f, 0f, p), 0f, 0f); break; // follow-through
                case EnemyState.ChargeWindup: scale = new Vector3(1f, 1f - 0.15f * p, 1f); rot = Quaternion.Euler(15f * p, 0f, 0f); break;
                case EnemyState.Charge: rot = Quaternion.Euler(25f, 0f, 0f); break;
                case EnemyState.Stagger: rot = Quaternion.Euler(-25f * (1f - p), 0f, 12f * (1f - p)); break;
            }
            _visual.localRotation = rot;
            _visual.localScale = scale;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
