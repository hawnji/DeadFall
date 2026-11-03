using System.Collections.Generic;
using Deadfall.Rules.Combat;
using Deadfall.Rules.Common;
using Deadfall.Rules.Progression;
using NUnit.Framework;

namespace Deadfall.Tests.EditMode.Rules
{
    /// <summary>M11: downs and revives (D-01/D-02), the rejoin window (D-16) and single banking of run rewards.</summary>
    public class MultiplayerRulesTests
    {
        [Test]
        public void BleedOutShortensWithEachDown()
        {
            Assert.AreEqual(30f, DownedRules.BleedOutSeconds(1));
            Assert.AreEqual(20f, DownedRules.BleedOutSeconds(2));
            Assert.AreEqual(10f, DownedRules.BleedOutSeconds(3));
            Assert.AreEqual(10f, DownedRules.BleedOutSeconds(9));
            Assert.AreEqual(30f, DownedRules.BleedOutSeconds(0), "bad input clamps to the first down");
        }

        [Test]
        public void SquadWipesOnlyWhenNobodyCanRecover()
        {
            Assert.IsFalse(DownedRules.SquadWiped(1, 3, 0), "someone is up");
            Assert.IsFalse(DownedRules.SquadWiped(0, 2, 1), "downed players still have a kit");
            Assert.IsTrue(DownedRules.SquadWiped(0, 2, 0), "all down, no kits");
            Assert.IsTrue(DownedRules.SquadWiped(0, 0, 3), "all eliminated");
        }

        [Test]
        public void ReviveNeedsAnUpReviverADownedTargetAndRange()
        {
            Assert.IsTrue(DownedRules.ReviveValid(LifeState.Alive, LifeState.Downed, 2f));
            Assert.IsTrue(DownedRules.ReviveValid(LifeState.Alive, LifeState.Downed, DownedRules.ReviveRange + DownedRules.ReviveRangeSlack));
            Assert.IsFalse(DownedRules.ReviveValid(LifeState.Alive, LifeState.Downed, 10f));
            Assert.IsFalse(DownedRules.ReviveValid(LifeState.Downed, LifeState.Downed, 1f));
            Assert.IsFalse(DownedRules.ReviveValid(LifeState.Alive, LifeState.Eliminated, 1f), "eliminated players wait for the surge to end");
            Assert.IsFalse(DownedRules.ReviveValid(LifeState.Alive, LifeState.Alive, 1f));
        }

        [Test]
        public void SelfReviveNeedsKitsAndDownedState()
        {
            Assert.IsTrue(DownedRules.CanSelfRevive(LifeState.Downed, 1));
            Assert.IsFalse(DownedRules.CanSelfRevive(LifeState.Downed, 0));
            Assert.IsFalse(DownedRules.CanSelfRevive(LifeState.Eliminated, 3));
        }

        [Test]
        public void EliminatedReturnOnSurgeOrObjective()
        {
            Assert.IsTrue(DownedRules.ReturnsNow(LifeState.Eliminated, true, false));
            Assert.IsTrue(DownedRules.ReturnsNow(LifeState.Eliminated, false, true));
            Assert.IsFalse(DownedRules.ReturnsNow(LifeState.Eliminated, false, false));
            Assert.IsFalse(DownedRules.ReturnsNow(LifeState.Downed, true, true), "downed players are revived, not returned");
        }

        [Test]
        public void RejoinClaimsOnceInsideTheWindow()
        {
            var l = new ReconnectLedger<int>();
            l.Hold("tok", 5, 42, 100);
            Assert.IsTrue(l.IsHeld("tok", 150));
            Assert.AreEqual(40.0, l.TimeLeft("tok", 150), 1e-6);
            Assert.IsTrue(l.TryClaim("tok", 150, out ulong old, out int snap));
            Assert.AreEqual(5UL, old);
            Assert.AreEqual(42, snap);
            Assert.IsFalse(l.TryClaim("tok", 151, out _, out _), "a slot is claimed once");
        }

        [Test]
        public void RejoinExpiresAfterNinetySeconds()
        {
            var l = new ReconnectLedger<int>();
            l.Hold("a", 1, 0, 0);
            l.Hold("b", 2, 0, 50);
            Assert.IsFalse(l.TryClaim("a", 90, out _, out _), "window is 90 s");
            List<ulong> gone = l.Expire(141);
            CollectionAssert.AreEquivalent(new[] { 2UL }, gone);
            Assert.AreEqual(0, l.Count);
            Assert.IsFalse(l.TryClaim("", 0, out _, out _));
        }

        [Test]
        public void RunWithTheSameIdPaysOnce()
        {
            var p = new PlayerProfile();
            var run = new RunResult { Won = false, SurgesCleared = 6, Kills = 80, RunId = "run-abc" };
            RunSummary first = ProfileRules.ApplyRun(p, "class.gunner", run, null);
            long xp = p.OperatorXp;
            Assert.IsFalse(first.Duplicate);
            Assert.Greater(xp, 0);
            RunSummary again = ProfileRules.ApplyRun(p, "class.gunner", run, null);
            Assert.IsTrue(again.Duplicate);
            Assert.AreEqual(xp, p.OperatorXp, "no second payout");
            Assert.AreEqual(1, p.Stat(StatKeys.RunsFailed));
        }

        [Test]
        public void RunsWithoutIdsAlwaysBank()
        {
            var p = new PlayerProfile();
            ProfileRules.ApplyRun(p, null, new RunResult { SurgesCleared = 2 }, null);
            ProfileRules.ApplyRun(p, null, new RunResult { SurgesCleared = 2 }, null);
            Assert.AreEqual(2, p.Stat(StatKeys.RunsFailed));
        }

        [Test]
        public void BankedRunIdsSurviveSaveAndStayBounded()
        {
            var p = new PlayerProfile();
            for (int i = 0; i < ProfileRules.BankedRunMemory + 5; i++)
                ProfileRules.ApplyRun(p, null, new RunResult { RunId = "run-" + i }, null);
            Assert.AreEqual(ProfileRules.BankedRunMemory, p.BankedRuns.Count);
            Assert.AreEqual("run-5", p.BankedRuns[0], "oldest dropped first");
            SaveLoadResult r = SaveFormat.Read(SaveFormat.Write(p));
            Assert.AreEqual(SaveLoadStatus.Ok, r.Status);
            CollectionAssert.AreEqual(p.BankedRuns, r.Profile.BankedRuns);
            Assert.IsTrue(ProfileRules.WasBanked(r.Profile, "run-24"));
        }
    }
}
