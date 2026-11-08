using Deadfall.Rules.Common;
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
        /// <summary>The killing blow hit the head (end-of-run headshot stats).</summary>
        public bool Headshot;
    }

    /// <summary>
    /// One pooled enemy body: health, hurtboxes, NavMesh movement, and carrying out what its EnemyBrain decides.
    /// It has no Update of its own; EnemyManager ticks every enemy from one loop and time-slices perception.
    /// Placeholder visuals are built from primitives; the real models replace the Visual child only.
    /// </summary>
    public sealed class Enemy : MonoBehaviour, IDamageable, IHostile
    {
        private const float CorpseSeconds = 4f;
        private const float StrikeRangeSlack = 0.5f;
        private const float ChargeContactSlack = 0.9f;
        /// <summary>A blocker counts as cutting the path when it is this close to where the partial path ends.</summary>
        private const float PathEndBlockerRange = 3f;
        /// <summary>How far a cut-off enemy looks for a blocker it can walk up to (a closet is a few metres across).</summary>
        private const float SealedBlockerRange = 14f;

        private static readonly Dictionary<Color, Material> SharedMaterials = new Dictionary<Color, Material>();

        private EnemyDefinition _definition;
        /// <summary>This enemy's own copy of its stats: traits, auras and Specimen phases change it, never the definition.</summary>
        private EnemyStats _stats;
        private EnemyBrain _brain;
        private EnemyTraits _traits;
        private Transform _traitMarkers;
        private SpecimenPhase _phase;
        private SpecimenModifiers _bossMods;
        private double _nextSummonAt;
        private double _lastDamagedAt;
        private bool _hasLineOfSight;
        private double _blockerUntil;

        /// <summary>
        /// The barricade of the breach closet this enemy spawned in (set by the spawn director). While it stands and
        /// the enemy is still on the closet side, the enemy breaks it however far away the players are.
        /// </summary>
        public IBlocker HomeBlocker { get; set; }
        private double _slowUntil;
        private float _slow;
        private bool _exploded;
        private bool _explodePending;
        private GameObject _explodeCredit;
        private readonly List<PhasePart> _phaseParts = new List<PhasePart>(4);

        /// <summary>A hurtbox that only exists in some Specimen phases (growths, core, plates).</summary>
        private struct PhasePart
        {
            public GameObject Go;
            public SpecimenPhase From;
            public SpecimenPhase To;
        }

        /// <summary>Host: a Specimen in its Rampage phase calls for help (boss, how many). The encounter layer spawns them.</summary>
        public static event System.Action<Enemy, int> SummonRequested;
        /// <summary>Host: global move speed and damage multipliers for events (Frenzy, M12). 1 = normal.</summary>
        public static float EventSpeedMultiplier = 1f;
        /// <summary>Host: the current surge's speed-up (rises with surges in every mode; bosses ignore it).</summary>
        public static float RoundSpeedScale = 1f;
        /// <summary>Round scaling never pushes an enemy past this (just under the player's 7 m/s sprint).</summary>
        public const float RoundSpeedCap = 6.6f;
        public static float EventDamageMultiplier = 1f;
        /// <summary>Any machine: a Specimen changed phase (boss, new phase).</summary>
        public static event System.Action<Enemy, SpecimenPhase> PhaseChanged;
        private NavMeshAgent _agent;
        private Transform _visual;
        private IEnemyRig _rig;
        private static int _viewerFrame = -1;
        private static Vector3 _viewer;

        /// <summary>The camera position this frame (rigs skip far-away enemies).</summary>
        private static Vector3 Viewer
        {
            get
            {
                if (_viewerFrame != Time.frameCount)
                {
                    _viewerFrame = Time.frameCount;
                    Camera cam = Camera.main;
                    _viewer = cam != null ? cam.transform.position : Vector3.zero;
                }
                return _viewer;
            }
        }
        private readonly List<Collider> _hurtColliders = new List<Collider>(4);
        private ICombatTarget _target;
        private float _targetDistance;
        private Vector3 _chargeDirection;
        private bool _chargeLanded;
        private float _weavePhase;
        /// <summary>Per-enemy pace (0.92 to 1.08) so a group strings out instead of marching in step.</summary>
        private float _paceJitter = 1f;
        // Corner spreading: instead of every enemy hugging the inside of a turn in single file, most swing out to a
        // point on the outside of the next corner first (a different distance each time), then path on from there.
        private bool _detouring;
        private Vector3 _detour;
        private Vector3 _lastDetourCorner = new Vector3(float.MaxValue, 0f, 0f);
        private double _detourUntil;
        private bool _checkCorners;
        private float _health;
        private float _maxHealth = 1f;
        private Vector3 _knockDirection;
        private float _knockRemaining;
        // Sidestep when shot (standard Husks only, DodgeRules).
        private Vector3 _dodgeDirection;
        private float _dodgeRemaining, _dodgeLean;
        private double _nextDodgeRollAt, _nextDodgeAt;

        /// <summary>The current surge (set by the wave manager), for the dodge chance ramp.</summary>
        public static int DodgeSurge;
        private const float KnockSpeed = 14f;
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
        public float DamageReduction => Mathf.Min(0.8f, _stats.DamageReduction + AuraReduction);
        /// <summary>This enemy's live stats (after traits and phase).</summary>
        public EnemyStats Stats => _stats;
        public EnemyTraits Traits => _traits;
        public SpecimenPhase Phase => _phase;
        public SpecimenModifiers BossModifiers => _bossMods;
        /// <summary>Set every frame by EnemyManager from nearby Alphas.</summary>
        internal float AuraSpeedBonus { get; set; }
        internal float AuraReduction { get; set; }
        /// <summary>The extra byte in each snapshot: traits, or phase and modifiers for a Specimen.</summary>
        internal byte ExtraByte => _definition.IsBoss ? SpecimenRules.Pack(_phase, _bossMods) : (byte)_traits;
        public Vector3 AimPoint => transform.position + Vector3.up * (_definition.Height * 0.6f);
        public IDamageable Damageable => this;
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
        private static void ResetStatics()
        {
            SharedMaterials.Clear();
            GlowMaterials.Clear();
            SummonRequested = null;
            EventSpeedMultiplier = EventDamageMultiplier = RoundSpeedScale = 1f;
            PhaseChanged = null;
        }

        // ---------------------------------------------------------------- construction (pooled)

        internal static Enemy Create(EnemyDefinition def, Transform parent, Vector3 position)
        {
            var root = new GameObject(def.DisplayName);
            root.SetActive(false);
            root.transform.SetParent(parent, false);
            root.transform.position = position;

            var enemy = root.AddComponent<Enemy>();
            enemy._definition = def;
            enemy._stats = def.Stats.Clone();
            enemy._brain = new EnemyBrain(enemy._stats);
            enemy._weavePhase = Random.value * Mathf.PI * 2f;
            enemy._paceJitter = Random.Range(0.92f, 1.08f);

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
            root.AddComponent<EnemyFootsteps>();
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
                case EnemyBodyType.Crawler:
                {
                    Transform body = Part(PrimitiveType.Capsule, "Body", new Vector3(0f, h * 0.45f, 0f), new Vector3(r * 1.8f, 0.45f, r * 1.8f), skin, HitZone.Body);
                    body.localRotation = Quaternion.Euler(90f, 0f, 0f); // lying flat
                    Part(PrimitiveType.Sphere, "Head", new Vector3(0f, h * 0.55f, 0.48f), Vector3.one * 0.24f, dark, HitZone.Head);
                    Part(PrimitiveType.Cube, "ArmL", new Vector3(-r, h * 0.2f, 0.35f), new Vector3(0.07f, 0.3f, 0.07f), skin, null);
                    Part(PrimitiveType.Cube, "ArmR", new Vector3(r, h * 0.2f, 0.35f), new Vector3(0.07f, 0.3f, 0.07f), skin, null);
                    break;
                }
                case EnemyBodyType.Bloated:
                {
                    Part(PrimitiveType.Sphere, "Body", new Vector3(0f, h * 0.45f, 0f), new Vector3(r * 2.4f, h * 0.5f, r * 2.2f), skin, HitZone.Body);
                    Part(PrimitiveType.Sphere, "Head", new Vector3(0f, h * 0.88f, 0.05f), Vector3.one * 0.26f, dark, HitZone.Head);
                    // Glowing sacs: the tell that it's about to burst, and a weak point.
                    Part(PrimitiveType.Sphere, "Sac", new Vector3(0f, h * 0.42f, r * 0.95f), Vector3.one * 0.38f,
                        new Color(1f, 0.55f, 0.1f), HitZone.WeakPoint, d.WeakPointMultiplier);
                    break;
                }
                case EnemyBodyType.Spitter:
                {
                    Transform torso = Part(PrimitiveType.Capsule, "Body", new Vector3(0f, h * 0.42f, 0f), new Vector3(r * 2f, h * 0.38f, r * 1.7f), skin, HitZone.Body);
                    torso.localRotation = Quaternion.Euler(12f, 0f, 0f);
                    Part(PrimitiveType.Sphere, "Head", new Vector3(0f, h * 0.84f, 0.14f), Vector3.one * 0.28f, dark, HitZone.Head);
                    Part(PrimitiveType.Sphere, "ThroatSac", new Vector3(0f, h * 0.7f, 0.28f), new Vector3(0.34f, 0.3f, 0.3f),
                        new Color(0.7f, 0.85f, 0.2f), HitZone.WeakPoint, d.WeakPointMultiplier);
                    break;
                }
                case EnemyBodyType.Alpha:
                {
                    Part(PrimitiveType.Capsule, "Body", new Vector3(0f, h * 0.43f, 0f), new Vector3(r * 2.1f, h * 0.42f, r * 1.7f), skin, HitZone.Body);
                    Part(PrimitiveType.Sphere, "Head", new Vector3(0f, h * 0.89f, 0.06f), Vector3.one * 0.32f, dark, HitZone.Head);
                    Part(PrimitiveType.Cube, "Growth", new Vector3(0f, h * 0.78f, -r * 0.6f), new Vector3(r * 1.6f, 0.28f, 0.34f),
                        new Color(0.35f, 0.85f, 1f), HitZone.WeakPoint, d.WeakPointMultiplier);
                    Part(PrimitiveType.Cube, "ArmL", new Vector3(-r - 0.1f, h * 0.62f, 0.3f), new Vector3(0.13f, 0.13f, 0.75f), skin, null);
                    Part(PrimitiveType.Cube, "ArmR", new Vector3(r + 0.1f, h * 0.62f, 0.3f), new Vector3(0.13f, 0.13f, 0.75f), skin, null);
                    break;
                }
                case EnemyBodyType.Specimen:
                {
                    Part(PrimitiveType.Capsule, "Body", new Vector3(0f, h * 0.45f, 0f), new Vector3(r * 2f, h * 0.42f, r * 1.6f), skin, HitZone.Body);
                    Part(PrimitiveType.Sphere, "Head", new Vector3(0f, h * 0.9f, 0.2f), Vector3.one * 0.55f, dark, HitZone.Head);
                    Color plate = new Color(0.25f, 0.24f, 0.27f);
                    PhaseOnly(Part(PrimitiveType.Cube, "ChestPlate", new Vector3(0f, h * 0.52f, r * 0.9f), new Vector3(r * 1.9f, h * 0.4f, 0.16f),
                        plate, HitZone.Armor, d.ArmorMultiplier), SpecimenPhase.Stalk, SpecimenPhase.Rampage);
                    PhaseOnly(Part(PrimitiveType.Sphere, "GrowthL", new Vector3(-r * 0.55f, h * 0.7f, -r * 0.8f), Vector3.one * 0.5f,
                        new Color(0.95f, 0.45f, 0.2f), HitZone.WeakPoint, SpecimenRules.GrowthMultiplier), SpecimenPhase.Rampage, SpecimenPhase.ExposedCore);
                    PhaseOnly(Part(PrimitiveType.Sphere, "GrowthR", new Vector3(r * 0.55f, h * 0.7f, -r * 0.8f), Vector3.one * 0.5f,
                        new Color(0.95f, 0.45f, 0.2f), HitZone.WeakPoint, SpecimenRules.GrowthMultiplier), SpecimenPhase.Rampage, SpecimenPhase.ExposedCore);
                    PhaseOnly(Part(PrimitiveType.Sphere, "Core", new Vector3(0f, h * 0.55f, r * 0.85f), Vector3.one * 0.6f,
                        new Color(1f, 0.2f, 0.35f), HitZone.WeakPoint, SpecimenRules.CoreMultiplier), SpecimenPhase.ExposedCore, SpecimenPhase.ExposedCore);
                    Part(PrimitiveType.Cube, "ArmL", new Vector3(-r - 0.25f, h * 0.55f, 0.4f), new Vector3(0.35f, 0.35f, 1.4f), skin, null);
                    Part(PrimitiveType.Cube, "ArmR", new Vector3(r + 0.25f, h * 0.55f, 0.4f), new Vector3(0.35f, 0.35f, 1.4f), skin, null);
                    break;
                }
                case EnemyBodyType.Brood:
                {
                    // Low swollen abdomen behind a raised front body; egg sacs on the back open in Rampage, the core in the maw last.
                    Part(PrimitiveType.Sphere, "Body", new Vector3(0f, h * 0.38f, -r * 0.35f), new Vector3(r * 2f, h * 0.62f, r * 2.3f), skin, HitZone.Body);
                    Part(PrimitiveType.Capsule, "Front", new Vector3(0f, h * 0.55f, r * 0.55f), new Vector3(r * 1.1f, h * 0.35f, r * 1.1f), skin, HitZone.Body);
                    Part(PrimitiveType.Sphere, "Head", new Vector3(0f, h * 0.78f, r * 0.95f), Vector3.one * 0.6f, dark, HitZone.Head);
                    Color plate = new Color(0.22f, 0.24f, 0.2f);
                    PhaseOnly(Part(PrimitiveType.Cube, "Carapace", new Vector3(0f, h * 0.72f, -r * 0.35f), new Vector3(r * 1.7f, 0.2f, r * 1.9f),
                        plate, HitZone.Armor, d.ArmorMultiplier), SpecimenPhase.Stalk, SpecimenPhase.Stalk);
                    Color egg = new Color(0.95f, 0.85f, 0.35f);
                    for (int e = 0; e < 3; e++)
                        PhaseOnly(Part(PrimitiveType.Sphere, "EggSac" + e, new Vector3((e - 1) * r * 0.6f, h * 0.72f, -r * (0.5f + 0.25f * (e % 2))), Vector3.one * 0.55f,
                            egg, HitZone.WeakPoint, SpecimenRules.GrowthMultiplier), SpecimenPhase.Rampage, SpecimenPhase.ExposedCore);
                    PhaseOnly(Part(PrimitiveType.Sphere, "Core", new Vector3(0f, h * 0.6f, r * 1.15f), Vector3.one * 0.55f,
                        new Color(1f, 0.25f, 0.3f), HitZone.WeakPoint, SpecimenRules.CoreMultiplier), SpecimenPhase.ExposedCore, SpecimenPhase.ExposedCore);
                    for (int l = 0; l < 3; l++)
                    {
                        float z = r * (0.6f - l * 0.6f);
                        Part(PrimitiveType.Cube, "LegL" + l, new Vector3(-r * 1.1f, h * 0.2f, z), new Vector3(0.9f, 0.16f, 0.16f), dark, null);
                        Part(PrimitiveType.Cube, "LegR" + l, new Vector3(r * 1.1f, h * 0.2f, z), new Vector3(0.9f, 0.16f, 0.16f), dark, null);
                    }
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

            DressWithModel(skin, dark);
        }

        /// <summary>
        /// Swaps the primitive look for the body type's Blender model (T-58). The primitives stay as hurtboxes with their
        /// renderers off; Specimen phase parts stay visible because they come and go by phase.
        /// </summary>
        private void DressWithModel(Color skin, Color dark)
        {
            if (!ModelFor(_definition.BodyType, out string id, out float h0, out float r0)) return;
            System.Func<string, Color?> tint = slot => slot == "Skin" ? skin : slot == "SkinDark" ? dark : (Color?)null;
            // The rigged, animated zombie when there is one for this body; else the limb-swing model.
            string zm = RiggedModelFor(_definition.BodyType);
            GameObject model = zm != null && ModelLibrary.Has(zm) ? ModelLibrary.Spawn(zm, _visual, tint, keepAnimator: true) : null;
            if (model != null)
            {
                _rig = ZombieAnimator.Bind(model, zm, _definition.Height);
                if (_rig == null)
                {
                    Destroy(model);
                    model = null;
                }
            }
            if (model == null)
            {
                model = ModelLibrary.Spawn(id, _visual, tint);
                if (model == null) return;
                _rig = EnemyRig.Bind(model.transform, _visual, _definition.Height, _definition.BodyType == EnemyBodyType.Crawler);
            }
            float sr = _definition.Radius / r0, sh = _definition.Height / h0;
            model.transform.localScale = new Vector3(sr, sh, sr);
            ModelLibrary.HideExcept(_visual, model.transform, r =>
            {
                foreach (PhasePart p in _phaseParts) if (p.Go != null && r.transform.IsChildOf(p.Go.transform)) return true;
                return false;
            });
        }

        /// <summary>The rigged zm_* model with animation clips for a body type (built at the same nominal size as
        /// <see cref="ModelFor"/>), or null. Crawlers and the Broodmother keep their limb-swing models.</summary>
        internal static string RiggedModelFor(EnemyBodyType body)
        {
            switch (body)
            {
                case EnemyBodyType.Standard: return "zm_standard";
                case EnemyBodyType.Lean: return "zm_lean";
                case EnemyBodyType.Plated: return "zm_plated";
                case EnemyBodyType.Bloated: return "zm_bloated";
                case EnemyBodyType.Spitter: return "zm_spitter";
                case EnemyBodyType.Alpha: return "zm_alpha";
                case EnemyBodyType.Specimen: return "zm_specimen";
                default: return null;
            }
        }

        /// <summary>Model id and the nominal height/radius it was built at (the prototype definition for that body).</summary>
        private static bool ModelFor(EnemyBodyType body, out string id, out float height, out float radius)
        {
            switch (body)
            {
                case EnemyBodyType.Standard: id = "enemy_standard"; height = 1.8f; radius = 0.35f; return true;
                case EnemyBodyType.Lean: id = "enemy_lean"; height = 1.65f; radius = 0.3f; return true;
                case EnemyBodyType.Plated: id = "enemy_plated"; height = 2.3f; radius = 0.6f; return true;
                case EnemyBodyType.Crawler: id = "enemy_crawler"; height = 0.7f; radius = 0.3f; return true;
                case EnemyBodyType.Bloated: id = "enemy_bloated"; height = 1.75f; radius = 0.42f; return true;
                case EnemyBodyType.Spitter: id = "enemy_spitter"; height = 1.7f; radius = 0.38f; return true;
                case EnemyBodyType.Alpha: id = "enemy_alpha"; height = 2.15f; radius = 0.42f; return true;
                case EnemyBodyType.Specimen: id = "enemy_specimen"; height = 3.4f; radius = 1f; return true;
                case EnemyBodyType.Brood: id = "enemy_brood"; height = 2.6f; radius = 1.3f; return true;
                default: id = null; height = radius = 1f; return false;
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

        private void PhaseOnly(Transform part, SpecimenPhase from, SpecimenPhase to) =>
            _phaseParts.Add(new PhasePart { Go = part.gameObject, From = from, To = to });

        private void ShowPhaseParts()
        {
            foreach (PhasePart p in _phaseParts)
                if (p.Go != null) p.Go.SetActive(_phase >= p.From && _phase <= p.To);
        }

        /// <summary>
        /// Tells players which mutations this enemy has (GAME_DESIGN §7: read the tell): a glowing ring on the floor
        /// around its feet per trait (concentric when it has several) and a soft light of the first trait's colour on
        /// the body. (The old floating bands poked through the real models.)
        /// </summary>
        private void ShowTraitMarkers()
        {
            if (_traitMarkers != null) Destroy(_traitMarkers.gameObject);
            _traitMarkers = null;
            if (_traits == EnemyTraits.None) return;
            _traitMarkers = new GameObject("Traits").transform;
            _traitMarkers.SetParent(transform, false);
            float h = _definition.Height, r = _definition.Radius;
            int i = 0;
            Color first = Color.white;
            void Ring(Color c)
            {
                if (i == 0) first = c;
                const int Segments = 18;
                float radius = r * 1.5f + 0.18f + i * 0.16f;
                float seg = 2f * Mathf.PI * radius / Segments * 0.8f;
                Material m = GlowFor(c);
                for (int k = 0; k < Segments; k++)
                {
                    float ang = k * Mathf.PI * 2f / Segments;
                    GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(go.GetComponent<Collider>());
                    go.transform.SetParent(_traitMarkers, false);
                    go.transform.localPosition = new Vector3(Mathf.Sin(ang) * radius, 0.03f, Mathf.Cos(ang) * radius);
                    go.transform.localRotation = Quaternion.Euler(0f, ang * Mathf.Rad2Deg + 90f, 0f);
                    go.transform.localScale = new Vector3(seg, 0.02f, 0.06f);
                    Renderer rend = go.GetComponent<Renderer>();
                    rend.sharedMaterial = m;
                    rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                i++;
            }
            if ((_traits & EnemyTraits.Regenerating) != 0) Ring(new Color(0.2f, 0.95f, 0.35f));
            if ((_traits & EnemyTraits.Splitting) != 0) Ring(new Color(0.85f, 0.35f, 0.8f));
            if ((_traits & EnemyTraits.Hardened) != 0) Ring(new Color(0.75f, 0.78f, 0.85f));
            if ((_traits & EnemyTraits.Swift) != 0) Ring(new Color(0.95f, 0.15f, 0.1f));
            var lightGo = new GameObject("TraitGlow");
            lightGo.transform.SetParent(_traitMarkers, false);
            lightGo.transform.localPosition = new Vector3(0f, h * 0.55f, r + 0.35f);
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = first;
            light.range = Mathf.Max(2f, h * 1.3f);
            light.intensity = 1.6f;
            light.shadows = LightShadows.None;
        }

        private static readonly Dictionary<Color, Material> GlowMaterials = new Dictionary<Color, Material>();

        private static Material GlowFor(Color c)
        {
            if (!GlowMaterials.TryGetValue(c, out Material m) || m == null)
            {
                m = RuntimeMaterials.CreateEmissive(c, 2.2f);
                GlowMaterials[c] = m;
            }
            return m;
        }

        private void SetPhase(SpecimenPhase phase, double now, bool announce)
        {
            if (phase == _phase && !announce) return;
            bool changed = phase != _phase;
            _phase = phase;
            if (!_mirrored) SpecimenRules.ApplyPhase(_definition.Stats, _stats, _phase, _bossMods, _definition.IsBrood);
            ShowPhaseParts();
            if (changed)
            {
                if (!_mirrored)
                {
                    _brain.Roar(now);
                    _nextSummonAt = now + 2.0;
                }
                PhaseChanged?.Invoke(this, _phase);
            }
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

        internal void Activate(Vector3 position, float yaw, double now, float healthScale = 1f,
            EnemyTraits traits = EnemyTraits.None, SpecimenModifiers bossMods = SpecimenModifiers.None)
        {
            _stats.CopyFrom(_definition.Stats);
            _traits = _definition.IsBoss ? EnemyTraits.None : traits;
            _bossMods = _definition.IsBoss ? bossMods : SpecimenModifiers.None;
            VariantRules.Apply(_stats, _traits);
            _phase = SpecimenPhase.Stalk;
            if (_definition.IsBoss) SpecimenRules.ApplyPhase(_definition.Stats, _stats, _phase, _bossMods, _definition.IsBrood);
            ShowPhaseParts();
            ShowTraitMarkers();
            _exploded = false;
            _explodePending = false;
            _explodeCredit = null;
            _hasLineOfSight = false;
            _lastDamagedAt = now;
            AuraSpeedBonus = AuraReduction = 0f;
            _maxHealth = _stats.MaxHealth * Mathf.Max(0.01f, healthScale);
            _mirrored = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            gameObject.SetActive(true);
            _agent.enabled = true;
            _agent.Warp(position);
            _agent.isStopped = true;
            _moving = false;
            _health = _maxHealth;
            _knockRemaining = 0f;
            _dodgeRemaining = _dodgeLean = 0f;
            _nextDodgeRollAt = _nextDodgeAt = 0.0;
            _target = null;
            HomeBlocker = null;
            _targetDistance = 0f;
            _ventStage = 0;
            foreach (Collider c in _hurtColliders) c.enabled = true;
            _visual.localRotation = Quaternion.identity;
            _visual.localScale = Vector3.one;
            _brain.Reset(now);
            _rig?.Reset();
            Hostiles.Register(this);
        }

        /// <summary>Activates as a client-side copy of a host enemy: no agent, no AI, health from snapshots.</summary>
        internal void ActivateMirror(Vector3 position, float yaw, double now)
        {
            _mirrored = true;
            _stats.CopyFrom(_definition.Stats);
            _traits = EnemyTraits.None;
            _bossMods = SpecimenModifiers.None;
            _phase = SpecimenPhase.Stalk;
            ShowPhaseParts();
            ShowTraitMarkers();
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
            _rig?.Reset();
            Hostiles.Register(this);
        }

        /// <summary>Applies one snapshot entry from the host.</summary>
        internal void ApplySnapshot(Vector3 position, float yaw, EnemyState state, float health01, byte extra, double now)
        {
            _lastSnapshot = now;
            if (_definition.IsBoss)
            {
                SpecimenRules.Unpack(extra, out SpecimenPhase phase, out SpecimenModifiers mods);
                _bossMods = mods;
                if (phase != _phase) SetPhase(phase, now, false);
            }
            else if ((byte)_traits != extra)
            {
                _traits = (EnemyTraits)extra;
                ShowTraitMarkers();
            }
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
                    if (_traitMarkers != null) _traitMarkers.gameObject.SetActive(false);
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
                if (_rig == null || !_rig.HandlesPose) _visual.localRotation = Quaternion.Euler(-85f * t, 0f, 0f);
                _rig?.Tick(transform.position, EnemyState.Dead, 1f, false, (float)now, Time.deltaTime, Viewer);
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
            Hostiles.Unregister(this);
            if (_agent.enabled && _agent.isOnNavMesh) _agent.ResetPath();
            gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- damage

        public float ApplyDamage(in DamageInfo info)
        {
            if (!IsAlive || _mirrored) return 0f; // clients never apply damage; the host does
            float applied = Mathf.Min(_health, info.Amount * DamageModifiers.For(info, gameObject));
            if (applied <= 0f) return 0f;
            _health -= applied;
            double now = Time.timeAsDouble;
            _lastDamagedAt = now;
            _brain.OnDamaged(applied, now);
            if (info.ForceStagger) _brain.ForceStagger(now);
            if (info.Knockback > 0f)
            {
                Vector3 dir = Flat(info.Direction);
                if (dir.sqrMagnitude > 0.0001f)
                {
                    // Heavier bodies move less (Bulwark about 60% of a Husk).
                    _knockDirection = dir.normalized;
                    _knockRemaining = Mathf.Min(6f, info.Knockback * Mathf.Clamp(0.35f / Mathf.Max(0.1f, _definition.Radius), 0.3f, 1.2f));
                }
            }
            if (_health <= 0f) Die(info.Source, now, info.Zone == HitZone.Head);
            else TryDodge(info, now);
            return applied;
        }

        /// <summary>Host: a standard Husk that's shot may sidestep left or right (DodgeRules), to a clear spot on the NavMesh.</summary>
        private void TryDodge(in DamageInfo info, double now)
        {
            if (!DodgeRules.CanDodge(_definition.ContentId) || !DodgeRules.IsGunfire(info.WeaponId, info.IsMelee)) return;
            if (now < _nextDodgeRollAt || now < _nextDodgeAt || _dodgeRemaining > 0f || _knockRemaining > 0f || InVent) return;
            if (!_agent.enabled || !_agent.isOnNavMesh || _brain.State == EnemyState.Stagger) return;
            _nextDodgeRollAt = now + DodgeRules.RollCooldown;
            if (Random.value >= DodgeRules.Chance(DodgeSurge)) return;
            Vector3 shot = Flat(info.Direction);
            if (shot.sqrMagnitude < 0.0001f) shot = transform.forward;
            Vector3 side = Vector3.Cross(Vector3.up, shot.normalized);
            if (Random.value < 0.5f) side = -side;
            // Take the side with room; if neither has room, stand and take it.
            for (int i = 0; i < 2; i++, side = -side)
            {
                Vector3 to = transform.position + side * DodgeRules.Distance;
                if (NavMesh.Raycast(transform.position, to, out NavMeshHit _, _agent.areaMask)) continue;
                _dodgeDirection = side;
                _dodgeRemaining = DodgeRules.Distance;
                _nextDodgeAt = now + DodgeRules.DodgeCooldown;
                // Lean into the step: positive rolls to the enemy's right.
                _dodgeLean = Vector3.Dot(side, transform.right) >= 0f ? -1f : 1f;
                return;
            }
        }

        /// <summary>Host: slows movement by <paramref name="fraction"/> (0..0.9) for a moment (Arc Fence).</summary>
        public void ApplySlow(float fraction, float seconds)
        {
            if (!IsAlive || _mirrored) return;
            double until = Time.timeAsDouble + seconds;
            if (fraction >= _slow || until > _slowUntil)
            {
                _slow = Mathf.Clamp(Mathf.Max(fraction, Time.timeAsDouble < _slowUntil ? _slow : 0f), 0f, 0.9f);
                _slowUntil = until;
            }
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

        private void Die(GameObject killer, double now, bool headshot = false)
        {
            _health = 0f;
            _diedAt = now;
            _brain.Kill(now);
            foreach (Collider c in _hurtColliders) c.enabled = false; // corpses don't soak bullets
            if (_traitMarkers != null) _traitMarkers.gameObject.SetActive(false); // corpses lose their mutation glow
            if (_agent.enabled)
            {
                if (_agent.isOnNavMesh) _agent.isStopped = true;
                _agent.enabled = false; // stop avoidance so the living walk through
            }
            EventBus<EnemyDied>.Publish(new EnemyDied { Enemy = this, Definition = _definition, Killer = killer, Position = transform.position, Headshot = headshot });
            // A Pyre bursts when killed too; chain kills are credited to whoever killed it. Deferred to its next tick so a
            // blast that kills another Pyre never runs inside the first blast's overlap loop. Culls (no killer) don't burst.
            if (_stats.CanExplode && _stats.ExplodesOnDeath && killer != null && !_exploded)
            {
                _explodePending = true;
                _explodeCredit = killer;
            }
        }

        private void Explode(GameObject credit)
        {
            if (_exploded) return;
            _exploded = true;
            EnemyAttacks.Blast(this, transform.position + Vector3.up * (_definition.Height * 0.4f), _stats.ExplodeRadius, _stats.ExplodeDamage,
                credit != null ? credit : gameObject);
        }

        // ---------------------------------------------------------------- crawl vents (Creepers)

        private const float VentSinkSeconds = 0.4f;
        private const float VentRiseSeconds = 0.45f;
        private const float VentSinkDepth = 1.4f;
        /// <summary>Inside a vent the body waits this far under the floor (out of sight and out of reach).</summary>
        private const float VentHideDepth = 3f;
        private int _ventStage; // 0 none, 1 sinking into the grate, 2 crawling, 3 climbing out
        private double _ventStageEnds;
        private float _ventTravel;
        private Vector3 _ventFrom, _ventTo;

        /// <summary>True from the moment it dives into a crawl vent until it has climbed out of another.</summary>
        public bool InVent => _ventStage != 0;

        /// <summary>
        /// Host: drops into the crawl vent at <paramref name="grate"/> and comes out of the one at <paramref name="exit"/>
        /// after <paramref name="travelSeconds"/>. While inside it is below the floor with its hurtboxes off; mirrors
        /// follow the position, so clients see it sink and climb out.
        /// </summary>
        public bool EnterVent(Vector3 grate, Vector3 exit, float travelSeconds)
        {
            if (!IsAlive || _mirrored || InVent) return false;
            if (_agent.enabled)
            {
                if (_agent.isOnNavMesh) _agent.ResetPath();
                _agent.enabled = false;
            }
            _moving = false;
            _knockRemaining = 0f;
            _ventFrom = grate;
            _ventTo = exit;
            _ventTravel = Mathf.Max(0.5f, travelSeconds);
            transform.position = grate;
            _ventStage = 1;
            _ventStageEnds = Time.timeAsDouble + VentSinkSeconds;
            return true;
        }

        private void TickVent(double now)
        {
            float left = (float)(_ventStageEnds - now);
            switch (_ventStage)
            {
                case 1:
                    transform.position = _ventFrom + Vector3.down * (VentSinkDepth * (1f - Mathf.Clamp01(left / VentSinkSeconds)));
                    if (left > 0f) return;
                    foreach (Collider c in _hurtColliders) c.enabled = false;
                    transform.position = _ventFrom + Vector3.down * VentHideDepth;
                    _ventStage = 2;
                    _ventStageEnds = now + _ventTravel;
                    return;
                case 2:
                    if (left > 0f) return;
                    transform.position = _ventTo + Vector3.down * VentSinkDepth;
                    foreach (Collider c in _hurtColliders) c.enabled = true;
                    _ventStage = 3;
                    _ventStageEnds = now + VentRiseSeconds;
                    return;
                default:
                    transform.position = _ventTo + Vector3.down * (VentSinkDepth * Mathf.Clamp01(left / VentRiseSeconds));
                    if (left > 0f) return;
                    transform.position = _ventTo;
                    _ventStage = 0;
                    _agent.enabled = true;
                    _agent.Warp(_ventTo);
                    _agent.isStopped = true;
                    _target = null; // look around on the next perception tick
                    _brain.Alert();
                    return;
            }
        }

        // ---------------------------------------------------------------- per tick (called by EnemyManager)

        internal void Tick(double now, float dt, bool perceive)
        {
            if (!IsAlive)
            {
                if (_explodePending)
                {
                    _explodePending = false;
                    Explode(_explodeCredit);
                    _explodeCredit = null;
                }
                float t = Mathf.Clamp01((float)(now - _diedAt) / 0.35f);
                if (_rig == null || !_rig.HandlesPose) _visual.localRotation = Quaternion.Euler(-85f * t, 0f, 0f);
                _rig?.Tick(transform.position, EnemyState.Dead, 1f, false, (float)now, Time.deltaTime, Viewer);
                return;
            }

            if (_ventStage != 0)
            {
                TickVent(now);
                _rig?.Tick(transform.position, EnemyState.Chase, 0f, true, (float)now, Time.deltaTime, Viewer);
                return;
            }

            bool stickToBlocker = _target is IBlocker && _target.IsTargetable && now < _blockerUntil;
            if ((perceive && !stickToBlocker) || (_target != null && !_target.IsTargetable))
                _target = _stats.CanExplode ? EnemyAttacks.BiggestCluster(transform.position, out _targetDistance)
                                            : CombatTargets.Nearest(transform.position, out _targetDistance);
            // Still in the closet it spawned in: break that barricade first, whatever the path finder says. Far from
            // the squad, the partial path can end in a back corner of the closet, which the checks below don't trust.
            if (HomeBlocker != null && (!HomeBlocker.IsTargetable || !HomeBlocker.BreakableFrom(transform.position)
                                        || HomeBlocker.DistanceFrom(transform.position) > SealedBlockerRange))
                HomeBlocker = null;
            if (perceive && HomeBlocker != null && !(_target is IBlocker))
            {
                _target = HomeBlocker;
                _blockerUntil = now + 2.5;
            }
            // A barricade has cut the path to the player: tear down the nearest blocker instead (never stuck for good).
            if (perceive && Blockers.Count > 0 && _target != null && !(_target is IBlocker) && _agent.enabled && _agent.isOnNavMesh
                && !_agent.pathPending && ((_agent.hasPath && _agent.pathStatus == NavMeshPathStatus.PathPartial) || _agent.pathStatus == NavMeshPathStatus.PathInvalid))
            {
                // Only the blocker that actually cuts the path (near where the partial path ends), never one through
                // a wall that is merely close in a straight line. The partial path ends at the reachable point nearest
                // the target, which in a sealed closet can be a back corner far from the boards when the player is off
                // to one side; then fall back to the nearest blocker this enemy can actually walk up to.
                IBlocker blocker = Blockers.Nearest(_agent.pathEndPosition, PathEndBlockerRange, b => b.BreakableFrom(transform.position))
                                   ?? Blockers.Nearest(transform.position, SealedBlockerRange, CanWalkUpTo);
                if (blocker != null)
                {
                    _target = blocker;
                    _blockerUntil = now + 2.5; // keep at it a while before checking for a way round again
                }
            }
            bool hasTarget = _target != null && _target.IsTargetable;
            Vector3 targetPos = hasTarget ? _target.TargetTransform.position : transform.position;
            if (hasTarget) _targetDistance = _target is IBlocker blk ? blk.DistanceFrom(transform.position) : Vector3.Distance(transform.position, targetPos);
            if (perceive && _stats.CanShoot)
                _hasLineOfSight = hasTarget && Explosions.HasLineOfSight(MouthPoint, targetPos + Vector3.up * 1.2f, null);

            if (_stats.RegenPerSecond > 0f && now - _lastDamagedAt >= VariantRules.RegenDelaySeconds && _health < _maxHealth)
                _health = Mathf.Min(_maxHealth, _health + _stats.RegenPerSecond * (_maxHealth / Mathf.Max(1f, _stats.MaxHealth)) * dt);

            if (_definition.IsBoss)
            {
                SpecimenPhase want = SpecimenRules.PhaseFor(Health01);
                if (want > _phase) SetPhase(want, now, false); // phases only move forward
                bool brood = _definition.IsBrood;
                if (SpecimenRules.SummonsIn(_phase, brood) && now >= _nextSummonAt)
                {
                    _nextSummonAt = now + SpecimenRules.SummonInterval(_phase, brood);
                    SummonRequested?.Invoke(this, SpecimenRules.SummonCount(Mathf.Max(1, CombatTargets.All.Count), _bossMods, brood));
                }
            }

            if (_knockRemaining > 0f && _agent.enabled && _agent.isOnNavMesh)
            {
                float step = Mathf.Min(_knockRemaining, KnockSpeed * dt);
                _knockRemaining -= step;
                _agent.Move(_knockDirection * step);
            }
            else _knockRemaining = 0f;

            if (_dodgeRemaining > 0f && _agent.enabled && _agent.isOnNavMesh)
            {
                float step = Mathf.Min(_dodgeRemaining, DodgeRules.Speed * dt);
                _dodgeRemaining -= step;
                _agent.Move(_dodgeDirection * step);
            }
            else _dodgeRemaining = 0f;

            BrainOutput o = _brain.Tick(new BrainInput
            {
                Now = now, HasTarget = hasTarget, TargetDistance = _targetDistance, HasLineOfSight = _hasLineOfSight,
            });
            EnemyStats s = _stats;
            if (o.Explode)
            {
                // Detonated on contact: the brain is already dead; the body dies (no killer, no credits) and bursts.
                _health = 0f;
                Die(null, now);
                Explode(gameObject);
                return;
            }
            if (o.Shoot && hasTarget)
                EnemyAttacks.Spit(this, MouthPoint, Lead(targetPos), s);
            float speedScale = (1f + AuraSpeedBonus) * (now < _slowUntil ? 1f - _slow : 1f);
            // Off the NavMesh (e.g. spawned in a bad spot): think and attack, but don't drive the agent.
            bool onMesh = _agent.isOnNavMesh;

            if (o.ChargeStarted && onMesh)
            {
                Vector3 dir = Flat(targetPos - transform.position);
                _chargeDirection = dir.sqrMagnitude > 0.001f ? dir.normalized : transform.forward;
                _chargeLanded = false;
                Move(s.ChargeSpeed * (now < _slowUntil ? 1f - _slow : 1f), s.Acceleration * 4f);
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
                Move(o.Speed * speedScale * _paceJitter, s.Acceleration);
                ChaseStep(targetPos, now, perceive, s);
            }
            else
            {
                Stop();
            }

            if (o.Strike && hasTarget && _targetDistance <= s.AttackRange + StrikeRangeSlack
                && (_target is IBlocker || Vector3.Angle(transform.forward, Flat(targetPos - transform.position)) <= s.AttackArcDegrees * 0.5f))
                DealDamage(s.AttackDamage, targetPos);

            Face(o, hasTarget, targetPos, dt, s);
            Pose(now);
        }

        private NavMeshPath _blockerPath;

        /// <summary>True when this enemy is on the blocker's break-in side and a NavMesh path from here ends at it.</summary>
        private bool CanWalkUpTo(IBlocker b)
        {
            if (!b.BreakableFrom(transform.position)) return false;
            if (_blockerPath == null) _blockerPath = new NavMeshPath();
            // The opening's centre sits in the carved hole, off the NavMesh; path to the nearest NavMesh point instead.
            Vector3 goal = b.TargetTransform.position;
            if (NavMesh.SamplePosition(goal, out NavMeshHit near, 2.5f, _agent.areaMask)) goal = near.position;
            if (!NavMesh.CalculatePath(transform.position, goal, _agent.areaMask, _blockerPath)) return false;
            Vector3[] corners = _blockerPath.corners;
            return corners.Length > 0 && b.DistanceFrom(corners[corners.Length - 1]) <= PathEndBlockerRange;
        }

        private Vector3 MouthPoint => transform.position + Vector3.up * (_definition.Height * 0.8f) + transform.forward * (_definition.Radius + 0.2f);

        /// <summary>Aims where the target will be when the glob lands (half lead, so strafing still dodges it).</summary>
        private Vector3 Lead(Vector3 targetPos)
        {
            Vector3 v = Vector3.zero;
            if (_target?.TargetTransform != null && _target.TargetTransform.TryGetComponent(out CharacterController cc)) v = cc.velocity;
            float t = Vector3.Distance(MouthPoint, targetPos) / Mathf.Max(1f, _stats.ProjectileSpeed);
            return targetPos + Flat(v) * (t * 0.5f);
        }

        private void Move(float speed, float acceleration)
        {
            if (RoundSpeedScale > 1f && !_definition.IsBoss)
                speed = Mathf.Min(speed * RoundSpeedScale, Mathf.Max(speed, RoundSpeedCap));
            _agent.speed = speed * EventSpeedMultiplier;
            _agent.acceleration = acceleration * EventSpeedMultiplier;
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

        private static readonly Vector3[] CornerBuffer = new Vector3[8];

        /// <summary>Paths towards the target, swinging wide around corners on the way (see <see cref="_detouring"/>).</summary>
        private void ChaseStep(Vector3 targetPos, double now, bool perceive, EnemyStats s)
        {
            if (_detouring)
            {
                bool reached = Flat(_detour - transform.position).sqrMagnitude < 1.1f * 1.1f;
                // Close fights and expired detours go straight back to the target.
                if (!reached && now < _detourUntil && _targetDistance > 5f) return;
                _detouring = false;
                _agent.SetDestination(targetPos + Weave(targetPos, now, s));
                _checkCorners = true;
                return;
            }
            if (perceive || !_agent.hasPath)
            {
                _agent.SetDestination(targetPos + Weave(targetPos, now, s));
                _checkCorners = true;
                return;
            }
            if (_checkCorners && !_agent.pathPending && _agent.hasPath)
            {
                _checkCorners = false;
                TryCornerDetour(now);
            }
        }

        private void TryCornerDetour(double now)
        {
            if (_targetDistance < 7f || _definition.IsBoss || _target is IBlocker || Random.value < 0.25f) return; // some still cut the corner
            int n = _agent.path.GetCornersNonAlloc(CornerBuffer);
            if (n < 3) return; // a straight run: nothing to round
            Vector3 c0 = transform.position, c1 = CornerBuffer[1], c2 = CornerBuffer[2];
            float toCorner = Flat(c1 - c0).magnitude;
            if (toCorner < 2.5f || toCorner > 16f) return;
            if (Flat(c1 - _lastDetourCorner).sqrMagnitude < 2.5f * 2.5f) return; // already rounded this one
            Vector3 inDir = Flat(c1 - c0).normalized, outDir = Flat(c2 - c1).normalized;
            Vector3 bend = outDir - inDir;
            if (bend.sqrMagnitude < 0.04f) return; // barely a turn
            Vector3 outside = -bend.normalized;
            float wide = Random.Range(0.8f, 2.4f);
            Vector3 goal = c1 + outside * wide - inDir * Random.Range(0f, 1f);
            // Keep the point on the walkable floor (stop at the wall if the corridor is narrow).
            if (NavMesh.Raycast(c1, goal, out NavMeshHit hit, _agent.areaMask)) goal = hit.position - (goal - c1).normalized * (_definition.Radius + 0.1f);
            if (!NavMesh.SamplePosition(goal, out NavMeshHit on, 1f, _agent.areaMask)) return;
            if (Flat(on.position - c1).sqrMagnitude < 0.5f * 0.5f) return; // no room to swing out
            _lastDetourCorner = c1;
            _detour = on.position;
            _detouring = true;
            _detourUntil = now + Mathf.Clamp(toCorner / Mathf.Max(1f, _agent.speed) * 1.6f + 0.5f, 1f, 5f);
            _agent.SetDestination(_detour);
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
            GameAudio.Play(SfxId.EnemyAttack, transform.position + Vector3.up, 1f, _definition != null && _definition.IsBoss ? 0.7f : 1f);
            IDamageable victim = _target?.Damageable;
            if (victim == null || !victim.IsAlive) return;
            victim.ApplyDamage(new DamageInfo
            {
                Amount = amount * EventDamageMultiplier * (1f - Mathf.Clamp01(victim.DamageReduction)),
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
                case EnemyState.RangedWindup: rot = Quaternion.Euler(-30f * p, 0f, 0f); scale = new Vector3(1f, 1f + 0.06f * p, 1f); break; // rear back, throat swells
                case EnemyState.RangedRecover: rot = Quaternion.Euler(Mathf.Lerp(15f, 0f, p), 0f, 0f); break;
                case EnemyState.ExplodeWindup:                                                                                       // swell and shudder
                    float swell = 1f + 0.35f * p;
                    scale = new Vector3(swell, swell, swell);
                    rot = Quaternion.Euler(0f, 0f, Mathf.Sin((float)now * 60f) * 6f * p);
                    break;
            }
            if (_rig != null && _rig.HandlesPose) rot = Quaternion.identity; // the clips act it out
            if (_dodgeRemaining > 0f) rot *= Quaternion.Euler(0f, 0f, 18f * _dodgeLean * Mathf.Clamp01(_dodgeRemaining / (DodgeRules.Distance * 0.5f)));
            _visual.localRotation = rot;
            _visual.localScale = scale;
            _rig?.Tick(transform.position, _brain.State, p, true, (float)now, Time.deltaTime, Viewer);
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
