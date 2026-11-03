using System;
using System.Collections.Generic;
using Deadfall.Rules.Common;

namespace Deadfall.Rules.Progression
{
    public enum ModRarity : byte { Common, Rare, Legendary }

    /// <summary>How a mod behaves when picked again (GAME_DESIGN §9.1).</summary>
    public enum StackRule : byte { Unique, Stacks, Evolves }

    /// <summary>What a Field Mod changes. Values add up per stack; multipliers are "+x" (0.1 = +10%).</summary>
    public enum ModStat : byte
    {
        Damage, FireRate, ReloadSpeed, Reserve, Recoil, MeleeDamage, MoveSpeed, MaxHealth, DamageReduction,
        CooldownRate, HealOnKill, HealOnSurgeClear, LowHealthDamage, KillFireRateBurst, PreferredFireRate,
    }

    public readonly struct ModEffect
    {
        public readonly ModStat Stat;
        public readonly float Value;
        public ModEffect(ModStat stat, float value) { Stat = stat; Value = value; }
    }

    public sealed class FieldModDef
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public ModRarity Rarity;
        /// <summary>Class content id this mod belongs to, or null for everyone.</summary>
        public string ClassId;
        public StackRule Stack = StackRule.Stacks;
        public int MaxStacks = 3;
        /// <summary>For <see cref="StackRule.Evolves"/>: the mod this turns into at <see cref="EvolveAt"/> stacks.</summary>
        public string EvolvesInto;
        public int EvolveAt = 3;
        /// <summary>Evolved forms are never offered directly.</summary>
        public bool IsEvolution;
        public ModEffect[] Effects = Array.Empty<ModEffect>();
    }

    /// <summary>A player's Field Mods for the current deployment (cleared when it ends).</summary>
    public sealed class FieldModInventory
    {
        private readonly Dictionary<string, int> _stacks = new Dictionary<string, int>();
        private readonly IReadOnlyList<FieldModDef> _catalog;

        public FieldModInventory(IReadOnlyList<FieldModDef> catalog) { _catalog = catalog; }

        public IReadOnlyDictionary<string, int> Owned => _stacks;
        public int Stacks(string id) => id != null && _stacks.TryGetValue(id, out int n) ? n : 0;
        public int Count => _stacks.Count;
        public void Clear() => _stacks.Clear();

        public FieldModDef Find(string id)
        {
            foreach (FieldModDef d in _catalog) if (d.Id == id) return d;
            return null;
        }

        public bool CanTake(FieldModDef d)
        {
            if (d == null || d.IsEvolution) return false;
            int n = Stacks(d.Id);
            switch (d.Stack)
            {
                case StackRule.Unique: return n == 0;
                case StackRule.Evolves: return n < d.EvolveAt && (d.EvolvesInto == null || Stacks(d.EvolvesInto) == 0);
                default: return n < Math.Max(1, d.MaxStacks);
            }
        }

        /// <summary>Adds one stack. Returns the mod actually gained (the evolved form when it evolves).</summary>
        public FieldModDef Add(FieldModDef d)
        {
            if (!CanTake(d)) return null;
            int n = Stacks(d.Id) + 1;
            if (d.Stack == StackRule.Evolves && d.EvolvesInto != null && n >= d.EvolveAt)
            {
                FieldModDef evolved = Find(d.EvolvesInto);
                if (evolved != null)
                {
                    _stacks.Remove(d.Id);
                    _stacks[evolved.Id] = 1;
                    return evolved;
                }
            }
            _stacks[d.Id] = n;
            return d;
        }

        /// <summary>Sum of a stat over every owned mod and stack.</summary>
        public float Total(ModStat stat)
        {
            float t = 0f;
            foreach (KeyValuePair<string, int> kv in _stacks)
            {
                FieldModDef d = Find(kv.Key);
                if (d == null) continue;
                foreach (ModEffect e in d.Effects) if (e.Stat == stat) t += e.Value * kv.Value;
            }
            return t;
        }
    }

    /// <summary>Rolls Field Mod offers: three different mods the player can take, from their class pool and the shared pool.</summary>
    public static class FieldModOffers
    {
        /// <summary>Offered after every third surge and after a Specimen surge.</summary>
        public static bool OfferAfterSurge(int surge, bool specimen) => specimen || (surge > 0 && surge % 3 == 0);

        /// <summary>Rarity weights (Common, Rare, Legendary): legendaries get likelier with surge number.</summary>
        public static void Weights(int surge, out float common, out float rare, out float legendary)
        {
            legendary = Math.Min(15f, 4f + surge * 0.5f);
            rare = Math.Min(35f, 22f + surge * 0.5f);
            common = 100f - rare - legendary;
        }

        public static List<FieldModDef> Roll(DeterministicRandom rng, IReadOnlyList<FieldModDef> catalog, FieldModInventory owned,
            string classId, int surge, int count = 3)
        {
            var result = new List<FieldModDef>(count);
            Weights(surge, out float wc, out float wr, out float wl);
            for (int i = 0; i < count; i++)
            {
                float r = rng.NextFloat() * (wc + wr + wl);
                ModRarity want = r < wl ? ModRarity.Legendary : r < wl + wr ? ModRarity.Rare : ModRarity.Common;
                FieldModDef pick = PickFrom(rng, catalog, owned, classId, want, result);
                // Fall back through the other rarities when a pool is used up.
                for (int k = 0; pick == null && k < 3; k++) pick = PickFrom(rng, catalog, owned, classId, (ModRarity)k, result);
                if (pick == null) break;
                result.Add(pick);
            }
            return result;
        }

        private static FieldModDef PickFrom(DeterministicRandom rng, IReadOnlyList<FieldModDef> catalog, FieldModInventory owned,
            string classId, ModRarity rarity, List<FieldModDef> already)
        {
            var pool = new List<FieldModDef>();
            foreach (FieldModDef d in catalog)
            {
                if (d.Rarity != rarity || already.Contains(d) || !owned.CanTake(d)) continue;
                if (d.ClassId != null && d.ClassId != classId) continue;
                pool.Add(d);
                if (d.ClassId != null) pool.Add(d); // class mods are twice as likely
            }
            return pool.Count == 0 ? null : pool[rng.Range(0, pool.Count)];
        }
    }

    /// <summary>The prototype Field Mod set (placeholder numbers, T-37).</summary>
    public static class FieldModCatalog
    {
        private static FieldModDef M(string id, string name, string desc, ModRarity rarity, StackRule stack, int max, params ModEffect[] effects) =>
            new FieldModDef { Id = "mod." + id, DisplayName = name, Description = desc, Rarity = rarity, Stack = stack, MaxStacks = max, Effects = effects };

        private static ModEffect E(ModStat s, float v) => new ModEffect(s, v);

        public static List<FieldModDef> Prototype()
        {
            var list = new List<FieldModDef>
            {
                // Common
                M("hollow_points", "Hollow Points", "+8% weapon damage.", ModRarity.Common, StackRule.Stacks, 5, E(ModStat.Damage, 0.08f)),
                M("quick_hands", "Quick Hands", "+12% reload speed.", ModRarity.Common, StackRule.Stacks, 3, E(ModStat.ReloadSpeed, 0.12f)),
                M("ammo_pouch", "Ammo Pouch", "+20% reserve ammo.", ModRarity.Common, StackRule.Stacks, 3, E(ModStat.Reserve, 0.2f)),
                M("fleet_foot", "Fleet Foot", "+5% move speed.", ModRarity.Common, StackRule.Stacks, 3, E(ModStat.MoveSpeed, 0.05f)),
                M("thick_skin", "Thick Skin", "+15 max health.", ModRarity.Common, StackRule.Stacks, 4, E(ModStat.MaxHealth, 15f)),
                M("steady_grip", "Steady Grip", "-10% recoil.", ModRarity.Common, StackRule.Stacks, 3, E(ModStat.Recoil, -0.1f)),
                M("second_wind", "Second Wind", "Heal 30 when a surge is cleared.", ModRarity.Common, StackRule.Unique, 1, E(ModStat.HealOnSurgeClear, 30f)),
                // M12 commons
                M("trigger_discipline", "Trigger Discipline", "+6% fire rate.", ModRarity.Common, StackRule.Stacks, 3, E(ModStat.FireRate, 0.06f)),
                M("light_pack", "Light Pack", "+4% move speed and +10% reserve ammo.", ModRarity.Common, StackRule.Stacks, 2, E(ModStat.MoveSpeed, 0.04f), E(ModStat.Reserve, 0.1f)),
                M("field_dressing", "Field Dressing", "Heal 15 when a surge is cleared.", ModRarity.Common, StackRule.Stacks, 3, E(ModStat.HealOnSurgeClear, 15f)),
                // Rare
                M("adrenal_surge", "Adrenal Surge", "Kills give +25% fire rate for 2 s.", ModRarity.Rare, StackRule.Unique, 1, E(ModStat.KillFireRateBurst, 0.25f)),
                M("overclock", "Overclock", "Ability cooldowns 15% faster.", ModRarity.Rare, StackRule.Stacks, 2, E(ModStat.CooldownRate, 0.15f)),
                M("brawler", "Brawler", "+20% melee damage.", ModRarity.Rare, StackRule.Stacks, 3, E(ModStat.MeleeDamage, 0.2f)),
                M("kevlar_weave", "Kevlar Weave", "5% damage reduction.", ModRarity.Rare, StackRule.Stacks, 3, E(ModStat.DamageReduction, 0.05f)),
                new FieldModDef
                {
                    Id = "mod.vampiric_rounds", DisplayName = "Vampiric Rounds", Description = "Kills heal 1. At 3 stacks becomes Blood Harvest.",
                    Rarity = ModRarity.Rare, Stack = StackRule.Evolves, EvolvesInto = "mod.blood_harvest", EvolveAt = 3,
                    Effects = new[] { E(ModStat.HealOnKill, 1f) },
                },
                new FieldModDef
                {
                    Id = "mod.blood_harvest", DisplayName = "Blood Harvest", Description = "Kills heal 4 and weapon damage +10%.",
                    Rarity = ModRarity.Legendary, Stack = StackRule.Unique, IsEvolution = true,
                    Effects = new[] { E(ModStat.HealOnKill, 4f), E(ModStat.Damage, 0.1f) },
                },
                // M12 rares
                M("hardened", "Hardened", "4% damage reduction and +10 max health.", ModRarity.Rare, StackRule.Stacks, 3, E(ModStat.DamageReduction, 0.04f), E(ModStat.MaxHealth, 10f)),
                M("sleight", "Sleight of Hand", "+15% reload speed and -8% recoil.", ModRarity.Rare, StackRule.Stacks, 2, E(ModStat.ReloadSpeed, 0.15f), E(ModStat.Recoil, -0.08f)),
                new FieldModDef
                {
                    Id = "mod.combat_stims", DisplayName = "Combat Stims", Description = "Kills give +10% fire rate for 2 s. At 3 stacks becomes Berserker Protocol.",
                    Rarity = ModRarity.Rare, Stack = StackRule.Evolves, EvolvesInto = "mod.berserker_protocol", EvolveAt = 3,
                    Effects = new[] { E(ModStat.KillFireRateBurst, 0.1f) },
                },
                new FieldModDef
                {
                    Id = "mod.berserker_protocol", DisplayName = "Berserker Protocol", Description = "Kills give +35% fire rate for 2 s; +8% move speed and +15% melee damage.",
                    Rarity = ModRarity.Legendary, Stack = StackRule.Unique, IsEvolution = true,
                    Effects = new[] { E(ModStat.KillFireRateBurst, 0.35f), E(ModStat.MoveSpeed, 0.08f), E(ModStat.MeleeDamage, 0.15f) },
                },
                // Legendary
                M("deadeye", "Deadeye", "+25% weapon damage and +20% more while below 30% health.", ModRarity.Legendary, StackRule.Unique, 1, E(ModStat.Damage, 0.25f), E(ModStat.LowHealthDamage, 0.2f)),
                M("phoenix_rig", "Phoenix Rig", "Ability cooldowns 30% faster and heal 40 when a surge is cleared.", ModRarity.Legendary, StackRule.Unique, 1, E(ModStat.CooldownRate, 0.3f), E(ModStat.HealOnSurgeClear, 40f)),
                M("last_stand", "Last Stand", "+40% damage while below 30% health.", ModRarity.Legendary, StackRule.Unique, 1, E(ModStat.LowHealthDamage, 0.4f)),
                M("juggernaut", "Juggernaut", "+50 max health and 10% damage reduction.", ModRarity.Legendary, StackRule.Unique, 1, E(ModStat.MaxHealth, 50f), E(ModStat.DamageReduction, 0.1f)),
            };
            // Class pools.
            list.Add(Class(M("belt_feed", "Belt Feed", "+15% fire rate with preferred weapons.", ModRarity.Rare, StackRule.Stacks, 2, E(ModStat.PreferredFireRate, 0.15f)), "class.gunner"));
            list.Add(Class(M("bulwark_plating", "Bulwark Plating", "8% damage reduction and +20 max health.", ModRarity.Rare, StackRule.Stacks, 2, E(ModStat.DamageReduction, 0.08f), E(ModStat.MaxHealth, 20f)), "class.tank"));
            list.Add(Class(M("executioner", "Executioner", "+30% melee damage.", ModRarity.Rare, StackRule.Stacks, 2, E(ModStat.MeleeDamage, 0.3f)), "class.warrior"));
            list.Add(Class(M("rapid_assembly", "Rapid Assembly", "Ability cooldown 25% faster.", ModRarity.Rare, StackRule.Unique, 1, E(ModStat.CooldownRate, 0.25f)), "class.engineer"));
            list.Add(Class(M("short_fuse", "Short Fuse", "Ability cooldown 20% faster and +10% damage.", ModRarity.Rare, StackRule.Stacks, 2, E(ModStat.CooldownRate, 0.2f), E(ModStat.Damage, 0.1f)), "class.demolitionist"));
            return list;
        }

        private static FieldModDef Class(FieldModDef d, string classId)
        {
            d.ClassId = classId;
            return d;
        }
    }

    /// <summary>Weapon level from XP (foundation; the FOB uses levels to unlock attachments, M10).</summary>
    public static class WeaponXp
    {
        public const int MaxLevel = 20;

        public static int XpForKill(float threatCost) => (int)Math.Max(5f, Math.Round(10f * threatCost));

        /// <summary>Total XP needed to reach <paramref name="level"/> (level 1 = 0).</summary>
        public static int XpForLevel(int level)
        {
            level = Math.Max(1, Math.Min(MaxLevel, level));
            return 150 * (level - 1) * level / 2;
        }

        public static int LevelForXp(long xp)
        {
            int level = 1;
            while (level < MaxLevel && xp >= XpForLevel(level + 1)) level++;
            return level;
        }

        /// <summary>Level at which an attachment (by catalog index) unlocks for this weapon: one every level from 2.</summary>
        public static int AttachmentUnlockLevel(int attachmentIndex) => 2 + Math.Max(0, attachmentIndex);
    }
}
