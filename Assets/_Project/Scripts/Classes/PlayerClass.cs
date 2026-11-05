using System;
using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Player;
using Deadfall.Rules.Classes;
using UnityEngine;

namespace Deadfall.Classes
{
    /// <summary>
    /// Runs the local player's class: Q activates the ability (cooldown and duration from <see cref="AbilityTimer"/>),
    /// passives tick every frame, and preferred-weapon handling plus ability and passive effects feed into
    /// <see cref="PlayerWeapons"/> through <see cref="IWeaponHandlingSource"/>.
    /// </summary>
    public sealed class PlayerClass : MonoBehaviour, IWeaponHandlingSource
    {
        private readonly ClassContext _ctx = new ClassContext();
        private readonly List<PassiveBehaviour> _passives = new List<PassiveBehaviour>();
        private PlayerInputReader _input;
        private AbilityTimer _timer;
        private AbilityBehaviour _ability;
        private readonly List<string> _doctrines = new List<string>(2);
        private readonly List<string> _rosterClasses = new List<string>(4);

        /// <summary>Cooldown speed with Combined Arms (placeholder, D-17).</summary>
        public const float CombinedArmsCooldownRate = 1.15f;

        /// <summary>The local player's class runner, or null.</summary>
        public static PlayerClass Local { get; private set; }

        /// <summary>Squad Doctrines this player has right now (at most two).</summary>
        public IReadOnlyList<string> ActiveDoctrines => _doctrines;
        public bool HasDoctrine(string id) => _doctrines.Contains(id);
        public event Action DoctrinesChanged;

        public ClassDefinition Class => _ctx.Class;
        public AbilityDefinition Ability => _ctx.Class != null ? _ctx.Class.Ability : null;
        /// <summary>False when the class's ability is not built yet.</summary>
        public bool HasAbility => _ability != null;
        public AbilityTimer Timer => _timer;
        public IReadOnlyList<PassiveBehaviour> Passives => _passives;
        /// <summary>Cooldown speed for the ability (doctrines and Field Mods later). 1 = normal.</summary>
        public float CooldownRate
        {
            get => _timer?.CooldownRate ?? 1f;
            set { if (_timer != null) _timer.CooldownRate = value; }
        }

        public event Action<ClassDefinition> ClassChanged;
        public event Action<AbilityDefinition> AbilityActivated;
        public event Action<AbilityDefinition> AbilityEnded;

        private void Awake()
        {
            _input = GetComponent<PlayerInputReader>();
            _ctx.Owner = this;
            _ctx.Player = gameObject;
            _ctx.Weapons = GetComponent<PlayerWeapons>();
            _ctx.Health = GetComponent<PlayerHealth>();
            _ctx.Motor = GetComponent<PlayerMotor>();
            _ctx.Look = GetComponent<PlayerLook>();
            if (_ctx.Weapons != null) _ctx.Weapons.HandlingSource = this;
            Local = this;
            ClassRoster.Changed += RefreshDoctrines;
        }

        private void OnDestroy()
        {
            ClassRoster.Changed -= RefreshDoctrines;
            if (Local == this) Local = null;
            Clear();
        }

        /// <summary>Cap on cooldown speed from Field Mods and skills (T-77): +75%. Combined Arms multiplies on top.</summary>
        public const float MaxExtraCooldownRate = 1.75f;
        private float _extraCooldownRate = 1f;

        /// <summary>Cooldown speed from Field Mods and skills, on top of doctrines (1 = none).</summary>
        public float ExtraCooldownRate
        {
            get => _extraCooldownRate;
            set { _extraCooldownRate = Mathf.Clamp(value, 0.1f, MaxExtraCooldownRate); ApplyCooldownRate(); }
        }

        private void ApplyCooldownRate()
        {
            if (_timer != null) _timer.CooldownRate = (HasDoctrine(PrototypeClasses.CombinedArms) ? CombinedArmsCooldownRate : 1f) * _extraCooldownRate;
        }

        /// <summary>Recomputes doctrines from everyone's classes (this player first).</summary>
        private void RefreshDoctrines()
        {
            _rosterClasses.Clear();
            string mine = _ctx.Class != null ? _ctx.Class.ContentId : null;
            _rosterClasses.Add(mine);
            ulong me = PlayerIdentity.Local != null ? PlayerIdentity.Local.ClientId : 0UL;
            foreach (KeyValuePair<ulong, string> kv in ClassRoster.All)
                if (kv.Key != me) _rosterClasses.Add(kv.Value);
            List<string>[] result = DoctrineResolver.Resolve(_rosterClasses, PrototypeClasses.Doctrines);
            bool changed = result[0].Count != _doctrines.Count;
            for (int i = 0; !changed && i < _doctrines.Count; i++) changed = _doctrines[i] != result[0][i];
            if (!changed) return;
            _doctrines.Clear();
            _doctrines.AddRange(result[0]);
            ApplyCooldownRate();
            DoctrinesChanged?.Invoke();
        }

        /// <summary>Switches class. The new ability starts ready.</summary>
        public void SetClass(ClassDefinition definition)
        {
            if (definition == _ctx.Class) return;
            Clear();
            _ctx.Class = definition;
            if (definition != null)
            {
                AbilityDefinition a = definition.Ability;
                _timer = new AbilityTimer(a != null ? a.CooldownSeconds : 0f, a != null ? a.DurationSeconds : 0f);
                _ability = ClassBehaviours.CreateAbility(a);
                _ability?.Bind(a, _ctx);
                foreach (PassiveDefinition p in definition.Passives)
                {
                    if (p == null) continue;
                    PassiveBehaviour b = ClassBehaviours.CreatePassive(p);
                    b.Bind(p, _ctx);
                    b.Attach();
                    _passives.Add(b);
                }
            }
            RefreshDoctrinesForced();
            ClassChanged?.Invoke(definition);
        }

        private void RefreshDoctrinesForced()
        {
            _doctrines.Clear();
            RefreshDoctrines();
            ApplyCooldownRate();
        }

        private void Clear()
        {
            if (_ability != null)
            {
                if (_timer != null && _timer.IsActive) _ability.OnEnd(Time.timeAsDouble);
                _ability.Detach();
            }
            foreach (PassiveBehaviour p in _passives) p.Detach();
            _passives.Clear();
            _ability = null;
            _timer = null;
            _ctx.Class = null;
        }

        /// <summary>Ends a running effect now (the shield broke). The cooldown starts from here.</summary>
        public void EndAbilityEarly()
        {
            if (_timer == null || !_timer.IsActive) return;
            double now = Time.timeAsDouble;
            AbilityEvents e = _timer.End(now);
            if ((e & AbilityEvents.Ended) != 0) EndEffect(now);
        }

        /// <summary>Uses the ability if it is ready. Returns true if it fired.</summary>
        public bool TryActivate()
        {
            if (_ability == null || _timer == null) return false;
            if (_ctx.Health != null && !_ctx.Health.IsAlive) return false;
            if (!_timer.IsReady || !_ability.CanActivate()) return false;
            double now = Time.timeAsDouble;
            if (!_timer.TryActivate(now)) return false;
            _ability.OnActivate(now);
            AbilityActivated?.Invoke(_ctx.Class.Ability);
            // Instant abilities have no active phase, so their "end" is now.
            if (!_timer.IsActive) _ability.OnEnd(now);
            return true;
        }

        private void Update()
        {
            if (_ctx.Class == null) return;
            double now = Time.timeAsDouble;

            if (_input != null && _input.Current.AbilityPressed) TryActivate();

            if (_timer != null && _ability != null)
            {
                AbilityEvents e = _timer.Tick(now);
                if ((e & AbilityEvents.Ended) != 0) EndEffect(now);
                _ability.Tick(now, _timer.IsActive);
            }
            for (int i = 0; i < _passives.Count; i++) _passives[i].Tick(now);
        }

        private void EndEffect(double now)
        {
            _ability.OnEnd(now);
            AbilityEnded?.Invoke(_ctx.Class.Ability);
        }

        public void ModifyHandling(WeaponDefinition weapon, ref WeaponHandling h)
        {
            ClassDefinition c = _ctx.Class;
            if (c == null || weapon == null) return;
            bool preferred = c.Prefers(weapon.Category);
            if (preferred)
            {
                h.ReloadSpeed *= c.PreferredReloadSpeed;
                h.EquipSpeed *= c.PreferredSwapSpeed;
                h.Recoil *= c.PreferredRecoil;
            }
            _ability?.ModifyHandling(weapon, preferred, _timer != null && _timer.IsActive, ref h);
            for (int i = 0; i < _passives.Count; i++) _passives[i].ModifyHandling(weapon, preferred, ref h);
        }
    }
}
