using Deadfall.Rules.Combat;
using NUnit.Framework;

namespace Deadfall.Tests.EditMode.Rules
{
    public class HealthModelTests
    {
        private static HealthStats Stats() => new HealthStats { MaxHealth = 100f, RegenDelaySeconds = 5f, RegenPerSecond = 10f, RegenSegmentSize = 25f };

        [Test]
        public void DamageReducesAndClampsAtZero()
        {
            var h = new HealthModel(Stats());
            Assert.AreEqual(30f, h.ApplyDamage(30f, 0));
            Assert.AreEqual(70f, h.Current);
            Assert.AreEqual(70f, h.ApplyDamage(500f, 1));
            Assert.IsTrue(h.IsDead);
            Assert.AreEqual(0f, h.ApplyDamage(10f, 2), "No damage after death");
        }

        [Test]
        public void RegenWaitsForDelayThenFillsOnlyCurrentSegment()
        {
            var h = new HealthModel(Stats());
            h.ApplyDamage(40f, 0); // 60 HP, segment cap 75
            h.Tick(4.9, 1f);
            Assert.AreEqual(60f, h.Current, "Still in delay");
            for (int i = 0; i < 50; i++) h.Tick(5 + i * 0.1, 0.1f);
            Assert.AreEqual(75f, h.Current, 1e-3);
        }

        [Test]
        public void DamageResetsRegenDelay()
        {
            var h = new HealthModel(Stats());
            h.ApplyDamage(10f, 0);
            h.ApplyDamage(10f, 4);
            h.Tick(8, 1f);
            Assert.AreEqual(80f, h.Current);
        }

        [Test]
        public void ZeroSegmentRegeneratesToFull()
        {
            var s = Stats();
            s.RegenSegmentSize = 0f;
            var h = new HealthModel(s);
            h.ApplyDamage(60f, 0);
            for (int i = 0; i < 100; i++) h.Tick(10 + i, 1f);
            Assert.AreEqual(100f, h.Current);
        }

        [Test]
        public void DeadDoesNotRegenOrHeal()
        {
            var h = new HealthModel(Stats());
            h.ApplyDamage(100f, 0);
            h.Tick(100, 10f);
            Assert.AreEqual(0f, h.Heal(50f));
            Assert.IsTrue(h.IsDead);
        }
    }
}
