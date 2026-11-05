using UnityEngine;

namespace Deadfall.Combat
{
    /// <summary>
    /// Shared particle effects built in code: one emit-only <see cref="ParticleSystem"/> per kind (sparks, dust, fire,
    /// smoke, blood, debris), so hundreds of impacts cost a handful of draw calls. Uses Sprites/Default (always
    /// included in builds) with a soft round texture made at startup.
    /// </summary>
    public static class FxParticles
    {
        public enum Kind { Spark, Dust, Fire, Smoke, Blood, Debris }

        private static ParticleSystem[] _systems;
        private static Material _material;
        private static Texture2D _soft;

        /// <summary>Off: no particles (debug comparison or very low-end machines).</summary>
        public static bool Enabled = true;

        public static void Emit(Kind kind, Vector3 position, Vector3 velocity, float size, float lifetime, Color color)
        {
            if (!Enabled) return;
            ParticleSystem ps = SystemFor(kind);
            if (ps == null) return;
            var p = new ParticleSystem.EmitParams
            {
                position = position,
                velocity = velocity,
                startSize = Mathf.Max(0.005f, size),
                startLifetime = Mathf.Max(0.02f, lifetime),
                startColor = color,
                rotation = Random.Range(0f, 360f),
                applyShapeToPosition = false,
            };
            ps.Emit(p, 1);
        }

        /// <summary>A bullet hitting a surface. <paramref name="flesh"/>: something alive (blood instead of sparks and no hole).</summary>
        public static void Impact(Vector3 point, Vector3 normal, bool flesh)
        {
            if (!Enabled) return;
            if (normal.sqrMagnitude < 0.01f) normal = Vector3.up;
            normal.Normalize();
            if (flesh)
            {
                for (int i = 0; i < 6; i++)
                    Emit(Kind.Blood, point, (normal + Random.insideUnitSphere * 0.8f) * Random.Range(1.5f, 4f), Random.Range(0.05f, 0.11f),
                        Random.Range(0.3f, 0.55f), new Color(0.35f, 0.04f, 0.03f, 0.95f));
                Emit(Kind.Dust, point, normal * 0.4f, 0.25f, 0.45f, new Color(0.3f, 0.05f, 0.04f, 0.5f));
                return;
            }
            for (int i = 0; i < 7; i++)
                Emit(Kind.Spark, point, (normal + Random.insideUnitSphere * 0.9f).normalized * Random.Range(3f, 9f), Random.Range(0.02f, 0.04f),
                    Random.Range(0.12f, 0.3f), new Color(1f, Random.Range(0.65f, 0.9f), 0.35f, 1f));
            for (int i = 0; i < 2; i++)
                Emit(Kind.Dust, point + normal * 0.05f, (normal + Random.insideUnitSphere * 0.4f) * Random.Range(0.4f, 1f), Random.Range(0.18f, 0.3f),
                    Random.Range(0.5f, 0.9f), new Color(0.55f, 0.52f, 0.48f, 0.45f));
            ImpactDecals.BulletHole(point, normal);
        }

        /// <summary>
        /// Impact for a shot traced from <paramref name="from"/> to <paramref name="to"/> when the caller has no hit
        /// info (tracers from other players, sentries, drones): a short ray at the end finds the surface, if any.
        /// </summary>
        public static void ImpactAt(Vector3 from, Vector3 to)
        {
            if (!Enabled) return;
            Vector3 dir = to - from;
            if (dir.sqrMagnitude < 0.01f) return;
            dir.Normalize();
            if (!Physics.Raycast(to - dir * 0.25f, dir, out RaycastHit hit, 0.5f, ~(1 << 2), QueryTriggerInteraction.Collide)) return;
            Impact(hit.point, hit.normal, hit.collider.GetComponentInParent<Hurtbox>() != null || hit.collider.GetComponent<Hurtbox>() != null);
        }

        /// <summary>Fireball, smoke, sparks, debris, a light flash and a scorch mark.</summary>
        public static void Explosion(Vector3 position, float radius)
        {
            if (!Enabled) return;
            radius = Mathf.Clamp(radius, 0.5f, 12f);
            int fire = Mathf.RoundToInt(Mathf.Lerp(10f, 28f, radius / 8f));
            for (int i = 0; i < fire; i++)
            {
                Vector3 v = Random.insideUnitSphere;
                v.y = Mathf.Abs(v.y) * 0.8f + 0.2f;
                Emit(Kind.Fire, position + v * radius * 0.15f, v * radius * Random.Range(1.6f, 3f), radius * Random.Range(0.45f, 0.8f),
                    Random.Range(0.3f, 0.55f), new Color(1f, Random.Range(0.6f, 0.85f), 0.35f, 1f));
            }
            for (int i = 0; i < fire / 2 + 4; i++)
            {
                Vector3 v = Random.insideUnitSphere * 0.6f + Vector3.up * Random.Range(0.4f, 1.2f);
                Emit(Kind.Smoke, position + Random.insideUnitSphere * radius * 0.3f, v * radius * 0.35f, radius * Random.Range(0.5f, 0.9f),
                    Random.Range(1.6f, 2.8f), new Color(0.16f, 0.15f, 0.14f, 0.7f));
            }
            for (int i = 0; i < fire + 6; i++)
                Emit(Kind.Spark, position, Random.onUnitSphere * Random.Range(6f, 16f) + Vector3.up * 3f, Random.Range(0.03f, 0.06f),
                    Random.Range(0.35f, 0.9f), new Color(1f, Random.Range(0.55f, 0.85f), 0.3f, 1f));
            for (int i = 0; i < 8; i++)
                Emit(Kind.Debris, position, (Random.onUnitSphere + Vector3.up * 1.2f) * Random.Range(3f, 7f), Random.Range(0.05f, 0.12f),
                    Random.Range(0.8f, 1.4f), new Color(0.12f, 0.11f, 0.1f, 1f));
            ImpactDecals.Flash(position, radius);
            if (Physics.Raycast(position + Vector3.up * 0.3f, Vector3.down, out RaycastHit hit, 2.5f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                ImpactDecals.Scorch(hit.point, hit.normal, radius);
        }

        /// <summary>A soft white disc, for particles and decals.</summary>
        public static Texture2D SoftTexture
        {
            get
            {
                if (_soft != null) return _soft;
                const int n = 32;
                _soft = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "FxSoft", wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
                }
                _soft.SetPixels32(px);
                _soft.Apply(false, true);
                return _soft;
            }
        }

        /// <summary>Unlit alpha-blended material (Sprites/Default) with the given texture, or null if missing.</summary>
        public static Material CreateUnlit(Texture2D texture, Color color)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return null;
            var m = new Material(shader) { mainTexture = texture, color = color };
            return m;
        }

        private static ParticleSystem SystemFor(Kind kind)
        {
            if (_systems == null) _systems = new ParticleSystem[6];
            ParticleSystem ps = _systems[(int)kind];
            if (ps != null) return ps;
            if (_material == null) _material = CreateUnlit(SoftTexture, Color.white);
            if (_material == null) return null;
            var go = new GameObject("[Fx " + kind + "]");
            Object.DontDestroyOnLoad(go);
            ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = kind == Kind.Spark ? 800 : 500;
            main.gravityModifier = kind == Kind.Spark ? 1.4f : kind == Kind.Debris ? 1.8f : kind == Kind.Blood ? 1.2f
                : kind == Kind.Smoke ? -0.04f : kind == Kind.Fire ? -0.08f : 0f;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;

            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            if (kind == Kind.Fire)
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.55f, 0.2f), 0.35f), new GradientColorKey(new Color(0.25f, 0.1f, 0.06f), 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.4f), new GradientAlphaKey(0f, 1f) });
            else if (kind == Kind.Smoke || kind == Kind.Dust)
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0f, 1f) });
            else
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);

            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            float s0 = 1f, s1 = 1f;
            switch (kind)
            {
                case Kind.Spark: s1 = 0.3f; break;
                case Kind.Dust: s0 = 0.6f; s1 = 2.2f; break;
                case Kind.Smoke: s0 = 0.5f; s1 = 2.6f; break;
                case Kind.Fire: s0 = 0.5f; s1 = 1.5f; break;
                case Kind.Blood: s1 = 0.6f; break;
            }
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, s0, 1f, s1));

            if (kind == Kind.Smoke || kind == Kind.Dust || kind == Kind.Fire)
            {
                ParticleSystem.LimitVelocityOverLifetimeModule drag = ps.limitVelocityOverLifetime;
                drag.enabled = true;
                drag.limit = 0f;
                drag.dampen = kind == Kind.Fire ? 0.12f : 0.06f;
            }
            if (kind == Kind.Debris || kind == Kind.Spark)
            {
                ParticleSystem.CollisionModule c = ps.collision;
                c.enabled = true;
                c.type = ParticleSystemCollisionType.World;
                c.mode = ParticleSystemCollisionMode.Collision3D;
                c.quality = ParticleSystemCollisionQuality.Low;
                c.bounce = 0.3f;
                c.dampen = 0.4f;
                c.lifetimeLoss = kind == Kind.Spark ? 0.3f : 0f;
                c.collidesWith = ~(1 << 2);
            }

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = _material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (kind == Kind.Spark)
            {
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.035f;
                r.lengthScale = 1.5f;
            }
            else
            {
                r.renderMode = ParticleSystemRenderMode.Billboard;
            }
            r.sortingFudge = kind == Kind.Smoke ? 10f : 0f;

            ps.Play();
            _systems[(int)kind] = ps;
            return ps;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _systems = null;
            _material = null;
            _soft = null;
            Enabled = true;
        }
    }
}
