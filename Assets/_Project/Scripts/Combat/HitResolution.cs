using Deadfall.Core;
using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Combat
{
    /// <summary>
    /// What an attacker claims it hit. In single player the local resolver applies it directly; in
    /// multiplayer the claim is sent to the host, which validates and resolves it. Weapons
    /// only ever produce claims, so the networking swap does not touch weapon code.
    /// </summary>
    public struct HitClaim
    {
        public GameObject Attacker;
        public IDamageable Target;
        public HitZone Zone;
        public float WeakPointMultiplier;
        public Vector3 Origin;
        public Vector3 Point;
        public string WeaponId;
        /// <summary>The damage request built from the attacker's weapon and modifiers.</summary>
        public DamageRequest Request;
    }

    public interface IHitResolver
    {
        void Submit(in HitClaim claim);
    }

    /// <summary>What happened when a claim was resolved.</summary>
    public struct HitOutcome
    {
        public bool Applied;
        public float Amount;
        public bool IsCritical;
        public bool Killed;
    }

    /// <summary>
    /// Authority for offline play and for the host: validates basic sanity, computes damage, applies it,
    /// raises events. The host also uses <see cref="Apply"/> for claims that arrive from remote players.
    /// </summary>
    public sealed class LocalHitResolver : IHitResolver
    {
        public void Submit(in HitClaim claim)
        {
            HitOutcome outcome = Apply(claim);
            if (!outcome.Applied) return;
            EventBus<HitConfirmed>.Publish(new HitConfirmed
            {
                Attacker = claim.Attacker,
                Amount = outcome.Amount,
                IsCritical = outcome.IsCritical,
                Killed = outcome.Killed,
                Point = claim.Point,
            });
        }

        /// <summary>
        /// Validates and applies a claim and raises <see cref="DamageableKilled"/>, but not
        /// <see cref="HitConfirmed"/>: that belongs to whichever machine the attacker plays on.
        /// </summary>
        public static HitOutcome Apply(in HitClaim claim)
        {
            if (claim.Target == null || !claim.Target.IsAlive) return default;

            float distance = Vector3.Distance(claim.Origin, claim.Point);
            if (distance > claim.Request.MaxRange + 1f) return default; // reject impossible claims

            DamageRequest request = new DamageRequest(
                claim.Request.BaseDamage, claim.Zone, distance, claim.Request.HeadMultiplier, claim.Request.LimbMultiplier,
                claim.WeakPointMultiplier, claim.Request.EffectiveRange, claim.Request.MaxRange, claim.Request.MinRangeMultiplier,
                claim.Request.OutgoingMultiplier, claim.Target.DamageReduction);
            DamageResult result = DamageCalculator.Compute(request);

            var info = new DamageInfo
            {
                Amount = result.Amount,
                Zone = claim.Zone,
                IsCritical = result.IsCritical,
                Point = claim.Point,
                Direction = (claim.Point - claim.Origin).normalized,
                Source = claim.Attacker,
                WeaponId = claim.WeaponId,
            };
            float applied = claim.Target.ApplyDamage(info);
            if (applied <= 0f) return default;

            bool killed = !claim.Target.IsAlive;
            if (killed && claim.Target is Component c)
                EventBus<DamageableKilled>.Publish(new DamageableKilled { Victim = c.gameObject, Killer = claim.Attacker });
            return new HitOutcome { Applied = true, Amount = applied, IsCritical = result.IsCritical, Killed = killed };
        }
    }

    /// <summary>Registers the default resolver. A network session replaces it when hosting or joining.</summary>
    public static class CombatServices
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterDefaults()
        {
            if (!ServiceRegistry.TryGet(out IHitResolver _))
                ServiceRegistry.Register<IHitResolver>(new LocalHitResolver());
        }

        public static IHitResolver Resolver
        {
            get
            {
                if (!ServiceRegistry.TryGet(out IHitResolver r))
                {
                    r = new LocalHitResolver();
                    ServiceRegistry.Register(r);
                }
                return r;
            }
        }
    }
}
