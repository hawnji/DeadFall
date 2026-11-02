using Deadfall.Core;
using UnityEngine;

namespace Deadfall.Combat
{
    /// <summary>Placeholder explosion flash: an orange sphere that swells and fades.</summary>
    public sealed class ExplosionFx : MonoBehaviour
    {
        private const float Lifetime = 0.35f;
        private float _radius, _age;
        private Material _material;

        public static void Spawn(Vector3 position, float radius)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.name = "ExplosionFx";
            go.transform.position = position;
            var fx = go.AddComponent<ExplosionFx>();
            fx._radius = radius;
            fx._material = RuntimeMaterials.CreateLit(new Color(1f, 0.6f, 0.2f));
            go.GetComponent<Renderer>().sharedMaterial = fx._material;
            go.transform.localScale = Vector3.one * radius * 0.4f;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / Lifetime);
            transform.localScale = Vector3.one * (_radius * 2f * Mathf.Lerp(0.2f, 1f, Mathf.Sqrt(t)));
            if (_material != null) RuntimeMaterials.SetColor(_material, Color.Lerp(new Color(1f, 0.85f, 0.4f), new Color(0.35f, 0.12f, 0.05f), t));
            if (t >= 1f) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
