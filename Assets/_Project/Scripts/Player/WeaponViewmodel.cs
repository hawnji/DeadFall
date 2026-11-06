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

        // ---------------------------------------------------------------- attachments

        /// <summary>Simple models for the fitted attachments: optic on the rail, muzzle device, underbarrel.</summary>
        private void AddAttachments(WeaponDefinition def, WeaponMods mods)
        {
            if (mods == null || def.Kind != WeaponKind.Firearm) return;
            WeaponSights.Sight sight = WeaponSights.For(def);
            float muzzleZ = _muzzle != null ? _muzzle.localPosition.z : MuzzleDistance(def.Category);
            float boreY = _muzzle != null ? _muzzle.localPosition.y : 0.02f;
            Color dark = new Color(0.09f, 0.09f, 0.1f), metal = new Color(0.25f, 0.26f, 0.28f);
            AttachmentDef optic = WeaponSights.Optic(def, mods);
            if (optic != null)
            {
                float rail = sight.RailY, z = sight.RailZ, line = rail + WeaponSights.OpticLift(optic);
                CreatePart("Mount", new Vector3(0.026f, 0.012f, 0.05f), new Vector3(0f, rail + 0.006f, z), metal);
                switch (optic.Id)
                {
                    case "att.red_dot":
                        CreatePart("RedDotHousing", new Vector3(0.03f, 0.03f, 0.04f), new Vector3(0f, line, z - 0.005f), dark);
                        // Hollow it: the housing is drawn as a frame round an open window, with the dot on the sight line.
                        CreatePart("RedDotTop", new Vector3(0.034f, 0.006f, 0.042f), new Vector3(0f, line + 0.018f, z), dark);
                        Glow("RedDot", new Vector3(0.0035f, 0.0035f, 0.001f), new Vector3(0f, line, z + 0.016f), new Color(1f, 0.15f, 0.1f));
                        StripCentre("RedDotHousing");
                        break;
                    case "att.holo_scope":
                        CreatePart("HoloBase", new Vector3(0.034f, 0.012f, 0.07f), new Vector3(0f, rail + 0.016f, z), dark);
                        CreatePart("HoloPostL", new Vector3(0.004f, 0.04f, 0.008f), new Vector3(-0.018f, line, z + 0.03f), dark);
                        CreatePart("HoloPostR", new Vector3(0.004f, 0.04f, 0.008f), new Vector3(0.018f, line, z + 0.03f), dark);
                        CreatePart("HoloTop", new Vector3(0.04f, 0.005f, 0.012f), new Vector3(0f, line + 0.02f, z + 0.03f), dark);
                        Glow("HoloRing", new Vector3(0.012f, 0.0015f, 0.001f), new Vector3(0f, line + 0.006f, z + 0.03f), new Color(0.3f, 1f, 0.45f));
                        Glow("HoloDot", new Vector3(0.003f, 0.003f, 0.001f), new Vector3(0f, line, z + 0.03f), new Color(0.3f, 1f, 0.45f));
                        break;
                    case "att.sniper_scope":
                        Tube("ScopeTube", 0.022f, 0.24f, new Vector3(0f, line, z), dark);
                        Tube("ScopeBell", 0.03f, 0.06f, new Vector3(0f, line, z + 0.12f), dark);
                        Tube("ScopeEye", 0.027f, 0.04f, new Vector3(0f, line, z - 0.12f), dark);
                        CreatePart("ScopeRingA", new Vector3(0.012f, line - rail, 0.012f), new Vector3(0f, (line + rail) * 0.5f, z - 0.06f), metal);
                        CreatePart("ScopeRingB", new Vector3(0.012f, line - rail, 0.012f), new Vector3(0f, (line + rail) * 0.5f, z + 0.06f), metal);
                        Glow("ScopeLens", new Vector3(0.05f, 0.05f, 0.002f), new Vector3(0f, line, z + 0.151f), new Color(0.2f, 0.5f, 0.9f));
                        break;
                }
            }
            foreach (AttachmentDef a in mods.Attachments)
            {
                switch (a.Id)
                {
                    case "att.compensator":
                        Tube("Compensator", 0.019f, 0.06f, new Vector3(0f, boreY, muzzleZ + 0.03f), dark);
                        CreatePart("CompPort", new Vector3(0.04f, 0.006f, 0.01f), new Vector3(0f, boreY + 0.012f, muzzleZ + 0.035f), metal);
                        ExtendMuzzle(0.06f);
                        break;
                    case "att.long_barrel":
                        Tube("LongBarrel", 0.012f, 0.13f, new Vector3(0f, boreY, muzzleZ + 0.065f), metal);
                        ExtendMuzzle(0.13f);
                        break;
                    case "att.foregrip":
                        CreatePart("Foregrip", new Vector3(0.024f, 0.075f, 0.028f), new Vector3(0f, boreY - 0.075f, muzzleZ * 0.62f), dark);
                        break;
                    case "att.laser":
                        CreatePart("Laser", new Vector3(0.022f, 0.022f, 0.06f), new Vector3(0.026f, boreY - 0.02f, muzzleZ * 0.6f), dark);
                        Glow("LaserEmitter", new Vector3(0.008f, 0.008f, 0.002f), new Vector3(0.026f, boreY - 0.02f, muzzleZ * 0.6f + 0.031f), new Color(1f, 0.1f, 0.1f));
                        break;
                    case "att.extended_mag":
                        CreatePart("ExtMag", new Vector3(0.028f, 0.06f, 0.04f), new Vector3(0f, -0.14f, sight.RailZ + 0.04f), dark);
                        break;
                }
            }
        }

        private void ExtendMuzzle(float by)
        {
            if (_muzzle != null) _muzzle.localPosition += new Vector3(0f, 0f, by);
        }

        /// <summary>Removes the named part and puts four thin bars in its place, leaving the window open to look through.</summary>
        private void StripCentre(string partName)
        {
            Transform t = _model.Find(partName);
            if (t == null) return;
            Vector3 size = t.localScale, at = t.localPosition;
            Color c = new Color(0.09f, 0.09f, 0.1f);
            float w = 0.004f;
            CreatePart(partName + "L", new Vector3(w, size.y, size.z), at + new Vector3(-size.x * 0.5f + w * 0.5f, 0f, 0f), c);
            CreatePart(partName + "R", new Vector3(w, size.y, size.z), at + new Vector3(size.x * 0.5f - w * 0.5f, 0f, 0f), c);
            CreatePart(partName + "B", new Vector3(size.x, w, size.z), at + new Vector3(0f, -size.y * 0.5f + w * 0.5f, 0f), c);
            Destroy(t.gameObject);
        }

        private void Tube(string partName, float radius, float length, Vector3 centre, Color color)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            part.name = partName;
            Object.Destroy(part.GetComponent<Collider>());
            part.layer = gameObject.layer;
            part.transform.SetParent(_model, false);
            part.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            part.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
            part.transform.localPosition = centre;
            var r = part.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sharedMaterial = RuntimeMaterials.CreateLit(color);
        }

        private void Glow(string partName, Vector3 size, Vector3 localPos, Color color)
        {
            CreatePart(partName, size, localPos, color);
            Transform t = _model.Find(partName);
            if (t != null) t.GetComponent<Renderer>().sharedMaterial = RuntimeMaterials.CreateEmissive(color, 3f);
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
