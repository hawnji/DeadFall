using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>The extraction ring (Operations finale): a glowing disc players have to stand in while the timer runs.</summary>
    public sealed class ExtractionZone : MonoBehaviour
    {
        public const float Radius = 5f;
        private Material _material;

        public static ExtractionZone Create(Vector3 position, Transform parent)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(go.GetComponent<Collider>());
            go.name = "ExtractionZone";
            go.transform.SetParent(parent, false);
            go.transform.position = position + Vector3.up * 0.03f;
            go.transform.localScale = new Vector3(Radius * 2f, 0.02f, Radius * 2f);
            var z = go.AddComponent<ExtractionZone>();
            z._material = RuntimeMaterials.CreateLit(new Color(0.2f, 0.8f, 1f));
            go.GetComponent<Renderer>().sharedMaterial = z._material;
            return z;
        }

        /// <summary>True when at least one living player stands inside.</summary>
        public bool AnyAlivePlayerInside()
        {
            IReadOnlyList<ICombatTarget> all = CombatTargets.All;
            for (int i = 0; i < all.Count; i++)
            {
                ICombatTarget t = all[i];
                if (t?.TargetTransform == null || t.Damageable == null || !t.Damageable.IsAlive) continue;
                if (PlayerIdentity.Of(t.TargetTransform.gameObject) == null) continue;
                Vector3 d = t.TargetTransform.position - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude <= Radius * Radius && Mathf.Abs(t.TargetTransform.position.y - transform.position.y) < 3f) return true;
            }
            return false;
        }

        private void Update()
        {
            if (_material != null)
                RuntimeMaterials.SetColor(_material, Color.Lerp(new Color(0.1f, 0.5f, 0.8f), new Color(0.4f, 0.95f, 1f), 0.5f + 0.5f * Mathf.Sin(Time.time * 2.5f)));
        }
    }
}
