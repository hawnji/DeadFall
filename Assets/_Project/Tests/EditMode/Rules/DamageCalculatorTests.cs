using Deadfall.Rules.Combat;
using NUnit.Framework;

namespace Deadfall.Tests.EditMode.Rules
{
    public class DamageCalculatorTests
    {
        private static WeaponStats Weapon() => new WeaponStats
        {
            Damage = 40f, HeadMultiplier = 2f, LimbMultiplier = 0.5f,
            EffectiveRange = 20f, MaxRange = 60f, MinRangeMultiplier = 0.5f,
        };

        [Test]
        public void FullDamageInsideEffectiveRange()
        {
            var r = DamageCalculator.Compute(DamageRequest.FromWeapon(Weapon(), HitZone.Body, 10f));
            Assert.AreEqual(40f, r.Amount, 1e-4);
            Assert.IsFalse(r.IsCritical);
        }

        [Test]
        public void LinearFalloffBetweenEffectiveAndMaxRange()
        {
            Assert.AreEqual(30f, DamageCalculator.Compute(DamageRequest.FromWeapon(Weapon(), HitZone.Body, 40f)).Amount, 1e-4);
            Assert.AreEqual(20f, DamageCalculator.Compute(DamageRequest.FromWeapon(Weapon(), HitZone.Body, 100f)).Amount, 1e-4);
        }

        [Test]
        public void HeadshotIsCriticalAndMultiplied()
        {
            var r = DamageCalculator.Compute(DamageRequest.FromWeapon(Weapon(), HitZone.Head, 5f));
            Assert.AreEqual(80f, r.Amount, 1e-4);
            Assert.IsTrue(r.IsCritical);
        }

        [Test]
        public void WeakPointUsesTargetMultiplier()
        {
            var r = DamageCalculator.Compute(DamageRequest.FromWeapon(Weapon(), HitZone.WeakPoint, 5f, weakPointMultiplier: 3f));
            Assert.AreEqual(120f, r.Amount, 1e-4);
            Assert.IsTrue(r.IsCritical);
        }

        [Test]
        public void LimbReducesDamage()
        {
            Assert.AreEqual(20f, DamageCalculator.Compute(DamageRequest.FromWeapon(Weapon(), HitZone.Limb, 5f)).Amount, 1e-4);
        }

        [Test]
        public void OutgoingAndReductionApply()
        {
            var r = DamageCalculator.Compute(DamageRequest.FromWeapon(Weapon(), HitZone.Body, 5f, outgoingMultiplier: 1.5f, targetReduction: 0.5f));
            Assert.AreEqual(30f, r.Amount, 1e-4);
        }

        [Test]
        public void ReductionAboveOneNeverHeals()
        {
            var r = DamageCalculator.Compute(DamageRequest.FromWeapon(Weapon(), HitZone.Body, 5f, targetReduction: 2f));
            Assert.AreEqual(0f, r.Amount);
        }

        [Test]
        public void MeleeIgnoresRangeFalloff()
        {
            var m = new MeleeStats { Damage = 50f, HeadMultiplier = 1.5f, Range = 2f };
            Assert.AreEqual(75f, DamageCalculator.Compute(DamageRequest.FromMelee(m, HitZone.Head)).Amount, 1e-4);
        }
    }
}
