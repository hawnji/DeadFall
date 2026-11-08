using System.Collections.Generic;
using Deadfall.Rules.Encounters;
using Deadfall.Rules.Progression;
using Deadfall.Rules.World;
using NUnit.Framework;

namespace Deadfall.Tests.EditMode.Rules
{
    /// <summary>M12: the second environment, environment seeds, Endless escalation and rewards, Operations per environment.</summary>
    public class ModesContentTests
    {
        [Test]
        public void HarrowLineSeedsProduceValidLayouts()
        {
            RoomKit kit = HarrowLineKit.Create();
            GraphTemplate g = HarrowLineKit.SurvivalGraph();
            int fallbacks = 0;
            var hashes = new HashSet<ulong>();
            for (ulong seed = 1; seed <= 1500; seed++)
            {
                Layout l = LayoutGenerator.Generate(kit, g, seed);
                Assert.IsNotNull(l, $"seed {seed}");
                List<string> errors = LayoutValidator.Validate(l);
                Assert.IsEmpty(errors, $"seed {seed}: {string.Join("; ", errors)}");
                if (l.UsedFallback) fallbacks++;
                hashes.Add(l.Hash());
                Assert.AreEqual(RoomRole.Spawn, l.Rooms[0].Template.Role);
                Assert.IsNotNull(l.FirstWithRole(RoomRole.Boss));
                Assert.IsNotNull(l.FirstWithRole(RoomRole.Objective));
            }
            Assert.Less(fallbacks, 10);
            Assert.Greater(hashes.Count, 1450);
        }

        [Test]
        public void MercyValeSeedsProduceValidLayouts()
        {
            RoomKit kit = MercyValeKit.Create();
            GraphTemplate g = MercyValeKit.SurvivalGraph();
            int fallbacks = 0;
            for (ulong seed = 1; seed <= 1000; seed++)
            {
                Layout l = LayoutGenerator.Generate(kit, g, seed);
                Assert.IsNotNull(l, $"seed {seed}");
                List<string> errors = LayoutValidator.Validate(l);
                Assert.IsEmpty(errors, $"seed {seed}: {string.Join("; ", errors)}");
                if (l.UsedFallback) fallbacks++;
                Assert.IsNotNull(l.FirstWithRole(RoomRole.Boss));
                Assert.IsNotNull(l.FirstWithRole(RoomRole.Objective));
            }
            Assert.Less(fallbacks, 10);
            Assert.AreEqual(MercyValeKit.Id, EnvironmentCatalog.ForSeed(EnvironmentCatalog.Encode(77UL, 2)).Id);
            Assert.AreEqual(ObjectiveRules.Quarantine, ObjectiveRules.MissionById(EnvironmentCatalog.All[2].MissionId));
        }

        [Test]
        public void EveryEnvironmentSocketSitsOnTheEdgeItFaces()
        {
            foreach (EnvironmentDef env in EnvironmentCatalog.All)
                foreach (RoomTemplate t in env.CreateKit().Templates)
                    foreach (SocketDef s in t.Sockets)
                        Assert.IsTrue(t.HasCell(s.X, s.Z) && !t.HasCell(s.X + s.Facing.Dx(), s.Z + s.Facing.Dz()), $"{env.Id} {t.Id} ({s.X},{s.Z}) {s.Facing}");
        }

        [Test]
        public void EnvironmentRidesInTheSeed()
        {
            Assert.AreEqual(0, EnvironmentCatalog.IndexOf(12345UL), "plain seeds are Cinder Ridge");
            Assert.AreEqual(0, EnvironmentCatalog.IndexOf(638_000_000_000_000_000UL), "tick-sized seeds stay Cinder Ridge");
            ulong h = EnvironmentCatalog.Encode(12345UL, 1);
            Assert.AreEqual(1, EnvironmentCatalog.IndexOf(h));
            Assert.AreEqual(HarrowLineKit.Id, EnvironmentCatalog.ForSeed(h).Id);
            Assert.AreNotEqual(0UL, EnvironmentCatalog.Encode(0UL, 0), "0 means no map");
            Assert.AreEqual(0, EnvironmentCatalog.IndexOf(EnvironmentCatalog.Encode(99UL, 0)));
            Assert.AreEqual(EnvironmentCatalog.All.Length - 1, EnvironmentCatalog.IndexOf(EnvironmentCatalog.Encode(5UL, 99)), "index clamps");
            for (int i = 0; i < EnvironmentCatalog.All.Length; i++) Assert.AreEqual(i, EnvironmentCatalog.All[i].Index);
        }

        [Test]
        public void EachEnvironmentHasItsOwnOperation()
        {
            Assert.AreEqual(ObjectiveRules.ColdStart, ObjectiveRules.MissionById(EnvironmentCatalog.All[0].MissionId));
            Assert.AreEqual(ObjectiveRules.LastTrain, ObjectiveRules.MissionById(EnvironmentCatalog.All[1].MissionId));
            Assert.AreEqual(ObjectiveRules.ColdStart, ObjectiveRules.MissionById("nope"));
            foreach (MissionTemplate m in ObjectiveRules.Missions) Assert.IsTrue(System.Array.Exists(m.Steps, s => s.Required), m.Id);
        }

        [Test]
        public void EndlessEscalatesFromSurge21EveryFiveSurges()
        {
            Assert.AreEqual(0, EndlessRules.Steps(20));
            Assert.AreEqual(EndlessModifier.None, EndlessRules.Active(20));
            Assert.AreEqual(EndlessModifier.Armored, EndlessRules.Active(21));
            Assert.AreEqual(EndlessModifier.Armored | EndlessModifier.Swarming, EndlessRules.Active(26));
            Assert.IsTrue(EndlessRules.StepsUpAt(31));
            Assert.IsFalse(EndlessRules.StepsUpAt(32));
            Assert.AreEqual(5, EndlessRules.Steps(41));
            Assert.AreEqual(1, EndlessRules.Stacks(45, EndlessModifier.Armored));
            Assert.AreEqual(2, EndlessRules.Stacks(46, EndlessModifier.Armored), "all five active, then they stack again");
            Assert.AreEqual("Armored x2 · Swarming · Frenzied · Elite Tide · Relentless", EndlessRules.Describe(46));
            Assert.AreEqual(string.Empty, EndlessRules.Describe(10));
        }

        [Test]
        public void EndlessModifiersMakeSurgesHarder()
        {
            var options = new List<EnemyOption> { new EnemyOption("a", 1f, 1, 1f, 1f, Deadfall.Rules.Combat.EnemyRole.Regular, true) };
            var normal = new WavePlanner(new DifficultyCurve(new DifficultySettings()), options);
            var endless = new WavePlanner(new DifficultyCurve(new DifficultySettings()), options) { Endless = true };
            SurgePlan a = normal.Plan(32, 2, 7UL, SurgeType.Standard);
            SurgePlan b = endless.Plan(32, 2, 7UL, SurgeType.Standard);
            Assert.Greater(b.HealthScale, a.HealthScale);
            Assert.Greater(b.Budget, a.Budget);
            Assert.GreaterOrEqual(b.Concurrency, a.Concurrency);
            Assert.AreEqual(EndlessRules.Active(32), b.Endless);
            Assert.AreEqual(1f, EndlessRules.LullMultiplier(40));
            Assert.Less(EndlessRules.LullMultiplier(41), 1f);
            Assert.GreaterOrEqual(EndlessRules.LullMultiplier(400), 0.35f);
            SurgePlan early = endless.Plan(10, 2, 7UL, SurgeType.Standard);
            Assert.AreEqual(normal.Plan(10, 2, 7UL, SurgeType.Standard).Budget, early.Budget, 1e-3, "no change before surge 21");
        }

        [Test]
        public void EndlessKeepsAllRewardsAndPaysForDepth()
        {
            RunReward shallow = RunRewardRules.Compute(new RunResult { Endless = true, SurgesCleared = 10, Kills = 100 });
            RunReward failed = RunRewardRules.Compute(new RunResult { SurgesCleared = 10, Kills = 100 });
            Assert.IsFalse(shallow.Retained);
            Assert.AreEqual(failed.OperatorXp * 2, shallow.OperatorXp, 1, "no failure cut in Endless");
            RunReward deep = RunRewardRules.Compute(new RunResult { Endless = true, SurgesCleared = 30, Kills = 100 });
            Assert.Greater(deep.OperatorXp, shallow.OperatorXp + 2000);
            var p = new PlayerProfile();
            ProfileRules.ApplyRun(p, null, new RunResult { Endless = true, SurgesCleared = 27 }, ChallengeCatalog.Prototype());
            ProfileRules.ApplyRun(p, null, new RunResult { Endless = true, SurgesCleared = 12 }, ChallengeCatalog.Prototype());
            Assert.AreEqual(27, p.Stat(StatKeys.EndlessBest), "best is a maximum");
            Assert.IsTrue(p.Completed.Count > 0);
        }

        [Test]
        public void PreferredBossIsUsedWhenPresent()
        {
            var options = new List<EnemyOption>
            {
                new EnemyOption("grunt", 1f, 1, 1f, 1f, Deadfall.Rules.Combat.EnemyRole.Regular, true),
                new EnemyOption("boss.a", 40f, 1, 1f, 1f, Deadfall.Rules.Combat.EnemyRole.Boss, false),
                new EnemyOption("boss.b", 40f, 1, 1f, 1f, Deadfall.Rules.Combat.EnemyRole.Boss, false),
            };
            var planner = new WavePlanner(new DifficultyCurve(new DifficultySettings()), options) { PreferredBossId = "boss.b" };
            for (ulong seed = 1; seed < 20; seed++) Assert.AreEqual("boss.b", planner.Plan(10, 1, seed, SurgeType.Specimen).BossId);
            planner.PreferredBossId = "missing";
            Assert.IsNotNull(planner.Plan(10, 1, 3UL, SurgeType.Specimen).BossId, "falls back to any boss");
        }
    }
}
