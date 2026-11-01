using Deadfall.Rules.Combat;
using Deadfall.Rules.Common;
using NUnit.Framework;

namespace Deadfall.Tests.EditMode.Rules
{
    public class CommonTests
    {
        [Test]
        public void SameSeedSameSequence()
        {
            var a = new DeterministicRandom(12345);
            var b = new DeterministicRandom(12345);
            for (int i = 0; i < 1000; i++) Assert.AreEqual(a.NextUInt(), b.NextUInt());
        }

        [Test]
        public void DifferentSeedsDiffer()
        {
            Assert.AreNotEqual(new DeterministicRandom(1).NextUInt(), new DeterministicRandom(2).NextUInt());
        }

        [Test]
        public void RangesStayInBounds()
        {
            var r = new DeterministicRandom(7);
            for (int i = 0; i < 10000; i++)
            {
                float f = r.NextFloat();
                Assert.That(f, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
                int n = r.Range(3, 9);
                Assert.That(n, Is.InRange(3, 8));
                r.InsideUnitDisc(out float x, out float y);
                Assert.That(x * x + y * y, Is.LessThanOrEqualTo(1.0001f));
            }
        }

        [Test]
        public void ModifierStackOrder()
        {
            var s = new ModifierStack();
            object src = new object();
            s.Add(new StatModifier(ModifierKind.Flat, 10f, src));
            s.Add(new StatModifier(ModifierKind.AddPercent, 0.1f, null));
            s.Add(new StatModifier(ModifierKind.AddPercent, 0.2f, null));
            s.Add(new StatModifier(ModifierKind.Multiply, 2f, null));
            Assert.AreEqual((100f + 10f) * 1.3f * 2f, s.Evaluate(100f), 1e-3);
            Assert.AreEqual(1, s.RemoveAllFromSource(src));
            Assert.AreEqual(100f * 1.3f * 2f, s.Evaluate(100f), 1e-3);
        }

        [Test]
        public void SpreadAimTighterThanHipAndMovementWidens()
        {
            var stats = new WeaponStats { HipSpread = 2f, AimSpread = 0.5f, MoveSpreadPenalty = 1f, BloomPerShot = 0.5f, MaxBloom = 1f, BloomRecoveryPerSecond = 2f };
            var s = new SpreadModel(stats);
            Assert.Less(s.CurrentHalfAngle(1f, 0f, false), s.CurrentHalfAngle(0f, 0f, false));
            Assert.Greater(s.CurrentHalfAngle(0f, 1f, false), s.CurrentHalfAngle(0f, 0f, false));
            s.OnShot(); s.OnShot(); s.OnShot();
            Assert.AreEqual(1f, s.Bloom, 1e-4, "Bloom capped");
            s.Recover(1f);
            Assert.AreEqual(0f, s.Bloom, 1e-4);
        }
    }
}
