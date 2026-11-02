using Deadfall.Rules.Combat;
using NUnit.Framework;

namespace Deadfall.Tests.EditMode.Rules
{
    public class EnemyBrainTests
    {
        private static EnemyStats Stats() => new EnemyStats
        {
            ChaseSpeed = 3f, DetectRange = 20f, AttackRange = 1.5f,
            AttackWindupSeconds = 0.5f, AttackRecoverSeconds = 0.5f,
            StaggerThreshold = 50f, StaggerWindowSeconds = 1f, StaggerSeconds = 0.5f, StaggerImmunitySeconds = 1f,
            CanCharge = false,
        };

        private static BrainOutput Tick(EnemyBrain b, double now, float distance, bool hasTarget = true) =>
            b.Tick(new BrainInput { Now = now, HasTarget = hasTarget, TargetDistance = distance });

        [Test]
        public void AlertedEnemyChasesFromBeyondDetectRange()
        {
            var b = new EnemyBrain(Stats());
            b.Reset(0);
            b.Alert();
            Assert.AreEqual(EnemyState.Chase, Tick(b, 0.1, 80f).State);
            // Losing the target (player dead) idles it; it resumes the hunt when a target is back.
            Assert.AreEqual(EnemyState.Idle, Tick(b, 0.2, 0f, hasTarget: false).State);
            Assert.AreEqual(EnemyState.Chase, Tick(b, 0.3, 80f).State);
        }

        [Test]
        public void IdleUntilTargetInDetectRange()
        {
            var b = new EnemyBrain(Stats());
            b.Reset(0);
            Assert.AreEqual(EnemyState.Idle, Tick(b, 0.1, 30f).State);
            BrainOutput o = Tick(b, 0.2, 15f);
            Assert.AreEqual(EnemyState.Chase, o.State);
            Assert.IsTrue(o.PathToTarget);
            Assert.AreEqual(3f, o.Speed);
        }

        [Test]
        public void DamageAlertsEvenOutOfRange()
        {
            var b = new EnemyBrain(Stats());
            b.Reset(0);
            b.OnDamaged(1f, 0.1);
            Assert.AreEqual(EnemyState.Chase, Tick(b, 0.2, 100f).State);
        }

        [Test]
        public void OnceAlertedKeepsChasingBeyondDetectRange()
        {
            var b = new EnemyBrain(Stats());
            b.Reset(0);
            Tick(b, 0.1, 10f);
            Assert.AreEqual(EnemyState.Chase, Tick(b, 0.2, 60f).State);
        }

        [Test]
        public void StrikeLandsExactlyOnceAfterWindup()
        {
            var b = new EnemyBrain(Stats());
            b.Reset(0);
            Tick(b, 0.0, 10f);
            Assert.AreEqual(EnemyState.AttackWindup, Tick(b, 1.0, 1f).State);
            int strikes = 0;
            double strikeTime = -1;
            for (double t = 1.0; t < 2.0; t += 1.0 / 60.0)
            {
                if (Tick(b, t, 1f).Strike)
                {
                    strikes++;
                    if (strikeTime < 0) strikeTime = t;
                }
            }
            Assert.AreEqual(1, strikes);
            Assert.AreEqual(1.5, strikeTime, 1.0 / 60.0 + 1e-6);
        }

        [TestCase(30)]
        [TestCase(144)]
        [TestCase(8)]
        public void AttackCycleTakesSameTimeAtAnyTickRate(int hz)
        {
            var b = new EnemyBrain(Stats());
            b.Reset(0);
            Tick(b, 0, 1f); // Idle -> Chase -> Windup on the same tick
            Assert.AreEqual(EnemyState.AttackWindup, b.State);
            double dt = 1.0 / hz, t = 0;
            while (b.State != EnemyState.Chase && t < 5) { t += dt; Tick(b, t, 1f); }
            // Windup 0.5 + recover 0.5; a slow tick can only add up to one tick per state.
            Assert.GreaterOrEqual(t, 1.0 - 1e-9);
            Assert.LessOrEqual(t, 1.0 + 2 * dt + 1e-9);
        }

        [Test]
        public void ChaseStopsWhenTargetLost()
        {
            var b = new EnemyBrain(Stats());
            b.Reset(0);
            Tick(b, 0.1, 10f);
            Assert.AreEqual(EnemyState.Idle, Tick(b, 0.2, 0f, hasTarget: false).State);
            Assert.AreEqual(EnemyState.Chase, Tick(b, 0.3, 50f).State, "Stays alerted, so it resumes at any range");
        }

        [Test]
        public void StaggerInterruptsWindupAndCannotChain()
        {
            var b = new EnemyBrain(Stats());
            b.Reset(0);
            Tick(b, 0.0, 1f);
            Assert.AreEqual(EnemyState.AttackWindup, b.State);
            b.OnDamaged(60f, 0.1);
            BrainOutput o = Tick(b, 0.1, 1f);
            Assert.AreEqual(EnemyState.Stagger, o.State);
            Assert.IsFalse(o.Strike);

            // Immune for stagger (0.5) + immunity (1.0) from the stagger hit.
            b.OnDamaged(100f, 0.7);
            Tick(b, 0.7, 5f);
            Assert.AreNotEqual(EnemyState.Stagger, b.State, "Stagger ended and the next one is blocked by immunity");

            b.OnDamaged(60f, 2.0);
            Assert.AreEqual(EnemyState.Stagger, Tick(b, 2.0, 5f).State, "Immunity over");
        }

        [Test]
        public void StaggerMeterDecaysBetweenHits()
        {
            var m = new StaggerMeter(Stats());
            Assert.IsFalse(m.Add(30f, 0));
            Assert.IsFalse(m.Add(30f, 1.0), "Fully decayed after one window");
            Assert.IsTrue(m.Add(30f, 1.1));
        }

        [Test]
        public void ZeroThresholdNeverStaggers()
        {
            EnemyStats s = Stats();
            s.StaggerThreshold = 0f;
            var m = new StaggerMeter(s);
            Assert.IsFalse(m.Add(10000f, 0));
        }

        [Test]
        public void ChargeTriggersInRangeThenCoolsDown()
        {
            EnemyStats s = Stats();
            s.CanCharge = true;
            s.ChargeMinRange = 5f; s.ChargeMaxRange = 10f;
            s.ChargeWindupSeconds = 0.5f; s.ChargeSeconds = 1f; s.ChargeCooldownSeconds = 4f; s.ChargeSpeed = 9f;
            var b = new EnemyBrain(s);
            b.Reset(0); // first charge allowed after half the cooldown (2 s)

            Assert.AreEqual(EnemyState.Chase, Tick(b, 1.0, 8f).State, "Not ready yet");
            Assert.AreEqual(EnemyState.ChargeWindup, Tick(b, 2.0, 8f).State);
            BrainOutput start = Tick(b, 2.5, 8f);
            Assert.IsTrue(start.ChargeStarted);
            Assert.AreEqual(9f, start.Speed);
            Assert.IsFalse(Tick(b, 2.6, 8f).ChargeStarted, "Direction locks only once");

            Assert.AreEqual(EnemyState.AttackRecover, Tick(b, 3.6, 8f).State, "Charge times out");
            Tick(b, 4.2, 8f); // recover -> chase
            Assert.AreEqual(EnemyState.Chase, Tick(b, 4.3, 8f).State, "Cooldown blocks a second charge");
            Assert.AreEqual(EnemyState.ChargeWindup, Tick(b, 6.6, 8f).State);
        }

        [Test]
        public void ChargeHitEndsCharge()
        {
            EnemyStats s = Stats();
            s.CanCharge = true; s.ChargeCooldownSeconds = 0f; s.ChargeWindupSeconds = 0.1f;
            var b = new EnemyBrain(s);
            b.Reset(0);
            Tick(b, 0, 8f);
            Tick(b, 0.2, 8f);
            Assert.AreEqual(EnemyState.Charge, b.State);
            b.OnChargeHit(0.3);
            Assert.AreEqual(EnemyState.AttackRecover, b.State);
        }

        [Test]
        public void UnstaggerableCharge()
        {
            EnemyStats s = Stats();
            s.CanCharge = true; s.ChargeCooldownSeconds = 0f; s.ChargeWindupSeconds = 0.1f; s.ChargeCanBeStaggered = false;
            var b = new EnemyBrain(s);
            b.Reset(0);
            Tick(b, 0, 8f);
            Tick(b, 0.2, 8f);
            b.OnDamaged(500f, 0.25);
            Assert.AreEqual(EnemyState.Charge, Tick(b, 0.3, 8f).State);
        }

        [Test]
        public void DeadIgnoresEverything()
        {
            var b = new EnemyBrain(Stats());
            b.Reset(0);
            b.Kill(0.1);
            b.OnDamaged(100f, 0.2);
            BrainOutput o = Tick(b, 0.3, 1f);
            Assert.AreEqual(EnemyState.Dead, o.State);
            Assert.IsFalse(o.Strike);
            Assert.AreEqual(0f, o.Speed);
        }

        [Test]
        public void ArmorZoneUsesReducedMultiplierAndIsNotCritical()
        {
            var r = new DamageRequest(100f, HitZone.Armor, 0f, 2f, 0.75f, 0.25f, 30f, 100f, 0.5f);
            DamageResult d = DamageCalculator.Compute(r);
            Assert.AreEqual(25f, d.Amount, 1e-4);
            Assert.IsFalse(d.IsCritical);
            var r2 = new DamageRequest(100f, HitZone.Armor, 0f, 2f, 0.75f, 3f, 30f, 100f, 0.5f);
            Assert.AreEqual(100f, DamageCalculator.Compute(r2).Amount, 1e-4, "Armor can never amplify");
        }
    }
}
