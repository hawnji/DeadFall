using Deadfall.Rules.Combat;
using NUnit.Framework;

namespace Deadfall.Tests.EditMode.Rules
{
    public class WeaponStateMachineTests
    {
        private static WeaponStats Rifle() => new WeaponStats
        {
            FireMode = FireMode.FullAuto,
            RoundsPerMinute = 600f, // 0.1 s per shot
            MagazineSize = 30,
            MaxReserve = 90,
            TacticalReloadSeconds = 2f,
            EmptyReloadSeconds = 3f,
            ReloadCommitFraction = 0.5f,
            EquipSeconds = 0.5f,
        };

        private static WeaponStateMachine ReadyWeapon(WeaponStats stats, double now = 0)
        {
            var w = WeaponStateMachine.CreateFull(stats);
            w.Equip(now);
            w.Tick(now + stats.EquipSeconds);
            return w;
        }

        [Test]
        public void CannotFireWhileEquipping()
        {
            var w = WeaponStateMachine.CreateFull(Rifle());
            w.Equip(0);
            Assert.AreEqual(0, w.TryFire(0.2, true, true).ShotsFired);
            Assert.AreEqual(1, w.TryFire(0.5, true, true).ShotsFired);
        }

        [Test]
        public void FullAutoRespectsFireRateRegardlessOfFrameRate()
        {
            // Held fire at 600 RPM releases one round per 0.1 s of elapsed time, at any frame length.
            foreach (double dt in new[] { 1.0 / 30, 1.0 / 60, 1.0 / 144, 0.25 })
            {
                var w = ReadyWeapon(Rifle());
                int shots = 0;
                double last = 0.5;
                for (double t = 0.5; t <= 1.5 + 1e-9; t += dt)
                {
                    shots += w.TryFire(t, false, true).ShotsFired;
                    last = t;
                }
                int expected = (int)System.Math.Floor((last - 0.5) / 0.1 + 1e-6) + 1;
                Assert.That(shots, Is.InRange(expected - 1, expected), $"dt={dt}");
            }
        }

        [Test]
        public void SemiAutoFiresOncePerPress()
        {
            var stats = Rifle();
            stats.FireMode = FireMode.SemiAuto;
            var w = ReadyWeapon(stats);
            Assert.AreEqual(1, w.TryFire(1.0, true, true).ShotsFired);
            Assert.AreEqual(0, w.TryFire(1.5, false, true).ShotsFired, "Holding must not refire");
            Assert.AreEqual(1, w.TryFire(1.6, true, true).ShotsFired);
        }

        [Test]
        public void SemiAutoCannotExceedFireRateBySpamming()
        {
            var stats = Rifle();
            stats.FireMode = FireMode.SemiAuto;
            var w = ReadyWeapon(stats);
            Assert.AreEqual(1, w.TryFire(1.0, true, true).ShotsFired);
            Assert.AreEqual(0, w.TryFire(1.05, true, true).ShotsFired);
        }

        [Test]
        public void MagazineDecrementsAndDryFireReported()
        {
            var stats = Rifle();
            stats.MagazineSize = 3;
            var w = ReadyWeapon(stats);
            int total = 0;
            for (double t = 1; t < 3; t += 0.01) total += w.TryFire(t, false, true).ShotsFired;
            Assert.AreEqual(3, total);
            Assert.AreEqual(0, w.Magazine);
            Assert.IsTrue(w.TryFire(3.5, true, true).DryFire);
        }

        [Test]
        public void TacticalReloadUsesTacticalTimeAndRefills()
        {
            var w = ReadyWeapon(Rifle());
            w.TryFire(1.0, true, true); // 29 left
            Assert.IsTrue(w.StartReload(2.0));
            w.Tick(3.9);
            Assert.IsTrue(w.IsReloading);
            w.Tick(4.0);
            Assert.IsFalse(w.IsReloading);
            Assert.AreEqual(30, w.Magazine);
            Assert.AreEqual(89, w.Reserve);
        }

        [Test]
        public void EmptyReloadTakesLonger()
        {
            var stats = Rifle();
            stats.MagazineSize = 1;
            var w = ReadyWeapon(stats);
            w.TryFire(1.0, true, true);
            w.StartReload(2.0);
            w.Tick(4.5);
            Assert.IsTrue(w.IsReloading);
            w.Tick(5.0);
            Assert.IsFalse(w.IsReloading);
        }

        [Test]
        public void ReloadInterruptedBeforeCommitKeepsOldMagazine()
        {
            var w = ReadyWeapon(Rifle());
            w.TryFire(1.0, true, true);
            w.StartReload(2.0);
            w.Tick(2.5); // commit at 3.0
            w.CancelReload();
            Assert.AreEqual(29, w.Magazine);
            Assert.AreEqual(90, w.Reserve);
            Assert.AreEqual(WeaponState.Ready, w.State);
        }

        [Test]
        public void ReloadInterruptedAfterCommitKeepsNewAmmo()
        {
            var w = ReadyWeapon(Rifle());
            w.TryFire(1.0, true, true);
            w.StartReload(2.0);
            w.Tick(3.1);
            w.Holster();
            Assert.AreEqual(30, w.Magazine);
            Assert.AreEqual(89, w.Reserve);
        }

        [Test]
        public void ReloadNotAllowedWhenFullOrNoReserve()
        {
            var w = ReadyWeapon(Rifle());
            Assert.IsFalse(w.StartReload(1.0), "full magazine");
            var empty = new WeaponStateMachine(Rifle(), 10, 0);
            empty.Equip(0);
            empty.Tick(1);
            Assert.IsFalse(empty.StartReload(1.0), "no reserve");
        }

        [Test]
        public void ReserveNeverNegativeWhenPartiallyRefilled()
        {
            var w = new WeaponStateMachine(Rifle(), 0, 5);
            w.Equip(0);
            w.Tick(1);
            w.StartReload(1);
            w.Tick(10);
            Assert.AreEqual(5, w.Magazine);
            Assert.AreEqual(0, w.Reserve);
        }

        [Test]
        public void CanFireImmediatelyAfterReloadEnds()
        {
            var w = ReadyWeapon(Rifle());
            w.TryFire(1.0, true, true);
            w.StartReload(1.01);
            Assert.AreEqual(1, w.TryFire(3.01, true, true).ShotsFired);
        }

        [Test]
        public void FiringAfterLongPauseDoesNotReleaseBacklog()
        {
            var w = ReadyWeapon(Rifle());
            Assert.AreEqual(1, w.TryFire(10.0, true, true).ShotsFired);
        }

        [Test]
        public void AddReserveClampsToMax()
        {
            var w = new WeaponStateMachine(Rifle(), 30, 80);
            Assert.AreEqual(10, w.AddReserve(50));
            Assert.AreEqual(90, w.Reserve);
        }
    
        [Test]
        public void LongHitchIsCappedPerCall()
        {
            var w = ReadyWeapon(Rifle());
            w.TryFire(1.0, true, true);
            Assert.That(w.TryFire(5.0, false, true).ShotsFired, Is.LessThanOrEqualTo(WeaponStateMachine.MaxShotsPerCall));
        }

        [Test]
        public void ReleasingTriggerResetsCadence()
        {
            var w = ReadyWeapon(Rifle());
            Assert.AreEqual(1, w.TryFire(1.0, true, true).ShotsFired);
            w.TryFire(1.02, false, false);
            Assert.AreEqual(1, w.TryFire(3.0, true, true).ShotsFired, "New pull fires one round, not a backlog");
        }
    }
}
