using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Rules.Combat;
using Deadfall.Rules.Common;
using UnityEngine;

namespace Deadfall.Player
{
    /// <summary>
    /// Placeholder first-person weapon built from primitives. Gives readable feedback for aiming,
    /// recoil, reload, swap, melee, and sprint until real models and animations arrive.
    /// </summary>
    public sealed class WeaponViewmodel : MonoBehaviour
    {
        [SerializeField] private Vector3 _hipPosition = new Vector3(0.22f, -0.2f, 0.42f);
        [SerializeField] private Vector3 _aimPosition = new Vector3(0f, -0.12f, 0.32f);
        [SerializeField] private float _kickBack = 0.05f;
        [SerializeField] private float _kickPitch = 4f;
        [SerializeField] private float _kickRecoverySharpness = 18f;
        [SerializeField] private float _swaySharpness = 12f;

        private Transform _model;
        private Transform _muzzle;
        private Renderer[] _renderers;
        private float _kick;
        private Vector3 _currentPos;
        private Quaternion _currentRot = Quaternion.identity;

        private float _aim, _equip = 1f, _reload, _melee = 1f, _move;
        private bool _meleeing, _sprinting;

        public Vector3 MuzzlePosition => _muzzle != null ? _muzzle.position : transform.position;

        private void Awake()
        {
            _currentPos = _hipPosition;
        }

        public void Show(WeaponDefinition def)
        {
            if (_model != null) Destroy(_model.gameObject);
            _model = new GameObject("Model").transform;
            _model.SetParent(transform, false);

            bool melee = def.Kind == WeaponKind.Melee;
            Vector3 muzzleAt;
            if (TryShowModel(def, melee, out muzzleAt))
            {
                _muzzle = new GameObject("Muzzle").transform;
                _muzzle.SetParent(_model, false);
                _muzzle.localPosition = muzzleAt;
                _renderers = _model.GetComponentsInChildren<Renderer>();
                return;
            }
            Vector3 bodySize;
            Vector3 barrelSize;
            switch (def.Category)
            {
                case WeaponCategory.Pistol: bodySize = new Vector3(0.05f, 0.1f, 0.18f); barrelSize = new Vector3(0.03f, 0.03f, 0.06f); break;
                case WeaponCategory.Shotgun: bodySize = new Vector3(0.07f, 0.09f, 0.55f); barrelSize = new Vector3(0.045f, 0.045f, 0.25f); break;
                case WeaponCategory.LightMachineGun: bodySize = new Vector3(0.1f, 0.13f, 0.6f); barrelSize = new Vector3(0.04f, 0.04f, 0.3f); break;
                case WeaponCategory.SubmachineGun: bodySize = new Vector3(0.06f, 0.1f, 0.32f); barrelSize = new Vector3(0.03f, 0.03f, 0.1f); break;
                case WeaponCategory.Melee: bodySize = new Vector3(0.03f, 0.03f, 0.25f); barrelSize = new Vector3(0.015f, 0.08f, 0.55f); break;
                default: bodySize = new Vector3(0.07f, 0.11f, 0.45f); barrelSize = new Vector3(0.035f, 0.035f, 0.2f); break;
            }

            CreatePart("Body", bodySize, new Vector3(0f, 0f, bodySize.z * 0.5f), def.PlaceholderColor);
            Color barrelColor = melee ? new Color(0.75f, 0.78f, 0.8f) : def.PlaceholderColor * 0.6f;
            CreatePart(melee ? "Blade" : "Barrel", barrelSize, new Vector3(0f, melee ? 0f : 0.02f, bodySize.z + barrelSize.z * 0.5f), barrelColor);

            _muzzle = new GameObject("Muzzle").transform;
            _muzzle.SetParent(_model, false);
            _muzzle.localPosition = new Vector3(0f, melee ? 0f : 0.02f, bodySize.z + barrelSize.z);
            _renderers = _model.GetComponentsInChildren<Renderer>();
        }

        /// <summary>
        /// The weapon's Blender model (T-58), named after its content id ("weapon.halberd_ar" -> "weapon_halberd_ar").
        /// Models put their muzzle tip where the placeholder's was, so the muzzle point is the same.
        /// </summary>
        private bool TryShowModel(WeaponDefinition def, bool melee, out Vector3 muzzle)
        {
            muzzle = Vector3.zero;
            GameObject model = ModelLibrary.Spawn(ModelId(def), _model, slot => ModelLibrary.SlotFromAccent(slot, def.PlaceholderColor));
            if (model == null) return false;
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = gameObject.layer;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            muzzle = new Vector3(0f, melee ? 0f : 0.02f, MuzzleDistance(def.Category));
            return true;
        }

        /// <summary>Model id for a weapon: its content id with the dot replaced.</summary>
        public static string ModelId(WeaponDefinition def) => def != null && def.ContentId != null ? def.ContentId.Replace('.', '_') : null;

        /// <summary>Placeholder body + barrel length per category (the models' muzzle tips match these).</summary>
        private static float MuzzleDistance(WeaponCategory c) => c switch
        {
            WeaponCategory.Pistol => 0.24f,
            WeaponCategory.Shotgun => 0.8f,
            WeaponCategory.LightMachineGun => 0.9f,
            WeaponCategory.SubmachineGun => 0.42f,
            WeaponCategory.Melee => 0.8f,
            _ => 0.65f,
        };

        private void CreatePart(string partName, Vector3 size, Vector3 localPos, Color color)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = partName;
            Object.Destroy(part.GetComponent<Collider>());
            part.layer = gameObject.layer;
            part.transform.SetParent(_model, false);
            part.transform.localScale = size;
            part.transform.localPosition = localPos;
            var r = part.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sharedMaterial = RuntimeMaterials.CreateLit(color);
        }

        public void Kick(float multiplier) => _kick = Mathf.Min(1.5f, _kick + multiplier);

        public void SetState(float aim, float equip, float reload, float melee, bool meleeing, bool sprinting, float moveFactor)
        {
            _aim = aim;
            _equip = equip;
            _reload = reload;
            _melee = melee;
            _meleeing = meleeing;
            _sprinting = sprinting;
            _move = moveFactor;
        }

        private void LateUpdate()
        {
            if (_model == null) return;
            float dt = Time.deltaTime;
            _kick = Mathf.Lerp(_kick, 0f, MotionMath.ExpBlend(_kickRecoverySharpness, dt));

            Vector3 pos = Vector3.Lerp(_hipPosition, _aimPosition, _aim);
            Vector3 euler = Vector3.zero;

            // Equip: rise from below.
            float lowered = 1f - Mathf.SmoothStep(0f, 1f, _equip);
            pos += new Vector3(0f, -0.25f * lowered, 0f);
            euler.x += 30f * lowered;

            // Reload: dip and roll in the middle of the reload.
            float r = Mathf.Sin(Mathf.Clamp01(_reload) * Mathf.PI);
            pos += new Vector3(0f, -0.12f * r, 0f);
            euler += new Vector3(15f * r, 0f, 35f * r);

            // Sprint: lower and tilt.
            if (_sprinting)
            {
                pos += new Vector3(-0.05f, -0.06f, -0.04f);
                euler += new Vector3(10f, -25f, 10f);
            }

            // Melee: quick horizontal swipe.
            if (_meleeing)
            {
                float m = Mathf.Sin(Mathf.Clamp01(_melee) * Mathf.PI);
                pos += new Vector3(-0.15f * m, 0.05f * m, 0.12f * m);
                euler += new Vector3(-10f * m, -50f * m, -20f * m);
            }

            // Recoil kick.
            pos += new Vector3(0f, 0f, -_kickBack * _kick * Mathf.Lerp(1f, 0.5f, _aim));
            euler.x -= _kickPitch * _kick * Mathf.Lerp(1f, 0.4f, _aim);

            // Subtle movement sway (reduced while aiming).
            float sway = Mathf.Clamp01(_move) * (1f - _aim) * 0.006f;
            pos += new Vector3(Mathf.Sin(Time.time * 7f) * sway, Mathf.Abs(Mathf.Cos(Time.time * 7f)) * sway, 0f);

            float t = MotionMath.ExpBlend(_swaySharpness, dt);
            _currentPos = Vector3.Lerp(_currentPos, pos, t);
            _currentRot = Quaternion.Slerp(_currentRot, Quaternion.Euler(euler), t);
            // Kick and aim should feel immediate, so bypass smoothing for those components.
            _model.localPosition = _currentPos + new Vector3(0f, 0f, -_kickBack * _kick * 0.5f);
            _model.localRotation = _currentRot;
        }
    }
}
