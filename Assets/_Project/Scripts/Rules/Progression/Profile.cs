using System;
using System.Collections.Generic;

namespace Deadfall.Rules.Progression
{
    /// <summary>Everything permanent about one player (GAME_DESIGN §9.2). Saved by <see cref="SaveFormat"/>.</summary>
    public sealed class PlayerProfile
    {
        public readonly Wallet Wallet = new Wallet();
        public long OperatorXp;
        public string SelectedClass;
        public readonly Dictionary<string, long> ClassXp = new Dictionary<string, long>();
        public readonly Dictionary<string, int> Prestige = new Dictionary<string, int>();
        public readonly HashSet<string> Skills = new HashSet<string>();
        public readonly HashSet<string> Research = new HashSet<string>();
        public readonly HashSet<string> Completed = new HashSet<string>();
        public readonly Dictionary<string, string[]> Loadouts = new Dictionary<string, string[]>();
        /// <summary>Attachments fitted at the Armory, per weapon id (attachment ids).</summary>
        public readonly Dictionary<string, string[]> Attachments = new Dictionary<string, string[]>();
        public readonly Dictionary<string, long> Stats = new Dictionary<string, long>();
        public readonly Dictionary<string, long> WeaponXp = new Dictionary<string, long>();
        /// <summary>Ids of the most recent banked runs (oldest first), so a run can never pay out twice (M11).</summary>
        public readonly List<string> BankedRuns = new List<string>();

        public int OperatorLevel => OperatorLevels.LevelForXp(OperatorXp);
        public long XpOf(string classId) => classId != null && ClassXp.TryGetValue(classId, out long v) ? v : 0;
        public int RankOf(string classId) => ClassRanks.RankForXp(XpOf(classId));
        public int PrestigeOf(string classId) => classId != null && Prestige.TryGetValue(classId, out int v) ? v : 0;
        public long Stat(string key) => key != null && Stats.TryGetValue(key, out long v) ? v : 0;

        public void AddStat(string key, long amount = 1)
        {
            if (string.IsNullOrEmpty(key) || amount == 0) return;
            Stats[key] = Math.Max(0, Stat(key) + amount);
        }

        public void SetStatMax(string key, long value)
        {
            if (value > Stat(key)) Stats[key] = value;
        }

        public bool ClassUnlocked(string classId) => OperatorLevel >= OperatorLevels.ClassUnlockLevel(classId);
    }

    /// <summary>What one run produced for one player, collected on that player's machine.</summary>
    public struct RunResult
    {
        public bool Won;
        public bool Operation;
        /// <summary>Endless run (M12): it always ends in a wipe, so nothing is cut for failing; deep surges pay extra.</summary>
        public bool Endless;
        public int SurgesCleared;
        /// <summary>Gauntlet run: the environment it ran on and the clear time (ms, 0 if not cleared).</summary>
        public bool Gauntlet;
        public int GauntletEnvironment;
        public long GauntletMs;
        public int Kills;
        public int Objectives;
        public int Specimens;
        /// <summary>Unique id of the run on this machine. A run with an id already banked pays nothing (M11 reconnects).</summary>
        public string RunId;
    }

    public struct RunReward
    {
        public long OperatorXp;
        public long ClassXp;
        public Cost Currency;
        public bool Retained;
    }

    /// <summary>Permanent rewards for a run. Failure keeps <see cref="FailureRetention"/> of them (D-03).</summary>
    public static class RunRewardRules
    {
        public const float FailureRetention = 0.5f;
        /// <summary>Endless pays a bonus for every surge cleared past this one.</summary>
        public const int EndlessDeepFrom = 15;
        public const float ClassXpShare = 0.8f;

        public static RunReward Compute(RunResult r)
        {
            long xp = 10L * r.Kills + 100L * r.SurgesCleared + 250L * r.Objectives + 750L * r.Specimens;
            long req = 2L * r.Kills + 25L * r.SurgesCleared + 100L * r.Objectives + 300L * r.Specimens;
            long intel = r.Objectives + 3L * r.Specimens;
            if (r.Won)
            {
                xp += r.Operation ? 1500 : 1000;
                req += 600;
                intel += r.Operation ? 4 : 2;
            }
            if (r.Endless)
            {
                int deep = Math.Max(0, r.SurgesCleared - EndlessDeepFrom);
                xp += 60L * deep;
                req += 15L * deep;
                intel += deep / 5;
            }
            float keep = r.Won || r.Endless ? 1f : FailureRetention;
            xp = (long)Math.Floor(xp * keep);
            req = (long)Math.Floor(req * keep);
            intel = (long)Math.Floor(intel * keep);
            return new RunReward
            {
                OperatorXp = xp,
                ClassXp = (long)Math.Floor(xp * ClassXpShare),
                Currency = new Cost((int)Math.Min(int.MaxValue, req), (int)Math.Min(int.MaxValue, intel)),
                Retained = !r.Won && !r.Endless,
            };
        }
    }

    public struct RunSummary
    {
        public RunReward Reward;
        public int OperatorLevelBefore, OperatorLevelAfter;
        public int RankBefore, RankAfter;
        public List<ChallengeCompletion> Completed;
        /// <summary>The run had already been banked; nothing was paid.</summary>
        public bool Duplicate;
        /// <summary>Gauntlet: the best time before this run (ms, 0 = none) and whether this run beat it.</summary>
        public long GauntletBestBefore;
        public bool GauntletNewBest;
    }

    /// <summary>Changes to a profile. Every write path goes through here so tests cover it.</summary>
    public static class ProfileRules
    {
        /// <summary>
        /// Banks a finished run: XP, currency and run stats. Per-kill stats are counted live during the run (so
        /// challenge progress is never lost, D-03) and are not touched here.
        /// </summary>
        public const int BankedRunMemory = 20;

        public static bool WasBanked(PlayerProfile p, string runId) => !string.IsNullOrEmpty(runId) && p.BankedRuns.Contains(runId);

        public static RunSummary ApplyRun(PlayerProfile p, string classId, RunResult result, IReadOnlyList<ChallengeDef> challenges)
        {
            if (WasBanked(p, result.RunId))
                return new RunSummary { Duplicate = true, OperatorLevelBefore = p.OperatorLevel, OperatorLevelAfter = p.OperatorLevel,
                    RankBefore = p.RankOf(classId), RankAfter = p.RankOf(classId), Completed = new List<ChallengeCompletion>() };
            if (!string.IsNullOrEmpty(result.RunId))
            {
                p.BankedRuns.Add(result.RunId);
                while (p.BankedRuns.Count > BankedRunMemory) p.BankedRuns.RemoveAt(0);
            }
            var s = new RunSummary
            {
                Reward = RunRewardRules.Compute(result),
                OperatorLevelBefore = p.OperatorLevel,
                RankBefore = p.RankOf(classId),
            };
            p.OperatorXp += s.Reward.OperatorXp;
            if (!string.IsNullOrEmpty(classId)) p.ClassXp[classId] = p.XpOf(classId) + s.Reward.ClassXp;
            p.Wallet.Add(s.Reward.Currency);
            p.AddStat(result.Won ? StatKeys.RunsWon : StatKeys.RunsFailed);
            if (result.Endless) p.SetStatMax(StatKeys.EndlessBest, result.SurgesCleared);
            if (result.Gauntlet)
            {
                string key = GauntletRecords.BestKey(result.GauntletEnvironment);
                s.GauntletBestBefore = p.Stat(key);
                if (result.Won && GauntletRecords.IsNewBest(result.GauntletMs, s.GauntletBestBefore))
                {
                    p.Stats[key] = result.GauntletMs;
                    s.GauntletNewBest = true;
                }
                if (result.Won) p.AddStat(StatKeys.GauntletsWon);
            }
            if (result.Won && result.Operation) p.AddStat(StatKeys.OperationsWon);
            s.OperatorLevelAfter = p.OperatorLevel;
            s.RankAfter = p.RankOf(classId);
            s.Completed = EvaluateChallenges(p, challenges);
            return s;
        }

        /// <summary>Records and pays every newly met challenge tier and achievement.</summary>
        public static List<ChallengeCompletion> EvaluateChallenges(PlayerProfile p, IReadOnlyList<ChallengeDef> catalog)
        {
            p.SetStatMax(StatKeys.OperatorLevel, p.OperatorLevel);
            p.SetStatMax(StatKeys.Research, p.Research.Count);
            if (catalog == null) return new List<ChallengeCompletion>();
            List<ChallengeCompletion> done = ChallengeRules.Evaluate(catalog, p.Stat, p.Completed);
            foreach (ChallengeCompletion c in done) p.Wallet.Add(c.Reward);
            return done;
        }

        public static SkillUnlockError UnlockSkill(PlayerProfile p, IReadOnlyList<SkillNodeDef> catalog, string nodeId)
        {
            SkillNodeDef node = SkillTreeRules.Find(catalog, nodeId);
            SkillUnlockError e = SkillTreeRules.CanUnlock(catalog, node, p.RankOf(node?.ClassId), p.Skills);
            if (e == SkillUnlockError.Ok) p.Skills.Add(node.Id);
            return e;
        }

        public static ResearchError Research(PlayerProfile p, IReadOnlyList<ResearchDef> catalog, string id)
        {
            ResearchError e = ResearchRules.Research(ResearchRules.Find(catalog, id), p.Research, p.OperatorLevel, p.Wallet);
            if (e == ResearchError.Ok) p.SetStatMax(StatKeys.Research, p.Research.Count);
            return e;
        }

        /// <summary>
        /// Prestige (foundation): a max-rank class goes back to rank 1 with its skill points refunded, and its
        /// prestige count goes up. Unlocks, research, weapons and currencies stay.
        /// </summary>
        public static bool Prestige(PlayerProfile p, IReadOnlyList<SkillNodeDef> skills, string classId)
        {
            if (string.IsNullOrEmpty(classId) || !ClassRanks.CanPrestige(p.XpOf(classId), p.PrestigeOf(classId))) return false;
            p.ClassXp[classId] = 0;
            p.Prestige[classId] = p.PrestigeOf(classId) + 1;
            SkillTreeRules.Reset(skills, classId, p.Skills);
            p.AddStat(StatKeys.Prestiges);
            return true;
        }

        /// <summary>Permanent stat bonuses (skills of the class in play + research), indexed by ModStat.</summary>
        public static float[] PermanentTotals(PlayerProfile p, string classId, IReadOnlyList<SkillNodeDef> skills, IReadOnlyList<ResearchDef> research)
        {
            var totals = new float[Enum.GetValues(typeof(ModStat)).Length];
            if (p == null) return totals;
            if (skills != null && classId != null) SkillTreeRules.AddEffects(skills, classId, p.Skills, totals);
            if (research != null) ResearchRules.AddEffects(research, p.Research, classId, totals);
            return totals;
        }
    }
}
