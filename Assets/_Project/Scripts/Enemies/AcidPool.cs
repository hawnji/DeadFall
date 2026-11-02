using Deadfall.Core;
using UnityEngine;

namespace Deadfall.Enemies
{
    /// <summary>A pool of acid on the floor. The authoritative copy hurts targets standing in it twice a second.</summary>
    public sealed class AcidPool : MonoBehaviour
    {
        private const float TickSeconds = 0.5f;
        private static Material _material;
        private float _radius, _life, _age, _nextTick, _dps;
        private bool _authoritative;
        private string _weaponId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _material = null;

        public static GameObject Create(Vector3 at, float radius, float seconds, bool authoritative, float dps, string weaponId)
        {
            // Settle on the floor below the landing point.
            if (Physics.Raycast(at + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                at = hit.point;
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(go.GetComponent<Collider>());
            go.name = "AcidPool";
            go.transform.position = at + Vector3.up * 0.02f;
            go.transform.localScale = new Vector3(radius * 2f, 0.01f, radius * 2f);
            if (_material == null) _material = RuntimeMaterials.CreateLit(new Color(0.4f, 0.75f, 0.1f));
            go.GetComponent<Renderer>().sharedMaterial = _material;
            var p = go.AddComponent<AcidPool>();
            p._radius = radius;
            p._life = seconds;
            p._authoritative = authoritative;
            p._dps = dps;
            p._weaponId = weaponId;
            p._nextTick = TickSeconds;
            return go;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_age >= _life)
            {
                Destroy(gameObject);
                return;
            }
            // Shrinks over its last second so players can see it ending.
            float fade = Mathf.Clamp01(_life - _age);
            transform.localScale = new Vector3(_radius * 2f * fade, 0.01f, _radius * 2f * fade);
            if (!_authoritative || _age < _nextTick) return;
            _nextTick += TickSeconds;
            EnemyAttacks.HurtTargets(transform.position + Vector3.up * 0.2f, _radius, _dps * TickSeconds, false, _weaponId);
        }
    }
}
