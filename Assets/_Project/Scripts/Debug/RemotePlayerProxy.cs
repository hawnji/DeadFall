using System;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Game;
using Deadfall.Rules.Combat;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Deadfall.DebugTools
{
    /// <summary>
    /// How another player looks on this machine during the networking spike: a coloured capsule with a
    /// head and a gun barrel that follows their aim. It has no colliders, so it never blocks shots or
    /// movement. Positions are smoothed toward the latest state; real interpolation arrives with M11.
    /// </summary>
    public sealed class RemotePlayerProxy : MonoBehaviour, ICombatTarget, IDamageable, ISquadMember, IChannelInteractable, INearbyInteractable
    {
        private const float PositionSharpness = 18f;
        private const float RotationSharpness = 22f;
        private const float SnapDistance = 5f;
        private const float StandEye = 1.68f;
        private const float CrouchEye = 1.05f;

        private Transform _body;
        private Transform _bodyModel;
        private GameObject _weaponModel;
        private WeaponDefinition _weaponShown;
        private bool _hasModels;

        /// <summary>Where the held weapon's model origin sits under the aim (matches player_head's hands).</summary>
        private static readonly Vector3 WeaponAnchor = new Vector3(0.25f, -0.27f, 0.18f);
        private Transform _aim;
        // Animation rig (player_body / player_head children): legs, torso and the arms holding the weapon.
        private Transform _thighL, _thighR, _shinL, _shinR, _torso, _arms;
        private Vector3 _armsRest;
        // Per part: rest local rotation and the turn from character axes into the part's parent axes (FBX import can
        // give rig children their own axes), so poses below are written as plain pitch/yaw/roll on the character.
        private Quaternion _restThighL, _restThighR, _restShinL, _restShinR, _restTorso, _restArms;
        private Quaternion _cThighL, _cThighR, _cShinL, _cShinR, _cTorso, _cArms;
        private bool _aiming, _sprinting, _airborne;
        private Vector3 _lastPos, _velocity;
        private float _gait, _aimPose, _sprintPose, _airPose, _crouchPose;
        private Renderer[] _renderers;
        private Vector3 _targetPos;
        private float _targetYaw, _targetPitch;
        private float _yaw, _pitch;
        private bool _crouched, _dead;
        private bool _hasState;
        private float _eyeHeight = StandEye;

        public ulong ClientId { get; private set; }
        public float Health01 { get; private set; } = 1f;
        public bool IsDead => _dead;
        public bool IsDowned => _downed;

        private bool _downed;
        private float _bleed01;
        private float _ghostUntil = -1f;

        // ISquadMember (M11)
        public bool IsLocalMember => false;
        public LifeState Life => _dead ? LifeState.Eliminated : _downed ? LifeState.Downed : LifeState.Alive;
        public float BleedOut01 => _bleed01;
        /// <summary>Dropped mid-run and held for a rejoin (D-16): shown downed in place until then.</summary>
        public bool Disconnected => _ghostUntil >= 0f;
        public Transform Body => transform;
        public Vector3 Eye => transform.position + Vector3.up * (_downed || _dead ? 0.5f : _eyeHeight);
        public float GhostSecondsLeft => Disconnected ? Mathf.Max(0f, _ghostUntil - Time.time) : 0f;

        public void MarkDisconnected(float seconds)
        {
            _ghostUntil = Time.time + seconds;
            _downed = true;
            _hasState = true;
        }

        // Revive (D-01): hold Interact near a downed teammate. No colliders, so it is found by distance.
        public float ChannelSeconds => DownedRules.ReviveSeconds / Mathf.Max(0.1f, PerkMachineDirector.LocalReviveSpeed);
        public bool InReach(Vector3 feet, out float distance)
        {
            distance = Vector3.Distance(feet, transform.position);
            return _downed && !_dead && !Disconnected && distance <= DownedRules.ReviveRange;
        }
        public string InteractPrompt(GameObject interactor) => $"Revive player {ClientId}";
        public bool CanInteract(GameObject interactor) => _downed && !_dead && !Disconnected && SquadLife.Current != null;
        public void Interact(GameObject interactor)
        {
            if (SquadLife.Current != null) SquadLife.Current.RequestRevive(ClientId);
        }
        public void OnChannel(GameObject interactor, bool active)
        {
            if (SquadLife.Current != null) SquadLife.Current.SetReviving(ClientId, active);
        }

        /// <summary>Host: an enemy hit this player. The amount is sent to their machine, which applies it.</summary>
        public event Action<RemotePlayerProxy, float, Vector3> DamageRequested;

        // Host enemies hunt remote players through these (ICombatTarget / IDamageable).
        public Transform TargetTransform => transform;
        public bool IsTargetable => _hasState && !_dead && !_downed && isActiveAndEnabled;
        public IDamageable Damageable => this;
        public bool IsAlive => !_dead && !_downed;
        public float DamageReduction => 0f; // the owner applies their own reduction

        public float ApplyDamage(in DamageInfo info)
        {
            if (_dead || _downed || info.Amount <= 0f) return 0f;
            float amount = info.Amount * DamageModifiers.For(info, gameObject);
            if (amount <= 0f) return 0f;
            DamageRequested?.Invoke(this, amount, info.Direction);
            return amount;
        }

        private void OnEnable()
        {
            CombatTargets.Register(this);
            SquadMembers.Register(this);
            NearbyInteractables.Register(this);
        }

        private void OnDisable()
        {
            CombatTargets.Unregister(this);
            SquadMembers.Unregister(this);
            NearbyInteractables.Unregister(this);
        }
        /// <summary>Latest reported eye position (unsmoothed), used by the host to validate hit claims.</summary>
        public Vector3 EyePosition => _targetPos + Vector3.up * (_crouched ? CrouchEye : StandEye);

        public static RemotePlayerProxy Create(ulong clientId, Transform parent)
        {
            var root = new GameObject($"RemotePlayer_{clientId}");
            root.transform.SetParent(parent, false);
            Color color = ColorFor(clientId);

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Object.Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            SandboxBootstrap.Tint(body, color);

            var aim = new GameObject("Aim").transform;
            aim.SetParent(root.transform, false);
            aim.localPosition = new Vector3(0f, StandEye, 0f);

            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            Object.Destroy(head.GetComponent<Collider>());
            head.transform.SetParent(aim, false);
            head.transform.localScale = Vector3.one * 0.34f;
            SandboxBootstrap.Tint(head, color * 0.8f);

            GameObject visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visor.name = "Visor";
            Object.Destroy(visor.GetComponent<Collider>());
            visor.transform.SetParent(aim, false);
            visor.transform.localPosition = new Vector3(0f, 0.02f, 0.15f);
            visor.transform.localScale = new Vector3(0.26f, 0.08f, 0.08f);
            SandboxBootstrap.Tint(visor, new Color(0.1f, 0.1f, 0.12f));

            GameObject gun = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gun.name = "Gun";
            Object.Destroy(gun.GetComponent<Collider>());
            gun.transform.SetParent(aim, false);
            gun.transform.localPosition = new Vector3(0.25f, -0.25f, 0.35f);
            gun.transform.localScale = new Vector3(0.08f, 0.1f, 0.6f);
            SandboxBootstrap.Tint(gun, new Color(0.2f, 0.2f, 0.22f));

            var proxy = root.AddComponent<RemotePlayerProxy>();
            proxy.ClientId = clientId;
            root.AddComponent<PlayerIdentity>().SetRemote(clientId);
            proxy._body = body.transform;
            proxy._aim = aim;
            // Blender models (T-58): the body follows crouching, the head/arms/rifle pitch with the aim.
            Color accent = color;
            GameObject bodyModel = ModelLibrary.Spawn("player_body", root.transform, slot => ModelLibrary.SlotFromAccent(slot, accent));
            GameObject headModel = ModelLibrary.Spawn("player_head", aim, slot => ModelLibrary.SlotFromAccent(slot, accent));
            if (bodyModel != null && headModel != null)
            {
                proxy._bodyModel = bodyModel.transform;
                proxy._hasModels = true;
                proxy.BindRig(bodyModel.transform, headModel.transform);
                proxy.SetWeapon(WeaponCatalog.HalberdAR); // until their first state message says otherwise
                body.GetComponent<Renderer>().enabled = false;
                head.GetComponent<Renderer>().enabled = false;
                visor.GetComponent<Renderer>().enabled = false;
                gun.GetComponent<Renderer>().enabled = false;
            }
            else
            {
                if (bodyModel != null) Object.Destroy(bodyModel);
                if (headModel != null) Object.Destroy(headModel);
            }
            proxy._renderers = root.GetComponentsInChildren<Renderer>();
            // Outlined only while downed (hold F to revive).
            InteractGlow.Add(root, InteractGlow.Objectives, () => proxy.CanInteract(null));
            return proxy;
        }

        private static Color ColorFor(ulong id)
        {
            Color[] palette =
            {
                new Color(0.25f, 0.55f, 0.95f), new Color(0.95f, 0.6f, 0.2f),
                new Color(0.6f, 0.35f, 0.9f), new Color(0.3f, 0.8f, 0.75f),
            };
            return palette[(int)(id % (ulong)palette.Length)];
        }

        /// <summary>Shows the model of the weapon this teammate is holding (null keeps the current one).</summary>
        public void SetWeapon(WeaponDefinition def)
        {
            if (!_hasModels || def == null || def == _weaponShown) return;
            if (_weaponModel != null) Destroy(_weaponModel);
            _weaponShown = def;
            _weaponModel = ModelLibrary.Spawn(def.ContentId.Replace('.', '_'), _arms != null ? _arms : _aim, slot => ModelLibrary.SlotFromAccent(slot, def.PlaceholderColor));
            if (_weaponModel != null && _arms != null)
            {
                // Under the arms so it moves with them; placed as if the arms were at rest.
                Quaternion restWorld = _arms.parent.rotation * _restArms;
                Vector3 restPos = _arms.parent.TransformPoint(_armsRest);
                _weaponModel.transform.localRotation = Quaternion.Inverse(restWorld) * _aim.rotation;
                _weaponModel.transform.localPosition = Quaternion.Inverse(restWorld) * (_aim.TransformPoint(WeaponAnchor) - restPos);
            }
            else if (_weaponModel != null) _weaponModel.transform.localPosition = WeaponAnchor;
            _renderers = GetComponentsInChildren<Renderer>();
        }

        private static Transform FindChild(Transform root, string childName)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name == childName) return t;
            return null;
        }

        /// <summary>Finds the rig children; each shin goes under its thigh so the knee follows the hip.</summary>
        private void BindRig(Transform body, Transform head)
        {
            _thighL = FindChild(body, "ThighL");
            _thighR = FindChild(body, "ThighR");
            _shinL = FindChild(body, "ShinL");
            _shinR = FindChild(body, "ShinR");
            _torso = FindChild(body, "Torso");
            _arms = FindChild(head, "Arms");
            if (_thighL != null && _shinL != null) _shinL.SetParent(_thighL, true);
            if (_thighR != null && _shinR != null) _shinR.SetParent(_thighR, true);
            Bind(_thighL, transform, out _restThighL, out _cThighL);
            Bind(_thighR, transform, out _restThighR, out _cThighR);
            Bind(_shinL, transform, out _restShinL, out _cShinL);
            Bind(_shinR, transform, out _restShinR, out _cShinR);
            Bind(_torso, transform, out _restTorso, out _cTorso);
            Bind(_arms, _aim, out _restArms, out _cArms);
            if (_arms != null) _armsRest = _arms.localPosition;
        }

        private static void Bind(Transform part, Transform frame, out Quaternion rest, out Quaternion c)
        {
            rest = part != null ? part.localRotation : Quaternion.identity;
            c = part != null ? Quaternion.Inverse(part.parent.rotation) * frame.rotation : Quaternion.identity;
        }

        private static void Pose(Transform part, Quaternion rest, Quaternion c, float pitch, float yaw, float roll)
        {
            if (part == null) return;
            part.localRotation = c * Quaternion.Euler(pitch, yaw, roll) * Quaternion.Inverse(c) * rest;
        }

        /// <summary>Pose hints from the owner's state: aiming down sights, sprinting, off the ground.</summary>
        public void SetPose(bool aiming, bool sprinting, bool airborne)
        {
            _aiming = aiming;
            _sprinting = sprinting;
            _airborne = airborne;
        }

        public void SetTarget(Vector3 position, float yaw, float pitch, bool crouched, bool dead, float health01, bool downed = false, float bleed01 = 0f)
        {
            _downed = downed && !dead;
            _bleed01 = Mathf.Clamp01(bleed01);
            _targetPos = position;
            _targetYaw = yaw;
            _targetPitch = pitch;
            _crouched = crouched;
            _dead = dead;
            Health01 = Mathf.Clamp01(health01);
            if (!_hasState || (transform.position - position).sqrMagnitude > SnapDistance * SnapDistance)
            {
                transform.position = position;
                _yaw = yaw;
                _pitch = pitch;
                _hasState = true;
            }
        }

        public void Tick(float dt)
        {
            if (!_hasState) return;
            float kp = 1f - Mathf.Exp(-PositionSharpness * dt);
            float kr = 1f - Mathf.Exp(-RotationSharpness * dt);
            transform.position = Vector3.Lerp(transform.position, _targetPos, kp);
            _yaw = Mathf.LerpAngle(_yaw, _targetYaw, kr);
            _pitch = Mathf.Lerp(_pitch, _targetPitch, kr);
            _eyeHeight = Mathf.Lerp(_eyeHeight, _crouched ? CrouchEye : StandEye, kp);

            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            _aim.localPosition = new Vector3(0f, _eyeHeight, 0f);
            _aim.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            float bodyScale = _eyeHeight / StandEye;
            _body.localScale = new Vector3(0.7f, 0.9f * bodyScale, 0.7f);
            _body.localPosition = new Vector3(0f, 0.9f * bodyScale, 0f);
            if (_bodyModel != null)
            {
                if (_thighL != null && _thighR != null) Animate(dt, bodyScale);
                else _bodyModel.localScale = new Vector3(1f, bodyScale, 1f);
            }

            // Downed players sit low; eliminated (and dropped) players lie down until they come back.
            if (_dead) transform.rotation = Quaternion.Euler(-80f, _yaw, 0f);
            else if (_downed) transform.rotation = Quaternion.Euler(-35f, _yaw, 0f);
        }

        /// <summary>
        /// Procedural animation from the replicated movement: a walk or run cycle from the speed (backwards when
        /// moving backwards), knees tucked in the air, legs folded when crouched, the weapon shouldered when aiming
        /// and lowered across the chest when sprinting.
        /// </summary>
        private void Animate(float dt, float bodyScale)
        {
            Vector3 v = dt > 0f ? (transform.position - _lastPos) / dt : Vector3.zero;
            _lastPos = transform.position;
            if (v.sqrMagnitude > 20f * 20f) v = Vector3.zero; // a snap, not movement
            _velocity = Vector3.Lerp(_velocity, v, 1f - Mathf.Exp(-10f * dt));
            Vector3 flat = new Vector3(_velocity.x, 0f, _velocity.z);
            float speed = _downed || _dead ? 0f : flat.magnitude;
            float forward = Vector3.Dot(flat, transform.forward);
            float k = 1f - Mathf.Exp(-10f * dt);
            _aimPose = Mathf.Lerp(_aimPose, _aiming && !_sprinting ? 1f : 0f, k);
            _sprintPose = Mathf.Lerp(_sprintPose, _sprinting && speed > 3f ? 1f : 0f, k);
            _airPose = Mathf.Lerp(_airPose, _airborne && !_downed && !_dead ? 1f : 0f, k);
            _crouchPose = Mathf.Clamp01((StandEye - _eyeHeight) / (StandEye - CrouchEye));

            // Gait: one full stride per ~1.5 m walking, longer strides running.
            float stride = Mathf.Lerp(1.4f, 2.1f, _sprintPose);
            _gait += (forward < -0.2f ? -1f : 1f) * speed / stride * Mathf.PI * 2f * dt * (1f - _airPose);
            float move = Mathf.Clamp01(speed / 2f) * (1f - _airPose);
            float swing = Mathf.Lerp(26f, 46f, _sprintPose) * move * Mathf.Lerp(1f, 0.6f, _crouchPose);
            float knee = Mathf.Lerp(30f, 70f, _sprintPose) * move;
            float sl = Mathf.Sin(_gait), sr = Mathf.Sin(_gait + Mathf.PI);
            // Negative pitch swings a leg forward; the knee bends while that leg comes through.
            float thighL = -swing * sl, thighR = -swing * sr;
            float shinL = knee * Mathf.Max(0f, Mathf.Cos(_gait)) + 4f * move;
            float shinR = knee * Mathf.Max(0f, Mathf.Cos(_gait + Mathf.PI)) + 4f * move;
            // Jump: knees up. Crouch: legs folded under the hips.
            thighL = Mathf.Lerp(thighL, -40f, _airPose) - 70f * _crouchPose;
            thighR = Mathf.Lerp(thighR, -20f, _airPose) - 70f * _crouchPose;
            shinL = Mathf.Lerp(shinL, 75f, _airPose) + 105f * _crouchPose;
            shinR = Mathf.Lerp(shinR, 45f, _airPose) + 105f * _crouchPose;
            Pose(_thighL, _restThighL, _cThighL, thighL, 0f, 0f);
            Pose(_thighR, _restThighR, _cThighR, thighR, 0f, 0f);
            Pose(_shinL, _restShinL, _cShinL, shinL, 0f, 0f);
            Pose(_shinR, _restShinR, _cShinR, shinR, 0f, 0f);

            // Body: bob with each step, lean into a run, drop into the crouch.
            float bob = Mathf.Abs(Mathf.Cos(_gait)) * 0.035f * move;
            _bodyModel.localScale = Vector3.one;
            _bodyModel.localPosition = new Vector3(0f, bob - 0.4f * _crouchPose, 0f);
            if (_torso != null)
            {
                float twist = Mathf.Sin(_gait) * 6f * move * (1f - _aimPose);
                Pose(_torso, _restTorso, _cTorso, 10f * _sprintPose + 4f * move + 35f * _crouchPose - 6f * _airPose, twist, 0f);
            }

            // Arms (with the weapon): shouldered toward the eye line when aiming, lowered and angled across the chest
            // when sprinting, with a little sway from the stride.
            if (_arms != null)
            {
                Vector3 aimShift = new Vector3(-0.13f, 0.1f, 0.05f) * _aimPose;
                Vector3 sprintShift = new Vector3(-0.05f, -0.06f, -0.04f) * _sprintPose;
                Vector3 sway = new Vector3(Mathf.Sin(_gait) * 0.015f, Mathf.Abs(Mathf.Sin(_gait)) * 0.012f, 0f) * move * (1f - _aimPose);
                _arms.localPosition = _armsRest + _cArms * (aimShift + sprintShift + sway);
                Pose(_arms, _restArms, _cArms, 30f * _sprintPose, -40f * _sprintPose, 15f * _sprintPose);
            }
        }

        private void OnGUI()
        {
            Camera cam = Camera.main;
            if (cam == null || _renderers == null) return;
            Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * (_eyeHeight + 0.45f));
            if (sp.z <= 0f || sp.z > 60f) return;
            Deadfall.Classes.ClassDefinition cls = Deadfall.Classes.PrototypeClasses.ById(Deadfall.Classes.ClassRoster.ClassOf(ClientId));
            // Name plate: name and class over a small health bar, red when down.
            Deadfall.Core.HudKit.Begin();
            Vector2 p = Deadfall.Core.HudKit.FromScreen(sp);
            float fade = Mathf.Clamp01((60f - sp.z) / 15f);
            string label = $"PLAYER {ClientId}{(cls != null ? "  ·  " + cls.DisplayName.ToUpperInvariant() : "")}";
            Color c = cls != null ? cls.Color : Color.white;
            string state = Disconnected ? $"  ·  RECONNECTING {Mathf.CeilToInt(GhostSecondsLeft)}s" : _dead ? "  ·  OUT" : _downed ? "  ·  DOWN" : "";
            bool bad = _dead || _downed;
            Deadfall.Core.HudKit.Label(new Rect(p.x - 140f, p.y - 26f, 280f, 18f), label + state, 12,
                bad ? new Color(1f, 0.4f, 0.35f, fade) : new Color(c.r, c.g, c.b, fade), TextAnchor.MiddleCenter, head: true);
            // Downed: the bar shows the bleed-out instead of health.
            Deadfall.Core.HudKit.Bar(new Rect(p.x - 40f, p.y - 6f, 80f, 7f), _downed && !Disconnected ? _bleed01 : Health01, bad ? Deadfall.Core.HudKit.Bad : Deadfall.Core.HudKit.Good,
                0, new Color(0f, 0f, 0f, 0.5f * fade));
            if (_downed && !_dead && !Disconnected)
            {
                // Downed marker that reads from further away.
                Deadfall.Core.HudKit.Label(new Rect(p.x - 20f, p.y - 54f, 40f, 26f), "+", 26, new Color(1f, 0.35f, 0.3f, Mathf.Max(fade, 0.6f)), TextAnchor.MiddleCenter, head: true);
            }
            Deadfall.Core.HudKit.End();
        }
    }
}
