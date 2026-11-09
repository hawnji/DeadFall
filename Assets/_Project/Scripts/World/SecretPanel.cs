using System;
using Deadfall.Combat;
using UnityEngine;
using UnityEngine.AI;

namespace Deadfall.World
{
    /// <summary>
    /// A loose vent cover hiding a secret room. Shoot or hit it to break it open. Hits are resolved by the host
    /// (it is an <see cref="INetTarget"/>), and the map session tells every peer when it breaks.
    /// </summary>
    public sealed class SecretPanel : MonoBehaviour, INetTarget
    {
        public const float MaxHealth = 40f;
        private float _health = MaxHealth;
        private int _connection;
        private NavMeshObstacle _obstacle;

        public int ConnectionIndex
        {
            get => _connection;
            set { NetTargets.Unregister(this); _connection = value; NetTargets.Register(this); }
        }

        public bool IsBroken { get; private set; }
        public bool IsAlive => !IsBroken;
        public float DamageReduction => 0f;
        public ushort NetTargetId => (ushort)(NetTargets.MapBase + _connection);
        public Vector3 NetTargetCenter => transform.position;
        public float NetTargetRadius => 1.2f;

        /// <summary>Raised where the panel broke from damage (offline or on the host).</summary>
        public static event Action<SecretPanel> BrokenByDamage;

        public static SecretPanel Create(Vector3 center, bool alongX, float width, float height, Material material, Transform parent)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "SecretPanel";
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = alongX ? new Vector3(width, height, 0.12f) : new Vector3(0.12f, height, width);
            go.GetComponent<Renderer>().sharedMaterial = material;
            var panel = go.AddComponent<SecretPanel>();
            // Closed, it blocks paths into the secret room (it's built after the bake, so it carves); broken, it's gone.
            panel._obstacle = go.AddComponent<NavMeshObstacle>();
            panel._obstacle.shape = NavMeshObstacleShape.Box;
            panel._obstacle.center = Vector3.zero;
            // Local space of the scaled cube: the full opening, and 5 × the 0.12 m thickness = 0.6 m deep. Breaking hides the panel, which drops it.
            panel._obstacle.size = alongX ? new Vector3(1f, 1f, 5f) : new Vector3(5f, 1f, 1f);
            panel._obstacle.carving = true;
            panel._obstacle.carveOnlyStationary = true;
            return panel;
        }

        public float ApplyDamage(in DamageInfo info)
        {
            if (IsBroken || info.Amount <= 0f) return 0f;
            float applied = Mathf.Min(_health, info.Amount);
            _health -= applied;
            if (_health <= 0f)
            {
                Break();
                BrokenByDamage?.Invoke(this);
            }
            return applied;
        }

        /// <summary>Opens the vent (also used when the host says it broke).</summary>
        public void Break()
        {
            if (IsBroken) return;
            IsBroken = true;
            _health = 0f;
            gameObject.SetActive(false);
        }

        private void OnDestroy() => NetTargets.Unregister(this);
    }
}
