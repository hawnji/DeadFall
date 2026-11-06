using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// An infestation nest (DestroyNest): a pulsing mass that keeps birthing Creepers until destroyed. Damage is
    /// applied on the host (client hits arrive by id as an <see cref="INetTarget"/>); the director syncs its health.
    /// </summary>
    public sealed class ObjectiveNest : MonoBehaviour, INetTarget
    {
        private float _health, _max;
        private Transform _body;

        public ushort NetTargetId { get; private set; }
        public byte ObjectiveId { get; private set; }
        public Vector3 NetTargetCenter => transform.position + Vector3.up * 0.8f;
        public float NetTargetRadius => 1.8f;
        public bool IsAlive => _health > 0f;
        public float DamageReduction => 0f;
        public float Health01 => _max > 0f ? Mathf.Clamp01(_health / _max) : 0f;
        /// <summary>Raised where damage is authoritative when the nest dies.</summary>
        public event System.Action<ObjectiveNest> Destroyed;

        public static ObjectiveNest Create(ushort id, byte objectiveId, Vector3 position, float health, Transform parent)
        {
            var root = new GameObject($"Nest #{id}");
            root.transform.SetParent(parent, false);
            root.transform.position = position;
            var n = root.AddComponent<ObjectiveNest>();
            n.NetTargetId = id;
            n.ObjectiveId = objectiveId;
            n._max = n._health = Mathf.Max(1f, health);
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "NestBody";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.7f, 0f);
            body.transform.localScale = new Vector3(2.2f, 1.5f, 2.2f);
            body.GetComponent<Renderer>().sharedMaterial = RuntimeMaterials.CreateLit(new Color(0.45f, 0.18f, 0.3f));
            body.AddComponent<Hurtbox>().Configure(HitZone.Body, 1f);
            n._body = body.transform;
            // Blender model (fit to the body sphere, so it pulses with it); the sphere keeps its collider and hurtbox.
            GameObject model = ModelLibrary.Spawn("obj_nest", body.transform);
            if (model != null) body.GetComponent<Renderer>().enabled = false;
            InteractGlow.AddRainbow(root, () => n.IsAlive);
            NetTargets.Register(n);
            return n;
        }

        public float ApplyDamage(in DamageInfo info)
        {
            if (!IsAlive || info.Amount <= 0f) return 0f;
            float applied = Mathf.Min(_health, info.Amount);
            _health -= applied;
            if (_health <= 0f)
            {
                Kill();
                Destroyed?.Invoke(this);
            }
            return applied;
        }

        /// <summary>Clients: health from the host's state.</summary>
        public void SetHealth01(float value)
        {
            if (!IsAlive) return;
            _health = Mathf.Max(0.01f, value * _max);
            if (value <= 0f) Kill();
        }

        public void Kill()
        {
            _health = 0f;
            gameObject.SetActive(false);
        }

        private void Update()
        {
            float pulse = 1f + Mathf.Sin(Time.time * 3f) * 0.05f;
            _body.localScale = new Vector3(2.2f * pulse, 1.5f / pulse, 2.2f * pulse);
        }

        private void OnDestroy() => NetTargets.Unregister(this);
    }
}
