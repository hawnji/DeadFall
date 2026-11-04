using System;

namespace Deadfall.Rules.Combat
{
    /// <summary>
    /// Boss arena hazards (GAME_DESIGN §7 Specimen framework): once a Specimen or the Broodmother is enraged, the floor
    /// under the squad ruptures. Each rupture is telegraphed by a glowing ring, bursts, then leaves a burning pool for a
    /// few seconds. The Broodmother's ruptures also hatch a Creeper. Pure numbers so they can be tested and tuned.
    /// </summary>
    public static class ArenaHazardRules
    {
        /// <summary>Only players this close to the boss are targeted (the arena).</summary>
        public const float Range = 26f;
        public const float TelegraphSeconds = 1.5f;
        public const float BurstRadius = 2.4f;
        public const float BurstDamage = 25f;
        public const float PoolRadius = 2f;
        public const float PoolSeconds = 4f;
        public const float PoolDamagePerSecond = 10f;
        /// <summary>Ruptures land within this distance of the targeted player, so standing still is what gets you hit.</summary>
        public const float Scatter = 1.2f;

        /// <summary>Seconds between volleys; none while the boss is still stalking.</summary>
        public static float Interval(SpecimenPhase phase) => phase switch
        {
            SpecimenPhase.Rampage => 7f,
            SpecimenPhase.ExposedCore => 5f,
            _ => 0f,
        };

        public static bool Active(SpecimenPhase phase) => Interval(phase) > 0f;

        /// <summary>Ruptures per volley: one per targeted player, up to a cap that grows in the last phase.</summary>
        public static int Count(SpecimenPhase phase, int playersInRange)
        {
            if (!Active(phase) || playersInRange <= 0) return 0;
            int cap = phase == SpecimenPhase.ExposedCore ? 4 : 2;
            return Math.Min(cap, playersInRange + (phase == SpecimenPhase.ExposedCore ? 1 : 0));
        }

        /// <summary>Burst damage at a distance from the rupture centre (full inside, none outside).</summary>
        public static float BurstDamageAt(float distance) => distance <= BurstRadius ? BurstDamage : 0f;
    }
}
