using System.Collections.Generic;
using Deadfall.Rules.Combat;
using Deadfall.Rules.Common;
using Deadfall.Rules.Encounters;
using NUnit.Framework;

namespace Deadfall.Tests.EditMode.Rules
{
    public class AdvancedEnemyBrainTests
    {
        private static BrainOutput Tick(EnemyBrain b, double now, float distance, bool los = true) =>
            b.Tick(new BrainInput { Now = now, HasTarget = true, TargetDistance = distance, HasLineOfSight = los });

        private static EnemyStats Spitter() => new EnemyStats
        {
            CanShoot = true, PreferredRange = 12f, RangedMaxRange = 20f, RangedWindupSeconds = 0.5f,
            RangedRecoverSeconds = 0.4f, RangedCooldownSeconds = 2f, AttackRange = 1.5f,
        };

        [Test]
        public void SpitterShootsWithLineOfSightAndHoldsWhileReloading()
        {
            var b = new EnemyBrain(Spitter());
            b.Reset(0);
            b.Alert();
            // First shot waits for half the cooldown after spawning.
            Assert.AreEqual(EnemyState.Chase, Tick(b, 0.1, 10f).State);
            BrainOutput o = Tick(b, 0.2, 10f);
            Assert.AreEqual(0f, o.Speed, "holds ground in range while not ready");
            Assert.AreEqual(EnemyState.RangedWindup, Tick(b, 1.0, 10f).State);
            o = Tick(b, 1.5, 10f);
            Assert.IsTrue(o.Shoot);
            Assert.AreEqual(EnemyState.RangedRecover, o.State);
            Assert.AreEqual(EnemyState.Chase, Tick(b, 1.95, 10f).State);
            Assert.AreEqual(EnemyState.Chase, Tick(b, 2.0, 10f).State, "cooldown not over");
            Assert.AreEqual(EnemyState.RangedWindup, Tick(b, 3.6, 10f).State);
        }

        [Test]
        public void SpitterWithoutLineOfSightCloses()
        {
            var b = new EnemyBrain(Spitter());
            b.Reset(0);
            b.Alert();
            Tick(b, 0.1, 10f, los: false);
            BrainOutput o = Tick(b, 2.0, 10f, los: false);
            Assert.AreEqual(EnemyState.Chase, o.State);
            Assert.Greater(o.Speed, 0f);
            Assert.IsTrue(o.PathToTarget);
        }

        [Test]
        public void SpitterStillMeleesUpClose()
        {
            var b = new EnemyBrain(Spitter());
            b.Reset(0);
            b.Alert();
            Tick(b, 0.1, 5f);
            Assert.AreEqual(EnemyState.AttackWindup, Tick(b, 0.2, 1f).State);
        }

        [Test]
        public void ExploderCommitsAndCannotBeStaggeredOut()
        {
            var stats = new EnemyStats { CanExplode = true, ExplodeRange = 2f, ExplodeWindupSeconds = 0.8f, StaggerThreshold = 10f };
            var b = new EnemyBrain(stats);
            b.Reset(0);
            b.Alert();
            Tick(b, 0.1, 6f);
            Assert.AreEqual(EnemyState.ExplodeWindup, Tick(b, 0.2, 1.5f).State);
            b.OnDamaged(100f, 0.3);
            Assert.AreEqual(EnemyState.ExplodeWindup, Tick(b, 0.5, 5f).State, "no stagger, no cancel");
            BrainOutput o = Tick(b, 1.05, 5f);
            Assert.IsTrue(o.Explode);
            Assert.IsTrue(b.IsDead);
        }

        [Test]
        public void StatsCloneIsIndependent()
        {
            var a = new EnemyStats { MaxHealth = 50f };
            EnemyStats c = a.Clone();
            c.MaxHealth = 80f;
            Assert.AreEqual(50f, a.MaxHealth);
            var d = new EnemyStats();
            d.CopyFrom(c);
            Assert.AreEqual(80f, d.MaxHealth);
        }
    }

    public class VariantAndSpecimenTests
    {
        [Test]
        public void NoVariantsBeforeSurgeFiveAndChanceIsCapped()
        {
            Assert.AreEqual(0f, VariantRules.Chance(4));
            Assert.Greater(VariantRules.Chance(5), 0f);
            Assert.AreEqual(VariantRules.MaxChance, VariantRules.Chance(200), 0.0001f);
            var rng = new DeterministicRandom(7);
            for (int i = 0; i < 500; i++) Assert.AreEqual(EnemyTraits.None, VariantRules.Roll(rng, 3, EnemyRole.Regular, true));
        }

        [Test]
        public void BossesAndHuntersNeverMutateAndCreepersNeverSplit()
        {
            var rng = new DeterministicRandom(11);
            for (int i = 0; i < 2000; i++)
            {
                Assert.AreEqual(EnemyTraits.None, VariantRules.Roll(rng, 40, EnemyRole.Boss, true, 10f));
                Assert.AreEqual(EnemyTraits.None, VariantRules.Roll(rng, 40, EnemyRole.Hunter, true, 10f));
                Assert.AreEqual(EnemyTraits.None, VariantRules.Roll(rng, 40, EnemyRole.Regular, false, 10f) & EnemyTraits.Splitting);
            }
        }

        [Test]
        public void LateSurgesRollSomeDoubleTraits()
        {
            var rng = new DeterministicRandom(3);
            int doubles = 0, any = 0;
            for (int i = 0; i < 4000; i++)
            {
                EnemyTraits t = VariantRules.Roll(rng, 30, EnemyRole.Regular, true);
                if (t != EnemyTraits.None) any++;
                if (VariantRules.CreditMultiplier(t) > 1.9f) doubles++;
            }
            Assert.Greater(any, 1000);
            Assert.Greater(doubles, 50);
        }

        [Test]
        public void TraitsChangeStats()
        {
            var s = new EnemyStats { MaxHealth = 100f, ChaseSpeed = 2f, DamageReduction = 0.5f };
            VariantRules.Apply(s, EnemyTraits.Hardened | EnemyTraits.Swift | EnemyTraits.Regenerating);
            Assert.AreEqual(120f, s.MaxHealth, 0.001f);
            Assert.AreEqual(VariantRules.MaxReduction, s.DamageReduction, 0.001f);
            Assert.AreEqual(2.7f, s.ChaseSpeed, 0.001f);
            Assert.AreEqual(4.8f, s.RegenPerSecond, 0.001f);
            Assert.AreEqual(2.5f, VariantRules.CreditMultiplier(EnemyTraits.Hardened | EnemyTraits.Swift | EnemyTraits.Regenerating), 0.001f);
        }

        [Test]
        public void SpecimenPhasesFollowHealth()
        {
            Assert.AreEqual(SpecimenPhase.Stalk, SpecimenRules.PhaseFor(1f));
            Assert.AreEqual(SpecimenPhase.Stalk, SpecimenRules.PhaseFor(0.67f));
            Assert.AreEqual(SpecimenPhase.Rampage, SpecimenRules.PhaseFor(0.66f));
            Assert.AreEqual(SpecimenPhase.ExposedCore, SpecimenRules.PhaseFor(0.2f));

            var baseStats = new EnemyStats { ChaseSpeed = 4f, DamageReduction = 0.3f, CanCharge = true, CanShoot = true };
            var live = baseStats.Clone();
            SpecimenRules.ApplyPhase(baseStats, live, SpecimenPhase.Stalk, SpecimenModifiers.None);
            Assert.IsFalse(live.CanCharge);
            Assert.IsFalse(live.CanShoot);
            SpecimenRules.ApplyPhase(baseStats, live, SpecimenPhase.Rampage, SpecimenModifiers.None);
            Assert.IsTrue(live.CanCharge);
            SpecimenRules.ApplyPhase(baseStats, live, SpecimenPhase.ExposedCore, SpecimenModifiers.Armored);
            Assert.IsTrue(live.CanShoot);
            Assert.AreEqual(0.2f, live.DamageReduction, 0.0001f);
            Assert.AreEqual(4f, baseStats.ChaseSpeed, "base stats untouched");
        }

        [Test]
        public void SpecimenModifiersAndPacking()
        {
            Assert.AreEqual(SpecimenModifiers.None, SpecimenRules.RollModifiers(new DeterministicRandom(1), 10));
            for (ulong seed = 1; seed < 50; seed++)
            {
                SpecimenModifiers one = SpecimenRules.RollModifiers(new DeterministicRandom(seed), 20);
                SpecimenModifiers two = SpecimenRules.RollModifiers(new DeterministicRandom(seed), 40);
                Assert.AreEqual(1, Bits((int)one));
                Assert.AreEqual(2, Bits((int)two));
                byte packed = SpecimenRules.Pack(SpecimenPhase.Rampage, two);
                SpecimenRules.Unpack(packed, out SpecimenPhase p, out SpecimenModifiers m);
                Assert.AreEqual(SpecimenPhase.Rampage, p);
                Assert.AreEqual(two, m);
            }
            Assert.AreEqual(5, SpecimenRules.SummonCount(3, SpecimenModifiers.None));
            Assert.AreEqual(8, SpecimenRules.SummonCount(3, SpecimenModifiers.Brood));
        }

        private static int Bits(int v)
        {
            int n = 0;
            for (; v != 0; v &= v - 1) n++;
            return n;
        }
    }

    public class SurgeTypeTests
    {
        private static readonly EnemyOption[] Options =
        {
            new EnemyOption("husk", 1f, 1, 10f),
            new EnemyOption("creeper", 0.8f, 4, 4f, 1f, EnemyRole.Regular, canSplit: false),
            new EnemyOption("bulwark", 6f, 6, 1f, 0.4f),
            new EnemyOption("alpha", 5f, 10, 0.5f, 0.3f, EnemyRole.Elite),
            new EnemyOption("stalker", 10f, 6, 1f, 1f, EnemyRole.Hunter),
            new EnemyOption("specimen", 40f, 10, 1f, 1f, EnemyRole.Boss),
        };

        private static WavePlanner Planner() => new WavePlanner(new DifficultyCurve(new DifficultySettings()), Options);

        [Test]
        public void HuntersAndBossesAreNeverBoughtWithBudget()
        {
            WavePlanner p = Planner();
            for (ulong seed = 1; seed <= 30; seed++)
            for (int surge = 1; surge <= 30; surge++)
            {
                SurgeType t = WavePlanner.TypeFor(surge);
                SurgePlan plan = p.Plan(surge, 2, seed, t == SurgeType.Hunt || t == SurgeType.Specimen ? SurgeType.Standard : t);
                Assert.AreEqual(0, plan.Count("stalker"));
                Assert.AreEqual(0, plan.Count("specimen"));
                Assert.AreEqual(plan.Spawns.Count, plan.Traits.Count);
            }
        }

        [Test]
        public void SpecimenSurgeLeadsWithTheBoss()
        {
            SurgePlan plan = Planner().Plan(10, 2, 9, SurgeType.Specimen);
            Assert.AreEqual("specimen", plan.Spawns[0]);
            Assert.AreEqual("specimen", plan.BossId);
            Assert.AreEqual(1, plan.Count("specimen"));
            Assert.AreEqual(EnemyTraits.None, plan.Traits[0]);
            Assert.Less(plan.Budget, Planner().Plan(10, 2, 9, SurgeType.Standard).Budget);
        }

        [Test]
        public void HuntSurgeLeadsWithOnePursuer()
        {
            SurgePlan plan = Planner().Plan(6, 1, 4, SurgeType.Hunt);
            Assert.AreEqual("stalker", plan.Spawns[0]);
            Assert.AreEqual(1, plan.Count("stalker"));
        }

        [Test]
        public void EliteSurgeGuaranteesElitesEarly()
        {
            // Alpha unlocks at 10, but an Elite surge 7 may field it early.
            for (int players = 1; players <= 4; players++)
            {
                SurgePlan plan = Planner().Plan(7, players, 2, SurgeType.Elite);
                Assert.GreaterOrEqual(plan.Count("alpha"), 1 + players / 2, $"players {players}");
            }
            Assert.AreEqual(0, Planner().Plan(7, 1, 2, SurgeType.Standard).Count("alpha"));
        }

        [Test]
        public void SupplySurgeDropsCrates()
        {
            Assert.AreEqual(2, Planner().Plan(9, 1, 1, SurgeType.Supply).SupplyCrates);
            Assert.AreEqual(4, Planner().Plan(9, 4, 1, SurgeType.Supply).SupplyCrates);
            Assert.AreEqual(0, Planner().Plan(9, 4, 1, SurgeType.Standard).SupplyCrates);
        }

        [Test]
        public void MissingRolesFallBackToANormalSurge()
        {
            var planner = new WavePlanner(new DifficultyCurve(new DifficultySettings()), new[] { new EnemyOption("husk", 1f, 1, 1f) });
            SurgePlan plan = planner.Plan(10, 1, 1, SurgeType.Specimen);
            Assert.IsNull(plan.BossId);
            Assert.Greater(plan.Spawns.Count, 0);
            Assert.AreEqual(0, plan.Spawns.FindAll(s => s != "husk").Count);
        }

        [Test]
        public void PlansAreDeterministicIncludingTraits()
        {
            SurgePlan a = Planner().Plan(22, 3, 77);
            SurgePlan b = Planner().Plan(22, 3, 77);
            CollectionAssert.AreEqual(a.Spawns, b.Spawns);
            CollectionAssert.AreEqual(a.Traits, b.Traits);
        }
    }
}
