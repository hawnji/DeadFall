using System;
using System.Collections.Generic;
using Deadfall.Rules.Progression;
using NUnit.Framework;

namespace Deadfall.Tests.EditMode.Rules
{
    public class ProgressionTests
    {
        private static readonly List<SkillNodeDef> Skills = SkillTreeCatalog.Prototype();
        private static readonly List<ResearchDef> ResearchList = ResearchCatalog.Prototype();
        private static readonly List<ChallengeDef> Challenges = ChallengeCatalog.Prototype();

        // ---------- currencies and levels ----------

        [Test]
        public void WalletSpendsOnlyWhenAffordable()
        {
            var w = new Wallet();
            w.Add(new Cost(500, 3));
            Assert.IsFalse(w.TrySpend(new Cost(600, 0)));
            Assert.IsFalse(w.TrySpend(new Cost(100, 4)));
            Assert.AreEqual(500, w.Requisition);
            Assert.IsTrue(w.TrySpend(new Cost(400, 3)));
            Assert.AreEqual(100, w.Requisition);
            Assert.AreEqual(0, w.Intel);
            w.Add(Currencies.Requisition.Id, -50);
            Assert.AreEqual(100, w.Requisition, "negative adds are ignored");
        }

        [Test]
        public void FourCurrenciesTwoPermanent()
        {
            Assert.AreEqual(4, Currencies.All.Length);
            int permanent = 0;
            foreach (CurrencyDefinition c in Currencies.All) if (c.Permanent) permanent++;
            Assert.AreEqual(2, permanent);
        }

        [Test]
        public void OperatorLevelCurveIsMonotonicAndCapped()
        {
            Assert.AreEqual(1, OperatorLevels.LevelForXp(0));
            Assert.AreEqual(2, OperatorLevels.LevelForXp(OperatorLevels.XpForLevel(2)));
            Assert.AreEqual(1, OperatorLevels.LevelForXp(OperatorLevels.XpForLevel(2) - 1));
            for (int l = 2; l <= OperatorLevels.MaxLevel; l++) Assert.Greater(OperatorLevels.XpForLevel(l), OperatorLevels.XpForLevel(l - 1));
            Assert.AreEqual(OperatorLevels.MaxLevel, OperatorLevels.LevelForXp(long.MaxValue / 2));
            Assert.AreEqual(1f, OperatorLevels.Progress(long.MaxValue / 2));
        }

        [Test]
        public void ClassRanksGiveSkillPointsUpToTheFullTree()
        {
            Assert.AreEqual(0, ClassRanks.SkillPoints(1));
            Assert.AreEqual(1, ClassRanks.SkillPoints(2));
            Assert.AreEqual(6, ClassRanks.SkillPoints(10));
            Assert.Less(ClassRanks.SkillPoints(20), ClassRanks.FullTreePoints, "choices matter before the late ranks");
            Assert.AreEqual(ClassRanks.FullTreePoints, ClassRanks.SkillPoints(28));
            Assert.AreEqual(ClassRanks.FullTreePoints, ClassRanks.SkillPoints(ClassRanks.MaxRank));
            Assert.AreEqual(ClassRanks.MaxRank, ClassRanks.RankForXp(ClassRanks.XpForRank(ClassRanks.MaxRank)));
        }

        [Test]
        public void SevenFacilitiesWithLevelGates()
        {
            Assert.AreEqual(7, Facilities.All.Length);
            Assert.IsTrue(Facilities.IsOpen(FacilityId.CommandCenter, 1));
            Assert.IsTrue(Facilities.IsOpen(FacilityId.Armory, 1));
            Assert.IsFalse(Facilities.IsOpen(FacilityId.ResearchLab, 1));
            Assert.IsTrue(Facilities.IsOpen(FacilityId.ResearchLab, 2));
            Assert.IsFalse(Facilities.IsOpen(FacilityId.IntelligenceCenter, 2));
        }

        [Test]
        public void LaterClassesUnlockByOperatorLevel()
        {
            var p = new PlayerProfile();
            Assert.IsTrue(p.ClassUnlocked("class.gunner"));
            Assert.IsTrue(p.ClassUnlocked("class.warrior"));
            Assert.IsFalse(p.ClassUnlocked("class.engineer"));
            p.OperatorXp = OperatorLevels.XpForLevel(3);
            Assert.IsTrue(p.ClassUnlocked("class.engineer"));
            Assert.IsTrue(p.ClassUnlocked("class.demolitionist"));
        }

        // ---------- skill trees ----------

        [Test]
        public void EveryClassHasThreeBranchesOfThreeTiers()
        {
            Assert.AreEqual(45, Skills.Count);
            var ids = new HashSet<string>();
            foreach (SkillNodeDef n in Skills)
            {
                Assert.IsTrue(ids.Add(n.Id), n.Id);
                Assert.IsTrue(SaveFormat.IsId(n.Id), n.Id);
                Assert.Greater(n.Effects.Length, 0, n.Id);
                if (n.Tier > 1) Assert.IsNotNull(SkillTreeRules.Prerequisite(Skills, n), n.Id);
            }
        }

        [Test]
        public void SkillUnlockChecksRankPrerequisiteAndPoints()
        {
            var p = new PlayerProfile();
            const string cls = "class.gunner";
            Assert.AreEqual(SkillUnlockError.RankTooLow, ProfileRules.UnlockSkill(p, Skills, "skill.gunner.0.1"));
            p.ClassXp[cls] = ClassRanks.XpForRank(6); // 4 points (T-77: about 2 per 3 ranks)
            Assert.AreEqual(SkillUnlockError.MissingPrerequisite, ProfileRules.UnlockSkill(p, Skills, "skill.gunner.0.2"));
            Assert.AreEqual(SkillUnlockError.Ok, ProfileRules.UnlockSkill(p, Skills, "skill.gunner.0.1"));
            Assert.AreEqual(SkillUnlockError.AlreadyUnlocked, ProfileRules.UnlockSkill(p, Skills, "skill.gunner.0.1"));
            Assert.AreEqual(SkillUnlockError.Ok, ProfileRules.UnlockSkill(p, Skills, "skill.gunner.0.2"));
            Assert.AreEqual(1, SkillTreeRules.Available(Skills, cls, p.RankOf(cls), p.Skills));
            Assert.AreEqual(SkillUnlockError.RankTooLow, ProfileRules.UnlockSkill(p, Skills, "skill.gunner.0.3"));
            Assert.AreEqual(SkillUnlockError.Ok, ProfileRules.UnlockSkill(p, Skills, "skill.gunner.1.1"));
            Assert.AreEqual(SkillUnlockError.NotEnoughPoints, ProfileRules.UnlockSkill(p, Skills, "skill.gunner.2.1"));
            Assert.AreEqual(SkillUnlockError.UnknownNode, ProfileRules.UnlockSkill(p, Skills, "skill.nope"));
        }

        [Test]
        public void SkillPointsArePerClass()
        {
            var p = new PlayerProfile();
            p.ClassXp["class.tank"] = ClassRanks.XpForRank(3);
            Assert.AreEqual(SkillUnlockError.RankTooLow, ProfileRules.UnlockSkill(p, Skills, "skill.gunner.0.1"));
            Assert.AreEqual(SkillUnlockError.Ok, ProfileRules.UnlockSkill(p, Skills, "skill.tank.0.1"));
        }

        [Test]
        public void FullTreeFitsInMaxRankPoints()
        {
            int perClass = 0;
            foreach (SkillNodeDef n in Skills) if (n.ClassId == "class.gunner") perClass += n.Cost;
            Assert.LessOrEqual(perClass, ClassRanks.SkillPoints(ClassRanks.MaxRank));
        }

        // ---------- prestige ----------

        [Test]
        public void PrestigeResetsRankAndRefundsSkillsButKeepsTheRest()
        {
            var p = new PlayerProfile();
            const string cls = "class.tank";
            p.ClassXp[cls] = ClassRanks.XpForRank(ClassRanks.MaxRank - 1);
            Assert.IsFalse(ProfileRules.Prestige(p, Skills, cls), "not max rank yet");
            p.ClassXp[cls] = ClassRanks.XpForRank(ClassRanks.MaxRank);
            ProfileRules.UnlockSkill(p, Skills, "skill.tank.0.1");
            ProfileRules.UnlockSkill(p, Skills, "skill.gunner.0.1"); // other class, rank too low: stays out
            p.Research.Add("res.field_medicine");
            p.Wallet.Add(new Cost(1000, 5));
            Assert.IsTrue(ProfileRules.Prestige(p, Skills, cls));
            Assert.AreEqual(1, p.RankOf(cls));
            Assert.AreEqual(1, p.PrestigeOf(cls));
            Assert.IsFalse(p.Skills.Contains("skill.tank.0.1"));
            Assert.IsTrue(p.Research.Contains("res.field_medicine"));
            Assert.AreEqual(1000, p.Wallet.Requisition);
            Assert.AreEqual(1, p.Stat(StatKeys.Prestiges));
            Assert.AreEqual("Veteran", ClassRanks.PrestigeTitle(1));
        }

        [Test]
        public void PrestigeStopsAtTheCap()
        {
            var p = new PlayerProfile();
            p.Prestige["class.tank"] = ClassRanks.MaxPrestige;
            p.ClassXp["class.tank"] = ClassRanks.XpForRank(ClassRanks.MaxRank);
            Assert.IsFalse(ProfileRules.Prestige(p, Skills, "class.tank"));
        }

        // ---------- research ----------

        [Test]
        public void ResearchCatalogIsConsistent()
        {
            var ids = new HashSet<string>();
            foreach (ResearchDef r in ResearchList) Assert.IsTrue(ids.Add(r.Id), r.Id);
            foreach (ResearchDef r in ResearchList)
                foreach (string pre in r.Prerequisites) Assert.IsTrue(ids.Contains(pre), $"{r.Id} needs unknown {pre}");
            foreach (ResearchDef r in ResearchList) Assert.IsTrue(SaveFormat.IsId(r.Id), r.Id);
        }

        [Test]
        public void ResearchChecksLevelPrerequisitesAndCost()
        {
            var p = new PlayerProfile();
            Assert.AreEqual(ResearchError.CannotAfford, ProfileRules.Research(p, ResearchList, "res.field_medicine"));
            p.Wallet.Add(new Cost(10000, 50));
            Assert.AreEqual(ResearchError.MissingPrerequisite, ProfileRules.Research(p, ResearchList, "res.trauma_plating"));
            Assert.AreEqual(ResearchError.Ok, ProfileRules.Research(p, ResearchList, "res.field_medicine"));
            Assert.AreEqual(10000 - 600, p.Wallet.Requisition);
            Assert.AreEqual(49, p.Wallet.Intel);
            Assert.AreEqual(ResearchError.AlreadyDone, ProfileRules.Research(p, ResearchList, "res.field_medicine"));
            Assert.AreEqual(ResearchError.LevelTooLow, ProfileRules.Research(p, ResearchList, "res.capacitors"));
            Assert.AreEqual(ResearchError.UnknownProject, ProfileRules.Research(p, ResearchList, "res.none"));
            Assert.AreEqual(1, p.Stat(StatKeys.Research));
        }

        [Test]
        public void PermanentTotalsCombineSkillsAndResearchForTheClassInPlay()
        {
            var p = new PlayerProfile();
            p.ClassXp["class.tank"] = ClassRanks.XpForRank(3);
            ProfileRules.UnlockSkill(p, Skills, "skill.tank.0.1"); // +15 max health
            p.Research.Add("res.field_medicine");                    // +10 max health
            p.Research.Add("res.eng_hardhat");                       // engineer only
            float[] tank = ProfileRules.PermanentTotals(p, "class.tank", Skills, ResearchList);
            float[] eng = ProfileRules.PermanentTotals(p, "class.engineer", Skills, ResearchList);
            Assert.AreEqual(25f, tank[(int)ModStat.MaxHealth], 1e-4f);
            Assert.AreEqual(25f, eng[(int)ModStat.MaxHealth], 1e-4f, "engineer: research 10 + site safety 15, no tank skill");
        }

        [Test]
        public void DossiersRevealByResearchOrKills()
        {
            p_DossierTier();
            Assert.IsFalse(DossierRules.Revealed(1, 0, 0));
            Assert.IsTrue(DossierRules.Revealed(1, 1, 0));
            Assert.IsFalse(DossierRules.Revealed(3, 2, 10));
            Assert.IsTrue(DossierRules.Revealed(3, 0, DossierRules.KillsToReveal));
        }

        private static void p_DossierTier()
        {
            var done = new HashSet<string> { "res.dossier_common", "res.dossier_elite" };
            Assert.AreEqual(2, ResearchRules.DossierTier(ResearchList, done));
        }

        // ---------- loadouts ----------

        private static readonly LoadoutOption[] Options =
        {
            new LoadoutOption("w.ar", LoadoutSlot.Primary, false),
            new LoadoutOption("w.smg", LoadoutSlot.Primary, false),
            new LoadoutOption("w.lmg", LoadoutSlot.Primary, true),
            new LoadoutOption("w.pistol", LoadoutSlot.Sidearm, false),
            new LoadoutOption("w.knife", LoadoutSlot.Melee, false),
            new LoadoutOption("w.axe", LoadoutSlot.Melee, true),
        };

        [Test]
        public void LoadoutsRespectSlotsAndResearch()
        {
            string[] def = { "w.ar", "w.smg", "w.pistol", "w.knife" };
            Func<string, bool> none = _ => false;
            Assert.IsTrue(LoadoutRules.IsValid(def, Options, def, none));
            Assert.IsFalse(LoadoutRules.IsValid(new[] { "w.ar", "w.ar", "w.pistol", "w.knife" }, Options, def, none), "duplicate primary");
            Assert.IsFalse(LoadoutRules.IsValid(new[] { "w.ar", "w.pistol", "w.smg", "w.knife" }, Options, def, none), "wrong slot");
            Assert.IsFalse(LoadoutRules.IsValid(new[] { "w.ar", "w.lmg", "w.pistol", "w.knife" }, Options, def, none), "lmg needs research");
            Assert.IsTrue(LoadoutRules.IsValid(new[] { "w.ar", "w.lmg", "w.pistol", "w.knife" }, Options, def, id => id == "w.lmg"));
            string[] tankDefault = { "w.lmg", "w.ar", "w.pistol", "w.axe" };
            Assert.IsTrue(LoadoutRules.IsValid(tankDefault, Options, tankDefault, none), "class defaults are always allowed");
            Assert.AreSame(def, LoadoutRules.Resolve(new[] { "w.gone", "w.ar", "w.pistol", "w.knife" }, Options, def, none));
            Assert.AreSame(def, LoadoutRules.Resolve(null, Options, def, none));
        }

        // ---------- run rewards ----------

        [Test]
        public void WinsPayFullAndFailuresKeepHalf()
        {
            var r = new RunResult { Won = true, SurgesCleared = 10, Kills = 100, Objectives = 2, Specimens = 1 };
            RunReward win = RunRewardRules.Compute(r);
            r.Won = false;
            RunReward loss = RunRewardRules.Compute(r);
            Assert.AreEqual(1000 + 1000 + 500 + 750 + 1000, win.OperatorXp);
            Assert.AreEqual(200 + 250 + 200 + 300 + 600, win.Currency.Requisition);
            Assert.AreEqual(2 + 3 + 2, win.Currency.Intel);
            Assert.AreEqual((1000 + 1000 + 500 + 750) / 2, loss.OperatorXp);
            Assert.AreEqual((200 + 250 + 200 + 300) / 2, loss.Currency.Requisition);
            Assert.IsTrue(loss.Retained);
            Assert.AreEqual((long)Math.Floor(win.OperatorXp * RunRewardRules.ClassXpShare), win.ClassXp);
        }

        [Test]
        public void OperationWinsPayMoreIntel()
        {
            RunReward survival = RunRewardRules.Compute(new RunResult { Won = true });
            RunReward op = RunRewardRules.Compute(new RunResult { Won = true, Operation = true });
            Assert.Greater(op.Currency.Intel, survival.Currency.Intel);
            Assert.Greater(op.OperatorXp, survival.OperatorXp);
        }

        [Test]
        public void ApplyRunBanksRewardsStatsAndLevels()
        {
            var p = new PlayerProfile();
            p.AddStat(StatKeys.Kills, 120);
            RunSummary s = ProfileRules.ApplyRun(p, "class.gunner", new RunResult { Won = true, Operation = true, Kills = 120, SurgesCleared = 8 }, Challenges);
            Assert.AreEqual(s.Reward.OperatorXp, p.OperatorXp);
            Assert.AreEqual(s.Reward.ClassXp, p.XpOf("class.gunner"));
            Assert.AreEqual(1, p.Stat(StatKeys.RunsWon));
            Assert.AreEqual(1, p.Stat(StatKeys.OperationsWon));
            Assert.Greater(s.OperatorLevelAfter, s.OperatorLevelBefore);
            Assert.Greater(s.RankAfter, s.RankBefore);
            Assert.IsTrue(p.Completed.Contains("ch.exterminator.1"));
            Assert.IsTrue(p.Completed.Contains("ach.first_win"));
            Assert.IsTrue(p.Completed.Contains("ach.extraction"));
            Assert.AreEqual(s.Reward.Currency.Requisition + 200, p.Wallet.Requisition, "Exterminator I pays 200 REQ");
        }

        // ---------- challenges ----------

        [Test]
        public void ChallengesCompleteEachTierOnceInOrder()
        {
            var done = new HashSet<string>();
            long kills = 600;
            Func<string, long> stat = k => k == StatKeys.Kills ? kills : 0;
            List<ChallengeCompletion> first = ChallengeRules.Evaluate(Challenges, stat, done);
            var ids = first.ConvertAll(c => c.Id);
            CollectionAssert.Contains(ids, "ch.exterminator.1");
            CollectionAssert.Contains(ids, "ch.exterminator.2");
            CollectionAssert.DoesNotContain(ids, "ch.exterminator.3");
            Assert.AreEqual(0, ChallengeRules.Evaluate(Challenges, stat, done).Count, "nothing twice");
            kills = 2000;
            List<ChallengeCompletion> later = ChallengeRules.Evaluate(Challenges, stat, done);
            CollectionAssert.Contains(later.ConvertAll(c => c.Id), "ch.exterminator.3");
            CollectionAssert.Contains(later.ConvertAll(c => c.Id), "ach.thousand");
        }

        [Test]
        public void ChallengeProgressText()
        {
            ChallengeDef def = Challenges.Find(c => c.Id == "ch.butcher");
            var done = new HashSet<string>();
            Assert.AreEqual("10 / 25", ChallengeRules.ProgressText(def, 10, done));
            done.Add("ch.butcher.1");
            Assert.AreEqual("30 / 150", ChallengeRules.ProgressText(def, 30, done));
            done.Add("ch.butcher.2");
            done.Add("ch.butcher.3");
            Assert.AreEqual("Complete", ChallengeRules.ProgressText(def, 999, done));
        }

        [Test]
        public void OperatorLevelAndResearchCountFeedAchievements()
        {
            var p = new PlayerProfile { OperatorXp = OperatorLevels.XpForLevel(10) };
            for (int i = 0; i < 5; i++) p.Research.Add("r" + i);
            List<ChallengeCompletion> done = ProfileRules.EvaluateChallenges(p, Challenges);
            var ids = done.ConvertAll(c => c.Id);
            CollectionAssert.Contains(ids, "ach.veteran");
            CollectionAssert.Contains(ids, "ach.scholar");
        }

        // ---------- save format ----------

        private static PlayerProfile Sample()
        {
            var p = new PlayerProfile { OperatorXp = 12345, SelectedClass = "class.tank" };
            p.Wallet.Add(new Cost(4200, 17));
            p.ClassXp["class.tank"] = 9000;
            p.Prestige["class.tank"] = 2;
            p.Skills.Add("skill.tank.0.1");
            p.Research.Add("res.field_medicine");
            p.Completed.Add("ach.first_deploy");
            p.Loadouts["class.tank"] = new[] { "weapon.anvil_lmg", "weapon.breacher_12", "weapon.ranger_9", "weapon.tether_cleaver" };
            p.Stats[StatKeys.Kills] = 777;
            p.WeaponXp["weapon.halberd_ar"] = 3100;
            return p;
        }

        [Test]
        public void SaveRoundTripsEveryField()
        {
            string text = SaveFormat.Write(Sample());
            SaveLoadResult r = SaveFormat.Read(text);
            Assert.AreEqual(SaveLoadStatus.Ok, r.Status);
            Assert.IsFalse(r.Migrated);
            PlayerProfile p = r.Profile;
            Assert.AreEqual(12345, p.OperatorXp);
            Assert.AreEqual("class.tank", p.SelectedClass);
            Assert.AreEqual(4200, p.Wallet.Requisition);
            Assert.AreEqual(17, p.Wallet.Intel);
            Assert.AreEqual(9000, p.XpOf("class.tank"));
            Assert.AreEqual(2, p.PrestigeOf("class.tank"));
            Assert.IsTrue(p.Skills.Contains("skill.tank.0.1"));
            Assert.IsTrue(p.Research.Contains("res.field_medicine"));
            Assert.IsTrue(p.Completed.Contains("ach.first_deploy"));
            CollectionAssert.AreEqual(Sample().Loadouts["class.tank"], p.Loadouts["class.tank"]);
            Assert.AreEqual(777, p.Stat(StatKeys.Kills));
            Assert.AreEqual(3100, p.WeaponXp["weapon.halberd_ar"]);
            Assert.AreEqual(text, SaveFormat.Write(p), "writing is deterministic");
        }

        [Test]
        public void EmptyCorruptAndTamperedFilesAreDetected()
        {
            Assert.AreEqual(SaveLoadStatus.Empty, SaveFormat.Read("").Status);
            Assert.AreEqual(SaveLoadStatus.Empty, SaveFormat.Read(null).Status);
            Assert.AreEqual(SaveLoadStatus.Corrupt, SaveFormat.Read("hello\nworld").Status);
            string text = SaveFormat.Write(Sample());
            Assert.AreEqual(SaveLoadStatus.Corrupt, SaveFormat.Read(text.Replace("cur.cur.req=4200", "cur.cur.req=999999")).Status, "hand edit");
            Assert.AreEqual(SaveLoadStatus.Corrupt, SaveFormat.Read(text.Substring(0, text.Length / 2)).Status, "truncated");
            Assert.AreEqual(SaveLoadStatus.Ok, SaveFormat.Read(text.Replace("\n", "\r\n")).Status, "Windows line endings");
        }

        [Test]
        public void NewerFilesAreRefusedAndOlderOnesMigrate()
        {
            string v2 = Resign(SaveFormat.Write(Sample()).Replace("version=1", "version=2"));
            Assert.AreEqual(SaveLoadStatus.TooNew, SaveFormat.Read(v2).Status);

            // A pretend v1 -> v2 step that renames the old stat key.
            var steps = new List<Action<Dictionary<string, string>>>
            {
                m => { if (m.TryGetValue("stat.oldkills", out string v)) { m.Remove("stat.oldkills"); m["stat.kills"] = v; } },
            };
            string v1 = Resign(SaveFormat.Write(Sample()).Replace("stat.kills=777", "stat.oldkills=42"));
            SaveLoadResult r = SaveFormat.Read(v1, steps);
            Assert.AreEqual(SaveLoadStatus.Ok, r.Status);
            Assert.AreEqual(1, r.FromVersion);
            Assert.AreEqual(42, r.Profile.Stat(StatKeys.Kills));
            Assert.AreEqual(SaveLoadStatus.Ok, SaveFormat.Read(v2, steps).Status, "v2 is current for that build");
        }

        [Test]
        public void UnknownKeysAndBadIdsAreSkipped()
        {
            var p = Sample();
            p.Skills.Add("bad,id");
            p.Stats["has=equals"] = 5;
            string text = SaveFormat.Write(p);
            StringAssert.DoesNotContain("bad,id", text);
            StringAssert.DoesNotContain("has=equals", text);
            string extra = Resign(text.Replace("opxp=", "future.thing=1\nopxp="));
            Assert.AreEqual(SaveLoadStatus.Ok, SaveFormat.Read(extra).Status);
        }

        [Test]
        public void LegacyWeaponXpImportKeepsTheLargerValue()
        {
            var p = new PlayerProfile();
            p.WeaponXp["weapon.wasp_smg"] = 5000;
            int n = SaveFormat.ImportLegacyWeaponXp(p, "weapon.halberd_ar=1200;weapon.wasp_smg=300;junk;=5;weapon.x=-4;");
            Assert.AreEqual(2, n);
            Assert.AreEqual(1200, p.WeaponXp["weapon.halberd_ar"]);
            Assert.AreEqual(5000, p.WeaponXp["weapon.wasp_smg"]);
            Assert.IsFalse(p.WeaponXp.ContainsKey("weapon.x"));
            Assert.AreEqual(0, SaveFormat.ImportLegacyWeaponXp(p, null));
        }

        private static string Resign(string text)
        {
            int at = text.LastIndexOf("sum=", StringComparison.Ordinal);
            string body = text.Substring(0, at);
            return body + "sum=" + SaveFormat.Checksum(body).ToString("x8") + "\n";
        }
    }
}
