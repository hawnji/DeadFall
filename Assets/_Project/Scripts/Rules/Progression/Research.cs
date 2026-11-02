using System;
using System.Collections.Generic;

namespace Deadfall.Rules.Progression
{
    /// <summary>Which FOB facility shows a research project.</summary>
    public enum ResearchCategory : byte { Survival, Combat, Arsenal, Intel, Engineering }

    /// <summary>
    /// A permanent technology bought with Requisition and Intel (GAME_DESIGN §9.2). Effects are personal: stat
    /// bonuses (optionally for one class), weapon unlocks for loadouts, and dossier tiers (T-53).
    /// </summary>
    public sealed class ResearchDef
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public ResearchCategory Category;
        public Cost Cost;
        public string[] Prerequisites = Array.Empty<string>();
        public int RequiredOperatorLevel = 1;
        public ModEffect[] Effects = Array.Empty<ModEffect>();
        /// <summary>Effects apply only while playing this class (null: always).</summary>
        public string EffectClassId;
        /// <summary>Weapon content id this unlocks for every class's loadout, or null.</summary>
        public string UnlocksWeapon;
        /// <summary>Dossier tier this reveals (0: none). See <see cref="DossierRules"/>.</summary>
        public int DossierTier;
    }

    public enum ResearchError : byte { Ok, UnknownProject, AlreadyDone, LevelTooLow, MissingPrerequisite, CannotAfford }

    public static class ResearchRules
    {
        public static ResearchDef Find(IReadOnlyList<ResearchDef> catalog, string id)
        {
            foreach (ResearchDef r in catalog) if (r.Id == id) return r;
            return null;
        }

        public static ResearchError CanResearch(ResearchDef def, ICollection<string> done, int operatorLevel, Wallet wallet)
        {
            if (def == null) return ResearchError.UnknownProject;
            if (done.Contains(def.Id)) return ResearchError.AlreadyDone;
            if (operatorLevel < def.RequiredOperatorLevel) return ResearchError.LevelTooLow;
            foreach (string p in def.Prerequisites) if (!done.Contains(p)) return ResearchError.MissingPrerequisite;
            if (!wallet.CanAfford(def.Cost)) return ResearchError.CannotAfford;
            return ResearchError.Ok;
        }

        /// <summary>Pays and records the project. Returns the error when it can't.</summary>
        public static ResearchError Research(ResearchDef def, ICollection<string> done, int operatorLevel, Wallet wallet)
        {
            ResearchError e = CanResearch(def, done, operatorLevel, wallet);
            if (e != ResearchError.Ok) return e;
            wallet.TrySpend(def.Cost);
            done.Add(def.Id);
            return ResearchError.Ok;
        }

        public static void AddEffects(IReadOnlyList<ResearchDef> catalog, ICollection<string> done, string classId, float[] totals)
        {
            foreach (ResearchDef r in catalog)
            {
                if (!done.Contains(r.Id)) continue;
                if (r.EffectClassId != null && r.EffectClassId != classId) continue;
                foreach (ModEffect e in r.Effects) totals[(int)e.Stat] += e.Value;
            }
        }

        public static bool WeaponUnlocked(IReadOnlyList<ResearchDef> catalog, ICollection<string> done, string weaponId)
        {
            foreach (ResearchDef r in catalog)
                if (r.UnlocksWeapon == weaponId && done.Contains(r.Id)) return true;
            return false;
        }

        public static int DossierTier(IReadOnlyList<ResearchDef> catalog, ICollection<string> done)
        {
            int tier = 0;
            foreach (ResearchDef r in catalog)
                if (r.DossierTier > tier && done.Contains(r.Id)) tier = r.DossierTier;
            return tier;
        }
    }

    public static class ResearchCatalog
    {
        public static List<ResearchDef> Prototype() => new List<ResearchDef>
        {
            // Survival
            R("res.field_medicine", "Field Medicine", "+10 max health", ResearchCategory.Survival, 600, 1, effects: E(ModStat.MaxHealth, 10f)),
            R("res.trauma_plating", "Trauma Plating", "+4% damage reduction", ResearchCategory.Survival, 1600, 4, new[] { "res.field_medicine" }, effects: E(ModStat.DamageReduction, 0.04f)),
            R("res.adrenal_boosters", "Adrenal Boosters", "+4% move speed", ResearchCategory.Survival, 1400, 3, new[] { "res.field_medicine" }, effects: E(ModStat.MoveSpeed, 0.04f)),
            R("res.nanite_recovery", "Nanite Recovery", "Heal 8 after each surge", ResearchCategory.Survival, 2400, 6, new[] { "res.trauma_plating" }, level: 5, effects: E(ModStat.HealOnSurgeClear, 8f)),
            // Combat
            R("res.ballistics_1", "Ballistics I", "+4% weapon damage", ResearchCategory.Combat, 800, 2, effects: E(ModStat.Damage, 0.04f)),
            R("res.ballistics_2", "Ballistics II", "+5% weapon damage", ResearchCategory.Combat, 2200, 6, new[] { "res.ballistics_1" }, level: 4, effects: E(ModStat.Damage, 0.05f)),
            R("res.quick_hands", "Quick Hands", "+8% reload speed", ResearchCategory.Combat, 900, 2, effects: E(ModStat.ReloadSpeed, 0.08f)),
            R("res.capacitors", "Ability Capacitors", "+8% ability cooldown rate", ResearchCategory.Combat, 1500, 4, level: 3, effects: E(ModStat.CooldownRate, 0.08f)),
            // Arsenal (weapon unlocks for every class's loadout; reserve ammo)
            R("res.load_bearing", "Load-Bearing Rigs", "+15% reserve ammo", ResearchCategory.Arsenal, 700, 1, effects: E(ModStat.Reserve, 0.15f)),
            R("res.wpn_anvil", "Anvil LMG Clearance", "Unlocks the Anvil LMG for every class", ResearchCategory.Arsenal, 1000, 2, weapon: "weapon.anvil_lmg"),
            R("res.wpn_cleaver", "Tether Cleaver Clearance", "Unlocks the Tether Cleaver for every class", ResearchCategory.Arsenal, 800, 2, weapon: "weapon.tether_cleaver"),
            R("res.wpn_mule", "Mule Launcher Clearance", "Unlocks the Mule Launcher for every class", ResearchCategory.Arsenal, 1800, 5, new[] { "res.wpn_anvil" }, level: 4, weapon: "weapon.mule_launcher"),
            // Intel
            R("res.dossier_common", "Field Dossiers", "Reveals dossiers for common infected", ResearchCategory.Intel, 500, 2, dossier: 1),
            R("res.dossier_elite", "Elite Dossiers", "Reveals dossiers for elites and hunters", ResearchCategory.Intel, 1200, 6, new[] { "res.dossier_common" }, level: 3, dossier: 2),
            R("res.dossier_specimen", "Specimen Files", "Reveals Specimen dossiers", ResearchCategory.Intel, 2000, 12, new[] { "res.dossier_elite" }, level: 5, dossier: 3),
            // Engineering (class-specific bonuses for the builders)
            R("res.eng_toolkit", "Engineer Toolkit", "Engineer: +12% ability cooldown rate", ResearchCategory.Engineering, 1000, 3, level: 2, effects: E(ModStat.CooldownRate, 0.12f), cls: "class.engineer"),
            R("res.eng_hardhat", "Site Safety", "Engineer: +15 max health", ResearchCategory.Engineering, 1200, 3, new[] { "res.eng_toolkit" }, level: 2, effects: E(ModStat.MaxHealth, 15f), cls: "class.engineer"),
            R("res.demo_primer", "Demolition Primer", "Demolitionist: +6% weapon damage", ResearchCategory.Engineering, 1000, 3, level: 3, effects: E(ModStat.Damage, 0.06f), cls: "class.demolitionist"),
            R("res.demo_bandolier", "Heavy Bandolier", "Demolitionist: +25% reserve ammo", ResearchCategory.Engineering, 1200, 3, new[] { "res.demo_primer" }, level: 3, effects: E(ModStat.Reserve, 0.25f), cls: "class.demolitionist"),
        };

        private static ModEffect[] E(ModStat s, float v) => new[] { new ModEffect(s, v) };

        private static ResearchDef R(string id, string name, string desc, ResearchCategory cat, int req, int intel,
            string[] prereq = null, int level = 1, ModEffect[] effects = null, string weapon = null, int dossier = 0, string cls = null)
            => new ResearchDef
            {
                Id = id, DisplayName = name, Description = desc, Category = cat, Cost = new Cost(req, intel),
                Prerequisites = prereq ?? Array.Empty<string>(), RequiredOperatorLevel = level,
                Effects = effects ?? Array.Empty<ModEffect>(), UnlocksWeapon = weapon, DossierTier = dossier, EffectClassId = cls,
            };
    }

    /// <summary>
    /// Enemy dossiers in the Intelligence Center: tier 1 regular infected, tier 2 elites/hunters, tier 3 Specimens.
    /// Research reveals a tier; killing enough of one type reveals its dossier anyway.
    /// </summary>
    public static class DossierRules
    {
        public const int KillsToReveal = 25;

        public static bool Revealed(int enemyTier, int researchedTier, long kills) => researchedTier >= enemyTier || kills >= KillsToReveal;
    }

    /// <summary>D-07 slots.</summary>
    public enum LoadoutSlot : byte { Primary, Sidearm, Melee }

    /// <summary>A weapon a loadout can hold: id, which slot type it fits, and whether research gates it.</summary>
    public readonly struct LoadoutOption
    {
        public readonly string WeaponId;
        public readonly LoadoutSlot Slot;
        public readonly bool NeedsResearch;
        public LoadoutOption(string weaponId, LoadoutSlot slot, bool needsResearch) { WeaponId = weaponId; Slot = slot; NeedsResearch = needsResearch; }
    }

    /// <summary>
    /// Loadout validation: four entries (primary, primary, sidearm, melee). A weapon is allowed when it needs no
    /// research, is in the class's default loadout, or has been researched.
    /// </summary>
    public static class LoadoutRules
    {
        public const int Size = 4;

        public static LoadoutSlot SlotAt(int index) => index switch { 0 => LoadoutSlot.Primary, 1 => LoadoutSlot.Primary, 2 => LoadoutSlot.Sidearm, _ => LoadoutSlot.Melee };

        public static bool Allowed(LoadoutOption option, string[] classDefault, Func<string, bool> researched)
        {
            if (!option.NeedsResearch) return true;
            if (classDefault != null && Array.IndexOf(classDefault, option.WeaponId) >= 0) return true;
            return researched != null && researched(option.WeaponId);
        }

        /// <summary>True when <paramref name="loadout"/> is a complete, legal loadout.</summary>
        public static bool IsValid(string[] loadout, IReadOnlyList<LoadoutOption> options, string[] classDefault, Func<string, bool> researched)
        {
            if (loadout == null || loadout.Length != Size) return false;
            if (loadout[0] == loadout[1]) return false;
            for (int i = 0; i < Size; i++)
            {
                LoadoutOption? o = Find(options, loadout[i]);
                if (o == null || o.Value.Slot != SlotAt(i) || !Allowed(o.Value, classDefault, researched)) return false;
            }
            return true;
        }

        /// <summary>The saved loadout if legal, otherwise the class default.</summary>
        public static string[] Resolve(string[] saved, IReadOnlyList<LoadoutOption> options, string[] classDefault, Func<string, bool> researched)
            => IsValid(saved, options, classDefault, researched) ? saved : classDefault;

        public static LoadoutOption? Find(IReadOnlyList<LoadoutOption> options, string id)
        {
            foreach (LoadoutOption o in options) if (o.WeaponId == id) return o;
            return null;
        }
    }
}
