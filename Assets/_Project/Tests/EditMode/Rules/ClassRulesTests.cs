using System.Collections.Generic;
using Deadfall.Rules.Classes;
using Deadfall.Rules.Combat;
using NUnit.Framework;

namespace Deadfall.Tests.EditMode.Rules
{
    public class AbilityTimerTests
    {
        [Test]
        public void ActiveThenCooldownThenReady()
        {
            var t = new AbilityTimer(45f, 8f);
            Assert.IsTrue(t.TryActivate(0));
            Assert.AreEqual(AbilityPhase.Active, t.Phase);
            Assert.AreEqual(8f, t.ActiveRemaining(0), 0.0001f);
            Assert.IsFalse(t.TryActivate(1), "can't use while active");
            Assert.AreEqual(AbilityEvents.None, t.Tick(7.9));
            Assert.AreEqual(AbilityEvents.Ended, t.Tick(8));
            // Cooldown starts when the effect ends (T-26).
            Assert.AreEqual(45f, t.CooldownRemaining(8), 0.0001f);
            Assert.IsFalse(t.TryActivate(52));
            Assert.AreEqual(AbilityEvents.Ready, t.Tick(53));
            Assert.IsTrue(t.TryActivate(53));
        }

        [Test]
        public void InstantAbilityGoesStraightToCooldown()
        {
            var t = new AbilityTimer(12f, 0f);
            Assert.IsTrue(t.TryActivate(0));
            Assert.AreEqual(AbilityPhase.Cooldown, t.Phase);
            Assert.AreEqual(0.5f, t.CooldownProgress(6), 0.0001f);
            Assert.IsTrue(t.TryActivate(12));
        }

        [Test]
        public void EndingEarlyStartsCooldownFromThen()
        {
            var t = new AbilityTimer(40f, 8f);
            t.TryActivate(0);
            Assert.AreEqual(AbilityEvents.Ended, t.End(3));
            Assert.AreEqual(40f, t.CooldownRemaining(3), 0.0001f);
            Assert.AreEqual(AbilityEvents.None, t.End(4), "ending twice does nothing");
        }

        [Test]
        public void LongFrameCanEndAndReadyInOneTick()
        {
            var t = new AbilityTimer(2f, 1f);
            t.TryActivate(0);
            Assert.AreEqual(AbilityEvents.Ended | AbilityEvents.Ready, t.Tick(10));
            Assert.IsTrue(t.IsReady);
        }

        [Test]
        public void CooldownRateAndReduction()
        {
            var t = new AbilityTimer(40f, 0f) { CooldownRate = 1.25f };
            t.TryActivate(0);
            Assert.AreEqual(32f, t.CooldownRemaining(0), 0.0001f);
            t.ReduceCooldown(10f);
            Assert.AreEqual(22f, t.CooldownRemaining(0), 0.0001f);
            t.Reset();
            Assert.IsTrue(t.IsReady);
        }
    }

    public class ClassRulesTests
    {
        [Test]
        public void GuardOnlyAboveThreshold()
        {
            Assert.AreEqual(0.3f, ClassRules.GuardReduction(0.8f, 0.5f, 0.3f), 0.0001f);
            Assert.AreEqual(0f, ClassRules.GuardReduction(0.5f, 0.5f, 0.3f), 0.0001f);
            Assert.AreEqual(0f, ClassRules.GuardReduction(0.2f, 0.5f, 0.3f), 0.0001f);
        }

        [Test]
        public void SustainedRecoilFallsToFloor()
        {
            Assert.AreEqual(1f, ClassRules.SustainedRecoil(0f, 0.1f, 0.6f), 0.0001f);
            Assert.AreEqual(0.8f, ClassRules.SustainedRecoil(2f, 0.1f, 0.6f), 0.0001f);
            Assert.AreEqual(0.6f, ClassRules.SustainedRecoil(30f, 0.1f, 0.6f), 0.0001f);
        }

        [Test]
        public void ReductionsCombineWithoutReachingFull()
        {
            Assert.AreEqual(0.65f, ClassRules.CombineReduction(0.3f, 0.5f), 0.0001f);
            Assert.Less(ClassRules.CombineReduction(0.9f, 0.9f), 1f);
        }

        [Test]
        public void ComboCountsToThreeThenRestarts()
        {
            var c = new MeleeCombo(1.2f, 3);
            Assert.AreEqual(1, c.Register(0));
            Assert.AreEqual(2, c.Register(1));
            Assert.IsTrue(c.NextIsFinisher(1.5));
            Assert.AreEqual(3, c.Register(2));
            Assert.AreEqual(1, c.Register(2.5), "chain restarts after the finisher");
            Assert.AreEqual(1, c.Register(5), "a slow swing restarts the chain");
            Assert.IsFalse(c.NextIsFinisher(5.1));
        }
    }

    public class DoctrineResolverTests
    {
        private static readonly DoctrineRule[] Rules =
        {
            DoctrineRule.Pair("covering_fire", "gunner", "tank"),
            DoctrineRule.Pair("demolition_crew", "engineer", "demolitionist"),
            DoctrineRule.Pair("vanguard", "warrior", "tank"),
            DoctrineRule.Pair("overwatch", "gunner", "engineer"),
            DoctrineRule.Distinct("combined_arms", 4),
        };

        [Test]
        public void PairNeedsBothClassesOnDifferentPlayers()
        {
            List<string>[] r = DoctrineResolver.Resolve(new[] { "gunner", "tank" }, Rules);
            CollectionAssert.AreEqual(new[] { "covering_fire" }, r[0]);
            CollectionAssert.AreEqual(new[] { "covering_fire" }, r[1]);
            List<string>[] solo = DoctrineResolver.Resolve(new[] { "gunner" }, Rules);
            Assert.IsEmpty(solo[0]);
        }

        [Test]
        public void DuplicatesStillGetThePair()
        {
            List<string>[] r = DoctrineResolver.Resolve(new[] { "tank", "tank", "gunner" }, Rules);
            CollectionAssert.AreEqual(new[] { "covering_fire" }, r[0]);
            CollectionAssert.AreEqual(new[] { "covering_fire" }, r[1]);
            List<string>[] same = DoctrineResolver.Resolve(new[] { "tank", "tank" }, Rules);
            Assert.IsEmpty(same[0]);
        }

        [Test]
        public void AtMostTwoPerPlayerInListOrder()
        {
            List<string>[] r = DoctrineResolver.Resolve(new[] { "tank", "gunner", "warrior", "engineer" }, Rules);
            // Tank qualifies for covering_fire, vanguard and combined_arms: keeps the first two.
            CollectionAssert.AreEqual(new[] { "covering_fire", "vanguard" }, r[0]);
            // Gunner: covering_fire, overwatch (combined_arms would be third).
            CollectionAssert.AreEqual(new[] { "covering_fire", "overwatch" }, r[1]);
            // Warrior: vanguard, combined_arms.
            CollectionAssert.AreEqual(new[] { "vanguard", "combined_arms" }, r[2]);
        }

        [Test]
        public void CombinedArmsNeedsFourDistinct()
        {
            List<string>[] three = DoctrineResolver.Resolve(new[] { "warrior", "demolitionist", "warrior", "demolitionist" }, Rules);
            Assert.IsFalse(three[0].Contains("combined_arms"));
        }
    }

    public class WeaponHandlingTests
    {
        private static WeaponStats Rifle() => new WeaponStats
        {
            FireMode = FireMode.FullAuto, RoundsPerMinute = 600f, MagazineSize = 30, MaxReserve = 90,
            TacticalReloadSeconds = 2f, EmptyReloadSeconds = 2f, ReloadCommitFraction = 0.5f, EquipSeconds = 0f,
        };

        [Test]
        public void FireRateMultiplierFiresFaster()
        {
            var normal = WeaponStateMachine.CreateFull(Rifle());
            var fast = WeaponStateMachine.CreateFull(Rifle());
            fast.FireRateMultiplier = 1.4f;
            normal.Equip(0);
            fast.Equip(0);
            int a = 0, b = 0;
            for (int i = 0; i <= 100; i++)
            {
                double t = i / 100.0; // one second at 100 Hz
                a += normal.TryFire(t, i == 0, true).ShotsFired;
                b += fast.TryFire(t, i == 0, true).ShotsFired;
            }
            // About 10 per second, about 14 with +40% (the edge shot depends on float rounding).
            Assert.That(a, Is.InRange(10, 11));
            Assert.That(b, Is.InRange(14, 15));
        }

        [Test]
        public void ReloadSpeedShortensReload()
        {
            var w = new WeaponStateMachine(Rifle(), 0, 90) { ReloadSpeedMultiplier = 2f };
            w.Equip(0);
            Assert.IsTrue(w.StartReload(0));
            w.Tick(0.99);
            Assert.IsTrue(w.IsReloading);
            w.Tick(1.0);
            Assert.IsFalse(w.IsReloading);
            Assert.AreEqual(30, w.Magazine);
        }

        [Test]
        public void FreeReloadsKeepReserve()
        {
            var w = new WeaponStateMachine(Rifle(), 5, 0) { FreeReloads = true };
            w.Equip(0);
            Assert.IsTrue(w.CanReload, "free reloads work with an empty reserve");
            w.StartReload(0);
            w.Tick(5);
            Assert.AreEqual(30, w.Magazine);
            Assert.AreEqual(0, w.Reserve);
        }

        [Test]
        public void ReserveCapacityChange()
        {
            var w = WeaponStateMachine.CreateFull(Rifle());
            w.SetMaxReserve(112, fill: true);
            Assert.AreEqual(112, w.Reserve);
            Assert.AreEqual(0, w.AddReserve(50), "already at the new cap");
            w.SetMaxReserve(90, fill: false);
            Assert.AreEqual(90, w.Reserve);
        }
    }
}
