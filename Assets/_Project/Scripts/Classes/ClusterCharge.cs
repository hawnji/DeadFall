using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Rules.Common;
using UnityEngine;

namespace Deadfall.Classes
{
    /// <summary>
    /// The Demolitionist's thrown charge: bounces, then bursts (main blast) and scatters bomblets that each go off a
    /// moment later. Only the host's copy deals damage; client copies fly and flash for show. Bomblet scatter is
    /// seeded by the network id so both copies scatter roughly alike.
    /// </summary>
    public sealed class ClusterCharge : MonoBehaviour, IAbilitySpawn
    {
        public const string WeaponId = "ability.cluster_charge";
        private const int IgnoreRaycastLayer = 2;
        private const float Fuse = 1.1f;
        private const float ArmDelay = 0.25f;

        private bool _mirror, _bomblet, _detonated;
        private float _damage, _radius, _selfMultiplier, _bombletDamage, _bombletRadius;
        private int _bomblets;
        private double _detonateAt, _armedAt;
        private Material _material;

        public GameObject Owner { get; private set; }
        public ulong OwnerClientId { get; private set; }
        public ushort NetId { get; set; }

        public static ClusterCharge Throw(Vector3 position, Vector3 velocity, int bomblets, float damage, float radius,
            float bombletDamage, float bombletRadius, float selfMultiplier, GameObject owner, ulong ownerClientId, bool mirror)
        {
            ClusterCharge c = CreateBody(position, velocity, 0.22f, new Color(0.75f, 0.45f, 0.9f), mirror ? "ClusterCharge (mirror)" : "ClusterCharge");
            c._mirror = mirror;
            c._bomblets = bomblets;
            c._damage = damage;
            c._radius = radius;
            c._bombletDamage = bombletDamage;
            c._bombletRadius = bombletRadius;
            c._selfMultiplier = selfMultiplier;
            c.Owner = owner;
            c.OwnerClientId = ownerClientId;
            c._armedAt = Time.timeAsDouble + ArmDelay;
            c._detonateAt = Time.timeAsDouble + Fuse;
            return c;
        }

        private static ClusterCharge CreateBody(Vector3 position, Vector3 velocity, float size, Color color, string name)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.layer = IgnoreRaycastLayer; // never blocks bullets or the thrower
            go.transform.position = position;
            go.transform.localScale = Vector3.one * size;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.6f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.AddForce(velocity, ForceMode.VelocityChange); // same call on every Unity version (no velocity/linearVelocity rename)
            var c = go.AddComponent<ClusterCharge>();
            c._material = RuntimeMaterials.CreateLit(color);
            go.GetComponent<Renderer>().sharedMaterial = c._material;
            return c;
        }

        private void OnCollisionEnter(Collision collision)
        {
            // Bursts on the first solid hit once armed (so it doesn't pop in the thrower's face), or when the fuse runs out.
            if (!_bomblet && Time.timeAsDouble >= _armedAt && _detonateAt > Time.timeAsDouble + 0.05)
                _detonateAt = Time.timeAsDouble + 0.05;
        }

        private void Update()
        {
            if (_detonated || Time.timeAsDouble < _detonateAt) return;
            _detonated = true;
            Vector3 pos = transform.position;
            float radius = _bomblet ? _bombletRadius : _radius;
            ExplosionFx.Spawn(pos, radius);
            if (!_mirror)
                Explosions.Detonate(pos, radius, _bomblet ? _bombletDamage : _damage, Owner, WeaponId, _selfMultiplier, _bomblet ? 0.8f : 2f);

            if (!_bomblet && _bomblets > 0)
            {
                var rng = new DeterministicRandom(0xC1A5u ^ ((ulong)NetId << 16) ^ OwnerClientId);
                for (int i = 0; i < _bomblets; i++)
                {
                    float angle = (i / (float)_bomblets) * Mathf.PI * 2f + rng.Range(-0.4f, 0.4f);
                    float speed = rng.Range(4f, 7f);
                    Vector3 v = new Vector3(Mathf.Cos(angle) * speed, rng.Range(4f, 6.5f), Mathf.Sin(angle) * speed);
                    ClusterCharge b = CreateBody(pos + Vector3.up * 0.3f, v, 0.12f, new Color(0.9f, 0.55f, 0.2f), "Bomblet");
                    b._bomblet = true;
                    b._mirror = _mirror;
                    b._bombletDamage = _bombletDamage;
                    b._bombletRadius = _bombletRadius;
                    b._selfMultiplier = _selfMultiplier;
                    b.Owner = Owner;
                    b.OwnerClientId = OwnerClientId;
                    b._detonateAt = Time.timeAsDouble + rng.Range(0.55f, 0.95f);
                }
            }
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
            if (!_bomblet) ClassNetwork.NotifyGone(this);
        }
    }
}
