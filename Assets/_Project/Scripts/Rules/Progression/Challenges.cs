using System;
using System.Collections.Generic;

namespace Deadfall.Rules.Progression
{
    /// <summary>Lifetime stat counters kept in the profile. Challenges and achievements read these.</summary>
    public static class StatKeys
    {
        public const string Kills = "kills";
        public const string MeleeKills = "kills.melee";
        public const string CritKills = "kills.crit";
        public const string SurgesCleared = "surges";
        public const string RunsStarted = "runs.started";
        public const string RunsWon = "runs.won";
        public const string RunsFailed = "runs.failed";
        public const string OperationsWon = "operations.won";
        public const string Objectives = "objectives";
        public const string Specimens = "specimens";
        public const string StructuresBuilt = "structures";
        public const string FieldMods = "fieldmods";
        public const string Research = "research";
        public const string Prestiges = "prestiges";
        public const string OperatorLevel = "operator.level";
        public static string KillsOf(string enemyId) => "kills." + enemyId;
    }

    public enum ChallengeKind : byte
    {
        /// <summary>Tiered goals that pay Requisition/Intel.</summary>
        Challenge,
        /// <summary>One-off milestones shown in the Trophy Room.</summary>
        Achievement,
    }

    public sealed class ChallengeDef
    {
        public string Id;
        public string DisplayName;
        /// <summary>"{0}" is replaced by the tier target.</summary>
        public string Description;
        public ChallengeKind Kind;
        public string Stat;
        public long[] Targets;
        public Cost[] Rewards;

        public int TierCount => Targets.Length;
        public string TierId(int tier) => TierCount == 1 ? Id : $"{Id}.{tier + 1}";
    }

    public readonly struct ChallengeCompletion
    {
        public readonly ChallengeDef Def;
        public readonly int Tier;
        public ChallengeCompletion(ChallengeDef def, int tier) { Def = def; Tier = tier; }
        public Cost Reward => Def.Rewards != null && Tier < Def.Rewards.Length ? Def.Rewards[Tier] : default;
        public string Id => Def.TierId(Tier);
    }

    public static class ChallengeRules
    {
        /// <summary>How many tiers of <paramref name="def"/> are already recorded as done.</summary>
        public static int TiersDone(ChallengeDef def, ICollection<string> completed)
        {
            int n = 0;
            while (n < def.TierCount && completed.Contains(def.TierId(n))) n++;
            return n;
        }

        /// <summary>
        /// Every tier whose target the stats now meet that isn't recorded yet, lowest tier first. Records them in
        /// <paramref name="completed"/>; the caller pays the rewards.
        /// </summary>
        public static List<ChallengeCompletion> Evaluate(IReadOnlyList<ChallengeDef> catalog, Func<string, long> stat, ICollection<string> completed)
        {
            var result = new List<ChallengeCompletion>();
            foreach (ChallengeDef def in catalog)
            {
                long value = stat(def.Stat);
                for (int t = TiersDone(def, completed); t < def.TierCount; t++)
                {
                    if (value < def.Targets[t]) break;
                    completed.Add(def.TierId(t));
                    result.Add(new ChallengeCompletion(def, t));
                }
            }
            return result;
        }

        /// <summary>Progress text toward the next tier, e.g. "120 / 500".</summary>
        public static string ProgressText(ChallengeDef def, long value, ICollection<string> completed)
        {
            int t = TiersDone(def, completed);
            if (t >= def.TierCount) return "Complete";
            return $"{Math.Min(value, def.Targets[t])} / {def.Targets[t]}";
        }
    }

    public static class ChallengeCatalog
    {
        public static List<ChallengeDef> Prototype() => new List<ChallengeDef>
        {
            C("ch.exterminator", "Exterminator", "Kill {0} infected", StatKeys.Kills, new long[] { 100, 500, 2000 }, R(200), R(500), R(1200, 2)),
            C("ch.sharpshooter", "Sharpshooter", "Get {0} kills with critical hits", StatKeys.CritKills, new long[] { 50, 250, 1000 }, R(200), R(500), R(1200, 2)),
            C("ch.butcher", "Butcher", "Get {0} melee kills", StatKeys.MeleeKills, new long[] { 25, 150, 600 }, R(200), R(500), R(1200, 2)),
            C("ch.survivor", "Survivor", "Clear {0} surges", StatKeys.SurgesCleared, new long[] { 10, 50, 200 }, R(250), R(600), R(1500, 3)),
            C("ch.specialist", "Specialist", "Complete {0} objectives", StatKeys.Objectives, new long[] { 5, 25, 100 }, R(200, 1), R(500, 2), R(1200, 4)),
            C("ch.hunter", "Specimen Hunter", "Destroy {0} Specimens", StatKeys.Specimens, new long[] { 1, 5, 20 }, R(300, 2), R(800, 4), R(2000, 8)),
            C("ch.architect", "Architect", "Build {0} structures", StatKeys.StructuresBuilt, new long[] { 10, 50, 200 }, R(200), R(500), R(1200, 2)),
            C("ch.adaptive", "Adaptive", "Pick {0} Field Mods", StatKeys.FieldMods, new long[] { 10, 50, 150 }, R(150), R(400), R(1000, 2)),

            A("ach.first_deploy", "First Deployment", "Start a run", StatKeys.RunsStarted, 1),
            A("ach.first_win", "Mission Accomplished", "Win a run", StatKeys.RunsWon, 1),
            A("ach.extraction", "Clean Extraction", "Win an Operation", StatKeys.OperationsWon, 1),
            A("ach.specimen_zero", "Patient Zero", "Destroy a Specimen", StatKeys.Specimens, 1),
            A("ach.veteran", "Veteran Operator", "Reach operator level {0}", StatKeys.OperatorLevel, 10),
            A("ach.scholar", "Scholar", "Finish {0} research projects", StatKeys.Research, 5),
            A("ach.prestige", "Born Again", "Prestige a class", StatKeys.Prestiges, 1),
            A("ach.thousand", "One Thousand", "Kill {0} infected", StatKeys.Kills, 1000),
        };

        private static Cost R(int req, int intel = 0) => new Cost(req, intel);

        private static ChallengeDef C(string id, string name, string desc, string stat, long[] targets, params Cost[] rewards)
            => new ChallengeDef { Id = id, DisplayName = name, Description = desc, Kind = ChallengeKind.Challenge, Stat = stat, Targets = targets, Rewards = rewards };

        private static ChallengeDef A(string id, string name, string desc, string stat, long target)
            => new ChallengeDef { Id = id, DisplayName = name, Description = desc, Kind = ChallengeKind.Achievement, Stat = stat, Targets = new[] { target }, Rewards = Array.Empty<Cost>() };
    }
}
