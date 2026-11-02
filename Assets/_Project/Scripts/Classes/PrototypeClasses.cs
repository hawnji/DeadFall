using System.Collections.Generic;
using Deadfall.Rules.Classes;
using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Classes
{
    /// <summary>
    /// The five launch classes (C-03) with their prototype kits from GAME_DESIGN §5, created in memory so they work
    /// with no asset setup. They become ClassDefinition assets in Data/Classes once tuning settles. Engineer and
    /// Demolitionist abilities, and the passives that need Scrap, grenades or structures, arrive later (their
    /// behaviour id has no code yet, so the HUD marks them as coming soon).
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

        /// <summary>Squad Doctrines from GAME_DESIGN §5.1 (effects come after M4 playtests, D-17).</summary>
        public static readonly DoctrineRule[] Doctrines =
        {
            DoctrineRule.Pair("doctrine.covering_fire", "class.gunner", "class.tank"),
            DoctrineRule.Pair("doctrine.demolition_crew", "class.engineer", "class.demolitionist"),
            DoctrineRule.Pair("doctrine.vanguard", "class.warrior", "class.tank"),
            DoctrineRule.Pair("doctrine.overwatch", "class.gunner", "class.engineer"),
            DoctrineRule.Distinct("doctrine.combined_arms", 4),
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
                PassiveDefinition.CreateRuntime("passive.shove", "Shove", "melee_knockback", "Melee knocks enemies back (coming soon).", 2.5f));

            var warrior = ClassDefinition.CreateRuntime("class.warrior", "Warrior", "Melee, mobility, executions", new Color(0.85f, 0.3f, 0.3f),
                new[] { WeaponCategory.Melee },
                AbilityDefinition.CreateRuntime("ability.lunge", "Lunge", "lunge",
                    "Dash 8 m and strike: triple damage, staggers what it hits.", 12f, 0f, 3f, 0f, 8f),
                PassiveDefinition.CreateRuntime("passive.bloodlust", "Bloodlust", "bloodlust", "Melee kills restore 5 health.", 5f),
                PassiveDefinition.CreateRuntime("passive.combo", "Combo", "combo_cleave",
                    "Every third melee hit in a quick chain cleaves through more enemies.", 1.2f, 2.2f, 3f));

            var engineer = ClassDefinition.CreateRuntime("class.engineer", "Engineer", "Construction, automation", new Color(0.4f, 0.8f, 0.5f),
                new[] { WeaponCategory.SubmachineGun, WeaponCategory.Pistol },
                AbilityDefinition.CreateRuntime("ability.deploy_sentry", "Deploy Sentry", "deploy_sentry",
                    "Place a light turret for 60 s (coming in M4 part 2).", 35f, 60f, 1f),
                PassiveDefinition.CreateRuntime("passive.salvager", "Salvager", "scrap_gain", "+50% Scrap (arrives with Scrap, M9).", 0.5f),
                PassiveDefinition.CreateRuntime("passive.handy", "Handy", "repair_speed", "Repairs structures faster (arrives with Engineering, M9).", 0.5f));

            var demolitionist = ClassDefinition.CreateRuntime("class.demolitionist", "Demolitionist", "Explosives, area control", new Color(0.75f, 0.45f, 0.9f),
                new[] { WeaponCategory.Heavy, WeaponCategory.Shotgun },
                AbilityDefinition.CreateRuntime("ability.cluster_charge", "Cluster Charge", "cluster_charge",
                    "Throw a charge that splits into 5 bomblets (coming in M4 part 2).", 25f, 0f, 5f),
                PassiveDefinition.CreateRuntime("passive.bandolier", "Bandolier", "grenade_capacity", "+2 grenades (arrives with grenades).", 2f),
                PassiveDefinition.CreateRuntime("passive.blast_padding", "Blast Padding", "self_blast_reduction",
                    "50% less damage from your own explosives (arrives with explosives).", 0.5f));

            return new[] { gunner, tank, warrior, engineer, demolitionist };
        }
    }
}
