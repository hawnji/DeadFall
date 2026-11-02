using System;
using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Rules.Combat;
using Deadfall.Rules.Common;
using UnityEngine;

namespace Deadfall.Player
{
    public struct WeaponFired
    {
        public GameObject Owner;
        public string WeaponId;
        public int Shots;
    }

    /// <summary>One visible bullet path (per pellet), for tracers on other players' screens.</summary>
    public struct ShotTraced
    {
        public GameObject Owner;
        public Vector3 From;
        public Vector3 To;
    }

    /// <summary>
    /// The player's loadout and everything they do with it: swap, aim, fire, reload, melee.
    /// Numbers come from WeaponDefinition assets; rules come from the engine-free WeaponStateMachine.
    /// Hits become HitClaims sent to the hit resolver, which keeps this class unchanged when networking arrives.
    /// The number of slots is whatever the loadout contains (D-07 default: 2 primaries, sidearm, melee).
    /// </summary>
    [DefaultExecutionOrder(10)]
    public sealed class PlayerWeapons : MonoBehaviour
    {
        private sealed class Slot
        {
            public WeaponDefinition Definition;
            public WeaponStateMachine Firearm;
            public SpreadModel Spread;
            public bool IsMelee => Definition.Kind == WeaponKind.Melee;
        }

        [SerializeField] private List<WeaponDefinition> _loadout = new List<WeaponDefinition>();
        [Tooltip("Quick melee available with any weapon (rifle butt / knife).")]
        [SerializeField] private MeleeStats _quickMelee = new MeleeStats();
        [SerializeField] private float _meleeEquipSeconds = 0.35f;

        private readonly List<Slot> _slots = new List<Slot>();
        private readonly List<MeleeTarget> _meleeTargets = new List<MeleeTarget>(8);
        private PlayerInputReader _input;
        private PlayerLook _look;
        private PlayerMotor _motor;
        private PlayerStatus _status;
        private WeaponViewmodel _viewmodel;
        private DeterministicRandom _rng;

        private int _current = -1;
        private double _equipStart, _equipEnd;
        private double _lastSprintTime = double.NegativeInfinity;
        private double _meleeHitTime = double.NegativeInfinity;
        private double _meleeEndTime = double.NegativeInfinity;
        private bool _meleePending;
        private MeleeStats _activeMelee;
        private float _aimBlend;
        private static uint _rngCounter;
        private WeaponHandling _handling = WeaponHandling.Default;
        private MeleeBonus? _nextMeleeBonus;
        private MeleeBonus? _activeMeleeBonus;
        private readonly MeleeStats _bonusMelee = new MeleeStats();
        private double _sustainStart = double.NaN;
        private double _lastShotTime = double.NegativeInfinity;

        /// <summary>Class passives and abilities plug in here.</summary>
        public IWeaponHandlingSource HandlingSource { get; set; }
        /// <summary>The handling applied this frame to the weapon in hand.</summary>
        public WeaponHandling Handling => _handling;
        /// <summary>Seconds of continuous fire so far (0 when not firing). Short gaps between shots don't reset it.</summary>
        public float SustainedFireSeconds => double.IsNaN(_sustainStart) ? 0f : (float)(Time.timeAsDouble - _sustainStart);
        /// <summary>A melee bonus is waiting for the next swing.</summary>
        public bool HasMeleeBonus => _nextMeleeBonus.HasValue;

        /// <summary>Raised after a melee swing resolves, with the number of targets it hit.</summary>
        public event Action<int> MeleeResolved;

        /// <summary>The next melee swing gets this bonus (replaces any waiting bonus).</summary>
        public void SetNextMeleeBonus(MeleeBonus bonus) => _nextMeleeBonus = bonus;

        public void ClearMeleeBonus() => _nextMeleeBonus = null;

        /// <summary>Clears the waiting bonus only if <paramref name="tag"/> set it.</summary>
        public void ClearMeleeBonus(string tag)
        {
            if (_nextMeleeBonus.HasValue && _nextMeleeBonus.Value.Tag == tag) _nextMeleeBonus = null;
        }

        /// <summary>The waiting bonus's tag, or null.</summary>
        public string MeleeBonusTag => _nextMeleeBonus?.Tag;

        /// <summary>Raised when the slots are (re)built from the loadout.</summary>
        public event Action LoadoutBuilt;

        /// <summary>Starts a melee swing now if nothing blocks it (abilities that end in a strike).</summary>
        public bool TryMeleeNow()
        {
            if (_current < 0 || IsMeleeing || Time.timeAsDouble < _equipEnd) return false;
            Slot slot = _slots[_current];
            StartMelee(slot.IsMelee ? slot.Definition.Melee : _quickMelee, Time.timeAsDouble, slot);
            return true;
        }

        /// <summary>Scales every firearm's reserve capacity (Gunner +25%). <paramref name="refill"/> fills to the new cap.</summary>
        public void SetReserveCapacity(float multiplier, bool refill)
        {
            foreach (Slot s in _slots)
                s.Firearm?.SetMaxReserve(UnityEngine.Mathf.RoundToInt(s.Definition.Firearm.MaxReserve * UnityEngine.Mathf.Max(0f, multiplier)), refill);
        }

        public int SlotCount => _slots.Count;
        public int CurrentIndex => _current;
        public WeaponDefinition CurrentDefinition => _current >= 0 ? _slots[_current].Definition : null;
        public int Magazine => CurrentFirearm?.Magazine ?? 0;
        public int Reserve => CurrentFirearm?.Reserve ?? 0;
        public bool IsReloading => CurrentFirearm != null && CurrentFirearm.IsReloading;
        public float ReloadProgress => CurrentFirearm != null && CurrentFirearm.IsReloading ? CurrentFirearm.StateProgress(Time.timeAsDouble) : 0f;
        public float EquipProgress => _equipEnd > _equipStart ? Mathf.Clamp01((float)((Time.timeAsDouble - _equipStart) / (_equipEnd - _equipStart))) : 1f;
        public bool IsMeleeing => Time.timeAsDouble < _meleeEndTime;
        public float MeleeProgress => _activeMelee != null && IsMeleeing ? Mathf.Clamp01(1f - (float)((_meleeEndTime - Time.timeAsDouble) / Math.Max(0.01f, _activeMelee.RecoverySeconds))) : 1f;
        public float AimBlend => _aimBlend;
        /// <summary>Current spread cone half-angle in degrees (for the crosshair).</summary>
        public float CurrentSpread { get; private set; }

        public event Action<int> WeaponEquipped;

        private WeaponStateMachine CurrentFirearm => _current >= 0 ? _slots[_current].Firearm : null;

        /// <summary>Sets the loadout before the player activates (used by PlayerFactory and loadout screens).</summary>
        public void SetLoadout(IList<WeaponDefinition> loadout)
        {
            _loadout = new List<WeaponDefinition>(loadout);
            if (isActiveAndEnabled) BuildSlots();
        }

        private void Awake()
        {
            _input = GetComponent<PlayerInputReader>();
            _look = GetComponent<PlayerLook>();
            _motor = GetComponent<PlayerMotor>();
            _status = GetComponent<PlayerStatus>();
            _viewmodel = GetComponentInChildren<WeaponViewmodel>(true);
            // Spread only needs to differ between players; it is not gameplay-authoritative.
            _rng = new DeterministicRandom((ulong)System.Environment.TickCount ^ ((ulong)++_rngCounter << 32) ^ 0x5DEECE66DUL);
        }

        private void Start() => BuildSlots();

        private void BuildSlots()
        {
            _slots.Clear();
            foreach (WeaponDefinition def in _loadout)
            {
                if (def == null) continue;
                var slot = new Slot { Definition = def };
                if (def.Kind == WeaponKind.Firearm)
                {
                    slot.Firearm = WeaponStateMachine.CreateFull(def.Firearm);
                    slot.Spread = new SpreadModel(def.Firearm);
                }
                _slots.Add(slot);
            }
            _current = -1;
            if (_slots.Count > 0) Equip(0);
            LoadoutBuilt?.Invoke();
        }

        public void Equip(int index)
        {
            if (index < 0 || index >= _slots.Count || index == _current) return;
            double now = Time.timeAsDouble;
            if (_current >= 0) _slots[_current].Firearm?.Holster();
            _current = index;
            Slot slot = _slots[_current];
            _equipStart = now;
            RefreshHandling(slot);
            if (slot.Firearm != null)
            {
                slot.Firearm.Equip(now);
                _equipEnd = now + slot.Definition.Firearm.EquipSeconds / _handling.EquipSpeed;
                slot.Spread.Reset();
                if (_look != null) _look.AimFovMultiplier = slot.Definition.Firearm.AimFovMultiplier;
            }
            else
            {
                _equipEnd = now + _meleeEquipSeconds / _handling.EquipSpeed;
                if (_look != null) _look.AimFovMultiplier = 1f;
            }
            if (_viewmodel != null) _viewmodel.Show(slot.Definition);
            WeaponEquipped?.Invoke(_current);
        }

        private void Update()
        {
            if (_current < 0 || _input == null) return;
            double now = Time.timeAsDouble;
            float dt = Time.deltaTime;
            PlayerInputFrame f = _input.Current;
            Slot slot = _slots[_current];

            if (_status != null && _status.IsDead)
            {
                slot.Firearm?.CancelReload();
                _status.SprintBlocked = true;
                _aimBlend = 0f;
                _status.AimBlend = 0f;
                return;
            }

            HandleSwap(f);
            slot = _slots[_current];
            RefreshHandling(slot);
            bool equipping = now < _equipEnd;

            // Sprint → fire delay: weapons need a moment to come up after sprinting.
            if (_motor != null && _motor.IsSprinting) _lastSprintTime = now;
            float sprintToFire = slot.Firearm != null ? slot.Definition.Firearm.SprintToFireSeconds : 0.1f;
            bool sprintRecovered = now >= _lastSprintTime + sprintToFire;

            // Aim
            bool canAim = slot.Firearm != null && !IsMeleeing && !equipping;
            float aimTime = slot.Firearm != null ? Mathf.Max(0.01f, slot.Definition.Firearm.AimInSeconds) : 0.2f;
            _aimBlend = Mathf.MoveTowards(_aimBlend, canAim && f.AimHeld ? 1f : 0f, dt / aimTime);

            bool fireIntent = f.FireHeld || f.FirePressed;

            // Melee (quick melee from any weapon, or the primary attack of a melee weapon)
            if (!IsMeleeing && !equipping)
            {
                if (f.MeleePressed) StartMelee(slot.IsMelee ? slot.Definition.Melee : _quickMelee, now, slot);
                else if (slot.IsMelee && fireIntent && sprintRecovered) StartMelee(slot.Definition.Melee, now, slot);
            }
            if (_meleePending && now >= _meleeHitTime) ResolveMelee();

            // Reload
            if (slot.Firearm != null && f.ReloadPressed && !IsMeleeing) slot.Firearm.StartReload(now);

            // Fire
            int shots = 0;
            if (slot.Firearm != null)
            {
                bool allowed = !IsMeleeing && sprintRecovered && (_motor == null || !_motor.IsSprinting);
                FireResult result = allowed ? slot.Firearm.TryFire(now, f.FirePressed, f.FireHeld) : slot.Firearm.TryFire(now, false, false);
                shots = result.ShotsFired;
                if (result.DryFire && slot.Firearm.CanReload) slot.Firearm.StartReload(now);
                for (int i = 0; i < shots; i++) FireOneShot(slot);
                if (shots > 0)
                {
                    if (double.IsNaN(_sustainStart)) _sustainStart = now;
                    _lastShotTime = now;
                }
                else if (!double.IsNaN(_sustainStart) && (!f.FireHeld || now - _lastShotTime > 0.35))
                {
                    _sustainStart = double.NaN;
                }
                if (shots > 0)
                    EventBus<WeaponFired>.Publish(new WeaponFired { Owner = gameObject, WeaponId = slot.Definition.ContentId, Shots = shots });

                slot.Spread.Recover(dt);
                float moveFactor = _motor != null ? _motor.MoveFactor : 0f;
                bool airborne = _motor != null && !_motor.IsGrounded;
                CurrentSpread = slot.Spread.CurrentHalfAngle(_aimBlend, moveFactor, airborne);

                if (shots == 0 && !f.FireHeld && _look != null)
                    _look.RecoverRecoil(slot.Definition.Firearm.RecoilRecoveryPerSecond, dt);
            }
            else
            {
                CurrentSpread = 0f;
            }

            if (_status != null)
            {
                _status.AimBlend = _aimBlend;
                _status.SprintBlocked = _aimBlend > 0.05f || fireIntent || IsMeleeing;
                float weaponMove = slot.Firearm != null ? slot.Definition.Firearm.MoveSpeedMultiplier : slot.Definition.Melee.MoveSpeedMultiplier;
                _status.WeaponMoveMultiplier = weaponMove;
            }

            if (_viewmodel != null)
            {
                _viewmodel.SetState(_aimBlend, EquipProgress, ReloadProgress, MeleeProgress, IsMeleeing,
                    _motor != null && _motor.IsSprinting, _motor != null ? _motor.MoveFactor : 0f);
            }
        }

        private void HandleSwap(in PlayerInputFrame f)
        {
            if (_slots.Count < 2) return;
            if (f.WeaponSlotPressed >= 0 && f.WeaponSlotPressed < _slots.Count)
            {
                Equip(f.WeaponSlotPressed);
            }
            else if (f.WeaponScroll != 0)
            {
                int next = (_current + f.WeaponScroll + _slots.Count) % _slots.Count;
                Equip(next);
            }
        }

        private void FireOneShot(Slot slot)
        {
            WeaponStats w = slot.Definition.Firearm;
            Transform cam = _look != null && _look.Camera != null ? _look.Camera.transform : transform;
            Vector3 origin = cam.position;
            float spread = slot.Spread.CurrentHalfAngle(_aimBlend, _motor != null ? _motor.MoveFactor : 0f, _motor != null && !_motor.IsGrounded);
            Vector3 muzzle = _viewmodel != null ? _viewmodel.MuzzlePosition : origin + cam.forward * 0.5f;

            int pellets = Mathf.Max(1, w.PelletsPerShot);
            for (int p = 0; p < pellets; p++)
            {
                SpreadModel.SampleOffset(_rng, spread, out float yawOff, out float pitchOff);
                Vector3 dir = cam.rotation * (Quaternion.Euler(-pitchOff, yawOff, 0f) * Vector3.forward);

                Vector3 end = origin + dir * w.MaxRange;
                if (CombatQueries.Raycast(origin, dir, w.MaxRange, transform, out RaycastHit hit))
                {
                    end = hit.point;
                    if (Hurtbox.TryResolve(hit.collider, out IDamageable target, out HitZone zone, out float weak))
                    {
                        CombatServices.Resolver.Submit(new HitClaim
                        {
                            Attacker = gameObject,
                            Target = target,
                            Zone = zone,
                            WeakPointMultiplier = weak,
                            Origin = origin,
                            Point = hit.point,
                            WeaponId = slot.Definition.ContentId,
                            Request = DamageRequest.FromWeapon(w, zone, hit.distance, weak, _handling.Damage),
                        });
                    }
                }
                TracerPool.Instance.Spawn(muzzle, end);
                EventBus<ShotTraced>.Publish(new ShotTraced { Owner = gameObject, From = muzzle, To = end });
            }

            slot.Spread.OnShot();
            float recoilMult = Mathf.Lerp(1f, w.AimRecoilMultiplier, _aimBlend) * _handling.Recoil;
            if (_look != null) _look.AddRecoil(w.RecoilPitch * recoilMult, _rng.Range(-w.RecoilYaw, w.RecoilYaw) * recoilMult);
            if (_viewmodel != null) _viewmodel.Kick(recoilMult);
        }

        private void StartMelee(MeleeStats stats, double now, Slot slot)
        {
            slot.Firearm?.CancelReload();
            _activeMeleeBonus = _nextMeleeBonus;
            _nextMeleeBonus = null;
            if (_activeMeleeBonus.HasValue)
            {
                // A copy of the swing with a wider arc / more targets, so the shared stats are never changed.
                MeleeBonus b = _activeMeleeBonus.Value;
                _bonusMelee.Damage = stats.Damage;
                _bonusMelee.HeadMultiplier = stats.HeadMultiplier;
                _bonusMelee.Range = stats.Range;
                _bonusMelee.ArcDegrees = Mathf.Min(300f, stats.ArcDegrees * Mathf.Max(1f, b.ArcMultiplier));
                _bonusMelee.WindupSeconds = stats.WindupSeconds;
                _bonusMelee.RecoverySeconds = stats.RecoverySeconds;
                _bonusMelee.MaxTargets = stats.MaxTargets + Mathf.Max(0, b.ExtraTargets);
                _bonusMelee.MoveSpeedMultiplier = stats.MoveSpeedMultiplier;
                stats = _bonusMelee;
            }
            _activeMelee = stats;
            _meleePending = true;
            _meleeHitTime = now + stats.WindupSeconds;
            _meleeEndTime = now + Math.Max(stats.RecoverySeconds, stats.WindupSeconds);
            _aimBlend = 0f;
        }

        private void ResolveMelee()
        {
            _meleePending = false;
            if (_activeMelee == null) return;
            Transform cam = _look != null && _look.Camera != null ? _look.Camera.transform : transform;
            CombatQueries.MeleeArc(cam.position, cam.forward, _activeMelee, transform, _meleeTargets);
            string weaponId = CurrentDefinition != null && CurrentDefinition.Kind == WeaponKind.Melee ? CurrentDefinition.ContentId : "melee.quick";
            float outgoing = _handling.MeleeDamage * (_activeMeleeBonus.HasValue ? Mathf.Max(0f, _activeMeleeBonus.Value.DamageMultiplier) : 1f);
            bool stagger = _activeMeleeBonus.HasValue && _activeMeleeBonus.Value.Stagger;
            _activeMeleeBonus = null;
            for (int i = 0; i < _meleeTargets.Count; i++)
            {
                MeleeTarget t = _meleeTargets[i];
                CombatServices.Resolver.Submit(new HitClaim
                {
                    Attacker = gameObject,
                    Target = t.Target,
                    Zone = t.Zone,
                    WeakPointMultiplier = t.WeakPointMultiplier,
                    Origin = cam.position,
                    Point = t.Point,
                    WeaponId = weaponId,
                    Request = DamageRequest.FromMelee(_activeMelee, t.Zone, t.WeakPointMultiplier, outgoing),
                    IsMelee = true,
                    ForceStagger = stagger,
                    Knockback = _handling.MeleeKnockback,
                });
            }
            MeleeResolved?.Invoke(_meleeTargets.Count);
        }

        private void RefreshHandling(Slot slot)
        {
            _handling = WeaponHandling.Default;
            HandlingSource?.ModifyHandling(slot.Definition, ref _handling);
            if (slot.Firearm == null) return;
            slot.Firearm.FireRateMultiplier = _handling.FireRate;
            slot.Firearm.ReloadSpeedMultiplier = _handling.ReloadSpeed;
            slot.Firearm.EquipSpeedMultiplier = _handling.EquipSpeed;
            slot.Firearm.FreeReloads = _handling.FreeReloads;
        }

        /// <summary>Refills reserve ammo on all firearms (ammo crates, debug).</summary>
        public void RefillAllAmmo()
        {
            foreach (Slot s in _slots) s.Firearm?.AddReserve(int.MaxValue / 2);
        }
    }
}
