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
            public bool OneHanded;
            public Hold(Vector3 grip, float rollR, Vector3 support, float rollL, bool oneHanded = false)
            {
                Grip = grip; RollR = rollR; Support = support; RollL = rollL; OneHanded = oneHanded;
            }
        }

        /// <summary>Melee weapons are held blade-up like a real grip, not pointed at the screen like a gun.</summary>
        private enum MeleeStyle { None, OneHand, TwoHand }

        private static MeleeStyle StyleFor(WeaponDefinition def) =>
            def == null || def.Kind != WeaponKind.Melee ? MeleeStyle.None
            : def.ContentId != null && def.ContentId.Contains("maul") ? MeleeStyle.TwoHand : MeleeStyle.OneHand;

        // Rest pose and swing keys, as offsets from the hip position (position, euler). The haft runs along model +Z,
        // so a negative pitch stands the blade up.
        private static readonly Vector3 OneRestPos = new Vector3(0.03f, -0.1f, -0.08f), OneRestRot = new Vector3(-58f, -8f, 12f);
        private static readonly Vector3 OneWindPos = new Vector3(0.1f, 0.06f, -0.12f), OneWindRot = new Vector3(-80f, 25f, 35f);
        private static readonly Vector3 OneHitPos = new Vector3(-0.32f, -0.12f, 0.16f), OneHitRot = new Vector3(25f, -70f, -40f);
        private static readonly Vector3 TwoRestPos = new Vector3(-0.02f, -0.16f, -0.06f), TwoRestRot = new Vector3(-48f, -20f, 30f);
        private static readonly Vector3 TwoWindPos = new Vector3(0.06f, 0.12f, -0.18f), TwoWindRot = new Vector3(-100f, 0f, 15f);
        private static readonly Vector3 TwoHitPos = new Vector3(-0.12f, -0.2f, 0.2f), TwoHitRot = new Vector3(40f, -15f, -10f);
        private MeleeStyle _style;

        private static Hold HoldFor(WeaponDefinition def)
        {
            // Melee: the haft passes through the fist like a pistol grip (the rest pose stands it up).
            if (def.Kind == WeaponKind.Melee)
                return StyleFor(def) == MeleeStyle.TwoHand
                    ? new Hold(new Vector3(0f, 0f, 0.06f), 0f, new Vector3(0f, 0f, 0.43f), 0f)
                    : new Hold(new Vector3(0f, 0f, 0.1f), 0f, Vector3.zero, 0f, oneHanded: true);
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

        private Vector3 _aimLocal;
        private bool _scoped, _hiddenForScope;

        /// <summary>Aimed in with a magnified scope: the model is hidden and the HUD draws the scope view.</summary>
        public bool ScopeActive => _scoped && _aim > 0.88f;
        public bool HasScope => _scoped;

        private void Awake()
        {
            _currentPos = _hipPosition;
            _aimLocal = _aimPosition;
        }

        public void Show(WeaponDefinition def) => Show(def, null);

        public void Show(WeaponDefinition def, WeaponMods mods)
        {
            _hiddenForScope = false;
            _scoped = WeaponSights.Scoped(def, mods);
            // Aim so the sight line (irons, built-in optic or fitted optic) sits exactly on the screen centre.
            _aimLocal = def.Kind == WeaponKind.Firearm
                ? new Vector3(0f, -WeaponSights.LineY(def, mods), _aimPosition.z)
                : _aimPosition;
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
                AddAttachments(def, mods);
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
            AddAttachments(def, mods);
            AddArms(def);
            _renderers = _model.GetComponentsInChildren<Renderer>();
        }

        /// <summary>Puts a hand on the grip and one on the support point; each shoulder is fixed in camera space.</summary>
        private void AddArms(WeaponDefinition def)
        {
            _armR = _armL = null;
            Hold h = HoldFor(def);
            _style = StyleFor(def);
            _armR = SpawnArm("ArmR", h.Grip, false);
            _armL = h.OneHanded ? null : SpawnArm("ArmL", h.Support, true);
            _rollR = h.RollR;
            _rollL = h.RollL;
            // Shoulders sit behind where the hands are in the rest pose (melee stands the weapon up first).
            Vector3 restPos = _hipPosition;
            Quaternion restRot = Quaternion.identity;
            if (_style != MeleeStyle.None)
            {
                restPos += _style == MeleeStyle.TwoHand ? TwoRestPos : OneRestPos;
                restRot = Quaternion.Euler(_style == MeleeStyle.TwoHand ? TwoRestRot : OneRestRot);
            }
            _shoulderR = restPos + restRot * h.Grip + BackR * ArmReach;
            _shoulderL = restPos + restRot * h.Support + BackL * ArmReach;
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
        /// <summary>Melee pose at swing progress p (1 = at rest): rest, wind-up to 18%, strike to 45%, ease back.</summary>
        private void MeleePose(float p, out Vector3 pos, out Vector3 euler)
        {
            bool two = _style == MeleeStyle.TwoHand;
            Vector3 restP = two ? TwoRestPos : OneRestPos, restR = two ? TwoRestRot : OneRestRot;
            Vector3 windP = two ? TwoWindPos : OneWindPos, windR = two ? TwoWindRot : OneWindRot;
            Vector3 hitP = two ? TwoHitPos : OneHitPos, hitR = two ? TwoHitRot : OneHitRot;
            const float wind = 0.18f, hit = 0.45f;
            if (p < wind)
            {
                float t = Mathf.SmoothStep(0f, 1f, p / wind);
                pos = Vector3.Lerp(restP, windP, t);
                euler = Vector3.Lerp(restR, windR, t);
            }
            else if (p < hit)
            {
                float t = (p - wind) / (hit - wind);
                t = t * t * (3f - 2f * t);
                t = Mathf.Sqrt(t); // fast through the cut, settling at the end
                pos = Vector3.Lerp(windP, hitP, t);
                euler = Vector3.Lerp(windR, hitR, t);
            }
            else
            {
                float t = Mathf.SmoothStep(0f, 1f, (p - hit) / (1f - hit));
                pos = Vector3.Lerp(hitP, restP, t);
                euler = Vector3.Lerp(hitR, restR, t);
            }
        }

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

        /// <summary>The bare muzzle point in model space (before muzzle devices), for previews of the weapon.</summary>
        public static Vector3 MuzzlePoint(WeaponDefinition def) =>
            new Vector3(0f, def != null && def.Kind == WeaponKind.Melee ? 0f : 0.02f, MuzzleDistance(def != null ? def.Category : WeaponCategory.AssaultRifle));

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

        // ---------------------------------------------------------------- attachments

        /// <summary>The fitted attachments' models (<see cref="WeaponDresser"/>); muzzle devices move the muzzle point forward.</summary>
        private void AddAttachments(WeaponDefinition def, WeaponMods mods)
        {
            if (mods == null || def.Kind != WeaponKind.Firearm) return;
            Vector3 muzzle = _muzzle != null ? _muzzle.localPosition : new Vector3(0f, 0.02f, MuzzleDistance(def.Category));
            float extend = WeaponDresser.Fit(_model, def, mods, muzzle, gameObject.layer);
            if (extend > 0f) ExtendMuzzle(extend);
        }

        private void ExtendMuzzle(float by)
        {
            if (_muzzle != null) _muzzle.localPosition += new Vector3(0f, 0f, by);
        }

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

            Vector3 pos = Vector3.Lerp(_hipPosition, _aimLocal, _aim);
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

            // Melee: blade-up rest pose; a swing winds up, cuts across (one hand) or chops down (two hands), recovers.
            if (_style != MeleeStyle.None)
            {
                MeleePose(_meleeing ? Mathf.Clamp01(_melee) : 1f, out Vector3 mp, out Vector3 me);
                pos += mp;
                euler += me;
            }
            else if (_meleeing)
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

            float t = MotionMath.ExpBlend(_meleeing && _style != MeleeStyle.None ? 32f : _swaySharpness, dt);
            _currentPos = Vector3.Lerp(_currentPos, pos, t);
            _currentRot = Quaternion.Slerp(_currentRot, Quaternion.Euler(euler), t);
            // Kick and aim should feel immediate, so bypass smoothing for those components.
            _model.localPosition = _currentPos + new Vector3(0f, 0f, -_kickBack * _kick * 0.5f);
            _model.localRotation = _currentRot;
            AimArms();
            // Looking through a scope: the scope view replaces the weapon on screen.
            bool hide = ScopeActive;
            if (hide != _hiddenForScope && _renderers != null)
            {
                _hiddenForScope = hide;
                foreach (Renderer rend in _renderers) if (rend != null) rend.enabled = !hide;
            }
        }
    }
}
