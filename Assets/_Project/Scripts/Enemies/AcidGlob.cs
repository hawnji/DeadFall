using Deadfall.Core;
using UnityEngine;

namespace Deadfall.Enemies
{
    /// <summary>A lobbed acid glob on a fixed arc. Only the authoritative copy deals damage when it lands.</summary>
    public sealed class AcidGlob : MonoBehaviour
    {
        private static Material _material;
        private Vector3 _from, _to;
        private float _flight, _age, _arc;
        private bool _authoritative;
        private float _damage, _poolDps, _poolRadius, _poolSeconds;
        private string _weaponId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _material = null;

        public static GameObject Launch(Vector3 from, Vector3 to, float flight, bool authoritative, float damage,
            float poolDps, float poolRadius, float poolSeconds, string weaponId)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.name = "AcidGlob";
            go.transform.position = from;
            go.transform.localScale = Vector3.one * 0.3f;
            if (_material == null) _material = RuntimeMaterials.CreateLit(new Color(0.55f, 0.9f, 0.15f));
            go.GetComponent<Renderer>().sharedMaterial = _material;
            var g = go.AddComponent<AcidGlob>();
            g._from = from;
            g._to = to;
            g._flight = flight;
            g._arc = Mathf.Clamp(Vector3.Distance(from, to) * 0.18f, 0.5f, 4f);
            g._authoritative = authoritative;
            g._damage = damage;
            g._poolDps = poolDps;
            g._poolRadius = poolRadius;
            g._poolSeconds = poolSeconds;
            g._weaponId = weaponId;
            return go;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / _flight);
            transform.position = Vector3.Lerp(_from, _to, t) + Vector3.up * (_arc * 4f * t * (1f - t));
            if (t < 1f) return;
            if (_authoritative) EnemyAttacks.GlobLanded(_to, _damage, _poolDps, _poolRadius, _poolSeconds, _weaponId);
            Destroy(gameObject);
        }
    }
}
