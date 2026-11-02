using System;
using System.Collections.Generic;

namespace Deadfall.Rules.Progression
{
    /// <summary>A currency the economy knows about (D-04). Temporary ones reset every deployment.</summary>
    public sealed class CurrencyDefinition
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly string Short;
        public readonly bool Permanent;

        public CurrencyDefinition(string id, string displayName, string shortName, bool permanent)
        {
            Id = id; DisplayName = displayName; Short = shortName; Permanent = permanent;
        }
    }

    public static class Currencies
    {
        public static readonly CurrencyDefinition FieldCredits = new CurrencyDefinition("cur.credits", "Field Credits", "FC", false);
        public static readonly CurrencyDefinition Scrap = new CurrencyDefinition("cur.scrap", "Scrap", "SCR", false);
        public static readonly CurrencyDefinition Requisition = new CurrencyDefinition("cur.req", "Requisition", "REQ", true);
        public static readonly CurrencyDefinition Intel = new CurrencyDefinition("cur.intel", "Intel", "INT", true);
        public static readonly CurrencyDefinition[] All = { FieldCredits, Scrap, Requisition, Intel };
    }

    /// <summary>A price in permanent currencies.</summary>
    public readonly struct Cost
    {
        public readonly int Requisition;
        public readonly int Intel;
        public Cost(int requisition, int intel) { Requisition = Math.Max(0, requisition); Intel = Math.Max(0, intel); }
        public bool IsFree => Requisition == 0 && Intel == 0;
        public override string ToString() => Intel > 0 ? $"{Requisition} REQ + {Intel} Intel" : $"{Requisition} REQ";
    }

    /// <summary>Balances by currency id. Never goes negative.</summary>
    public sealed class Wallet
    {
        private readonly Dictionary<string, long> _amounts = new Dictionary<string, long>();

        public long Get(string currencyId) => currencyId != null && _amounts.TryGetValue(currencyId, out long v) ? v : 0;
        public long Requisition => Get(Currencies.Requisition.Id);
        public long Intel => Get(Currencies.Intel.Id);
        public IEnumerable<KeyValuePair<string, long>> All => _amounts;

        public void Set(string currencyId, long amount) => _amounts[currencyId] = Math.Max(0, amount);
        public void Add(string currencyId, long amount) { if (amount > 0) Set(currencyId, Get(currencyId) + amount); }
        public void Add(Cost c) { Add(Currencies.Requisition.Id, c.Requisition); Add(Currencies.Intel.Id, c.Intel); }

        public bool CanAfford(Cost c) => Requisition >= c.Requisition && Intel >= c.Intel;

        public bool TrySpend(Cost c)
        {
            if (!CanAfford(c)) return false;
            Set(Currencies.Requisition.Id, Requisition - c.Requisition);
            Set(Currencies.Intel.Id, Intel - c.Intel);
            return true;
        }

        public void Clear() => _amounts.Clear();
    }

    /// <summary>Account (operator) level from account XP. Unlocks classes and FOB facilities.</summary>
    public static class OperatorLevels
    {
        public const int MaxLevel = 50;

        public static long XpForLevel(int level)
        {
            level = Math.Max(1, Math.Min(MaxLevel, level));
            return 600L * (level - 1) * level / 2;
        }

        public static int LevelForXp(long xp)
        {
            int level = 1;
            while (level < MaxLevel && xp >= XpForLevel(level + 1)) level++;
            return level;
        }

        /// <summary>0..1 progress through the current level (1 at max).</summary>
        public static float Progress(long xp)
        {
            int l = LevelForXp(xp);
            if (l >= MaxLevel) return 1f;
            long a = XpForLevel(l), b = XpForLevel(l + 1);
            return (float)(xp - a) / (b - a);
        }

        /// <summary>Operator level a class needs. The three launch classes are open from the start.</summary>
        public static int ClassUnlockLevel(string classId) => classId switch
        {
            "class.engineer" => 2,
            "class.demolitionist" => 3,
            _ => 1,
        };
    }

    /// <summary>Class rank (per class XP), skill points and prestige.</summary>
    public static class ClassRanks
    {
        public const int MaxRank = 30;
        public const int MaxPrestige = 10;

        public static long XpForRank(int rank)
        {
            rank = Math.Max(1, Math.Min(MaxRank, rank));
            return 400L * (rank - 1) * rank / 2;
        }

        public static int RankForXp(long xp)
        {
            int rank = 1;
            while (rank < MaxRank && xp >= XpForRank(rank + 1)) rank++;
            return rank;
        }

        /// <summary>One skill point per rank after the first.</summary>
        public static int SkillPoints(int rank) => Math.Max(0, Math.Min(MaxRank, rank) - 1);

        public static bool CanPrestige(long classXp, int prestige) => RankForXp(classXp) >= MaxRank && prestige < MaxPrestige;

        public static string PrestigeTitle(int prestige) => prestige <= 0 ? string.Empty : prestige switch
        {
            1 => "Veteran",
            2 => "Elite",
            3 => "Vanguard",
            4 => "Warden",
            5 => "Paragon",
            _ => $"Paragon {prestige - 4}",
        };
    }

    /// <summary>The seven FOB Halcyon facilities (GAME_DESIGN §13).</summary>
    public enum FacilityId : byte
    {
        CommandCenter, Armory, TrainingRange, EngineeringWorkshop, ResearchLab, IntelligenceCenter, TrophyRoom,
    }

    public static class Facilities
    {
        public static readonly FacilityId[] All =
        {
            FacilityId.CommandCenter, FacilityId.Armory, FacilityId.TrainingRange, FacilityId.EngineeringWorkshop,
            FacilityId.ResearchLab, FacilityId.IntelligenceCenter, FacilityId.TrophyRoom,
        };

        public static int UnlockLevel(FacilityId f) => f switch
        {
            FacilityId.ResearchLab => 2,
            FacilityId.EngineeringWorkshop => 2,
            FacilityId.IntelligenceCenter => 3,
            FacilityId.TrophyRoom => 1,
            _ => 1,
        };

        public static bool IsOpen(FacilityId f, int operatorLevel) => operatorLevel >= UnlockLevel(f);

        public static string Name(FacilityId f) => f switch
        {
            FacilityId.CommandCenter => "Command Center",
            FacilityId.Armory => "Armory",
            FacilityId.TrainingRange => "Training Range",
            FacilityId.EngineeringWorkshop => "Engineering Workshop",
            FacilityId.ResearchLab => "Research Lab",
            FacilityId.IntelligenceCenter => "Intelligence Center",
            FacilityId.TrophyRoom => "Trophy Room",
            _ => f.ToString(),
        };
    }
}
