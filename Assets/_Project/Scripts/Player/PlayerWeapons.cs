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
            public WeaponMods Mods;
            /// <summary>Firearm stats after tier and attachments (a copy; the definition is shared).</summary>
            public WeaponStats Stats;
            public float DamageMultiplier = 1f;
            public WeaponStateMachine Firearm;
            public SpreadModel Spread;
            /// <summary>Magazine size before the explosive bonus (Bandolier).</summary>
            public int BaseMagazine;
            public bool IsMelee => Definition.Kind == WeaponKind.Melee;
            public bool IsExplosive => Stats != null && Stats.ExplosionRadius > 0f;
        }

        /// <summary>A weapon and its modifications, for loadouts and pickups.</summary>
        public struct LoadoutEntry
        {
            public WeaponDefinition Definition;
            public WeaponMods Mods;
            public LoadoutEntry(WeaponDefinition definition, WeaponMods mods = null) { Definition = definition; Mods = mods; }
        }

        [SerializeField] private List<WeaponDefinition> _loadout = new List<WeaponDefinition>();
        private List<WeaponMods> _loadoutMods = new List<WeaponMods>();
        private readonly List<IWeaponHandlingSource> _extraSources = new List<IWeaponHandlingSource>();
        private readonly Dictionary<string, float> _reserveMultipliers = new Dictionary<string, float>();
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
        public void SetReserveCapacity(float multiplier, bool refill) => SetReserveCapacity("class", multiplier, refill);

        /// <summary>Reserve capacity from several sources (class passive, Field Mods); they multiply.</summary>
        public void SetReserveCapacity(string key, float multiplier, bool refill)
        {
            _reserveMultipliers[key ?? string.Empty] = UnityEngine.Mathf.Max(0f, multiplier);
            ApplyReserveCaps(refill);
        }

        private float ReserveMultiplier
        {
            get
            {
                float m = 1f;
                foreach (float v in _reserveMultipliers.Values) m *= v;
                return m;
            }
        }

        private void ApplyReserveCaps(bool refill)
        {
            float mult = ReserveMultiplier;
            foreach (Slot s in _slots)
                s.Firearm?.SetMaxReserve(UnityEngine.Mathf.RoundToInt(s.Stats.MaxReserve * mult) + (s.IsExplosive ? _explosiveBonus * ExplosiveReservePerBonus : 0), refill);
        }

        private int _explosiveBonus;
        /// <summary>Reserve rounds an explosive weapon gains for each extra magazine round (Bandolier).</summary>
        public const int ExplosiveReservePerBonus = 3;

        /// <summary>Extra rounds per magazine for explosive weapons (Demolitionist Bandolier); reserve grows 3x as much.</summary>
        public int ExplosiveBonus => _explosiveBonus;

        /// <summary>Sets the explosive-weapon bonus (0 = none). <paramref name="refill"/> tops up those weapons.</summary>
        public void SetExplosiveBonus(int extra, bool refill)
        {
            _explosiveBonus = Mathf.Max(0, extra);
            foreach (Slot s in _slots) ApplyMagazineBonus(s);
            ApplyReserveCaps(refill);
        }

        private void ApplyMagazineBonus(Slot s)
        {
            if (s.Stats == null) return;
            s.Stats.MagazineSize = s.BaseMagazine + (s.IsExplosive ? _explosiveBonus : 0);
        }

        /// <summary>More handling modifiers on top of <see cref="HandlingSource"/> (Field Mods).</summary>
        public void AddHandlingSource(IWeaponHandlingSource source)
        {
            if (source != null && !_extraSources.Contains(source)) _extraSources.Add(source);
        }

        public void RemoveHandlingSource(IWeaponHandlingSource source) => _extraSources.Remove(source);

        /// <summary>Raised when a slot's weapon changes (pickups, purchases, loadout resets).</summary>
        public event Action LoadoutChanged;

        public WeaponDefinition DefinitionAt(int index) => index >= 0 && index < _slots.Count ? _slots[index].Definition : null;
        public WeaponMods ModsAt(int index) => index >= 0 && index < _slots.Count ? _slots[index].Mods : null;
        public WeaponMods CurrentMods => ModsAt(_current);
        public WeaponStats CurrentStats => _current >= 0 ? _slots[_current].Stats : null;

        /// <summary>
        /// Which slot a new weapon goes in (D-07: two primaries, a sidearm, a melee weapon): pistols to the sidearm
        /// slot, melee to the melee slot, anything else replaces the primary in hand (or the first primary).
        /// </summary>
        public int SlotFor(WeaponDefinition def)
        {
            if (def == null || _slots.Count == 0) return -1;
            for (int i = 0; i < _slots.Count; i++) if (_slots[i].Definition == def) return i; // same weapon: upgrade in place
            if (def.Category == WeaponCategory.Pistol || def.Category == WeaponCategory.Melee)
            {
                for (int i = 0; i < _slots.Count; i++) if (_slots[i].Definition.Category == def.Category) return i;
            }
            bool currentIsPrimary = _current >= 0 && _slots[_current].Definition.Category != WeaponCategory.Pistol && !_slots[_current].IsMelee;
            if (currentIsPrimary) return _current;
            for (int i = 0; i < _slots.Count; i++)
                if (_slots[i].Definition.Category != WeaponCategory.Pistol && !_slots[i].IsMelee) return i;
            return 0;
        }

        /// <summary>Puts a weapon in a slot (full ammo) and equips it.</summary>
        public void ReplaceSlot(int index, WeaponDefinition def, WeaponMods mods)
        {
            if (def == null || index < 0 || index >= _slots.Count) return;
            _slots[index] = MakeSlot(def, mods);
            if (index < _loadout.Count) { _loadout[index] = def; while (_loadoutMods.Count <= index) _loadoutMods.Add(null); _loadoutMods[index] = mods; }
            ApplyReserveCaps(true);
            _current = -1;
            Equip(index);
            LoadoutChanged?.Invoke();
        }

        /// <summary>Replaces the whole loadout at runtime (run start, class change).</summary>
        public void ResetLoadout(IList<LoadoutEntry> entries)
        {
            _loadout = new List<WeaponDefinition>();
            _loadoutMods = new List<WeaponMods>();
            foreach (LoadoutEntry e in entries) { _loadout.Add(e.Definition); _loadoutMods.Add(e.Mods); }
            if (isActiveAndEnabled) BuildSlots();
            LoadoutChanged?.Invoke();
        }

        private Slot MakeSlot(WeaponDefinition def, WeaponMods mods)
        {
            var slot = new Slot { Definition = def, Mods = mods ?? WeaponMods.Standard() };
            if (def.Kind == WeaponKind.Firearm)
            {
                slot.Stats = WeaponModder.Apply(def.Firearm, slot.Mods, out slot.DamageMultiplier);
                slot.BaseMagazine = slot.Stats.MagazineSize;
                ApplyMagazineBonus(slot);
                slot.Firearm = WeaponStateMachine.CreateFull(slot.Stats);
                slot.Spread = new SpreadModel(slot.Stats);
            }
            else
            {
                slot.DamageMultiplier = WeaponModder.TierDamage(slot.Mods.Tier) * Mathf.Clamp(slot.Mods.DamageRoll, 0.9f, 1.15f);
            }
            return slot;
        }

        public int SlotCount => _slots.Count;
        public int CurrentIndex => _current;
        public WeaponDefinition CurrentDefinition => _current >= 0 ? _slots[_current].Definition : null;
        public int Magazine => CurrentFirearm?.Magazine ?? 0;
        public int Reserve => CurrentFirearm?.Reserve ?? 0;
        /// <summary>Magazine capacity of the current firearm with its mods (0 for melee).</summary>
        public int MagazineCapacity => CurrentFirearm?.Capacity ?? 0;
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
            for (int i = 0; i < _loadout.Count; i++)
            {
                WeaponDefinition def = _loadout[i];
                if (def == null) continue;
                _slots.Add(MakeSlot(def, i < _loadoutMods.Count ? _loadoutMods[i] : null));
            }
            ApplyReserveCaps(true);
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
                _equipEnd = now + slot.Stats.EquipSeconds / _handling.EquipSpeed;
                slot.Spread.Reset();
                if (_look != null) _look.AimFovMultiplier = WeaponSights.AimFov(slot.Definition, slot.Mods, slot.Stats.AimFovMultiplier);
            }
            else
            {
                _equipEnd = now + _meleeEquipSeconds / _handling.EquipSpeed;
                if (_look != null) _look.AimFovMultiplier = 1f;
            }
            if (_viewmodel != null) _viewmodel.Show(slot.Definition, slot.Mods);
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

            bool downed = _status != null && _status.IsDowned;
            if (downed)
            {
                // Downed (D-01): sidearm only.
                int side = SidearmIndex();
                if (side >= 0 && side != _current) Equip(side);
            }
            else HandleSwap(f);
            slot = _slots[_current];
            RefreshHandling(slot);
            bool equipping = now < _equipEnd;

            // Sprint → fire delay: weapons need a moment to come up after sprinting.
            if (_motor != null && _motor.IsSprinting) _lastSprintTime = now;
            float sprintToFire = slot.Firearm != null ? slot.Stats.SprintToFireSeconds : 0.1f;
            bool sprintRecovered = now >= _lastSprintTime + sprintToFire;

            // Aim
            bool canAim = slot.Firearm != null && !IsMeleeing && !equipping;
            float aimTime = slot.Firearm != null ? Mathf.Max(0.01f, slot.Stats.AimInSeconds) : 0.2f;
            _aimBlend = Mathf.MoveTowards(_aimBlend, canAim && f.AimHeld ? 1f : 0f, dt / aimTime);

            bool fireIntent = f.FireHeld || f.FirePressed;

            // Melee (quick melee from any weapon, or the primary attack of a melee weapon)
            if (!IsMeleeing && !equipping && !downed)
            {
                if (f.MeleePressed) StartMelee(slot.IsMelee ? slot.Definition.Melee : _quickMelee, now, slot);
                else if (slot.IsMelee && fireIntent && sprintRecovered) StartMelee(slot.Definition.Melee, now, slot);
            }
            if (_meleePending && now >= _meleeHitTime) ResolveMelee();

            // Reload
            if (slot.Firearm != null && f.ReloadPressed && !IsMeleeing && slot.Firearm.StartReload(now)) GameAudio.Play2D(SfxId.Reload);

            // Fire
            int shots = 0;
            if (slot.Firearm != null)
            {
                bool allowed = !IsMeleeing && sprintRecovered && (_motor == null || !_motor.IsSprinting);
                FireResult result = allowed ? slot.Firearm.TryFire(now, f.FirePressed, f.FireHeld) : slot.Firearm.TryFire(now, false, false);
                shots = result.ShotsFired;
                if (result.DryFire)
                {
                    if (slot.Firearm.CanReload && slot.Firearm.StartReload(now)) GameAudio.Play2D(SfxId.Reload);
                    else if (f.FirePressed) GameAudio.Play2D(SfxId.DryFire);
                }
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
                    _look.RecoverRecoil(slot.Stats.RecoilRecoveryPerSecond, dt);
            }
            else
            {
                CurrentSpread = 0f;
            }

            if (_status != null)
            {
                _status.AimBlend = _aimBlend;
                _status.SprintBlocked = _aimBlend > 0.05f || fireIntent || IsMeleeing;
                float weaponMove = slot.Firearm != null ? slot.Stats.MoveSpeedMultiplier : slot.Definition.Melee.MoveSpeedMultiplier;
                _status.WeaponMoveMultiplier = weaponMove;
            }

            if (_viewmodel != null)
            {
                _viewmodel.SetState(_aimBlend, EquipProgress, ReloadProgress, MeleeProgress, IsMeleeing,
                    _motor != null && _motor.IsSprinting, _motor != null ? _motor.MoveFactor : 0f);
            }
        }

        /// <summary>The pistol slot (first one), else the first firearm, else -1.</summary>
        public int SidearmIndex()
        {
            int firearm = -1;
            for (int i = 0; i < _slots.Count; i++)
            {
                WeaponDefinition d = _slots[i].Definition;
                if (d == null || _slots[i].Firearm == null) continue;
                if (d.Category == WeaponCategory.Pistol) return i;
                if (firearm < 0) firearm = i;
            }
            return firearm;
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
            WeaponStats w = slot.Stats;
            Transform cam = _look != null && _look.Camera != null ? _look.Camera.transform : transform;
            Vector3 origin = cam.position;
            float spread = slot.Spread.CurrentHalfAngle(_aimBlend, _motor != null ? _motor.MoveFactor : 0f, _motor != null && !_motor.IsGrounded);
            Vector3 muzzle = _viewmodel != null ? _viewmodel.MuzzlePosition : origin + cam.forward * 0.5f;

            if (w.IsProjectile)
            {
                // Launchers: one projectile per shot, exploding on impact (damage resolved where authoritative).
                SpreadModel.SampleOffset(_rng, spread, out float yo, out float po);
                Vector3 pdir = cam.rotation * (Quaternion.Euler(-po, yo, 0f) * Vector3.forward);
                ProjectileServices.Current.Launch(gameObject, slot.Definition.ContentId, origin + pdir * 0.7f, pdir * w.ProjectileSpeed,
                    w.Damage * _handling.Damage * slot.DamageMultiplier, w.ExplosionRadius);
                slot.Spread.OnShot();
                float pr = Mathf.Lerp(1f, w.AimRecoilMultiplier, _aimBlend) * _handling.Recoil;
                if (_look != null) _look.AddRecoil(w.RecoilPitch * pr, _rng.Range(-w.RecoilYaw, w.RecoilYaw) * pr);
                if (_viewmodel != null) _viewmodel.Kick(pr);
                return;
            }

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
                            Request = DamageRequest.FromWeapon(w, zone, hit.distance, weak, _handling.Damage * slot.DamageMultiplier),
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
            GameAudio.Play2D(SfxId.MeleeSwing, 1f, stats != null && stats.Damage > 60f ? 0.8f : 1f);
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
            float tierMult = CurrentDefinition != null && CurrentDefinition.Kind == WeaponKind.Melee && _current >= 0 ? _slots[_current].DamageMultiplier : 1f;
            float outgoing = tierMult * _handling.MeleeDamage * (_activeMeleeBonus.HasValue ? Mathf.Max(0f, _activeMeleeBonus.Value.DamageMultiplier) : 1f);
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
            for (int i = 0; i < _extraSources.Count; i++) _extraSources[i].ModifyHandling(slot.Definition, ref _handling);
            if (slot.Firearm == null) return;
            slot.Firearm.FireRateMultiplier = _handling.FireRate;
            slot.Firearm.ReloadSpeedMultiplier = _handling.ReloadSpeed;
            slot.Firearm.EquipSpeedMultiplier = _handling.EquipSpeed;
            slot.Firearm.FreeReloads = _handling.FreeReloads;
        }

        /// <summary>Refills reserve ammo on all firearms (ammo crates, debug).</summary>
        public void RefillAllAmmo() => RefillAllAmmo(true);

        /// <summary>Fills every reserve; <paramref name="grenades"/> false leaves grenades alone (Supply Cache sells them apart).</summary>
        public void RefillAllAmmo(bool grenades)
        {
            foreach (Slot s in _slots) s.Firearm?.AddReserve(int.MaxValue / 2);
            if (grenades) AmmoRefilled?.Invoke();
        }

        /// <summary>Full magazines, full reserves and full grenades (a game mode starting).</summary>
        /// <summary>Aimed in through a magnified scope (the HUD draws the scope view instead of the crosshair).</summary>
        public bool ScopeView => _viewmodel != null && _viewmodel.ScopeActive;

        public void RestockAll()
        {
            foreach (Slot s in _slots) s.Firearm?.FillMagazine();
            RefillAllAmmo();
        }

        /// <summary>Raised by <see cref="RefillAllAmmo"/> (ammo stations, caches, Max Ammo): grenades refill too.</summary>
        public event System.Action AmmoRefilled;
    }
}
