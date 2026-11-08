using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Rules.Encounters;
using UnityEngine;
using UnityEngine.AI;

namespace Deadfall.Game
{
    /// <summary>
    /// The Escort payload: a rail flatbed carrying a glowing reactor core, with a ring on the floor showing how close
    /// you must stay to push it. On the host it is a combat target the infected hunt (pulled in from
    /// <see cref="EscortRules.AggroRadius"/>) and their hits wear its health down; clients show a mirror the
    /// <see cref="EscortDirector"/> moves from the host's state. The core shifts from cyan to red as it is damaged.
    /// </summary>
    public sealed class EscortCart : MonoBehaviour, IAggroTarget, IDamageable
    {
        private static readonly Color CoreHealthy = new Color(0.35f, 0.95f, 1f), CoreDying = new Color(1f, 0.2f, 0.12f);
        private static readonly Color RingMoving = new Color(0.35f, 1f, 0.5f), RingWaiting = new Color(1f, 0.78f, 0.25f), RingContested = new Color(1f, 0.3f, 0.25f);

        private float _health, _max;
        private bool _mirror;
        private Material _coreMat, _ringMat;
        private Transform _core;
        private Light _light;
        private float _flash;

        public EscortRules.Status Status { get; set; } = EscortRules.Status.Waiting;
        public float Health01 => _max > 0f ? Mathf.Clamp01(_health / _max) : 0f;
        public bool IsAlive => _health > 0f;
        public float DamageReduction => 0f;
        public Transform TargetTransform => transform;
        public bool IsTargetable => !_mirror && IsAlive && isActiveAndEnabled && Status != EscortRules.Status.Delivered;
        public IDamageable Damageable => this;
        public float AggroRadius => EscortRules.AggroRadius;

        /// <summary>Host: raised on every hit that lands, and once when the cart is destroyed.</summary>
        public event System.Action<EscortCart> Damaged, Destroyed;

        public static EscortCart Create(Vector3 position, float yaw, float health, bool mirror, Transform parent)
        {
            var root = new GameObject("Escort Cart");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            var cart = root.AddComponent<EscortCart>();
            cart._mirror = mirror;
            cart._max = cart._health = Mathf.Max(1f, health);
            cart.Build();
            if (!mirror)
            {
                // Infected walk round it instead of through it (no carving: it moves).
                NavMeshObstacle obstacle = root.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = new Vector3(0f, 0.6f, 0f);
                obstacle.size = new Vector3(1.4f, 1.2f, 2.1f);
                obstacle.carving = false;
                CombatTargets.Register(cart);
            }
            return cart;
        }

        private void Build()
        {
            Color steel = new Color(0.22f, 0.24f, 0.26f), dark = new Color(0.08f, 0.08f, 0.09f), hazard = new Color(0.95f, 0.72f, 0.12f);
            Part(PrimitiveType.Cube, new Vector3(0f, 0.42f, 0f), new Vector3(1.5f, 0.28f, 2.3f), steel, true);          // flatbed
            Part(PrimitiveType.Cube, new Vector3(0f, 0.58f, 1.12f), new Vector3(1.52f, 0.06f, 0.08f), hazard, false);   // hazard stripes
            Part(PrimitiveType.Cube, new Vector3(0f, 0.58f, -1.12f), new Vector3(1.52f, 0.06f, 0.08f), hazard, false);
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? -0.62f : 0.62f, z = i < 2 ? 0.75f : -0.75f;
                Transform w = Part(PrimitiveType.Cylinder, new Vector3(x, 0.24f, z), new Vector3(0.42f, 0.06f, 0.42f), dark, false);
                w.localRotation = Quaternion.Euler(0f, 0f, 90f);                                                         // wheels
                Part(PrimitiveType.Cube, new Vector3(x * 0.85f, 1.05f, z * 0.62f), new Vector3(0.07f, 1.0f, 0.07f), steel, false); // cage posts
            }
            Part(PrimitiveType.Cube, new Vector3(0f, 1.56f, 0f), new Vector3(1.2f, 0.06f, 1.05f), steel, false);          // cage roof
            Part(PrimitiveType.Cylinder, new Vector3(0f, 0.62f, 0f), new Vector3(0.95f, 0.08f, 0.95f), dark, false);      // core cradle
            _core = Part(PrimitiveType.Cylinder, new Vector3(0f, 1.08f, 0f), new Vector3(0.62f, 0.42f, 0.62f), Color.white, false);
            _coreMat = RuntimeMaterials.CreateEmissive(CoreHealthy, 2.6f);
            _core.GetComponent<Renderer>().sharedMaterial = _coreMat;
            Part(PrimitiveType.Cube, new Vector3(0f, 0.75f, -1.25f), new Vector3(0.9f, 0.5f, 0.08f), steel, false);       // push bar plate

            var lightGo = new GameObject("Core Light");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            _light = lightGo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.range = 6f;
            _light.intensity = 1.4f;
            _light.color = CoreHealthy;
            _light.shadows = LightShadows.None;

            // Push range ring on the floor.
            var ring = new GameObject("Push Ring");
            ring.transform.SetParent(transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            _ringMat = RuntimeMaterials.CreateEmissive(RingWaiting, 2f);
            float r = EscortRules.PushRadius;
            const int segments = 64;
            float len = 2f * Mathf.PI * r / segments * 1.04f;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                GameObject seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(seg.GetComponent<Collider>());
                seg.transform.SetParent(ring.transform, false);
                seg.transform.localPosition = new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
                seg.transform.localRotation = Quaternion.Euler(0f, a * Mathf.Rad2Deg + 90f, 0f);
                seg.transform.localScale = new Vector3(len, 0.02f, 0.07f);
                Renderer rend = seg.GetComponent<Renderer>();
                rend.sharedMaterial = _ringMat;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        private Transform Part(PrimitiveType type, Vector3 at, Vector3 size, Color color, bool solid)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            if (!solid) Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localPosition = at;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = RuntimeMaterials.CreateLit(color);
            return go.transform;
        }

        public float ApplyDamage(in DamageInfo info)
        {
            if (!IsTargetable || info.Amount <= 0f) return 0f;
            float applied = Mathf.Min(_health, info.Amount);
            _health -= applied;
            _flash = 1f;
            Damaged?.Invoke(this);
            if (_health <= 0f)
            {
                Status = EscortRules.Status.Destroyed;
                CombatTargets.Unregister(this);
                Destroyed?.Invoke(this);
            }
            return applied;
        }

        /// <summary>Host: restores a fraction of max health (checkpoint).</summary>
        public void Heal(float fraction)
        {
            if (IsAlive) _health = Mathf.Min(_max, _health + _max * Mathf.Clamp01(fraction));
        }

        /// <summary>Clients: health from the host's state.</summary>
        public void SetHealth01(float value)
        {
            float before = Health01;
            _health = Mathf.Clamp01(value) * _max;
            if (_health < before * _max - 0.5f) _flash = 1f;
        }

        private void Update()
        {
            float h = Health01;
            bool dead = Status == EscortRules.Status.Destroyed || !IsAlive;
            _flash = Mathf.MoveTowards(_flash, 0f, Time.deltaTime * 4f);
            // Core: cyan when healthy, red when dying, flickering under a quarter, white flash on hits.
            Color c = Color.Lerp(CoreDying, CoreHealthy, h);
            if (h < 0.25f) c *= 0.6f + 0.4f * Mathf.PerlinNoise(Time.time * 12f, 0f);
            c = Color.Lerp(c, Color.white, _flash * 0.6f);
            if (dead) c = new Color(0.1f, 0.1f, 0.1f);
            RuntimeMaterials.SetColor(_coreMat, c);
            if (_coreMat.HasProperty("_EmissionColor")) _coreMat.SetColor("_EmissionColor", c * (dead ? 0f : 2.6f));
            if (_light != null) { _light.color = c; _light.enabled = !dead; }
            if (_core != null && !dead) _core.localRotation = Quaternion.Euler(0f, Time.time * 40f, 0f);

            Color ring = Status == EscortRules.Status.Moving || Status == EscortRules.Status.Door ? RingMoving
                : Status == EscortRules.Status.Contested || Status == EscortRules.Status.Blocked ? RingContested : RingWaiting;
            float pulse = 0.65f + 0.35f * Mathf.Sin(Time.time * (Status == EscortRules.Status.Moving ? 4f : 2f));
            RuntimeMaterials.SetColor(_ringMat, ring);
            if (_ringMat.HasProperty("_EmissionColor")) _ringMat.SetColor("_EmissionColor", ring * 2f * pulse * (dead || Status == EscortRules.Status.Delivered ? 0f : 1f));
        }

        private void OnDisable() => CombatTargets.Unregister(this);
        private void OnDestroy() => CombatTargets.Unregister(this);
    }
}
