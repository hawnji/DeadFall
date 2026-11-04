using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Rules.Combat;
using Deadfall.Rules.Common;
using UnityEngine;

namespace Deadfall.Player
{
    /// <summary>
    /// First-person weapon: the Blender model (or a primitive placeholder) posed for aiming, recoil, reload, swap,
    /// melee and sprint, held by two gloved arms (<c>fp_arm</c>, the left one mirrored). Each hand sits on the
    /// weapon's grip or support point, so it follows every motion, and the forearm pivots about a fixed shoulder.
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

        private const float ArmReach = 0.55f;
        private static readonly Vector3 BackR = new Vector3(0.25f, -0.6f, -0.75f).normalized;
        private static readonly Vector3 BackL = new Vector3(-0.45f, -0.55f, -0.7f).normalized;
        private Transform _armR, _armL;
        private Vector3 _shoulderR, _shoulderL;
        private float _rollR, _rollL;

        /// <summary>Where the hands go on each kind of weapon (model space) and how the hand rolls around the forearm.</summary>
        private struct Hold
        {
            public Vector3 Grip, Support;
            public float RollR, RollL;
            public Hold(Vector3 grip, float rollR, Vector3 support, float rollL) { Grip = grip; RollR = rollR; Support = support; RollL = rollL; }
        }

        private static Hold HoldFor(WeaponDefinition def)
        {
            if (def.Kind == WeaponKind.Melee) return new Hold(new Vector3(0f, 0f, 0.03f), -80f, new Vector3(0f, 0f, 0.15f), -80f);
            return def.Category switch
            {
                WeaponCategory.Pistol => new Hold(new Vector3(0f, -0.07f, 0.01f), 0f, new Vector3(-0.03f, -0.085f, 0.02f), -20f),
                WeaponCategory.SubmachineGun => new Hold(new Vector3(0f, -0.085f, 0.06f), 0f, new Vector3(0f, -0.08f, 0.3f), -10f),
                WeaponCategory.Shotgun => new Hold(new Vector3(0f, -0.085f, 0.04f), 0f, new Vector3(0f, -0.04f, 0.5f), -80f),
                WeaponCategory.LightMachineGun => new Hold(new Vector3(0f, -0.09f, 0.08f), 0f, new Vector3(0f, -0.045f, 0.5f), -80f),
                WeaponCategory.Heavy => new Hold(new Vector3(0f, -0.09f, 0.06f), 0f, new Vector3(0f, -0.06f, 0.4f), -80f),
                _ => new Hold(new Vector3(0f, -0.09f, 0.04f), 0f, new Vector3(0f, -0.04f, 0.4f), -80f),
            };
        }
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
                AddArms(def);
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
            AddArms(def);
            _renderers = _model.GetComponentsInChildren<Renderer>();
        }

        /// <summary>Puts a hand on the grip and one on the support point; each shoulder is fixed in camera space.</summary>
        private void AddArms(WeaponDefinition def)
        {
            _armR = _armL = null;
            Hold h = HoldFor(def);
            _armR = SpawnArm("ArmR", h.Grip, false);
            _armL = SpawnArm("ArmL", h.Support, true);
            _rollR = h.RollR;
            _rollL = h.RollL;
            _shoulderR = _hipPosition + h.Grip + BackR * ArmReach;
            _shoulderL = _hipPosition + h.Support + BackL * ArmReach;
        }

        private Transform SpawnArm(string armName, Vector3 at, bool mirrored)
        {
            var anchor = new GameObject(armName).transform;
            anchor.SetParent(_model, false);
            anchor.localPosition = at;
            GameObject arm = ModelLibrary.Spawn("fp_arm", anchor);
            if (arm == null) { Destroy(anchor.gameObject); return null; }
            if (mirrored) arm.transform.localScale = new Vector3(-1f, 1f, 1f);
            foreach (Transform t in anchor.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = gameObject.layer;
            foreach (Renderer r in anchor.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            return anchor;
        }

        /// <summary>Points each forearm from its shoulder at its hand (after the weapon is posed this frame).</summary>
        private void AimArms()
        {
            AimArm(_armR, _shoulderR, _rollR);
            AimArm(_armL, _shoulderL, _rollL);
        }

        private void AimArm(Transform arm, Vector3 shoulderLocal, float roll)
        {
            if (arm == null) return;
            Vector3 shoulder = transform.TransformPoint(shoulderLocal);
            Vector3 dir = arm.position - shoulder;
            if (dir.sqrMagnitude < 1e-6f) return;
            arm.rotation = Quaternion.LookRotation(dir, transform.up) * Quaternion.Euler(0f, 0f, roll);
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
            AimArms();
        }
    }
}
