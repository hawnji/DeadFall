using Deadfall.Rules.Combat;
using Deadfall.Rules.Common;
using Deadfall.Rules.Encounters;
using Deadfall.Rules.World;
using NUnit.Framework;

namespace Deadfall.Tests.EditMode.Rules
{
    /// <summary>Lull ambushes, Red Sky, Containment Breach and boss arena hazards.</summary>
    public class RoadmapEventTests
    {
        [Test]
        public void AmbushesKeepTheirGapAndFitInTheLull()
        {
            var s = new AmbushScheduler();
            var rng = new DeterministicRandom(11);
            int last = -100, count = 0;
            for (int surge = 1; surge < 300; surge++)
            {
                float at = s.Roll(rng, surge, SurgeType.Standard, 30f);
                if (at < 0f) continue;
                Assert.GreaterOrEqual(surge, AmbushScheduler.FirstSurge);
                Assert.GreaterOrEqual(surge - last, AmbushScheduler.MinGapSurges);
                Assert.GreaterOrEqual(at, AmbushScheduler.WarningSeconds, "room for the warning");
                Assert.LessOrEqual(at, 30f * 0.65f + 0.001f, "room to fight before the surge");
                last = surge;
                count++;
            }
            Assert.Greater(count, 15);
        }

        [Test]
        public void NoAmbushInShortLullsOrBeforeASpecimen()
        {
            var rng = new DeterministicRandom(3);
            for (int i = 0; i < 500; i++)
            {
                Assert.Less(new AmbushScheduler().Roll(rng, 8, SurgeType.Standard, 4f), 0f);
                Assert.Less(new AmbushScheduler().Roll(rng, 8, SurgeType.Specimen, 30f), 0f);
            }
            Assert.AreEqual(AmbushScheduler.MinCount, AmbushScheduler.Count(1, 1));
            Assert.AreEqual(AmbushScheduler.MaxCount, AmbushScheduler.Count(60, 4));
        }

        [Test]
        public void RedSkyAndBreachCanBeRolled()
        {
            var rng = new DeterministicRandom(21);
            bool red = false, breach = false;
            for (int i = 0; i < 3000 && !(red && breach); i++)
            {
                RunEventKind k = new EventScheduler().Roll(rng, 8, SurgeType.Standard, true, 2);
                red |= k == RunEventKind.RedSky;
                breach |= k == RunEventKind.ContainmentBreach;
            }
            Assert.IsTrue(red && breach);
            Assert.AreEqual(0f, EventScheduler.Weight(RunEventKind.ContainmentBreach, false, 0), "breach needs a map");
            Assert.AreEqual("Shut down the containment system", ObjectiveRules.Title(ObjectiveKind.ShutDownBreach));
        }

        [Test]
        public void BreachFloorCyclesCalmWarningArc()
        {
            Assert.AreEqual(HazardRules.BreachPhase.Calm, HazardRules.BreachPhaseAt(0.1f));
            float arc = HazardRules.BreachCycleSeconds - HazardRules.BreachArcSeconds;
            Assert.AreEqual(HazardRules.BreachPhase.Warning, HazardRules.BreachPhaseAt(arc - 0.1f));
            Assert.AreEqual(HazardRules.BreachPhase.Arc, HazardRules.BreachPhaseAt(arc + 0.1f));
            Assert.AreEqual(HazardRules.BreachPhase.Calm, HazardRules.BreachPhaseAt(HazardRules.BreachCycleSeconds + 0.1f));
            Assert.AreEqual(0, HazardRules.BreachArcIndex(arc + 0.1f));
            Assert.AreEqual(1, HazardRules.BreachArcIndex(HazardRules.BreachCycleSeconds + arc + 0.1f));
        }

        [Test]
        public void ArenaHazardsStartWhenTheBossIsEnraged()
        {
            Assert.AreEqual(0, ArenaHazardRules.Count(SpecimenPhase.Stalk, 4), "nothing while it stalks");
            Assert.AreEqual(1, ArenaHazardRules.Count(SpecimenPhase.Rampage, 1));
            Assert.AreEqual(2, ArenaHazardRules.Count(SpecimenPhase.Rampage, 4));
            Assert.AreEqual(2, ArenaHazardRules.Count(SpecimenPhase.ExposedCore, 1), "an extra beside a lone player");
            Assert.AreEqual(4, ArenaHazardRules.Count(SpecimenPhase.ExposedCore, 4));
            Assert.Less(ArenaHazardRules.Interval(SpecimenPhase.ExposedCore), ArenaHazardRules.Interval(SpecimenPhase.Rampage));
            Assert.AreEqual(ArenaHazardRules.BurstDamage, ArenaHazardRules.BurstDamageAt(ArenaHazardRules.BurstRadius - 0.1f));
            Assert.AreEqual(0f, ArenaHazardRules.BurstDamageAt(ArenaHazardRules.BurstRadius + 0.1f));
        }
    }
}
