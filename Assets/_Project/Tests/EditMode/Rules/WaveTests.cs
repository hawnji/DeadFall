using System.Collections.Generic;
using Deadfall.Rules.Encounters;
using NUnit.Framework;

namespace Deadfall.Tests.EditMode.Rules
{
    public class WavePlannerTests
    {
        private static readonly EnemyOption[] Options =
        {
            new EnemyOption("husk", 1f, 1, 10f),
            new EnemyOption("bolter", 1.5f, 3, 4f),
            new EnemyOption("bulwark", 6f, 6, 1f, 0.4f),
        };

        private static WavePlanner Planner() => new WavePlanner(new DifficultyCurve(new DifficultySettings()), Options);

        [Test]
        public void BudgetRisesEverySurgeAndWithPlayers()
        {
            var curve = new DifficultyCurve(new DifficultySettings());
            for (int s = 1; s < 40; s++)
                Assert.Greater(curve.Budget(s + 1, 1), curve.Budget(s, 1), $"surge {s}");
            for (int p = 1; p < 4; p++)
                Assert.Greater(curve.Budget(5, p + 1), curve.Budget(5, p), $"players {p}");
        }

        [Test]
        public void AdaptiveFactorMovesBudgetAtMostTenPercent()
        {
            var curve = new DifficultyCurve(new DifficultySettings());
            float baseBudget = curve.Budget(10, 2);
            Assert.AreEqual(baseBudget * 1.1f, curve.Budget(10, 2, 5f), 0.001f);
            Assert.AreEqual(baseBudget * 0.9f, curve.Budget(10, 2, -5f), 0.001f);
        }

        [Test]
        public void HealthScaleIsCapped()
        {
            var curve = new DifficultyCurve(new DifficultySettings());
            Assert.AreEqual(1f, curve.HealthScale(1), 0.0001f);
            Assert.AreEqual(2.5f, curve.HealthScale(30), 0.0001f);
            Assert.AreEqual(2.5f, curve.HealthScale(80), 0.0001f);
        }

        [Test]
        public void UnlockSurgesAreRespected()
        {
            WavePlanner planner = Planner();
            for (ulong seed = 1; seed <= 20; seed++)
            {
                SurgePlan s1 = planner.Plan(1, 1, seed, SurgeType.Standard);
                Assert.AreEqual(0, s1.Count("bolter"));
                Assert.AreEqual(0, s1.Count("bulwark"));
                SurgePlan s4 = planner.Plan(4, 1, seed, SurgeType.Standard);
                Assert.AreEqual(0, s4.Count("bulwark"));
            }
        }

        [Test]
        public void SameSeedGivesSamePlan()
        {
            SurgePlan a = Planner().Plan(9, 3, 1234);
            SurgePlan b = Planner().Plan(9, 3, 1234);
            CollectionAssert.AreEqual(a.Spawns, b.Spawns);
            SurgePlan c = Planner().Plan(9, 3, 999);
            CollectionAssert.AreNotEqual(a.Spawns, c.Spawns);
        }

        [Test]
        public void PlanStaysWithinBudget()
        {
            WavePlanner planner = Planner();
            for (int s = 1; s <= 30; s++)
            {
                SurgePlan p = planner.Plan(s, 4, 7);
                Assert.LessOrEqual(p.BudgetSpent, p.Budget + 0.001f, $"surge {s}");
                Assert.Greater(p.Spawns.Count, 0);
            }
        }

        [Test]
        public void SwarmUsesOnlyCheapTypesAndMoreAtOnce()
        {
            WavePlanner planner = Planner();
            SurgePlan standard = planner.Plan(12, 2, 5, SurgeType.Standard);
            SurgePlan swarm = planner.Plan(12, 2, 5, SurgeType.Swarm);
            Assert.AreEqual(0, swarm.Count("bolter"));
            Assert.AreEqual(0, swarm.Count("bulwark"));
            Assert.Greater(swarm.Spawns.Count, standard.Spawns.Count);
            Assert.Greater(swarm.Concurrency, standard.Concurrency);
        }

        [Test]
        public void SiegeComesFromAtLeastThreeDirectionsAndUnlocksHeaviesEarly()
        {
            WavePlanner planner = Planner();
            SurgePlan siege = planner.Plan(5, 1, 3, SurgeType.Siege);
            Assert.GreaterOrEqual(siege.Directions, 3);
            bool anyBulwark = false;
            for (ulong seed = 1; seed <= 30 && !anyBulwark; seed++)
                anyBulwark = planner.Plan(5, 1, seed, SurgeType.Siege).Count("bulwark") > 0;
            Assert.IsTrue(anyBulwark, "Siege at surge 5 should be able to field a Bulwark (unlock 6 minus 2).");
        }

        [Test]
        public void BudgetShareCapLimitsHeavies()
        {
            WavePlanner planner = Planner();
            for (ulong seed = 1; seed <= 20; seed++)
            {
                SurgePlan p = planner.Plan(20, 4, seed, SurgeType.Standard);
                float bulwarkSpend = p.Count("bulwark") * 6f;
                Assert.LessOrEqual(bulwarkSpend, p.Budget * 0.4f + 0.001f);
            }
        }

        [Test]
        public void AlwaysAtLeastOneEnemyEvenWithTinyBudget()
        {
            var settings = new DifficultySettings { BaseBudget = 0.1f, BudgetPerSurge = 0f, BudgetPerSurgeSquared = 0f };
            var planner = new WavePlanner(new DifficultyCurve(settings), Options);
            SurgePlan p = planner.Plan(1, 1, 1);
            Assert.AreEqual(1, p.Spawns.Count);
            Assert.AreEqual("husk", p.Spawns[0]);
        }

        [Test]
        public void SpawnCountIsCapped()
        {
            var settings = new DifficultySettings { BaseBudget = 100000f };
            var planner = new WavePlanner(new DifficultyCurve(settings), Options);
            Assert.AreEqual(WavePlanner.MaxSpawnsPerSurge, planner.Plan(1, 4, 1).Spawns.Count);
        }

        [Test]
        public void TypeRotation()
        {
            var types = new List<SurgeType>();
            for (int s = 1; s <= 10; s++) types.Add(WavePlanner.TypeFor(s));
            CollectionAssert.AreEqual(new[]
            {
                SurgeType.Standard, SurgeType.Standard, SurgeType.Standard, SurgeType.Swarm, SurgeType.Siege,
                SurgeType.Hunt, SurgeType.Elite, SurgeType.Swarm, SurgeType.Supply, SurgeType.Specimen,
            }, types);
        }
    }

    public class SurgeRunTests
    {
        private static SurgeRunInput In(double now, bool allSpawned = false, int alive = 0, bool playersAlive = true, bool ready = false) =>
            new SurgeRunInput { Now = now, AllSpawned = allSpawned, Alive = alive, AnyPlayerAlive = playersAlive, AllReady = ready };

        private static SurgeRun NewRun(int finalSurge = 20) =>
            new SurgeRun(new SurgeRunSettings { FirstLullSeconds = 10f, LullSeconds = 30f, RewardSeconds = 3f, FinalSurge = finalSurge, StragglerTimeoutSeconds = 40f });

        [Test]
        public void FullCycleLullSurgeRewardLull()
        {
            SurgeRun run = NewRun();
            Assert.AreEqual(SurgeRunEvents.LullStarted, run.Start(0));
            Assert.AreEqual(RunPhase.Lull, run.Phase);
            Assert.AreEqual(1, run.Surge);
            Assert.AreEqual(SurgeRunEvents.None, run.Tick(In(9.9)));
            Assert.AreEqual(SurgeRunEvents.SurgeStarted, run.Tick(In(10)));
            Assert.AreEqual(RunPhase.Surge, run.Phase);
            // Spawning, then fighting: not cleared while anything is left to spawn or alive.
            Assert.AreEqual(SurgeRunEvents.None, run.Tick(In(11, allSpawned: false, alive: 0)));
            Assert.AreEqual(SurgeRunEvents.None, run.Tick(In(12, allSpawned: true, alive: 3)));
            Assert.AreEqual(SurgeRunEvents.SurgeCleared, run.Tick(In(20, allSpawned: true, alive: 0)));
            Assert.AreEqual(RunPhase.Reward, run.Phase);
            Assert.AreEqual(SurgeRunEvents.None, run.Tick(In(22.9)));
            Assert.AreEqual(SurgeRunEvents.LullStarted, run.Tick(In(23)));
            Assert.AreEqual(2, run.Surge);
            Assert.AreEqual(30.0, run.TimeLeft(23), 0.0001);
        }

        [Test]
        public void ClearIsReportedOnce()
        {
            SurgeRun run = NewRun();
            run.Start(0);
            run.Tick(In(10));
            Assert.AreEqual(SurgeRunEvents.SurgeCleared, run.Tick(In(11, true, 0)));
            Assert.AreEqual(SurgeRunEvents.None, run.Tick(In(11.5, true, 0)));
        }

        [Test]
        public void SkipAndReadyEndTheLullEarly()
        {
            SurgeRun run = NewRun();
            run.Start(0);
            run.SkipLull(2);
            Assert.AreEqual(SurgeRunEvents.SurgeStarted, run.Tick(In(2)));

            SurgeRun run2 = NewRun();
            run2.Start(0);
            Assert.AreEqual(SurgeRunEvents.SurgeStarted, run2.Tick(In(1, ready: true)));
        }

        [Test]
        public void FinalSurgeCompletesTheRun()
        {
            SurgeRun run = NewRun(finalSurge: 2);
            run.Start(0);
            run.Tick(In(10));
            run.Tick(In(11, true, 0));
            run.Tick(In(14));
            run.Tick(In(44));
            Assert.AreEqual(2, run.Surge);
            Assert.AreEqual(RunPhase.Surge, run.Phase);
            Assert.AreEqual(SurgeRunEvents.SurgeCleared | SurgeRunEvents.RunCompleted, run.Tick(In(50, true, 0)));
            Assert.AreEqual(RunPhase.Complete, run.Phase);
            Assert.IsFalse(run.IsRunning);
            Assert.AreEqual(SurgeRunEvents.None, run.Tick(In(60)));
        }

        [Test]
        public void EndlessNeverCompletes()
        {
            SurgeRun run = NewRun(finalSurge: 0);
            run.Start(0);
            double t = 0;
            for (int s = 1; s <= 50; s++)
            {
                run.SkipLull(t);
                run.Tick(In(t));
                Assert.AreEqual(SurgeRunEvents.SurgeCleared, run.Tick(In(t + 1, true, 0)));
                t += 5;
                run.Tick(In(t));
            }
            Assert.AreEqual(51, run.Surge);
        }

        [Test]
        public void WholeSquadDownFailsTheRun()
        {
            SurgeRun run = NewRun();
            run.Start(0);
            run.Tick(In(10));
            Assert.AreEqual(SurgeRunEvents.RunFailed, run.Tick(In(15, true, 5, playersAlive: false)));
            Assert.AreEqual(RunPhase.Failed, run.Phase);
            Assert.AreEqual(SurgeRunEvents.None, run.Tick(In(16, true, 0)));
        }

        [Test]
        public void StragglersAreCulledAfterTimeoutWithoutKills()
        {
            SurgeRun run = NewRun();
            run.Start(0);
            run.Tick(In(10));
            run.NotifyKill(20);
            Assert.AreEqual(SurgeRunEvents.None, run.Tick(In(59, true, 2)));
            Assert.AreEqual(SurgeRunEvents.CullStragglers, run.Tick(In(60, true, 2)));
            // Asked once; the engine removes them, then the surge clears normally.
            Assert.AreEqual(SurgeRunEvents.None, run.Tick(In(61, true, 2)));
            Assert.AreEqual(SurgeRunEvents.SurgeCleared, run.Tick(In(62, true, 0)));
        }

        [Test]
        public void NoCullWhileStillSpawning()
        {
            SurgeRun run = NewRun();
            run.Start(0);
            run.Tick(In(10));
            Assert.AreEqual(SurgeRunEvents.None, run.Tick(In(100, false, 5)));
        }

        [Test]
        public void SurgeThatCannotSpawnStillEnds()
        {
            SurgeRun run = NewRun();
            run.Start(0);
            run.Tick(In(10));
            Assert.AreEqual(SurgeRunEvents.SurgeCleared, run.Tick(In(10 + run.Settings.MaxSurgeSeconds, false, 0)));
        }

        [Test]
        public void CreditRules()
        {
            SurgeRun run = NewRun();
            Assert.AreEqual(10, run.KillCredits(1f));
            Assert.AreEqual(15, run.KillCredits(1.5f));
            Assert.AreEqual(60, run.KillCredits(6f));
            Assert.AreEqual(60, run.ClearBonus(1));
            Assert.AreEqual(250, run.ClearBonus(20));
        }

        [Test]
        public void LedgerIsPerPlayerAndNeverNegative()
        {
            var ledger = new CreditLedger();
            ledger.Add(0, 100);
            ledger.Add(1, 30);
            Assert.IsFalse(ledger.TrySpend(1, 50));
            Assert.AreEqual(30, ledger.Get(1));
            Assert.IsTrue(ledger.TrySpend(0, 40));
            Assert.AreEqual(60, ledger.Get(0));
            ledger.Add(1, -500);
            Assert.AreEqual(0, ledger.Get(1));
            Assert.AreEqual(0, ledger.Get(7));
        }
    }
}
