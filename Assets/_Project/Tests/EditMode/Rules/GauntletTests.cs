using System.Collections.Generic;
using Deadfall.Rules.Encounters;
using Deadfall.Rules.Progression;
using Deadfall.Rules.World;
using NUnit.Framework;

namespace Deadfall.Tests.EditMode.Rules
{
    public class GauntletTests
    {
        [Test]
        public void SurgesPlayAsFiveToTenEndingInASpecimen()
        {
            Assert.AreEqual(5, GauntletRules.PlannedSurge(1));
            Assert.AreEqual(10, GauntletRules.PlannedSurge(GauntletRules.Surges));
            Assert.AreEqual(SurgeType.Specimen, WavePlanner.TypeFor(GauntletRules.PlannedSurge(GauntletRules.Surges)));
        }

        [Test]
        public void ClockShowsMinutesSecondsTenths()
        {
            Assert.AreEqual("0:00.0", GauntletRules.Clock(0));
            Assert.AreEqual("7:42.3", GauntletRules.Clock(462.37));
            Assert.AreEqual("8:00.0", GauntletRules.Clock(GauntletRules.ParSeconds));
            Assert.AreEqual("61:05.0", GauntletRules.Clock(3665));
        }

        [Test]
        public void GauntletSeedKeepsItsEnvironmentAndIsFixed()
        {
            for (int env = 0; env < EnvironmentCatalog.All.Length; env++)
            {
                ulong seed = EnvironmentCatalog.GauntletSeed(env, GauntletRules.MapSeed);
                Assert.IsTrue(EnvironmentCatalog.IsGauntlet(seed));
                Assert.AreEqual(env, EnvironmentCatalog.IndexOf(seed));
                Assert.AreEqual(seed, EnvironmentCatalog.GauntletSeed(env, GauntletRules.MapSeed));
                Assert.IsFalse(EnvironmentCatalog.IsGauntlet(EnvironmentCatalog.Encode(12345UL, env)));
            }
        }

        [Test]
        public void GauntletMapsAreValidShortAndShopFreeWithMoreAmmo()
        {
            for (int env = 0; env < EnvironmentCatalog.All.Length; env++)
            {
                ulong seed = EnvironmentCatalog.GauntletSeed(env, GauntletRules.MapSeed);
                EnvironmentDef def = EnvironmentCatalog.At(env);
                GraphTemplate g = EnvironmentCatalog.GraphFor(seed);
                Layout l = LayoutGenerator.Generate(def.CreateKit(), g, seed);
                Assert.IsNotNull(l, def.Id);
                List<string> errors = LayoutValidator.Validate(l);
                Assert.IsEmpty(errors, def.Id + ": " + string.Join("; ", errors));
                Assert.LessOrEqual(l.SectorCount, 3, def.Id);
                Assert.IsFalse(l.Loot.Exists(x => x.Kind == LootKind.SupplyCache), def.Id);

                GraphTemplate normal = def.CreateGraph();
                Assert.Less(g.CriticalRoomsMax, normal.CriticalRoomsMin, "shorter than Survival");
                Assert.Greater(g.LootChance * g.AmmoShare, normal.LootChance * normal.AmmoShare, "more ammo per loot slot");
            }
        }

        [Test]
        public void BestTimeIsTheLowestClearAndOnlyWins()
        {
            var p = new PlayerProfile();
            string key = GauntletRecords.BestKey(1);
            RunSummary first = ProfileRules.ApplyRun(p, null, new RunResult { Won = true, Gauntlet = true, GauntletEnvironment = 1, GauntletMs = 500_000, RunId = "a" }, null);
            Assert.IsTrue(first.GauntletNewBest);
            Assert.AreEqual(0, first.GauntletBestBefore);
            Assert.AreEqual(500_000, p.Stat(key));

            RunSummary slower = ProfileRules.ApplyRun(p, null, new RunResult { Won = true, Gauntlet = true, GauntletEnvironment = 1, GauntletMs = 520_000, RunId = "b" }, null);
            Assert.IsFalse(slower.GauntletNewBest);
            Assert.AreEqual(500_000, p.Stat(key));

            ProfileRules.ApplyRun(p, null, new RunResult { Won = false, Gauntlet = true, GauntletEnvironment = 1, GauntletMs = 100_000, RunId = "c" }, null);
            Assert.AreEqual(500_000, p.Stat(key), "a failed run never sets a time");

            RunSummary faster = ProfileRules.ApplyRun(p, null, new RunResult { Won = true, Gauntlet = true, GauntletEnvironment = 1, GauntletMs = 450_000, RunId = "d" }, null);
            Assert.IsTrue(faster.GauntletNewBest);
            Assert.AreEqual(500_000, faster.GauntletBestBefore);
            Assert.AreEqual(450_000, p.Stat(key));
            Assert.AreEqual(0, p.Stat(GauntletRecords.BestKey(0)), "times are per environment");
            Assert.AreEqual(3, p.Stat(StatKeys.GauntletsWon));
        }
    }
}
