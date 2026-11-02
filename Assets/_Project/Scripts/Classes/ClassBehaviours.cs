using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Player;
using Deadfall.Rules.Classes;
using UnityEngine;

namespace Deadfall.Classes
{
    /// <summary>The player parts a class behaviour can use.</summary>
    public sealed class ClassContext
    {
        public PlayerClass Owner;
        public GameObject Player;
        public PlayerWeapons Weapons;
        public PlayerHealth Health;
        public PlayerMotor Motor;
        public PlayerLook Look;
        public ClassDefinition Class;
    }

    /// <summary>Code for one active ability. Numbers come from its <see cref="AbilityDefinition"/>.</summary>
    public abstract class AbilityBehaviour
    {
        protected AbilityDefinition Def { get; private set; }
        protected ClassContext Ctx { get; private set; }

        public void Bind(AbilityDefinition def, ClassContext ctx)
        {
            Def = def;
            Ctx = ctx;
        }

        /// <summary>Extra conditions beyond the cooldown (e.g. not while airborne).</summary>
        public virtual bool CanActivate() => true;
        public abstract void OnActivate(double now);
        /// <summary>The effect ended (duration ran out, or the ability ended itself early).</summary>
        public virtual void OnEnd(double now) { }
        public virtual void Tick(double now, bool active) { }
        public virtual void ModifyHandling(WeaponDefinition weapon, bool preferred, bool active, ref WeaponHandling h) { }
        /// <summary>The class is being removed: undo everything.</summary>
        public virtual void Detach() { }
    }

    /// <summary>Code for one passive. Numbers come from its <see cref="PassiveDefinition"/>.</summary>
    public abstract class PassiveBehaviour
    {
        protected PassiveDefinition Def { get; private set; }
        protected ClassContext Ctx { get; private set; }

        public void Bind(PassiveDefinition def, ClassContext ctx)
        {
            Def = def;
            Ctx = ctx;
        }

        /// <summary>False for passives whose system doesn't exist yet (Scrap, grenades); the HUD says so.</summary>
        public virtual bool Implemented => true;
        public virtual void Attach() { }
        public virtual void Detach() { }
        public virtual void Tick(double now) { }
        public virtual void ModifyHandling(WeaponDefinition weapon, bool preferred, ref WeaponHandling h) { }
    }

    /// <summary>Maps behaviour ids from definitions to code.</summary>
    public static class ClassBehaviours
    {
        public static AbilityBehaviour CreateAbility(AbilityDefinition def)
        {
            switch (def != null ? def.BehaviourId : null)
            {
                case "overdrive": return new OverdriveAbility();
                case "bulwark_shield": return new BulwarkShieldAbility();
                case "lunge": return new LungeAbility();
                case "deploy_sentry": return new DeploySentryAbility();
                case "cluster_charge": return new ClusterChargeAbility();
                default: return null; // no code for this id yet
            }
        }

        public static PassiveBehaviour CreatePassive(PassiveDefinition def)
        {
            switch (def != null ? def.BehaviourId : null)
            {
                case "reserve_capacity": return new ReserveCapacityPassive();
                case "steady_hands": return new SteadyHandsPassive();
                case "guard": return new GuardPassive();
                case "bloodlust": return new BloodlustPassive();
                case "combo_cleave": return new ComboCleavePassive();
                case "melee_knockback": return new ShovePassive();
                case "self_blast_reduction": return new HostAppliedPassive();
                default: return new PendingPassive();
            }
        }
    }

    // ------------------------------------------------------------------ abilities

    /// <summary>Gunner. Magnitude: fire rate bonus (0.4 = +40%). Preferred weapons reload without using reserve.</summary>
    public sealed class OverdriveAbility : AbilityBehaviour
    {
        public override void OnActivate(double now) { }

        public override void ModifyHandling(WeaponDefinition weapon, bool preferred, bool active, ref WeaponHandling h)
        {
            if (!active || !preferred) return;
            h.FireRate *= 1f + Def.Magnitude;
            h.FreeReloads = true;
        }
    }

    /// <summary>
    /// Tank. Plants the shield 2 m ahead. The shield's numbers (Magnitude: health, Range: aggro radius, Secondary:
    /// width) are read where it is created, on the host. It removes itself when its time is up or it breaks, which
    /// ends the effect early (see ClassNetwork.NotifyGone).
    /// </summary>
    public sealed class BulwarkShieldAbility : AbilityBehaviour
    {
        public override bool CanActivate() => Ctx.Motor == null || Ctx.Motor.IsGrounded;

        public override void OnActivate(double now)
        {
            Vector3 forward = FlatForward(Ctx.Player.transform);
            ClassNetwork.RequestSpawn(SpawnKind.Shield, Ctx.Player.transform.position + forward * 2f, forward);
        }

        internal static Vector3 FlatForward(Transform t)
        {
            Vector3 f = t.forward;
            f.y = 0f;
            return f.sqrMagnitude > 0.001f ? f.normalized : Vector3.forward;
        }
    }

    /// <summary>
    /// Engineer. Places the sentry on the ground 1.6 m ahead. Its numbers (Magnitude: damage per shot, Secondary:
    /// shots per second, Range: range, duration: lifetime) are read where it is created.
    /// </summary>
    public sealed class DeploySentryAbility : AbilityBehaviour
    {
        public override bool CanActivate() => (Ctx.Motor == null || Ctx.Motor.IsGrounded) && FindSpot(out _);

        public override void OnActivate(double now)
        {
            if (FindSpot(out Vector3 spot))
                ClassNetwork.RequestSpawn(SpawnKind.Sentry, spot, BulwarkShieldAbility.FlatForward(Ctx.Player.transform));
        }

        private bool FindSpot(out Vector3 spot)
        {
            Vector3 forward = BulwarkShieldAbility.FlatForward(Ctx.Player.transform);
            Vector3 from = Ctx.Player.transform.position + forward * 1.6f + Vector3.up * 1.5f;
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 4f, CombatQueries.HitMask, QueryTriggerInteraction.Ignore))
            {
                spot = hit.point;
                return true;
            }
            spot = default;
            return false;
        }
    }

    /// <summary>
    /// Demolitionist. Throws the charge from the eye along the aim. Its numbers (Magnitude: bomblets, Secondary:
    /// main blast damage, Range: main blast radius) are read where it is created.
    /// </summary>
    public sealed class ClusterChargeAbility : AbilityBehaviour
    {
        public override void OnActivate(double now)
        {
            Transform eye = Ctx.Look != null && Ctx.Look.Camera != null ? Ctx.Look.Camera.transform : Ctx.Player.transform;
            ClassNetwork.RequestSpawn(SpawnKind.ClusterCharge, eye.position + eye.forward * 0.6f, eye.forward);
        }
    }

    /// <summary>
    /// Warrior. Range: dash distance. Magnitude: damage multiplier of the strike. The strike swings automatically at
    /// the end of the dash and staggers whatever it hits; if a swing is already under way it waits for the next one.
    /// </summary>
    public sealed class LungeAbility : AbilityBehaviour
    {
        private const string BonusTag = "lunge";
        private const float DashSeconds = 0.22f;
        private const float BonusLifetime = 3f;
        private bool _dashing;
        private double _bonusExpires;

        public override void OnActivate(double now)
        {
            Ctx.Weapons.SetNextMeleeBonus(new MeleeBonus
            {
                DamageMultiplier = Def.Magnitude, Stagger = true, ArcMultiplier = 1.3f, ExtraTargets = 1, Tag = BonusTag,
            });
            _bonusExpires = now + BonusLifetime;
            if (Ctx.Motor != null)
            {
                Ctx.Motor.DashEnded -= OnDashEnded;
                Ctx.Motor.DashEnded += OnDashEnded;
                // Vanguard doctrine (with a Tank in the squad): a longer lunge.
                float range = Def.Range * (Ctx.Owner.HasDoctrine(PrototypeClasses.Vanguard) ? 1.25f : 1f);
                Ctx.Motor.Dash(Ctx.Player.transform.forward, range, DashSeconds);
                _dashing = true;
            }
            else
            {
                Ctx.Weapons.TryMeleeNow();
            }
        }

        private void OnDashEnded()
        {
            Ctx.Motor.DashEnded -= OnDashEnded;
            _dashing = false;
            Ctx.Weapons.TryMeleeNow();
        }

        public override void Tick(double now, bool active)
        {
            if (!_dashing && now > _bonusExpires) Ctx.Weapons.ClearMeleeBonus(BonusTag);
        }

        public override void Detach()
        {
            if (Ctx.Motor != null)
            {
                Ctx.Motor.DashEnded -= OnDashEnded;
                if (_dashing) Ctx.Motor.CancelDash();
            }
            _dashing = false;
            Ctx.Weapons.ClearMeleeBonus(BonusTag);
        }
    }

    // ------------------------------------------------------------------ passives

    /// <summary>Gunner. Magnitude: extra reserve capacity (0.25 = +25%).</summary>
    public sealed class ReserveCapacityPassive : PassiveBehaviour
    {
        public override void Attach()
        {
            Ctx.Weapons.LoadoutBuilt += Apply;
            Apply();
        }

        public override void Detach()
        {
            Ctx.Weapons.LoadoutBuilt -= Apply;
            Ctx.Weapons.SetReserveCapacity(1f, false);
        }

        private void Apply() => Ctx.Weapons.SetReserveCapacity(1f + Def.Magnitude, true);
    }

    /// <summary>Gunner. Magnitude: recoil reduction per second of sustained fire. Secondary: lowest recoil multiplier.</summary>
    public sealed class SteadyHandsPassive : PassiveBehaviour
    {
        public override void ModifyHandling(WeaponDefinition weapon, bool preferred, ref WeaponHandling h) =>
            h.Recoil *= ClassRules.SustainedRecoil(Ctx.Weapons.SustainedFireSeconds, Def.Magnitude, Def.Secondary);
    }

    /// <summary>Tank. Magnitude: damage reduction. Secondary: health fraction it applies above.</summary>
    public sealed class GuardPassive : PassiveBehaviour
    {
        public override void Tick(double now)
        {
            if (Ctx.Health != null)
                Ctx.Health.BonusDamageReduction = ClassRules.GuardReduction(Ctx.Health.Normalized, Def.Secondary, Def.Magnitude);
        }

        public override void Detach()
        {
            if (Ctx.Health != null) Ctx.Health.BonusDamageReduction = 0f;
        }
    }

    /// <summary>Warrior. Magnitude: health restored per melee kill.</summary>
    public sealed class BloodlustPassive : PassiveBehaviour
    {
        public override void Attach() => EventBus<HitConfirmed>.Subscribe(OnHit);
        public override void Detach() => EventBus<HitConfirmed>.Unsubscribe(OnHit);

        private void OnHit(HitConfirmed e)
        {
            if (e.Attacker == Ctx.Player && e.Killed && e.IsMelee && Ctx.Health != null && Ctx.Health.IsAlive)
                Ctx.Health.Heal(Def.Magnitude);
        }
    }

    /// <summary>
    /// Warrior. Third melee hit in a chain cleaves. Magnitude: chain window in seconds. Secondary: arc multiplier.
    /// Tertiary: extra targets.
    /// </summary>
    public sealed class ComboCleavePassive : PassiveBehaviour
    {
        private const string BonusTag = "combo";
        private MeleeCombo _combo;
        private double _finisherUntil = double.NegativeInfinity;

        public override void Attach()
        {
            _combo = new MeleeCombo(Def.Magnitude > 0f ? Def.Magnitude : 1.2f, 3);
            Ctx.Weapons.MeleeResolved += OnMelee;
        }

        public override void Detach()
        {
            Ctx.Weapons.MeleeResolved -= OnMelee;
            Ctx.Weapons.ClearMeleeBonus(BonusTag);
        }

        /// <summary>Hits in the current chain, for the HUD.</summary>
        public int ChainCount => _combo?.Count ?? 0;

        private void OnMelee(int hits)
        {
            if (hits <= 0) return;
            double now = Time.timeAsDouble;
            _combo.Register(now);
            if (_combo.NextIsFinisher(now) && !Ctx.Weapons.HasMeleeBonus)
            {
                Ctx.Weapons.SetNextMeleeBonus(new MeleeBonus
                {
                    DamageMultiplier = 1f,
                    ArcMultiplier = Def.Secondary > 0f ? Def.Secondary : 2.2f,
                    ExtraTargets = Mathf.RoundToInt(Def.Tertiary),
                    Tag = BonusTag,
                });
                _finisherUntil = now + _combo.WindowSeconds;
            }
        }

        public override void Tick(double now)
        {
            if (now > _finisherUntil) Ctx.Weapons.ClearMeleeBonus(BonusTag);
        }
    }

    /// <summary>Tank. Magnitude: metres melee hits push enemies back.</summary>
    public sealed class ShovePassive : PassiveBehaviour
    {
        public override void ModifyHandling(WeaponDefinition weapon, bool preferred, ref WeaponHandling h) =>
            h.MeleeKnockback = Mathf.Max(h.MeleeKnockback, Def.Magnitude);
    }

    /// <summary>A passive the host applies from the class roster (Blast Padding); nothing to do on the player.</summary>
    public sealed class HostAppliedPassive : PassiveBehaviour
    {
    }

    /// <summary>A passive whose game system arrives in a later milestone (Scrap, grenades, structures).</summary>
    public sealed class PendingPassive : PassiveBehaviour
    {
        public override bool Implemented => false;
    }
}
