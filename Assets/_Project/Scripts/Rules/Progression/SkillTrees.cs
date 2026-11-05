using System;
using System.Collections.Generic;

namespace Deadfall.Rules.Progression
{
    /// <summary>
    /// One node of a class skill tree: three branches per class, three tiers per branch. Effects use the Field Mod
    /// stats, so the player applies them through the same totals as Field Mods (permanent bonus layer).
    /// </summary>
    public sealed class SkillNodeDef
    {
        public string Id;
        public string ClassId;
        public int Branch;
        public string BranchName;
        /// <summary>1..3.</summary>
        public int Tier;
        public string DisplayName;
        public string Description;
        public ModEffect[] Effects = Array.Empty<ModEffect>();

        public int Cost => SkillTreeRules.CostForTier(Tier);
        public int RequiredRank => SkillTreeRules.RankForTier(Tier);
    }

    public enum SkillUnlockError : byte { Ok, UnknownNode, AlreadyUnlocked, RankTooLow, MissingPrerequisite, NotEnoughPoints }

    public static class SkillTreeRules
    {
        public const int Branches = 3;
        public const int Tiers = 3;

        public static int CostForTier(int tier) => Math.Max(1, Math.Min(Tiers, tier));
        public static int RankForTier(int tier) => tier switch { 1 => 2, 2 => 5, _ => 10 };

        /// <summary>The node one tier down in the same branch, or null for tier 1.</summary>
        public static SkillNodeDef Prerequisite(IReadOnlyList<SkillNodeDef> catalog, SkillNodeDef node)
        {
            if (node == null || node.Tier <= 1) return null;
            foreach (SkillNodeDef n in catalog)
                if (n.ClassId == node.ClassId && n.Branch == node.Branch && n.Tier == node.Tier - 1) return n;
            return null;
        }

        public static int Spent(IReadOnlyList<SkillNodeDef> catalog, string classId, ICollection<string> unlocked)
        {
            int total = 0;
            foreach (SkillNodeDef n in catalog)
                if (n.ClassId == classId && unlocked.Contains(n.Id)) total += n.Cost;
            return total;
        }

        public static int Available(IReadOnlyList<SkillNodeDef> catalog, string classId, int rank, ICollection<string> unlocked)
            => ClassRanks.SkillPoints(rank) - Spent(catalog, classId, unlocked);

        public static SkillUnlockError CanUnlock(IReadOnlyList<SkillNodeDef> catalog, SkillNodeDef node, int rank, ICollection<string> unlocked)
        {
            if (node == null) return SkillUnlockError.UnknownNode;
            if (unlocked.Contains(node.Id)) return SkillUnlockError.AlreadyUnlocked;
            if (rank < node.RequiredRank) return SkillUnlockError.RankTooLow;
            SkillNodeDef pre = Prerequisite(catalog, node);
            if (pre != null && !unlocked.Contains(pre.Id)) return SkillUnlockError.MissingPrerequisite;
            if (Available(catalog, node.ClassId, rank, unlocked) < node.Cost) return SkillUnlockError.NotEnoughPoints;
            return SkillUnlockError.Ok;
        }

        /// <summary>Removes every node of one class (respec, prestige). Returns how many were removed.</summary>
        public static int Reset(IReadOnlyList<SkillNodeDef> catalog, string classId, ICollection<string> unlocked)
        {
            int removed = 0;
            foreach (SkillNodeDef n in catalog)
                if (n.ClassId == classId && unlocked.Remove(n.Id)) removed++;
            return removed;
        }

        /// <summary>Adds the effects of the unlocked nodes of <paramref name="classId"/> into <paramref name="totals"/> (indexed by ModStat).</summary>
        public static void AddEffects(IReadOnlyList<SkillNodeDef> catalog, string classId, ICollection<string> unlocked, float[] totals)
        {
            foreach (SkillNodeDef n in catalog)
            {
                if (n.ClassId != classId || !unlocked.Contains(n.Id)) continue;
                foreach (ModEffect e in n.Effects) totals[(int)e.Stat] += e.Value;
            }
        }

        public static SkillNodeDef Find(IReadOnlyList<SkillNodeDef> catalog, string id)
        {
            foreach (SkillNodeDef n in catalog) if (n.Id == id) return n;
            return null;
        }
    }

    /// <summary>Skill trees for the five classes (T-52; first balance pass T-77, see BALANCE.md).</summary>
    public static class SkillTreeCatalog
    {
        public static List<SkillNodeDef> Prototype()
        {
            var list = new List<SkillNodeDef>(45);

            string c = "class.gunner";
            Branch(list, c, 0, "Marksman",
                N("Steady Aim", "-10% recoil", E(ModStat.Recoil, -0.1f)),
                N("Hollow Points", "+8% weapon damage", E(ModStat.Damage, 0.08f)),
                N("Deadeye", "+12% weapon damage", E(ModStat.Damage, 0.12f)));
            Branch(list, c, 1, "Suppressor",
                N("Deep Pockets", "+15% reserve ammo", E(ModStat.Reserve, 0.15f)),
                N("Trigger Discipline", "+6% fire rate", E(ModStat.FireRate, 0.06f)),
                N("Weapon Mastery", "+10% fire rate with preferred weapons", E(ModStat.PreferredFireRate, 0.1f)));
            Branch(list, c, 2, "Operator",
                N("Fast Mags", "+10% reload speed", E(ModStat.ReloadSpeed, 0.1f)),
                N("Light Kit", "+4% move speed", E(ModStat.MoveSpeed, 0.04f)),
                N("Field Drills", "+12% ability cooldown rate", E(ModStat.CooldownRate, 0.12f)));

            c = "class.tank";
            Branch(list, c, 0, "Bulwark",
                N("Plating", "+15 max health", E(ModStat.MaxHealth, 15f)),
                N("Hardened", "+5% damage reduction", E(ModStat.DamageReduction, 0.05f)),
                N("Immovable", "+25 max health, +5% damage reduction", E(ModStat.MaxHealth, 25f), E(ModStat.DamageReduction, 0.05f)));
            Branch(list, c, 1, "Enforcer",
                N("Heavy Rounds", "+5% weapon damage", E(ModStat.Damage, 0.05f)),
                N("Ammo Belt", "+20% reserve ammo", E(ModStat.Reserve, 0.2f)),
                N("Last Stand", "+20% damage below 30% health", E(ModStat.LowHealthDamage, 0.2f)));
            Branch(list, c, 2, "Guardian",
                N("Shield Drills", "+10% ability cooldown rate", E(ModStat.CooldownRate, 0.1f)),
                N("Field Triage", "Heal 10 after each surge", E(ModStat.HealOnSurgeClear, 10f)),
                N("Second Skin", "Heal 3 on kill", E(ModStat.HealOnKill, 3f)));

            c = "class.warrior";
            Branch(list, c, 0, "Blademaster",
                N("Honed Edge", "+10% melee damage", E(ModStat.MeleeDamage, 0.1f)),
                N("Butcher", "+15% melee damage", E(ModStat.MeleeDamage, 0.15f)),
                N("Executioner", "+20% melee damage", E(ModStat.MeleeDamage, 0.2f)));
            Branch(list, c, 1, "Berserker",
                N("Bloodthirst", "Heal 2 on kill", E(ModStat.HealOnKill, 2f)),
                N("Frenzy", "+12% melee damage", E(ModStat.MeleeDamage, 0.12f)),
                N("Red Mist", "+25% damage below 30% health", E(ModStat.LowHealthDamage, 0.25f)));
            Branch(list, c, 2, "Skirmisher",
                N("Footwork", "+4% move speed", E(ModStat.MoveSpeed, 0.04f)),
                N("Toughened", "+15 max health", E(ModStat.MaxHealth, 15f)),
                N("Relentless", "+15% ability cooldown rate", E(ModStat.CooldownRate, 0.15f)));

            c = "class.engineer";
            Branch(list, c, 0, "Fabricator",
                N("Quick Tools", "+10% ability cooldown rate", E(ModStat.CooldownRate, 0.1f)),
                N("Rapid Assembly", "+10% ability cooldown rate", E(ModStat.CooldownRate, 0.1f)),
                N("Overclock", "+15% ability cooldown rate", E(ModStat.CooldownRate, 0.15f)));
            Branch(list, c, 1, "Defender",
                N("Hazard Suit", "+10 max health", E(ModStat.MaxHealth, 10f)),
                N("Hard Hat", "+4% damage reduction", E(ModStat.DamageReduction, 0.04f)),
                N("Field Repairs", "Heal 2 on kill and 12 after each surge", E(ModStat.HealOnKill, 2f), E(ModStat.HealOnSurgeClear, 12f)));
            Branch(list, c, 2, "Gunsmith",
                N("Tuned Actions", "+8% reload speed", E(ModStat.ReloadSpeed, 0.08f)),
                N("Compensators", "-12% recoil", E(ModStat.Recoil, -0.12f)),
                N("Match Grade", "+8% weapon damage", E(ModStat.Damage, 0.08f)));

            c = "class.demolitionist";
            Branch(list, c, 0, "Sapper",
                N("Shaped Charges", "+5% weapon damage", E(ModStat.Damage, 0.05f)),
                N("Bigger Boom", "+7% weapon damage", E(ModStat.Damage, 0.07f)),
                N("Overkill", "+10% weapon damage", E(ModStat.Damage, 0.1f)));
            Branch(list, c, 1, "Bombardier",
                N("Extra Shells", "+20% reserve ammo", E(ModStat.Reserve, 0.2f)),
                N("Fast Loader", "+12% reload speed", E(ModStat.ReloadSpeed, 0.12f)),
                N("Short Fuse", "+15% ability cooldown rate", E(ModStat.CooldownRate, 0.15f)));
            Branch(list, c, 2, "Blast Proof",
                N("Padding", "+10 max health", E(ModStat.MaxHealth, 10f)),
                N("Flak Vest", "+5% damage reduction", E(ModStat.DamageReduction, 0.05f)),
                N("Adrenaline", "Heal 3 on kill", E(ModStat.HealOnKill, 3f)));

            return list;
        }

        private struct NodeSpec { public string Name, Desc; public ModEffect[] Effects; }

        private static NodeSpec N(string name, string desc, params ModEffect[] effects) => new NodeSpec { Name = name, Desc = desc, Effects = effects };
        private static ModEffect E(ModStat s, float v) => new ModEffect(s, v);

        private static void Branch(List<SkillNodeDef> list, string classId, int branch, string branchName, params NodeSpec[] tiers)
        {
            string cls = classId.Substring(classId.IndexOf('.') + 1);
            for (int t = 0; t < tiers.Length; t++)
            {
                list.Add(new SkillNodeDef
                {
                    Id = $"skill.{cls}.{branch}.{t + 1}",
                    ClassId = classId,
                    Branch = branch,
                    BranchName = branchName,
                    Tier = t + 1,
                    DisplayName = tiers[t].Name,
                    Description = tiers[t].Desc,
                    Effects = tiers[t].Effects,
                });
            }
        }
    }
}
