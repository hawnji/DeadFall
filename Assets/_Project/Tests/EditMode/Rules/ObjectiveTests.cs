using Deadfall.Rules.Common;
using Deadfall.Rules.Encounters;
using NUnit.Framework;

namespace Deadfall.Tests.EditMode.Rules
{
    public class ObjectiveTests
    {
        [Test]
        public void TrackerAdvancesToTargetAndReportsOnce()
        {
            var t = new ObjectiveTracker();
            int finished = 0;
            t.Finished += _ => finished++;
            Objective o = t.Add(ObjectiveKind.RestorePower, 3, true, 400);
            Assert.IsFalse(t.Advance(o.Id));
            Assert.IsFalse(t.Advance(o.Id));
            Assert.IsTrue(t.Advance(o.Id));
            Assert.IsFalse(t.Advance(o.Id), "already complete");
            Assert.AreEqual(ObjectiveStatus.Complete, o.Status);
            Assert.AreEqual(1, finished);
            Assert.AreEqual("Restart the generators 3/3", o.Describe());
        }

        [Test]
        public void SideObjectivesExpireAfterTheirSurge()
        {
            var t = new ObjectiveTracker();
            Objective a = t.Add(ObjectiveKind.DestroyNest, 1, false, 500, expiresAfterSurge: 4);
            Objective b = t.Add(ObjectiveKind.RetrieveDataCore, 1, true, 0);
            Assert.AreEqual(0, t.ExpireAfter(3));
            Assert.AreEqual(1, t.ExpireAfter(4));
            Assert.AreEqual(ObjectiveStatus.Failed, a.Status);
            Assert.AreEqual(ObjectiveStatus.Active, b.Status);
            t.PruneFinished();
            Assert.AreEqual(1, t.All.Count);
        }

        [Test]
        public void RequiredCompleteIgnoresOptionalAndExtraction()
        {
            var t = new ObjectiveTracker();
            Assert.IsFalse(t.RequiredComplete(ObjectiveKind.Extraction), "nothing required yet");
            Objective power = t.Add(ObjectiveKind.RestorePower, 1, true, 0);
            t.Add(ObjectiveKind.DestroyNest, 1, false, 0);
            t.Add(ObjectiveKind.Extraction, 1, true, 0);
            Assert.IsFalse(t.RequiredComplete(ObjectiveKind.Extraction));
            t.Advance(power.Id);
            Assert.IsTrue(t.RequiredComplete(ObjectiveKind.Extraction));
        }

        [Test]
        public void ChannelFillsInRangeAndDrainsAtHalfSpeed()
        {
            var c = new ChannelProgress(4f);
            Assert.IsFalse(c.Tick(1f, true), "not started");
            Assert.AreEqual(0f, c.Value01);
            c.Start();
            c.Tick(2f, true);
            Assert.AreEqual(0.5f, c.Value01, 0.0001f);
            c.Tick(2f, false);
            Assert.AreEqual(0.25f, c.Value01, 0.0001f);
            Assert.IsTrue(c.Tick(3f, true));
            Assert.IsTrue(c.Complete);
            Assert.IsFalse(c.Tick(1f, true), "reports completion once");
        }

        [Test]
        public void HoldTimerPausesWhenZoneEmpty()
        {
            var h = new HoldTimer(10f);
            h.Tick(4f, true);
            h.Tick(100f, false);
            Assert.AreEqual(6f, h.Remaining, 0.0001f);
            Assert.IsTrue(h.Tick(6f, true));
            Assert.IsTrue(h.Complete);
        }

        [Test]
        public void SideObjectiveScheduleAndPicks()
        {
            Assert.IsFalse(ObjectiveRules.OfferSideObjective(1));
            Assert.IsTrue(ObjectiveRules.OfferSideObjective(3));
            Assert.IsFalse(ObjectiveRules.OfferSideObjective(5));
            Assert.IsTrue(ObjectiveRules.OfferSideObjective(7));
            var rng = new DeterministicRandom(5);
            ObjectiveKind? last = null;
            for (int i = 0; i < 200; i++)
            {
                ObjectiveKind k = ObjectiveRules.PickSide(rng, last);
                Assert.AreNotEqual(last, k);
                Assert.AreNotEqual(ObjectiveKind.Extraction, k);
                Assert.AreNotEqual(ObjectiveKind.LiftLockdown, k);
                last = k;
            }
        }

        [Test]
        public void ColdStartHasRequiredAndOptionalSteps()
        {
            MissionTemplate m = ObjectiveRules.ColdStart;
            int required = 0, optional = 0;
            foreach (MissionStep s in m.Steps)
            {
                if (s.Required) required++;
                else optional++;
                Assert.AreNotEqual(ObjectiveKind.Extraction, s.Kind, "extraction is added when required steps are done");
            }
            Assert.AreEqual(2, required);
            Assert.AreEqual(2, optional);
            Assert.That(m.ExtractionSeconds, Is.InRange(120f, 180f));
        }
    }

    public class RunEventTests
    {
        [Test]
        public void NoEventsEarlyOrOnBossAndHuntSurges()
        {
            var s = new EventScheduler();
            var rng = new DeterministicRandom(1);
            for (int i = 0; i < 300; i++)
            {
                Assert.AreEqual(RunEventKind.None, s.Roll(rng, 3, SurgeType.Standard, true, 3));
                Assert.AreEqual(RunEventKind.None, s.Roll(rng, 10, SurgeType.Specimen, true, 3));
                Assert.AreEqual(RunEventKind.None, s.Roll(rng, 12, SurgeType.Hunt, true, 3));
            }
        }

        [Test]
        public void CooldownBetweenEvents()
        {
            var s = new EventScheduler();
            var rng = new DeterministicRandom(9);
            int lastSurge = -100;
            int events = 0;
            for (int surge = 4; surge < 400; surge++)
            {
                if (s.Roll(rng, surge, SurgeType.Standard, true, 3) == RunEventKind.None) continue;
                Assert.Greater(surge - lastSurge, EventScheduler.CooldownSurges);
                lastSurge = surge;
                events++;
            }
            Assert.Greater(events, 30);
        }

        [Test]
        public void LockdownNeedsAMapWithTwoSectorsOpen()
        {
            var rng = new DeterministicRandom(4);
            for (int i = 0; i < 2000; i++)
            {
                var s = new EventScheduler();
                Assert.AreNotEqual(RunEventKind.Lockdown, s.Roll(rng, 8, SurgeType.Standard, false, 0));
                Assert.AreNotEqual(RunEventKind.Lockdown, s.Roll(rng, 8, SurgeType.Standard, true, 1));
            }
            bool sawLockdown = false;
            for (int i = 0; i < 2000 && !sawLockdown; i++)
                sawLockdown = new EventScheduler().Roll(rng, 8, SurgeType.Standard, true, 2) == RunEventKind.Lockdown;
            Assert.IsTrue(sawLockdown);
        }
    }
}
