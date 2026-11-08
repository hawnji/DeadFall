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
    /// NavMesh obstacle. In co-op the host's copy is the real one; clients show a mirror that only looks the part.
    /// Look: an energy barrier (translucent hex field) between two emitter posts. It pulses when hit, shifts from
    /// blue to red as it weakens, flickers near the end of its life or health, and bursts into sparks when it breaks.
    /// </summary>
    public sealed class ShieldWall : MonoBehaviour, IAggroTarget, IDamageable, IAbilitySpawn, IAbilityHealth
    {
        private const int IgnoreRaycastLayer = 2;
        private static readonly Color Full = new Color(0.35f, 0.7f, 1f);
        private static readonly Color Low = new Color(1f, 0.3f, 0.25f);
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static Texture2D _hexTexture;
        private const float FieldAlpha = 0.32f;
        private const float GrowSeconds = 0.25f;
        /// <summary>Hex cell height in metres.</summary>
        private const float HexCell = 0.32f;

        private Renderer _field;
        private Material _fieldMaterial, _edgeMaterial, _postMaterial;
        private Transform _fieldRoot;
        private float _hitFlash;
        private double _spawnedAt;
        private float _width;

        private float _health, _maxHealth;
        private float _aggroRadius;
        private double _expiresAt;
        private Material _material;
        private bool _mirror;

        public GameObject Owner { get; private set; }
        public ulong OwnerClientId { get; private set; }
        public ushort NetId { get; set; }
        public float Health01 => _maxHealth > 0f ? Mathf.Clamp01(_health / _maxHealth) : 0f;
        public event Action<ShieldWall> Broken;

        public Transform TargetTransform => transform;
        public bool IsTargetable => !_mirror && _health > 0f && isActiveAndEnabled;
        public IDamageable Damageable => this;
        public float AggroRadius => _aggroRadius;
        public bool IsAlive => _health > 0f;
        public float DamageReduction => 0f;

        public static ShieldWall Create(Vector3 groundPosition, Quaternion rotation, float health, float aggroRadius, float width,
            float lifetime, GameObject owner, ulong ownerClientId, bool mirror)
        {
            var root = new GameObject(mirror ? "TankShield (mirror)" : "TankShield");
            root.transform.SetPositionAndRotation(groundPosition, rotation);
            root.layer = IgnoreRaycastLayer;

            GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = "Slab";
            slab.layer = IgnoreRaycastLayer;
            slab.transform.SetParent(root.transform, false);
            slab.transform.localPosition = new Vector3(0f, 1f, 0f);
            slab.transform.localScale = new Vector3(width, 2f, 0.25f);

            if (!mirror)
            {
                var obstacle = root.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = new Vector3(0f, 1f, 0f);
                obstacle.size = new Vector3(width, 2f, 0.4f);
                obstacle.carving = false;
            }

            var wall = root.AddComponent<ShieldWall>();
            wall._mirror = mirror;
            wall._maxHealth = wall._health = Mathf.Max(1f, health);
            wall._aggroRadius = Mathf.Max(0f, aggroRadius);
            wall._expiresAt = Time.timeAsDouble + Mathf.Max(0.1f, lifetime);
            wall.Owner = owner;
            wall.OwnerClientId = ownerClientId;
            wall._width = width;
            wall._spawnedAt = Time.timeAsDouble;
            wall.BuildLook(slab, width);
            if (!mirror) CombatTargets.Register(wall);
            return wall;
        }

        /// <summary>Emitter posts, edge bars and the hex field. The slab keeps its collider but is not drawn.</summary>
        private void BuildLook(GameObject slab, float width)
        {
            Renderer slabRenderer = slab.GetComponent<Renderer>();
            _fieldMaterial = CreateFieldMaterial();
            if (_fieldMaterial == null)
            {
                // No Sprites/Default in this build: fall back to the plain slab.
                _material = RuntimeMaterials.CreateLit(Full);
                slabRenderer.sharedMaterial = _material;
                return;
            }
            slabRenderer.enabled = false;
            _postMaterial = RuntimeMaterials.CreateLit(new Color(0.18f, 0.2f, 0.24f));
            _edgeMaterial = RuntimeMaterials.CreateEmissive(Full, 2.2f);

            _fieldRoot = new GameObject("Field").transform;
            _fieldRoot.SetParent(transform, false);
            _fieldRoot.localPosition = new Vector3(0f, 1f, 0f);
            for (int side = 0; side < 2; side++)
            {
                // Two quads back to back so the field reads from both sides whatever the shader's culling.
                GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Hex";
                q.layer = IgnoreRaycastLayer;
                Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(_fieldRoot, false);
                q.transform.localRotation = Quaternion.Euler(0f, side * 180f, 0f);
                q.transform.localScale = new Vector3(width, 1.9f, 1f);
                var r = q.GetComponent<Renderer>();
                r.sharedMaterial = _fieldMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (side == 0) _field = r;
            }

            for (int s = -1; s <= 1; s += 2)
            {
                Part("Post", new Vector3(s * (width * 0.5f + 0.08f), 1f, 0f), new Vector3(0.16f, 2.1f, 0.3f), _postMaterial);
                Part("Foot", new Vector3(s * (width * 0.5f + 0.08f), 0.06f, 0f), new Vector3(0.36f, 0.12f, 0.5f), _postMaterial);
                Part("Emitter", new Vector3(s * (width * 0.5f + 0.01f), 1f, 0f), new Vector3(0.05f, 1.8f, 0.12f), _edgeMaterial);
                Part("Cap", new Vector3(s * (width * 0.5f + 0.08f), 2.1f, 0f), new Vector3(0.22f, 0.08f, 0.22f), _edgeMaterial);
            }
            Part("EdgeLow", new Vector3(0f, 0.08f, 0f), new Vector3(width, 0.03f, 0.04f), _edgeMaterial);
            Part("EdgeHigh", new Vector3(0f, 1.95f, 0f), new Vector3(width, 0.03f, 0.04f), _edgeMaterial);
            ApplyLook(0f);
        }

        private void Part(string name, Vector3 local, Vector3 size, Material m)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = IgnoreRaycastLayer;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localPosition = local;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = m;
        }

        private static Material CreateFieldMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return null;
            var m = new Material(shader) { name = "ShieldField", mainTexture = HexTexture, color = Full };
            return m;
        }

        /// <summary>Hex grid lines on a faint fill. One tile is sqrt(3) x 1 hex units (apothem 0.5), so it repeats cleanly.</summary>
        private static Texture2D HexTexture
        {
            get
            {
                if (_hexTexture != null) return _hexTexture;
                const int w = 110, h = 64;
                const float sq3 = 1.7320508f;
                _hexTexture = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "ShieldHex", wrapMode = TextureWrapMode.Repeat };
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w * sq3, v = (y + 0.5f) / h;
                    float d = Mathf.Min(HexDistance(u, v, 0f, 0f), HexDistance(u, v, sq3 * 0.5f, 0.5f));
                    float line = Mathf.Clamp01(1f - (0.5f - d) / 0.04f);
                    float a = 0.18f + 0.82f * line;
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
                _hexTexture.SetPixels32(px);
                _hexTexture.Apply(true, true);
                return _hexTexture;
            }
        }

        /// <summary>Hex distance (0 at a centre, 0.5 on an edge) to the nearest centre of a lattice offset by (ox, oy).</summary>
        private static float HexDistance(float u, float v, float ox, float oy)
        {
            const float sq3 = 1.7320508f;
            float cx = Mathf.Round((u - ox) / sq3) * sq3 + ox;
            float cy = Mathf.Round(v - oy) + oy;
            float dx = Mathf.Abs(u - cx), dy = Mathf.Abs(v - cy);
            return Mathf.Max(dx * 0.8660254f + dy * 0.5f, dy);
        }

        private void ApplyLook(float dt)
        {
            if (_fieldMaterial == null) return;
            double now = Time.timeAsDouble;
            float grow = Mathf.Clamp01((float)(now - _spawnedAt) / GrowSeconds);
            if (_fieldRoot != null) _fieldRoot.localScale = new Vector3(1f, Mathf.SmoothStep(0.02f, 1f, grow), 1f);
            _hitFlash = Mathf.Max(0f, _hitFlash - dt * 3.5f);
            Color c = Color.Lerp(Low, Full, Health01);
            float left = (float)(_expiresAt - now);
            bool flicker = Health01 < 0.3f || left < 3f;
            float pulse = 0.85f + 0.15f * Mathf.Sin((float)now * 4f);
            float alpha = FieldAlpha * pulse + _hitFlash * 0.45f;
            if (flicker && Mathf.PerlinNoise((float)now * 14f, 0.3f) < 0.35f) alpha *= 0.25f;
            c.a = Mathf.Clamp01(alpha);
            _fieldMaterial.color = c;
            _fieldMaterial.mainTextureScale = new Vector2(_width / (HexCell * 1.7320508f), 1.9f / HexCell);
            _fieldMaterial.mainTextureOffset = new Vector2(0f, (float)(now * 0.12 % 1.0));
            if (_edgeMaterial != null)
            {
                Color e = Color.Lerp(Low, Full, Health01) * (1f + _hitFlash);
                RuntimeMaterials.SetColor(_edgeMaterial, e);
            }
        }

        private void Update()
        {
            if (!_mirror && Time.timeAsDouble >= _expiresAt) { Destroy(gameObject); return; }
            ApplyLook(Time.deltaTime);
        }

        private void OnDisable() => CombatTargets.Unregister(this);

        private void OnDestroy()
        {
            CombatTargets.Unregister(this);
            if (_material != null) Destroy(_material);
            if (_fieldMaterial != null) Destroy(_fieldMaterial);
            if (_edgeMaterial != null) Destroy(_edgeMaterial);
            if (_postMaterial != null) Destroy(_postMaterial);
            ClassNetwork.NotifyGone(this);
        }

        /// <summary>Sparks along the field when it breaks.</summary>
        private void Burst()
        {
            for (int i = 0; i < 30; i++)
            {
                Vector3 p = transform.TransformPoint(new Vector3(UnityEngine.Random.Range(-0.5f, 0.5f) * _width, UnityEngine.Random.Range(0.1f, 1.9f), 0f));
                FxParticles.Emit(FxParticles.Kind.Spark, p, UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(2f, 6f), 0.05f, UnityEngine.Random.Range(0.3f, 0.7f), Full);
            }
        }

        /// <summary>Clients: the host's health, with the same hit flash and tint a real hit gives.</summary>
        public void SetMirrorHealth01(float value)
        {
            if (!_mirror) return;
            float h = Mathf.Clamp01(value) * _maxHealth;
            if (h < _health - 0.5f)
            {
                _hitFlash = 1f;
                for (int i = 0; i < 3; i++)
                    FxParticles.Emit(FxParticles.Kind.Spark, transform.TransformPoint(new Vector3(UnityEngine.Random.Range(-0.4f, 0.4f) * _width, UnityEngine.Random.Range(0.3f, 1.6f), 0f)),
                        UnityEngine.Random.onUnitSphere * 3f, 0.04f, 0.25f, Color.Lerp(Low, Full, Mathf.Clamp01(value)));
            }
            _health = Mathf.Max(0.01f, h);
            if (_material != null) RuntimeMaterials.SetColor(_material, Color.Lerp(Low, Full, Health01));
        }

        public float ApplyDamage(in DamageInfo info)
        {
            if (_mirror || _health <= 0f || info.Amount <= 0f) return 0f;
            float applied = Mathf.Min(_health, info.Amount);
            _health -= applied;
            _hitFlash = 1f;
            if (_material != null) RuntimeMaterials.SetColor(_material, Color.Lerp(Low, Full, Health01));
            if (info.Point != Vector3.zero)
                for (int i = 0; i < 3; i++)
                    FxParticles.Emit(FxParticles.Kind.Spark, info.Point, UnityEngine.Random.onUnitSphere * 3f, 0.04f, 0.25f, Color.Lerp(Low, Full, Health01));
            if (_health <= 0f)
            {
                Burst();
                CombatTargets.Unregister(this);
                Broken?.Invoke(this);
                Destroy(gameObject);
            }
            return applied;
        }
    }
}
