using Deadfall.Rules.Combat;
using Deadfall.Rules.Common;
using Deadfall.Rules.Encounters;
using Deadfall.Rules.Engineering;
using Deadfall.Rules.World;
using NUnit.Framework;

namespace Deadfall.Tests.EditMode.Rules
{
    /// <summary>Lull ambushes, Red Sky, Containment Breach, boss arena hazards and vent-crawling Creepers.</summary>
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

        [Test]
        public void CreepersOnlyVentWhenItSavesDistance()
        {
            Assert.IsTrue(VentRules.ShouldDive(toTarget: 30f, toGrate: 2f, exitToTarget: 6f));
            Assert.IsFalse(VentRules.ShouldDive(10f, 2f, 6f), "already close: just walk");
            Assert.IsFalse(VentRules.ShouldDive(30f, 5f, 6f), "too far from a grate");
            Assert.IsFalse(VentRules.ShouldDive(30f, 2f, 2f), "never climbs out on top of a player");
            Assert.IsFalse(VentRules.ShouldDive(18f, 2f, 12f), "saves too little");
            Assert.AreEqual(VentRules.MinTravelSeconds, VentRules.TravelSeconds(1f));
            Assert.AreEqual(VentRules.MaxTravelSeconds, VentRules.TravelSeconds(500f));
        }
    
        [Test]
        public void GridHasOneGeneratorPerSectorWithinLimits()
        {
            Assert.AreEqual(PowerGridRules.MinGenerators, PowerGridRules.GeneratorCount(1));
            Assert.AreEqual(3, PowerGridRules.GeneratorCount(3));
            Assert.AreEqual(PowerGridRules.MaxGenerators, PowerGridRules.GeneratorCount(6));
            Assert.AreEqual(0, PowerGridRules.Capacity(0));
            Assert.AreEqual(2 * PowerGridRules.CapacityPerGenerator, PowerGridRules.Capacity(2));
        }

        [Test]
        public void GridPowersOldestStructuresFirst()
        {
            var draws = new System.Collections.Generic.List<int>
            {
                PowerGridRules.Draw(StructureKind.ArcFence), // 2
                PowerGridRules.Draw(StructureKind.Sentry),   // 1
                PowerGridRules.Draw(StructureKind.ArcFence), // 2: does not fit in what is left
                PowerGridRules.Draw(StructureKind.Barricade),// 0: always on
                PowerGridRules.Draw(StructureKind.Sentry),   // 1: still fits
            };
            var powered = new System.Collections.Generic.List<bool>();
            PowerGridRules.Allocate(4, draws, powered);
            CollectionAssert.AreEqual(new[] { true, true, false, true, true }, powered);
            PowerGridRules.Allocate(0, draws, powered);
            CollectionAssert.AreEqual(new[] { false, false, false, true, false }, powered);
        }

        [Test]
        public void GridTripsNeverTakeTheLastGenerator()
        {
            var rng = new DeterministicRandom(42);
            Assert.AreEqual(-1f, PowerGridRules.RollTrip(rng, PowerGridRules.FirstTripSurge - 1, 3));
            for (int i = 0; i < 50; i++) Assert.AreEqual(-1f, PowerGridRules.RollTrip(rng, 10, 1));
            int trips = 0;
            for (int i = 0; i < 400; i++)
            {
                float t = PowerGridRules.RollTrip(rng, 10, 2);
                if (t < 0f) continue;
                trips++;
                Assert.That(t, Is.InRange(PowerGridRules.TripMinSeconds, PowerGridRules.TripMaxSeconds));
            }
            Assert.That(trips, Is.InRange(60, 200));
        }
    
        [Test]
        public void TimedObjectivesFailWhenTheClockRunsOut()
        {
            var t = new ObjectiveTracker();
            Objective failed = null;
            t.Finished += o => failed = o;
            Objective timed = t.Add(ObjectiveKind.VentReactor, 2, true, 500, 0, 10f);
            Objective plain = t.Add(ObjectiveKind.RecoverSamples, 3, false, 350);
            Assert.IsTrue(timed.IsTimed);
            Assert.IsFalse(plain.IsTimed);
            StringAssert.EndsWith("0:10", timed.Describe());
            Assert.AreEqual(0, t.TickTimers(9f));
            Assert.AreEqual(ObjectiveStatus.Active, timed.Status);
            Assert.AreEqual(1, t.TickTimers(2f));
            Assert.AreEqual(timed, failed);
            Assert.AreEqual(ObjectiveStatus.Failed, timed.Status);
            Assert.IsTrue(timed.TimedOut);
            Assert.AreEqual(ObjectiveStatus.Active, plain.Status);
            Assert.IsTrue(t.AnyRequiredFailed());
        }

        [Test]
        public void ObjectivesFinishedInTimeStopTheirClock()
        {
            var t = new ObjectiveTracker();
            Objective timed = t.Add(ObjectiveKind.VentReactor, 2, true, 500, 0, 5f);
            t.Advance(timed.Id);
            t.Advance(timed.Id);
            Assert.AreEqual(ObjectiveStatus.Complete, timed.Status);
            Assert.AreEqual(0, t.TickTimers(10f));
            Assert.AreEqual(ObjectiveStatus.Complete, timed.Status);
            Assert.IsFalse(t.AnyRequiredFailed());
            Assert.AreEqual("2:05", ObjectiveRules.Clock(124.2f));
            Assert.AreEqual("0:00", ObjectiveRules.Clock(-3f));
        }

        [Test]
        public void NewMissionsHaveFailableRequiredSteps()
        {
            Assert.AreSame(ObjectiveRules.Quarantine, ObjectiveRules.MissionById("mission.quarantine"));
            Assert.AreSame(ObjectiveRules.Meltdown, ObjectiveRules.MissionById("mission.meltdown"));
            Assert.AreSame(ObjectiveRules.DeadAir, ObjectiveRules.MissionById("mission.dead_air"));
            Assert.IsTrue(System.Array.Exists(ObjectiveRules.Quarantine.Steps, s => s.Required && s.Kind == ObjectiveKind.SealContainers));
            Assert.IsTrue(System.Array.Exists(ObjectiveRules.Meltdown.Steps, s => s.Required && s.Kind == ObjectiveKind.VentReactor && s.TimeLimit > 0f));
            Assert.AreEqual(ObjectiveRules.Containers, ObjectiveRules.Target(ObjectiveKind.SealContainers));
            Assert.AreEqual(ObjectiveRules.Valves, ObjectiveRules.Target(ObjectiveKind.VentReactor));
            Assert.Greater(ObjectiveRules.SideTimeLimit(ObjectiveKind.VentReactor), 0f);
            Assert.AreEqual(0f, ObjectiveRules.SideTimeLimit(ObjectiveKind.RecoverSamples));
            var ids = new System.Collections.Generic.HashSet<string>();
            foreach (MissionTemplate m in ObjectiveRules.Missions) Assert.IsTrue(ids.Add(m.Id), m.Id);
        }

        [Test]
        public void RunsCanBeLostOnPurpose()
        {
            var run = new SurgeRun(new SurgeRunSettings());
            Assert.AreEqual(SurgeRunEvents.None, run.Fail());
            run.Start(0);
            Assert.AreEqual(SurgeRunEvents.RunFailed, run.Fail());
            Assert.AreEqual(RunPhase.Failed, run.Phase);
            Assert.IsFalse(run.IsRunning);
        }
    
        [Test]
        public void GunDispenserOnlyMovesAfterAFewUses()
        {
            for (int uses = 0; uses < GunDispenserRules.MinUsesBeforeMove; uses++) Assert.AreEqual(0f, GunDispenserRules.MoveChance(uses));
            Assert.Greater(GunDispenserRules.MoveChance(GunDispenserRules.MinUsesBeforeMove), 0f);
            Assert.Greater(GunDispenserRules.MoveChance(GunDispenserRules.MinUsesBeforeMove + 2), GunDispenserRules.MoveChance(GunDispenserRules.MinUsesBeforeMove));
            Assert.AreEqual(GunDispenserRules.MaxMoveChance, GunDispenserRules.MoveChance(100));
            var rng = new DeterministicRandom(5);
            for (int i = 0; i < 200; i++) Assert.IsFalse(GunDispenserRules.RollMove(rng, 100, false), "nowhere to go");
            int moves = 0;
            for (int i = 0; i < 1000; i++) if (GunDispenserRules.RollMove(rng, 100, true)) moves++;
            Assert.That(moves, Is.InRange(400, 600));
        }

        [Test]
        public void GunDispenserNeverRepeatsTheHeldWeapon()
        {
            var rng = new DeterministicRandom(9);
            var guns = new System.Collections.Generic.List<int> { 0, 2, 3, 5 };
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < 400; i++)
            {
                int pick = GunDispenserRules.PickIndex(rng, guns, 3);
                Assert.AreNotEqual(3, pick);
                Assert.IsTrue(guns.Contains(pick));
                seen.Add(pick);
            }
            Assert.AreEqual(3, seen.Count);
            Assert.AreEqual(7, GunDispenserRules.PickIndex(rng, new System.Collections.Generic.List<int> { 7 }, 7), "only choice");
            Assert.AreEqual(-1, GunDispenserRules.PickIndex(rng, new System.Collections.Generic.List<int>(), 0));
        }
    }
}
