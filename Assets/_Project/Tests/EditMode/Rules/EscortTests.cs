using System.Collections.Generic;
using Deadfall.Rules.Encounters;
using Deadfall.Rules.Progression;
using Deadfall.Rules.World;
using NUnit.Framework;

namespace Deadfall.Tests.EditMode.Rules
{
    public class EscortTests
    {
        [Test]
        public void HealthScalesWithSquadLikeObjectivePools()
        {
            Assert.AreEqual(EscortRules.BaseHealth, EscortRules.MaxHealth(1), 0.01f);
            Assert.AreEqual(EscortRules.BaseHealth * 2.05f, EscortRules.MaxHealth(4), 0.01f);
        }

        [Test]
        public void CartOnlyRollsWhenPushedAndNotSwarmed()
        {
            Assert.AreEqual(EscortRules.Status.Waiting, EscortRules.Evaluate(0, 0));
            Assert.AreEqual(EscortRules.Status.Moving, EscortRules.Evaluate(1, 1));
            Assert.AreEqual(EscortRules.Status.Contested, EscortRules.Evaluate(1, 2));
            Assert.AreEqual(EscortRules.Status.Moving, EscortRules.Evaluate(3, 3));
            Assert.AreEqual(0f, EscortRules.Speed(0));
            Assert.AreEqual(EscortRules.BaseSpeed, EscortRules.Speed(1), 1e-5f);
            Assert.Greater(EscortRules.Speed(2), EscortRules.Speed(1));
            Assert.AreEqual(EscortRules.MaxSpeed, EscortRules.Speed(10), 1e-5f);
        }

        [Test]
        public void RouteFollowsParentsFromSpawn()
        {
            // 0 spawn, 1 and 2 off spawn, 3 off 1, 4 off 3.
            var parents = new List<int> { -1, 0, 0, 1, 3 };
            CollectionAssert.AreEqual(new[] { 0, 1, 3, 4 }, EscortRules.Route(parents, 4));
            CollectionAssert.AreEqual(new[] { 0, 2 }, EscortRules.Route(parents, 2));
            CollectionAssert.IsEmpty(EscortRules.Route(new List<int> { -1, 2, 1 }, 2), "a loop gives no route");
        }

        [Test]
        public void EscortSeedsBuildValidMediumMapsWithABossRoomAtTheEnd()
        {
            for (int env = 0; env < EnvironmentCatalog.All.Length; env++)
                for (ulong s = 1; s <= 20; s++)
                {
                    ulong seed = EnvironmentCatalog.EscortSeed(s * 7919UL, env);
                    Assert.IsTrue(EnvironmentCatalog.IsEscort(seed));
                    Assert.IsFalse(EnvironmentCatalog.IsGauntlet(seed));
                    Assert.AreEqual(env, EnvironmentCatalog.IndexOf(seed));
                    EnvironmentDef def = EnvironmentCatalog.At(env);
                    GraphTemplate g = EnvironmentCatalog.GraphFor(seed);
                    Assert.AreEqual("escort", g.Id);
                    Layout l = LayoutGenerator.Generate(def.CreateKit(), g, seed);
                    Assert.IsNotNull(l);
                    Assert.IsEmpty(LayoutValidator.Validate(l), $"{def.Id} seed {s}");
                    PlacedRoom end = l.FirstWithRole(RoomRole.Boss) ?? l.FirstWithRole(RoomRole.Objective);
                    Assert.IsNotNull(end, $"{def.Id} seed {s}: the cart needs somewhere to go");
                    var parents = new List<int>();
                    foreach (PlacedRoom r in l.Rooms) parents.Add(r.Parent);
                    Assert.GreaterOrEqual(EscortRules.Route(parents, end.Index).Count, 3, $"{def.Id} seed {s}");
                }
        }

        [Test]
        public void PlainSeedsNeverTurnIntoModeMaps()
        {
            ulong big = (1UL << 54) | (1UL << 55) | 12345UL;
            ulong seed = EnvironmentCatalog.Encode(big, 1);
            Assert.IsFalse(EnvironmentCatalog.IsEscort(seed));
            Assert.IsFalse(EnvironmentCatalog.IsGauntlet(seed));
        }

        [Test]
        public void DeliveredEscortsAreCounted()
        {
            var p = new PlayerProfile();
            ProfileRules.ApplyRun(p, null, new RunResult { Won = true, Escort = true, RunId = "a" }, null);
            ProfileRules.ApplyRun(p, null, new RunResult { Won = false, Escort = true, RunId = "b" }, null);
            Assert.AreEqual(1, p.Stat(StatKeys.EscortsWon));
        }
    }
}
