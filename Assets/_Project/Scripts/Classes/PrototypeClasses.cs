using System.Collections.Generic;
using Deadfall.Rules.Classes;
using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Classes
{
    /// <summary>
    /// The five launch classes (C-03) with their prototype kits from GAME_DESIGN §5, created in memory so they work
    /// with no asset setup. They become ClassDefinition assets in Data/Classes once tuning settles. Every passive and
    /// doctrine is live; a behaviour id with no code would show as "soon" on the HUD.
    /// </summary>
    public static class PrototypeClasses
    {
        private static ClassDefinition[] _all;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _all = null;

        /// <summary>Fixed order (class select and, later, the network index). Append only.</summary>
        public static IReadOnlyList<ClassDefinition> All
        {
            get
            {
                if (_all == null || _all[0] == null) _all = Build();
                return _all;
            }
        }

        public static ClassDefinition ById(string contentId)
        {
            foreach (ClassDefinition c in All)
                if (c.ContentId == contentId) return c;
            return null;
        }

        public const string CoveringFire = "doctrine.covering_fire";
        public const string DemolitionCrew = "doctrine.demolition_crew";
        public const string Vanguard = "doctrine.vanguard";
        public const string Overwatch = "doctrine.overwatch";
        public const string CombinedArms = "doctrine.combined_arms";

        /// <summary>Display name of a doctrine id.</summary>
        public static string DoctrineName(string id)
        {
            switch (id)
            {
                case CoveringFire: return "Covering Fire";
                case DemolitionCrew: return "Demolition Crew";
                case Vanguard: return "Vanguard";
                case Overwatch: return "Overwatch";
                case CombinedArms: return "Combined Arms";
                default: return id;
            }
        }

        /// <summary>Squad Doctrines from GAME_DESIGN §5.1. Damage effects live in <see cref="DoctrineEffects"/>.</summary>
        public static readonly DoctrineRule[] Doctrines =
        {
            DoctrineRule.Pair(CoveringFire, "class.gunner", "class.tank"),
            DoctrineRule.Pair(DemolitionCrew, "class.engineer", "class.demolitionist"),
            DoctrineRule.Pair(Vanguard, "class.warrior", "class.tank"),
            DoctrineRule.Pair(Overwatch, "class.gunner", "class.engineer"),
            DoctrineRule.Distinct(CombinedArms, 4),
        };

        private static ClassDefinition[] Build()
        {
            var gunner = ClassDefinition.CreateRuntime("class.gunner", "Gunner", "Sustained fire, suppression", new Color(0.95f, 0.7f, 0.25f),
                new[] { WeaponCategory.AssaultRifle, WeaponCategory.SubmachineGun, WeaponCategory.LightMachineGun },
                AbilityDefinition.CreateRuntime("ability.overdrive", "Overdrive", "overdrive",
                    "8 s of +40% fire rate on preferred weapons, and their reloads use no reserve ammo.", 45f, 8f, 0.4f),
                PassiveDefinition.CreateRuntime("passive.deep_pockets", "Deep Pockets", "reserve_capacity", "+25% reserve ammo.", 0.25f),
                PassiveDefinition.CreateRuntime("passive.steady_hands", "Steady Hands", "steady_hands",
                    "Recoil eases the longer you keep firing (down to 60%).", 0.1f, 0.6f));

            var tank = ClassDefinition.CreateRuntime("class.tank", "Tank", "Durability, aggro, protection", new Color(0.35f, 0.6f, 0.95f),
                new[] { WeaponCategory.Shotgun, WeaponCategory.Heavy },
                AbilityDefinition.CreateRuntime("ability.bulwark_shield", "Bulwark Shield", "bulwark_shield",
                    "Plant a 600 HP shield for 8 s. Enemies within 12 m attack it instead of the squad.", 40f, 8f, 600f, 3f, 12f),
                PassiveDefinition.CreateRuntime("passive.guard", "Guard", "guard", "30% damage reduction while above 50% health.", 0.3f, 0.5f),
                PassiveDefinition.CreateRuntime("passive.shove", "Shove", "melee_knockback", "Melee knocks enemies back about 2.5 m.", 2.5f));

            var warrior = ClassDefinition.CreateRuntime("class.warrior", "Warrior", "Melee, mobility, executions", new Color(0.85f, 0.3f, 0.3f),
                new[] { WeaponCategory.Melee },
                AbilityDefinition.CreateRuntime("ability.lunge", "Lunge", "lunge",
                    "Dash 8 m and strike: triple damage, staggers what it hits.", 12f, 0f, 3f, 0f, 8f),
                PassiveDefinition.CreateRuntime("passive.bloodlust", "Bloodlust", "bloodlust", "Melee kills restore 1 health.", 1f),
                PassiveDefinition.CreateRuntime("passive.combo", "Combo", "combo_cleave",
                    "Every third melee hit in a quick chain cleaves through more enemies.", 1.2f, 2.2f, 3f));

            var engineer = ClassDefinition.CreateRuntime("class.engineer", "Engineer", "Construction, automation", new Color(0.4f, 0.8f, 0.5f),
                new[] { WeaponCategory.SubmachineGun, WeaponCategory.Pistol },
                AbilityDefinition.CreateRuntime("ability.deploy_sentry", "Deploy Sentry", "deploy_sentry",
                    "Place a light turret for 60 s that shoots the nearest enemy in range (scrap upgrades come with Engineering).", 35f, 60f, 9f, 6f, 22f),
                PassiveDefinition.CreateRuntime("passive.salvager", "Salvager", "scrap_gain", "+50% Scrap from your kills.", 0.5f),
                PassiveDefinition.CreateRuntime("passive.handy", "Handy", "repair_speed", "Structure repairs restore more for less Scrap; barricade boards go up 50% faster.", 0.5f));

            var demolitionist = ClassDefinition.CreateRuntime("class.demolitionist", "Demolitionist", "Explosives, area control", new Color(0.75f, 0.45f, 0.9f),
                new[] { WeaponCategory.Heavy, WeaponCategory.Shotgun },
                AbilityDefinition.CreateRuntime("ability.cluster_charge", "Cluster Charge", "cluster_charge",
                    "Throw a charge that bursts on impact and scatters 5 bomblets.", 25f, 0f, 5f, 90f, 4.5f),
                PassiveDefinition.CreateRuntime("passive.bandolier", "Bandolier", "grenade_capacity", "Explosive weapons hold +2 grenades per magazine and +6 in reserve.", 2f),
                PassiveDefinition.CreateRuntime("passive.blast_padding", "Blast Padding", "self_blast_reduction",
                    "50% less damage from your own explosives.", 0.5f));

            return new[] { gunner, tank, warrior, engineer, demolitionist };
        }
    }
}
