using Deadfall.Core;
using UnityEngine;

namespace Deadfall.Combat
{
    /// <summary>
    /// An impact grenade (launchers): explodes on the first thing it touches after a short arming time (or after 4 s).
    /// With a fuse (thrown frag grenades) it ignores contact, rolls, and goes off when the fuse runs out. Only an
    /// authoritative copy deals damage; mirrors just fly and flash.
    /// </summary>
    public sealed class Grenade : MonoBehaviour
    {
        private const int IgnoreRaycastLayer = 2;
        private const float ArmSeconds = 0.06f;
        private const float MaxLife = 4f;

        private float _damage, _radius, _selfMultiplier, _fuse;
        private bool _authoritative, _exploded;
        private double _spawnedAt;
        private Material _material;

        public GameObject Owner { get; private set; }
        public string WeaponId { get; private set; }

        /// <summary>Raised wherever a grenade explodes (visual effects hook in here).</summary>
        public static event System.Action<Vector3, float> Exploded;

        public static Grenade Spawn(Vector3 origin, Vector3 velocity, float damage, float radius, float selfMultiplier,
            GameObject owner, string weaponId, bool authoritative, float fuseSeconds = 0f)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = authoritative ? "Grenade" : "Grenade (mirror)";
            go.layer = IgnoreRaycastLayer;
            go.transform.position = origin;
            go.transform.localScale = Vector3.one * 0.14f;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = fuseSeconds > 0f ? 0.6f : 0.25f;
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
            g._fuse = fuseSeconds;
            g._material = RuntimeMaterials.CreateLit(fuseSeconds > 0f ? new Color(0.22f, 0.27f, 0.18f) : new Color(0.85f, 0.75f, 0.3f));
            go.GetComponent<Renderer>().sharedMaterial = g._material;
            if (owner != null)
            {
                Collider mine = go.GetComponent<Collider>();
                foreach (Collider c in owner.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(mine, c);
            }
            return g;
        }

        private Rigidbody _rb;
        private Rigidbody Body => _rb != null ? _rb : _rb = GetComponent<Rigidbody>();

        private void FixedUpdate()
        {
            if (_fuse <= 0f || Body == null) return;
            // Thrown frags feel heavy: extra gravity on top of the engine's 1 g, and spin dies off quickly.
            Body.AddForce(Physics.gravity * (GrenadeRules.GravityScale - 1f), ForceMode.Acceleration);
            Body.AddTorque(-Body.angularVelocity * 3f * Time.fixedDeltaTime, ForceMode.VelocityChange);
        }

        private void OnCollisionStay(Collision collision)
        {
            // Rolling friction: a heavy grenade skids to a stop instead of rolling away.
            if (_fuse <= 0f || Body == null) return;
            Vector3 v = Body.GetPointVelocity(Body.worldCenterOfMass);
            Body.AddForce(new Vector3(-v.x, 0f, -v.z) * Mathf.Clamp01(6f * Time.fixedDeltaTime), ForceMode.VelocityChange);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_fuse > 0f && Body != null)
            {
                // Low bounce: soak up most of the impact speed.
                Vector3 v = Body.GetPointVelocity(Body.worldCenterOfMass);
                Body.AddForce(-v * 0.55f, ForceMode.VelocityChange);
            }
            if (_fuse <= 0f && Time.timeAsDouble - _spawnedAt >= ArmSeconds) Explode();
        }

        private void Update()
        {
            if (Time.timeAsDouble - _spawnedAt > (_fuse > 0f ? _fuse : MaxLife)) Explode();
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
