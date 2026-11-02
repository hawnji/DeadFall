using System;
using Deadfall.Combat;
using Deadfall.Core;
using UnityEngine;
using UnityEngine.AI;

namespace Deadfall.Classes
{
    /// <summary>
    /// The Tank's deployable shield: a wall with its own health that enemies within its aggro radius attack
    /// instead of players. Bullets from players pass through it (Ignore Raycast layer); enemies are held back by a
    /// NavMesh obstacle. Placeholder look: a translucent-looking blue slab that darkens as it takes damage.
    /// </summary>
    public sealed class ShieldWall : MonoBehaviour, IAggroTarget, IDamageable
    {
        private const int IgnoreRaycastLayer = 2;
        private static readonly Color Full = new Color(0.35f, 0.6f, 0.95f);
        private static readonly Color Low = new Color(0.55f, 0.2f, 0.2f);

        private float _health, _maxHealth;
        private float _aggroRadius;
        private Material _material;

        public GameObject Owner { get; private set; }
        public float Health01 => _maxHealth > 0f ? Mathf.Clamp01(_health / _maxHealth) : 0f;
        public event Action<ShieldWall> Broken;

        public Transform TargetTransform => transform;
        public bool IsTargetable => _health > 0f && isActiveAndEnabled;
        public IDamageable Damageable => this;
        public float AggroRadius => _aggroRadius;
        public bool IsAlive => _health > 0f;
        public float DamageReduction => 0f;

        public static ShieldWall Create(Vector3 groundPosition, Quaternion rotation, float health, float aggroRadius, float width, GameObject owner)
        {
            var root = new GameObject("TankShield");
            root.transform.SetPositionAndRotation(groundPosition, rotation);
            root.layer = IgnoreRaycastLayer;

            GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = "Slab";
            slab.layer = IgnoreRaycastLayer;
            slab.transform.SetParent(root.transform, false);
            slab.transform.localPosition = new Vector3(0f, 1f, 0f);
            slab.transform.localScale = new Vector3(width, 2f, 0.25f);

            var obstacle = root.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = new Vector3(0f, 1f, 0f);
            obstacle.size = new Vector3(width, 2f, 0.4f);
            obstacle.carving = false;

            var wall = root.AddComponent<ShieldWall>();
            wall._maxHealth = wall._health = Mathf.Max(1f, health);
            wall._aggroRadius = Mathf.Max(0f, aggroRadius);
            wall.Owner = owner;
            wall._material = RuntimeMaterials.CreateLit(Full);
            slab.GetComponent<Renderer>().sharedMaterial = wall._material;
            return wall;
        }

        private void OnEnable() => CombatTargets.Register(this);

        private void OnDisable() => CombatTargets.Unregister(this);

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        public float ApplyDamage(in DamageInfo info)
        {
            if (_health <= 0f || info.Amount <= 0f) return 0f;
            float applied = Mathf.Min(_health, info.Amount);
            _health -= applied;
            if (_material != null) RuntimeMaterials.SetColor(_material, Color.Lerp(Low, Full, Health01));
            if (_health <= 0f)
            {
                CombatTargets.Unregister(this);
                Broken?.Invoke(this);
                Destroy(gameObject);
            }
            return applied;
        }
    }
}
