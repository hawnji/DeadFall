using System;
using System.Collections.Generic;
using Deadfall.Classes;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Player;
using Deadfall.Rules.Common;
using Deadfall.Rules.Progression;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// The local player's Field Mods for this deployment (GAME_DESIGN §9.1, D-13: picks are individual). Offers
    /// queue up after surges; the player picks one of three with T. Effects feed weapon handling, health, damage
    /// reduction, move speed, reserve ammo and ability cooldowns. Damage bonuses ride the capped outgoing
    /// multiplier in co-op claims, so they need no extra sync.
    /// </summary>
    public sealed class FieldModRunner : MonoBehaviour, IWeaponHandlingSource
    {
        public const float LowHealthThreshold = 0.3f;
        public const float BurstSeconds = 2f;

        public static readonly List<FieldModDef> Catalog = FieldModCatalog.Prototype();

        private readonly FieldModInventory _inventory = new FieldModInventory(Catalog);
        private readonly float[] _totals = new float[Enum.GetValues(typeof(ModStat)).Length];
        private readonly DeterministicRandom _rng = new DeterministicRandom((ulong)Environment.TickCount ^ 0xF1E1DUL);
        private PlayerWeapons _weapons;
        private PlayerHealth _health;
        private PlayerStatus _status;
        private PlayerClass _class;
        private List<FieldModDef> _offer;
        private double _burstUntil;

        public static FieldModRunner Local { get; private set; }
        public FieldModInventory Inventory => _inventory;
        public int PendingOffers { get; private set; }
        public int OfferSurge { get; private set; }
        public event Action<FieldModDef> Picked;

        private void Awake()
        {
            Local = this;
            _weapons = GetComponent<PlayerWeapons>();
            _health = GetComponent<PlayerHealth>();
            _status = GetComponent<PlayerStatus>();
            _class = GetComponent<PlayerClass>();
            if (_weapons != null) _weapons.AddHandlingSource(this);
            EventBus<HitConfirmed>.Subscribe(OnHit);
        }

        private void OnDestroy()
        {
            if (Local == this) Local = null;
            if (_weapons != null) _weapons.RemoveHandlingSource(this);
            EventBus<HitConfirmed>.Unsubscribe(OnHit);
        }

        public float Total(ModStat s) => _totals[(int)s];

        /// <summary>Deployment over or starting: every mod and pending offer goes.</summary>
        public void ResetRun()
        {
            _inventory.Clear();
            PendingOffers = 0;
            _offer = null;
            Apply();
        }

        public void QueueOffer(int surge)
        {
            PendingOffers++;
            OfferSurge = surge;
        }

        /// <summary>The current three choices (rolled once, kept until one is picked).</summary>
        public IReadOnlyList<FieldModDef> CurrentOffer()
        {
            if (PendingOffers <= 0) return null;
            if (_offer == null)
            {
                string classId = _class != null && _class.Class != null ? _class.Class.ContentId : null;
                _offer = FieldModOffers.Roll(_rng, Catalog, _inventory, classId, OfferSurge);
                if (_offer.Count == 0) { PendingOffers = 0; _offer = null; }
            }
            return _offer;
        }

        public void Pick(int index)
        {
            IReadOnlyList<FieldModDef> offer = CurrentOffer();
            if (offer == null || index < 0 || index >= offer.Count) return;
            FieldModDef gained = _inventory.Add(offer[index]);
            PendingOffers = Mathf.Max(0, PendingOffers - 1);
            _offer = null;
            Apply();
            if (gained != null)
            {
                bool evolved = gained != offer[index];
                RunToasts.Show(evolved ? $"{offer[index].DisplayName} evolved into {gained.DisplayName}!" : $"Field Mod: {gained.DisplayName}", new Color(1f, 0.85f, 0.4f));
                Picked?.Invoke(gained);
            }
        }

        /// <summary>A surge was cleared (Second Wind).</summary>
        public void OnSurgeCleared()
        {
            float heal = Total(ModStat.HealOnSurgeClear);
            if (heal > 0f && _health != null && _health.IsAlive) _health.Heal(heal);
        }

        private void Apply()
        {
            foreach (ModStat s in (ModStat[])Enum.GetValues(typeof(ModStat))) _totals[(int)s] = _inventory.Total(s);
            if (_health != null)
            {
                _health.SetBonusMaxHealth(Total(ModStat.MaxHealth));
                _health.ModDamageReduction = Total(ModStat.DamageReduction);
            }
            if (_status != null) _status.BonusMoveMultiplier = 1f + Total(ModStat.MoveSpeed);
            if (_weapons != null) _weapons.SetReserveCapacity("mods", 1f + Total(ModStat.Reserve), false);
            if (_class != null) _class.ExtraCooldownRate = 1f + Total(ModStat.CooldownRate);
        }

        private void OnHit(HitConfirmed e)
        {
            if (e.Attacker != gameObject || !e.Killed) return;
            float heal = Total(ModStat.HealOnKill);
            if (heal > 0f && _health != null && _health.IsAlive) _health.Heal(heal);
            if (Total(ModStat.KillFireRateBurst) > 0f) _burstUntil = Time.timeAsDouble + BurstSeconds;
        }

        public void ModifyHandling(WeaponDefinition weapon, ref WeaponHandling h)
        {
            float low = _health != null && _health.IsAlive && _health.Normalized < LowHealthThreshold ? Total(ModStat.LowHealthDamage) : 0f;
            h.Damage *= 1f + Total(ModStat.Damage) + low;
            h.MeleeDamage *= 1f + Total(ModStat.MeleeDamage) + low;
            h.ReloadSpeed *= 1f + Total(ModStat.ReloadSpeed);
            h.Recoil *= Mathf.Max(0.3f, 1f + Total(ModStat.Recoil));
            float fire = 0f;
            if (Time.timeAsDouble < _burstUntil) fire += Total(ModStat.KillFireRateBurst);
            if (weapon != null && _class != null && _class.Class != null && _class.Class.Prefers(weapon.Category)) fire += Total(ModStat.PreferredFireRate);
            h.FireRate *= 1f + fire;
        }
    }
}
