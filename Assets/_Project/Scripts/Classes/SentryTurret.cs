using Deadfall.Rules.Common;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Classes
{
    /// <summary>
    /// The Engineer's light sentry: turns to the nearest hostile in range with a clear line of fire and shoots it.
    /// Enemies can attack it (it is a combat target, not a taunt). Lasts a fixed time. The host's copy deals the
    /// damage; client copies aim and show tracers only. Scrap upgrades arrive with Engineering (M9).
    /// </summary>
    public sealed class SentryTurret : MonoBehaviour, ICombatTarget, IDamageable, IAbilitySpawn
    {
        public const string WeaponId = "ability.deploy_sentry";
        private const int IgnoreRaycastLayer = 2;
        private const float RetargetInterval = 0.25f;
        private const float TurnSpeed = 540f;
        /// <summary>The deployed sentry is the Engineer's scrap-built sentry (eng_sentry_*) at this scale.</summary>
        private const float ModelScale = 0.72f;
        private const float MuzzleHeight = 1.05f * ModelScale;

        private float _health = 300f, _maxHealth = 300f;
        private float _range, _damage, _shotInterval;
        private double _expiresAt, _nextShot, _nextRetarget;
        private bool _mirror;
        private IHostile _target;
        private Transform _head;
        private Material _bodyMat;

        public GameObject Owner { get; private set; }
        public ulong OwnerClientId { get; private set; }
        public ushort NetId { get; set; }

        public Transform TargetTransform => transform;
        public bool IsTargetable => !_mirror && _health > 0f && isActiveAndEnabled;
        public IDamageable Damageable => this;
        public bool IsAlive => _health > 0f;
        public float DamageReduction => 0f;

        /// <param name="damage">Damage per shot.</param>
        /// <param name="shotsPerSecond">Fire rate.</param>
        public static SentryTurret Create(Vector3 groundPosition, Quaternion rotation, float lifetime, float range, float damage,
            float shotsPerSecond, float health, GameObject owner, ulong ownerClientId, bool mirror)
        {
            var root = new GameObject(mirror ? "Sentry (mirror)" : "Sentry");
            root.layer = IgnoreRaycastLayer;
            root.transform.SetPositionAndRotation(groundPosition, rotation);
            var turret = root.AddComponent<SentryTurret>();
            turret._bodyMat = RuntimeMaterials.CreateLit(new Color(0.4f, 0.8f, 0.5f));
            Material dark = RuntimeMaterials.CreateLit(new Color(0.18f, 0.2f, 0.2f));

            Part(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.3f, 0f), new Vector3(0.6f, 0.3f, 0.6f), dark);
            turret._head = new GameObject("Head").transform;
            turret._head.SetParent(root.transform, false);
            turret._head.localPosition = new Vector3(0f, MuzzleHeight, 0f);
            Part(PrimitiveType.Cube, turret._head, Vector3.zero, new Vector3(0.45f, 0.3f, 0.5f), turret._bodyMat);
            Part(PrimitiveType.Cube, turret._head, new Vector3(0f, 0f, 0.45f), new Vector3(0.1f, 0.1f, 0.5f), dark);
            // A smaller copy of the scrap-built sentry over the placeholder parts; its accent panels take the health tint.
            Transform baseModel = turret.Dress("eng_sentry_base", root.transform);
            Transform headModel = turret.Dress("eng_sentry_head", turret._head);
            if (baseModel != null && headModel != null)
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                    if (!r.transform.IsChildOf(baseModel) && !r.transform.IsChildOf(headModel)) r.enabled = false;

            turret._mirror = mirror;
            turret._range = range;
            turret._damage = damage;
            turret._shotInterval = 1f / Mathf.Max(0.5f, shotsPerSecond);
            turret._maxHealth = turret._health = Mathf.Max(1f, health);
            turret._expiresAt = Time.timeAsDouble + Mathf.Max(0.1f, lifetime);
            turret.Owner = owner;
            turret.OwnerClientId = ownerClientId;
            if (!mirror) CombatTargets.Register(turret);
            return turret;
        }

        private Transform Dress(string id, Transform parent)
        {
            GameObject model = ModelLibrary.Spawn(id, parent);
            if (model == null) return null;
            model.transform.localScale = Vector3.one * ModelScale;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                r.gameObject.layer = IgnoreRaycastLayer;
                Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && mats[i].name == "Model Accent") { mats[i] = _bodyMat; changed = true; }
                if (changed) r.sharedMaterials = mats;
            }
            return model.transform;
        }

        private static void Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.layer = IgnoreRaycastLayer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
        }

        private void Update()
        {
            double now = Time.timeAsDouble;
            if (!_mirror && now >= _expiresAt)
            {
                Destroy(gameObject);
                return;
            }

            if (now >= _nextRetarget || _target == null || !_target.IsAlive)
            {
                _nextRetarget = now + RetargetInterval;
                _target = FindTarget();
            }
            if (_target == null) return;

            Vector3 muzzle = _head.position;
            Vector3 to = _target.AimPoint - muzzle;
            if (to.sqrMagnitude > 0.01f)
                _head.rotation = Quaternion.RotateTowards(_head.rotation, Quaternion.LookRotation(to), TurnSpeed * Time.deltaTime);

            if (now < _nextShot || Vector3.Angle(_head.forward, to) > 8f) return;
            _nextShot = now + _shotInterval;
            Vector3 end = _target.AimPoint;
            TracerPool.Instance.Spawn(muzzle + _head.forward * 0.7f, end);
            GameAudio.Play(SfxId.ShotTurret, muzzle);
            if (_mirror) return;

            LocalHitResolver.Apply(new HitClaim
            {
                Attacker = Owner != null ? Owner : gameObject,
                Target = _target.Damageable,
                Zone = HitZone.Body,
                WeakPointMultiplier = 1f,
                Origin = muzzle,
                Point = end,
                WeaponId = WeaponId,
                Request = new DamageRequest(_damage, HitZone.Body, 0f, 1f, 1f, 1f, _range, _range + 2f, 1f),
            });
        }

        private IHostile FindTarget()
        {
            IHostile best = null;
            float bestSq = _range * _range;
            Vector3 muzzle = _head.position;
            var all = Hostiles.All;
            for (int i = 0; i < all.Count; i++)
            {
                IHostile h = all[i];
                if (h == null || !h.IsAlive) continue;
                Vector3 to = h.AimPoint - muzzle;
                float sq = to.sqrMagnitude;
                if (sq >= bestSq) continue;
                // Line of fire: anything solid in between (other than enemies themselves) blocks.
                if (CombatQueries.Raycast(muzzle, to.normalized, Mathf.Sqrt(sq), transform, out RaycastHit hit)
                    && (hit.distance < Mathf.Sqrt(sq) - 0.8f) && !Hurtbox.TryResolve(hit.collider, out _, out _, out _)) continue;
                bestSq = sq;
                best = h;
            }
            return best;
        }

        public float ApplyDamage(in DamageInfo info)
        {
            if (_mirror || _health <= 0f || info.Amount <= 0f) return 0f;
            float applied = Mathf.Min(_health, info.Amount);
            _health -= applied;
            if (_bodyMat != null) RuntimeMaterials.SetColor(_bodyMat, Color.Lerp(new Color(0.55f, 0.2f, 0.2f), new Color(0.4f, 0.8f, 0.5f), _health / _maxHealth));
            if (_health <= 0f) Destroy(gameObject);
            return applied;
        }

        private void OnDisable() => CombatTargets.Unregister(this);

        private void OnDestroy()
        {
            CombatTargets.Unregister(this);
            ClassNetwork.NotifyGone(this);
        }
    }
}
