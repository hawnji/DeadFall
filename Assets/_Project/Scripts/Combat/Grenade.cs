using Deadfall.Core;
using UnityEngine;

namespace Deadfall.Combat
{
    /// <summary>
    /// An impact grenade. Explodes on the first thing it touches after a short arming time (or after 4 s). Only an
    /// authoritative copy deals damage; mirrors just fly and flash.
    /// </summary>
    public sealed class Grenade : MonoBehaviour
    {
        private const int IgnoreRaycastLayer = 2;
        private const float ArmSeconds = 0.06f;
        private const float MaxLife = 4f;

        private float _damage, _radius, _selfMultiplier;
        private bool _authoritative, _exploded;
        private double _spawnedAt;
        private Material _material;

        public GameObject Owner { get; private set; }
        public string WeaponId { get; private set; }

        /// <summary>Raised wherever a grenade explodes (visual effects hook in here).</summary>
        public static event System.Action<Vector3, float> Exploded;

        public static Grenade Spawn(Vector3 origin, Vector3 velocity, float damage, float radius, float selfMultiplier,
            GameObject owner, string weaponId, bool authoritative)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = authoritative ? "Grenade" : "Grenade (mirror)";
            go.layer = IgnoreRaycastLayer;
            go.transform.position = origin;
            go.transform.localScale = Vector3.one * 0.14f;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.25f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.AddForce(velocity, ForceMode.VelocityChange);
            var g = go.AddComponent<Grenade>();
            g._damage = damage;
            g._radius = radius;
            g._selfMultiplier = selfMultiplier;
            g._authoritative = authoritative;
            g.Owner = owner;
            g.WeaponId = weaponId;
            g._spawnedAt = Time.timeAsDouble;
            g._material = RuntimeMaterials.CreateLit(new Color(0.85f, 0.75f, 0.3f));
            go.GetComponent<Renderer>().sharedMaterial = g._material;
            if (owner != null)
            {
                Collider mine = go.GetComponent<Collider>();
                foreach (Collider c in owner.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(mine, c);
            }
            return g;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (Time.timeAsDouble - _spawnedAt >= ArmSeconds) Explode();
        }

        private void Update()
        {
            if (Time.timeAsDouble - _spawnedAt > MaxLife) Explode();
        }

        private void Explode()
        {
            if (_exploded) return;
            _exploded = true;
            Vector3 p = transform.position;
            if (_authoritative) Explosions.Detonate(p, _radius, _damage, Owner, WeaponId, _selfMultiplier, 1.5f);
            Exploded?.Invoke(p, _radius);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
