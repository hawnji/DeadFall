using Deadfall.Rules.Engineering;
using NUnit.Framework;

namespace Deadfall.Tests.EditMode.Rules
{
    public class EngineeringTests
    {
        private static PlacementQuery Good(StructureKind kind = StructureKind.Sentry, bool engineer = true) => new PlacementQuery
        {
            Kind = kind, Engineer = engineer, Scrap = 1000, GroundAngle = 0f, Clear = true, DistanceFromBuilder = 3f,
            NearestEnemySpawn = 50f, NearestObjective = 50f, NearestDoor = 50f, NearestPlayerSpawn = 50f, OnNavMesh = true,
        };

        [Test]
        public void ValidPlacementPasses()
        {
            Assert.AreEqual(PlacementError.Ok, PlacementRules.Validate(Good()));
            Assert.AreEqual(PlacementError.Ok, PlacementRules.Validate(Good(StructureKind.Barricade, engineer: false)));
        }

        [Test]
        public void OnlyEngineersBuildTurretsDronesAndFences()
        {
            Assert.AreEqual(PlacementError.ClassCannotBuild, PlacementRules.Validate(Good(StructureKind.Sentry, false)));
            Assert.AreEqual(PlacementError.ClassCannotBuild, PlacementRules.Validate(Good(StructureKind.Drone, false)));
            Assert.AreEqual(PlacementError.ClassCannotBuild, PlacementRules.Validate(Good(StructureKind.ArcFence, false)));
        }

        [Test]
        public void EachCheckReportsItsReason()
        {
            PlacementQuery q;
            q = Good(); q.OwnedOfKind = 2; Assert.AreEqual(PlacementError.PlayerCap, PlacementRules.Validate(q));
            q = Good(); q.SquadTotal = PlacementRules.SquadCap; Assert.AreEqual(PlacementError.SquadCap, PlacementRules.Validate(q));
            q = Good(); q.Scrap = 10; Assert.AreEqual(PlacementError.NotEnoughScrap, PlacementRules.Validate(q));
            q = Good(); q.Scrap = 10; q.Free = true; Assert.AreEqual(PlacementError.Ok, PlacementRules.Validate(q));
            q = Good(); q.DistanceFromBuilder = 9f; Assert.AreEqual(PlacementError.TooFar, PlacementRules.Validate(q));
            q = Good(); q.GroundAngle = 40f; Assert.AreEqual(PlacementError.TooSteep, PlacementRules.Validate(q));
            q = Good(); q.OnNavMesh = false; Assert.AreEqual(PlacementError.OffNavMesh, PlacementRules.Validate(q));
            q = Good(); q.Clear = false; Assert.AreEqual(PlacementError.Blocked, PlacementRules.Validate(q));
            q = Good(); q.NearestEnemySpawn = 2f; Assert.AreEqual(PlacementError.NearEnemySpawn, PlacementRules.Validate(q));
            q = Good(); q.NearestObjective = 1f; Assert.AreEqual(PlacementError.NearObjective, PlacementRules.Validate(q));
            q = Good(); q.NearestDoor = 1f; Assert.AreEqual(PlacementError.NearDoor, PlacementRules.Validate(q));
            q = Good(); q.NearestPlayerSpawn = 1f; Assert.AreEqual(PlacementError.NearPlayerSpawn, PlacementRules.Validate(q));
        }

        [Test]
        public void DronesSkipGroundChecks()
        {
            PlacementQuery q = Good(StructureKind.Drone);
            q.Clear = false;
            q.OnNavMesh = false;
            q.DistanceFromBuilder = 100f;
            Assert.AreEqual(PlacementError.Ok, PlacementRules.Validate(q));
            q.OwnedOfKind = 1;
            Assert.AreEqual(PlacementError.PlayerCap, PlacementRules.Validate(q));
        }

        [Test]
        public void NonEngineerBarricadesCostMoreAndCapLower()
        {
            Assert.AreEqual(40, ScrapRules.BuildCost(StructureCatalog.Barricade, true));
            Assert.AreEqual(60, ScrapRules.BuildCost(StructureCatalog.Barricade, false));
            PlacementQuery q = Good(StructureKind.Barricade, false);
            q.OwnedOfKind = 2;
            Assert.AreEqual(PlacementError.PlayerCap, PlacementRules.Validate(q));
        }

        [Test]
        public void ScrapEconomy()
        {
            Assert.AreEqual(3, ScrapRules.KillScrap(1f, false));
            Assert.AreEqual(5, ScrapRules.KillScrap(1f, true));
            Assert.AreEqual(1, ScrapRules.KillScrap(0f, false));
            Assert.AreEqual(100, ScrapRules.UpgradeCost(StructureCatalog.Sentry, 1));
            Assert.AreEqual(-1, ScrapRules.UpgradeCost(StructureCatalog.Sentry, 3));
            Assert.AreEqual(120 + 100 + 160, ScrapRules.Invested(StructureCatalog.Sentry, 3, true));
            Assert.AreEqual(190, ScrapRules.Refund(StructureCatalog.Sentry, 3, true));
            var ledger = new ScrapLedger();
            ledger.Add(50);
            Assert.IsFalse(ledger.TrySpend(60));
            Assert.IsTrue(ledger.TrySpend(50));
            ledger.Add(99999);
            Assert.AreEqual(ScrapRules.MaxScrap, ledger.Amount);
        }

        [Test]
        public void RepairsAreChunkedAndEngineersAreBetter()
        {
            Assert.AreEqual(120f, RepairRules.Amount(100f, 300f, true), 0.001f);
            Assert.AreEqual(75f, RepairRules.Amount(100f, 300f, false), 0.001f);
            Assert.AreEqual(10f, RepairRules.Amount(290f, 300f, true), 0.001f, "never more than missing");
            Assert.AreEqual(0f, RepairRules.Amount(300f, 300f, true));
            Assert.AreEqual(6, RepairRules.Cost(120f, true));
            Assert.AreEqual(7, RepairRules.Cost(75f, false));
            Assert.AreEqual(0, RepairRules.Cost(0f, true));
        }

        [Test]
        public void TiersImproveEveryStat()
        {
            foreach (StructureDef d in StructureCatalog.All)
            {
                Assert.AreEqual(3, d.MaxTier, d.Name);
                for (int t = 2; t <= d.MaxTier; t++)
                {
                    Assert.Greater(d.Tier(t).Health, d.Tier(t - 1).Health, d.Name);
                    Assert.GreaterOrEqual(d.Tier(t).Damage, d.Tier(t - 1).Damage, d.Name);
                    Assert.GreaterOrEqual(d.Tier(t).Supply, d.Tier(t - 1).Supply, d.Name);
                }
                Assert.AreSame(d, StructureCatalog.Of(d.Kind));
            }
        }
    }
}
