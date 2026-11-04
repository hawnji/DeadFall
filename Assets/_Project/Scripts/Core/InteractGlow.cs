using System;
using System.Collections.Generic;
using UnityEngine;

namespace Deadfall.Core
{
    /// <summary>
    /// A soft coloured outline on something the player can use: green for guns, blue for doors and objectives,
    /// yellow for power-ups (<see cref="Guns"/>, <see cref="Objectives"/>, <see cref="PowerUps"/>). Built as an
    /// inverted hull: each visible mesh gets a slightly larger copy that draws only its back faces in a glowing
    /// colour, so the rim shows around the object. The glow pulses gently, shows only within <see cref="Range"/> of the
    /// camera, and hides while the optional <c>active</c> check says the object can't be used.
    /// </summary>
    public sealed class InteractGlow : MonoBehaviour
    {
        public static readonly Color Guns = new Color(0.25f, 1f, 0.35f);
        public static readonly Color Objectives = new Color(0.3f, 0.65f, 1f);
        public static readonly Color PowerUps = new Color(1f, 0.85f, 0.2f);
        public const float Range = 16f;
        private const float Thickness = 0.035f;
        private const float CheckInterval = 0.2f;

        /// <summary>Settings toggle: false hides every glow.</summary>
        public static bool Enabled = true;

        private readonly List<GameObject> _shells = new List<GameObject>(4);
        private Material _material;
        private Color _color;
        private Func<bool> _active;
        private float _check;
        private bool _shown = true;
        private float _phase;

        /// <summary>
        /// Adds a glow to <paramref name="target"/> around every mesh renderer under it that is visible now (hidden
        /// placeholder parts are skipped). Call after the object's model is in place. Returns the glow (or null).
        /// </summary>
        public static InteractGlow Add(GameObject target, Color color, Func<bool> active = null)
        {
            if (target == null) return null;
            var g = target.GetComponent<InteractGlow>();
            if (g == null) g = target.AddComponent<InteractGlow>();
            g.Build(color, active);
            return g;
        }

        private void Build(Color color, Func<bool> active)
        {
            foreach (GameObject s in _shells) if (s != null) Destroy(s);
            _shells.Clear();
            _color = color;
            _active = active;
            _phase = UnityEngine.Random.value * 6f;
            if (_material == null)
            {
                _material = RuntimeMaterials.CreateEmissive(color, 2.2f);
                if (_material.HasProperty("_Cull")) _material.SetFloat("_Cull", 1f); // draw back faces only: the rim
            }
            foreach (MeshRenderer r in GetComponentsInChildren<MeshRenderer>(false))
            {
                if (r == null || !r.enabled || r.gameObject.name == "Glow shell") continue;
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                _shells.Add(Shell(r.transform, mf.sharedMesh));
            }
            _check = 0f;
            _shown = true;
        }

        private GameObject Shell(Transform owner, Mesh mesh)
        {
            var go = new GameObject("Glow shell");
            go.transform.SetParent(owner, false);
            Bounds b = mesh.bounds;
            Vector3 lossy = owner.lossyScale;
            // Grow each axis by the same world-space margin, about the mesh's own centre.
            var scale = new Vector3(Grow(b.size.x, lossy.x), Grow(b.size.y, lossy.y), Grow(b.size.z, lossy.z));
            go.transform.localScale = scale;
            go.transform.localPosition = b.center - Vector3.Scale(b.center, scale);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        private static float Grow(float size, float lossy)
        {
            float s = Mathf.Abs(size * lossy);
            if (s < 0.001f) return 1f;
            return 1f + 2f * Thickness / s;
        }

        private void Update()
        {
            _check -= Time.deltaTime;
            if (_check <= 0f)
            {
                _check = CheckInterval;
                Camera cam = Camera.main;
                bool show = Enabled && cam != null && (cam.transform.position - transform.position).sqrMagnitude <= Range * Range
                            && (_active == null || _active());
                if (show != _shown)
                {
                    _shown = show;
                    foreach (GameObject s in _shells) if (s != null) s.SetActive(show);
                }
            }
            if (!_shown || _material == null) return;
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 3f + _phase);
            if (_material.HasProperty("_EmissionColor")) _material.SetColor("_EmissionColor", _color * (2.2f * pulse));
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
