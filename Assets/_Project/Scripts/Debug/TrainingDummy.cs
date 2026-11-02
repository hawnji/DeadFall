using System;
using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.DebugTools
{
    /// <summary>
    /// Shootable target with head and body hurtboxes. Falls over at zero health and resets.
    /// Optional strafing to test tracking. Sandbox only.
    /// In a network session every peer builds the same dummies in the same order, so the creation index
    /// identifies a dummy across machines. On clients the host owns health (<see cref="NetworkMirrored"/>).
    /// </summary>
    public sealed class TrainingDummy : MonoBehaviour, IDamageable
    {
        [SerializeField] private float _maxHealth = 150f;
        [SerializeField] private float _resetSeconds = 2f;
        [SerializeField] private float _strafeDistance;
        [SerializeField] private float _strafeSpeed = 3f;

        private float _health;
        private float _resetAt = -1f;
        private Vector3 _origin;
        private float _strafePhase;

        private static readonly List<TrainingDummy> Registry = new List<TrainingDummy>();

        public bool IsAlive => _health > 0f;
        public float DamageReduction => 0f;
        public float TotalDamageTaken { get; private set; }
        public float Health => _health;
        public float MaxHealth => _maxHealth;
        public float StrafeDistance => _strafeDistance;
        /// <summary>Same on every peer: the order in which the sandbox created this dummy.</summary>
        public int NetworkIndex { get; private set; } = -1;
        /// <summary>When true, health only changes through <see cref="SetNetworkHealth"/> (client of a session).</summary>
        public bool NetworkMirrored { get; set; }

        /// <summary>Raised whenever health changes locally (damage or reset).</summary>
        public event Action<TrainingDummy> HealthChanged;

        public static IReadOnlyList<TrainingDummy> All => Registry;

        public static TrainingDummy ByIndex(int index) =>
            index >= 0 && index < Registry.Count ? Registry[index] : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() => Registry.Clear();

        public static TrainingDummy Create(Vector3 position, Transform parent, float maxHealth = 150f, float strafeDistance = 0f)
        {
            var root = new GameObject("TrainingDummy");
            root.transform.SetParent(parent, false);
            root.transform.position = position;

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.85f, 0f);
            body.transform.localScale = new Vector3(0.6f, 0.75f, 0.45f);
            body.AddComponent<Hurtbox>().Configure(HitZone.Body);
            SandboxBootstrap.Tint(body, new Color(0.45f, 0.5f, 0.42f));

            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, 1.8f, 0f);
            head.transform.localScale = Vector3.one * 0.32f;
            head.AddComponent<Hurtbox>().Configure(HitZone.Head);
            SandboxBootstrap.Tint(head, new Color(0.75f, 0.35f, 0.3f));

            var dummy = root.AddComponent<TrainingDummy>();
            dummy._maxHealth = maxHealth;
            dummy._strafeDistance = strafeDistance;
            dummy._health = maxHealth;
            dummy.NetworkIndex = Registry.Count;
            Registry.Add(dummy);
            return dummy;
        }

        private void Awake()
        {
            _health = _maxHealth;
            _origin = transform.position;
        }

        private void Start() => _origin = transform.position;

        private void OnDestroy()
        {
            // Keep indexes stable for the rest of the session; a destroyed slot just resolves to null.
            int i = Registry.IndexOf(this);
            if (i >= 0) Registry[i] = null;
            if (Registry.TrueForAll(d => d == null)) Registry.Clear();
        }

        public float ApplyDamage(in DamageInfo info)
        {
            if (!IsAlive || NetworkMirrored) return 0f;
            float applied = Mathf.Min(_health, info.Amount);
            _health -= applied;
            TotalDamageTaken += applied;
            if (_health <= 0f)
            {
                _resetAt = Time.time + _resetSeconds;
                SetFallen(true);
            }
            HealthChanged?.Invoke(this);
            return applied;
        }

        /// <summary>Applies the host's health value on a client.</summary>
        public void SetNetworkHealth(float health)
        {
            bool wasAlive = IsAlive;
            _health = Mathf.Clamp(health, 0f, _maxHealth);
            _resetAt = -1f;
            if (wasAlive != IsAlive) SetFallen(!IsAlive);
        }

        private void SetFallen(bool fallen) =>
            transform.rotation = Quaternion.Euler(fallen ? -80f : 0f, transform.eulerAngles.y, 0f);

        private void Update()
        {
            if (!NetworkMirrored && !IsAlive && _resetAt > 0f && Time.time >= _resetAt)
            {
                _health = _maxHealth;
                _resetAt = -1f;
                SetFallen(false);
                HealthChanged?.Invoke(this);
            }
            if (_strafeDistance > 0f && IsAlive)
            {
                _strafePhase += Time.deltaTime * _strafeSpeed / Mathf.Max(0.1f, _strafeDistance);
                transform.position = _origin + transform.right * (Mathf.Sin(_strafePhase) * _strafeDistance);
            }
        }
    }
}
