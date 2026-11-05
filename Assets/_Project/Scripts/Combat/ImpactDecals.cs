using System.Collections.Generic;
using UnityEngine;

namespace Deadfall.Combat
{
    /// <summary>
    /// Pooled bullet holes and explosion scorch marks (flat quads that fade out), plus short explosion light flashes.
    /// </summary>
    public sealed class ImpactDecals : MonoBehaviour
    {
        private const int MaxHoles = 80;
        private const int MaxScorches = 20;
        private const float HoleSeconds = 10f;
        private const float ScorchSeconds = 18f;
        private const float FadeSeconds = 2f;

        private sealed class Decal
        {
            public Transform Transform;
            public Renderer Renderer;
            public MaterialPropertyBlock Block;
            public float Born;
            public float Life;
            public Color Color;
        }

        private sealed class Flare
        {
            public Light Light;
            public float Born;
            public float Peak;
        }

        private static ImpactDecals _instance;
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly Queue<Decal> _holes = new Queue<Decal>();
        private readonly Queue<Decal> _scorches = new Queue<Decal>();
        private readonly List<Decal> _live = new List<Decal>();
        private readonly List<Flare> _flares = new List<Flare>();
        private Material _holeMaterial, _scorchMaterial;
        private Texture2D _scorchTexture;

        private static ImpactDecals Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var go = new GameObject("[ImpactDecals]");
                _instance = go.AddComponent<ImpactDecals>();
                return _instance;
            }
        }

        public static void BulletHole(Vector3 point, Vector3 normal) =>
            Instance.Place(Instance._holes, MaxHoles, true, point, normal, Random.Range(0.07f, 0.1f), HoleSeconds, new Color(0.05f, 0.05f, 0.05f, 0.85f));

        public static void Scorch(Vector3 point, Vector3 normal, float radius) =>
            Instance.Place(Instance._scorches, MaxScorches, false, point, normal, Mathf.Clamp(radius * 1.2f, 0.8f, 7f), ScorchSeconds, new Color(0.04f, 0.035f, 0.03f, 0.8f));

        /// <summary>A short orange point-light flash.</summary>
        public static void Flash(Vector3 position, float radius)
        {
            ImpactDecals d = Instance;
            Flare f = null;
            foreach (Flare x in d._flares) if (!x.Light.enabled) { f = x; break; }
            if (f == null)
            {
                if (d._flares.Count >= 6) f = d._flares[0];
                else
                {
                    var go = new GameObject("Flash");
                    go.transform.SetParent(d.transform, false);
                    var l = go.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.color = new Color(1f, 0.62f, 0.3f);
                    l.shadows = LightShadows.None;
                    f = new Flare { Light = l };
                    d._flares.Add(f);
                }
            }
            f.Light.transform.position = position + Vector3.up * 0.3f;
            f.Light.range = Mathf.Clamp(radius * 3.5f, 4f, 30f);
            f.Peak = Mathf.Clamp(radius * 1.5f, 3f, 9f);
            f.Light.intensity = f.Peak;
            f.Born = Time.time;
            f.Light.enabled = true;
        }

        private void Place(Queue<Decal> pool, int max, bool hole, Vector3 point, Vector3 normal, float size, float life, Color color)
        {
            Material mat = hole ? HoleMaterial : ScorchMaterial;
            if (mat == null) return;
            Decal d;
            if (pool.Count >= max) d = pool.Dequeue();
            else
            {
                GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = hole ? "BulletHole" : "Scorch";
                Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(transform, false);
                var r = q.GetComponent<Renderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                d = new Decal { Transform = q.transform, Renderer = r, Block = new MaterialPropertyBlock() };
                _live.Add(d);
            }
            if (normal.sqrMagnitude < 0.01f) normal = Vector3.up;
            // A Quad faces -Z, so look along -normal to face away from the surface.
            d.Transform.SetPositionAndRotation(point + normal * (hole ? 0.006f : 0.02f),
                Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
            d.Transform.localScale = new Vector3(size, size, 1f);
            d.Born = Time.time;
            d.Life = life;
            d.Color = color;
            d.Block.SetColor(ColorId, color);
            d.Renderer.SetPropertyBlock(d.Block);
            d.Renderer.enabled = true;
            pool.Enqueue(d);
        }

        private Material HoleMaterial => _holeMaterial != null ? _holeMaterial : (_holeMaterial = FxParticles.CreateUnlit(FxParticles.SoftTexture, Color.white));

        private Material ScorchMaterial
        {
            get
            {
                if (_scorchMaterial != null) return _scorchMaterial;
                const int n = 64;
                _scorchTexture = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "Scorch", wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float ang = Mathf.Atan2(dy, dx);
                    float edge = 0.75f + 0.18f * Mathf.Sin(ang * 7f) + 0.08f * Mathf.Sin(ang * 13f + 1.3f);
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / edge;
                    float noise = Mathf.PerlinNoise(x * 0.21f, y * 0.21f);
                    float a = Mathf.Clamp01(1f - r) * (0.6f + 0.4f * noise);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a * 1.6f) * 255f));
                }
                _scorchTexture.SetPixels32(px);
                _scorchTexture.Apply(false, true);
                _scorchMaterial = FxParticles.CreateUnlit(_scorchTexture, Color.white);
                return _scorchMaterial;
            }
        }

        private void Update()
        {
            float now = Time.time;
            foreach (Decal d in _live)
            {
                if (!d.Renderer.enabled) continue;
                float left = d.Born + d.Life - now;
                if (left <= 0f) { d.Renderer.enabled = false; continue; }
                if (left < FadeSeconds)
                {
                    Color c = d.Color;
                    c.a *= left / FadeSeconds;
                    d.Block.SetColor(ColorId, c);
                    d.Renderer.SetPropertyBlock(d.Block);
                }
            }
            foreach (Flare f in _flares)
            {
                if (!f.Light.enabled) continue;
                float t = (now - f.Born) / 0.3f;
                if (t >= 1f) f.Light.enabled = false;
                else f.Light.intensity = f.Peak * (1f - t) * (1f - t);
            }
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (_holeMaterial != null) Destroy(_holeMaterial);
            if (_scorchMaterial != null) Destroy(_scorchMaterial);
            if (_scorchTexture != null) Destroy(_scorchTexture);
        }
    }
}
