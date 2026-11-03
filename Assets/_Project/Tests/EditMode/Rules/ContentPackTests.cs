using System.Collections.Generic;
using Deadfall.Rules.Combat;
using Deadfall.Rules.Common;
using Deadfall.Rules.Encounters;
using Deadfall.Rules.Progression;
using Deadfall.Rules.World;
using NUnit.Framework;

namespace Deadfall.Tests.EditMode.Rules
{
    /// <summary>M12 part 2: Toxic Leak rooms, new events and objectives, the Broodmother's rules, new research and mods.</summary>
    public class ContentPackTests
    {
        private static Layout Make(EnvironmentDef env, ulong seed) => LayoutGenerator.Generate(env.CreateKit(), env.CreateGraph(), seed);

        [Test]
        public void ToxicRoomsAreDeterministicAndAvoidSafeRooms()
        {
            foreach (EnvironmentDef env in EnvironmentCatalog.All)
                for (ulong seed = 1; seed <= 60; seed++)
                {
                    Layout l = Make(env, seed);
                    for (int sectors = 1; sectors <= 3; sectors++)
                    {
                        List<int> a = HazardRules.PickToxicRooms(l, sectors, seed, 7);
                        List<int> b = HazardRules.PickToxicRooms(l, sectors, seed, 7);
                        CollectionAssert.AreEqual(a, b);
                        Assert.LessOrEqual(a.Count, HazardRules.ToxicRooms);
                        Assert.AreEqual(a.Count, new HashSet<int>(a).Count);
                        foreach (int i in a)
                        {
                            PlacedRoom r = l.Rooms[i];
                            Assert.AreNotEqual(0, i, "never the spawn room");
                            Assert.Less(r.Sector, sectors, "only open sectors");
                            Assert.That(r.Template.Role == RoomRole.Room || r.Template.Role == RoomRole.Objective, $"role {r.Template.Role}");
                        }
                    }
                }
        }

        [Test]
        public void ToxicRoomsChangeBetweenSurges()
        {
            EnvironmentDef env = EnvironmentCatalog.All[0];
            int differ = 0;
            for (ulong seed = 1; seed <= 40; seed++)
            {
                Layout l = Make(env, seed);
                if (!new List<int>(HazardRules.PickToxicRooms(l, 3, seed, 5)).TrueForAll(HazardRules.PickToxicRooms(l, 3, seed, 9).Contains)) differ++;
            }
            Assert.Greater(differ, 10);
            Assert.IsEmpty(HazardRules.PickToxicRooms(null, 3, 1, 5));
        }

        [Test]
        public void SchedulerCanRollTheNewEvents()
        {
            var seen = new HashSet<RunEventKind>();
            for (ulong seed = 1; seed <= 3000 && seen.Count < 5; seed++)
            {
                var s = new EventScheduler();
                RunEventKind k = s.Roll(new DeterministicRandom(seed), 8, SurgeType.Standard, true, 3);
                if (k != RunEventKind.None) seen.Add(k);
            }
            Assert.Contains(RunEventKind.Frenzy, new List<RunEventKind>(seen));
            Assert.Contains(RunEventKind.ToxicLeak, new List<RunEventKind>(seen));
            Assert.AreEqual(RunEventKind.ToxicLeak, EventScheduler.Last);
            Assert.IsNotEmpty(EventScheduler.Name(RunEventKind.Frenzy));
            Assert.IsNotEmpty(EventScheduler.Hint(RunEventKind.ToxicLeak));
        }

        [Test]
        public void ToxicLeakNeedsAMap()
        {
            for (ulong seed = 1; seed <= 2000; seed++)
            {
                var s = new EventScheduler();
                Assert.AreNotEqual(RunEventKind.ToxicLeak, s.Roll(new DeterministicRandom(seed), 8, SurgeType.Standard, false, 0));
            }
        }

        [Test]
        public void NewObjectivesAreAppendedAndRewarded()
        {
            Assert.AreEqual(7, (int)ObjectiveKind.DefendUplink, "wire order: append only");
            Assert.AreEqual(8, (int)ObjectiveKind.PurgeHives);
            Assert.AreEqual(ObjectiveKind.PurgeHives, ObjectiveRules.LastKind);
            Assert.AreEqual(ObjectiveRules.Hives, ObjectiveRules.Target(ObjectiveKind.PurgeHives));
            Assert.AreEqual(1, ObjectiveRules.Target(ObjectiveKind.DefendUplink));
            Assert.Greater(ObjectiveRules.Reward(ObjectiveKind.DefendUplink), ObjectiveRules.Reward(ObjectiveKind.RetrieveDataCore));
            Assert.Greater(ObjectiveRules.Reward(ObjectiveKind.PurgeHives), ObjectiveRules.Reward(ObjectiveKind.DestroyNest));
            Assert.AreNotEqual("DefendUplink", ObjectiveRules.Title(ObjectiveKind.DefendUplink));
        }

        [Test]
        public void SidePoolOffersTheNewObjectives()
        {
            var seen = new HashSet<ObjectiveKind>();
            var rng = new DeterministicRandom(42);
            ObjectiveKind? last = null;
            for (int i = 0; i < 400; i++)
            {
                ObjectiveKind k = ObjectiveRules.PickSide(rng, last);
                Assert.AreNotEqual(last, k);
                Assert.AreNotEqual(ObjectiveKind.Extraction, k);
                Assert.AreNotEqual(ObjectiveKind.LiftLockdown, k);
                seen.Add(k);
                last = k;
            }
            Assert.IsTrue(seen.Contains(ObjectiveKind.DefendUplink));
            Assert.IsTrue(seen.Contains(ObjectiveKind.PurgeHives));
        }

        [Test]
        public void BroodmotherSummonsInEveryPhaseAndNeverCharges()
        {
            foreach (SpecimenPhase p in new[] { SpecimenPhase.Stalk, SpecimenPhase.Rampage, SpecimenPhase.ExposedCore })
            {
                Assert.IsTrue(SpecimenRules.SummonsIn(p, true));
                var stats = new EnemyStats { ChaseSpeed = 3f, CanCharge = true, CanShoot = false, RangedCooldownSeconds = 2f };
                var copy = new EnemyStats();
                SpecimenRules.ApplyPhase(stats, copy, p, SpecimenModifiers.None, brood: true);
                Assert.IsFalse(copy.CanCharge);
                Assert.IsTrue(copy.CanShoot);
            }
            Assert.IsFalse(SpecimenRules.SummonsIn(SpecimenPhase.Stalk, false));
            Assert.Less(SpecimenRules.SummonInterval(SpecimenPhase.Rampage, true), SpecimenRules.SummonInterval(SpecimenPhase.Stalk, true));
            Assert.AreEqual(SpecimenRules.SummonCount(2, SpecimenModifiers.None) + 2, SpecimenRules.SummonCount(2, SpecimenModifiers.None, brood: true));
        }

        [Test]
        public void NewWeaponResearchGatesItsWeapon()
        {
            List<ResearchDef> cat = ResearchCatalog.Prototype();
            var done = new HashSet<string>();
            foreach (string w in new[] { "weapon.hailstorm", "weapon.railspike_maul" })
                Assert.IsFalse(ResearchRules.WeaponUnlocked(cat, done, w), w);
            done.Add("res.wpn_hailstorm");
            done.Add("res.wpn_railspike");
            Assert.IsTrue(ResearchRules.WeaponUnlocked(cat, done, "weapon.hailstorm"));
            Assert.IsTrue(ResearchRules.WeaponUnlocked(cat, done, "weapon.railspike_maul"));
        }

        [Test]
        public void CombatStimsEvolveIntoBerserkerProtocol()
        {
            List<FieldModDef> cat = FieldModCatalog.Prototype();
            var inv = new FieldModInventory(cat);
            FieldModDef stims = cat.Find(d => d.Id == "mod.combat_stims");
            Assert.IsNotNull(stims);
            inv.Add(stims);
            inv.Add(stims);
            Assert.AreEqual("mod.berserker_protocol", inv.Add(stims).Id);
            Assert.AreEqual(0, inv.Stacks("mod.combat_stims"));
            Assert.IsFalse(inv.CanTake(stims));
        }

        [Test]
        public void FieldModIdsAreUnique()
        {
            var ids = new HashSet<string>();
            foreach (FieldModDef d in FieldModCatalog.Prototype()) Assert.IsTrue(ids.Add(d.Id), d.Id);
            foreach (string id in new[] { "mod.trigger_discipline", "mod.light_pack", "mod.field_dressing", "mod.hardened", "mod.sleight", "mod.deadeye", "mod.phoenix_rig" })
                Assert.IsTrue(ids.Contains(id), id);
        }
    }
}
