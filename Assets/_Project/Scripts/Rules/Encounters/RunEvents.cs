using System;
using Deadfall.Rules.Common;

namespace Deadfall.Rules.Encounters
{
    /// <summary>Random events (GAME_DESIGN §12). Red Sky and Containment Breach come later.</summary>
    public enum RunEventKind : byte
    {
        None,
        /// <summary>The lights fail for a surge; restarting the generators brings them back early.</summary>
        Blackout,
        /// <summary>The Stalker joins the surge and hunts the squad until killed; drops a rare weapon.</summary>
        Stalker,
        /// <summary>Sector doors seal; a terminal has to be hacked to lift it.</summary>
        Lockdown,
        /// <summary>M12: the infected are faster and hit harder for the surge.</summary>
        Frenzy,
        /// <summary>M12: two rooms fill with toxic gas for the surge (hurts players, not the infected).</summary>
        ToxicLeak,
    }

    /// <summary>
    /// Decides when a random event happens: at most one at a time, never before <see cref="FirstSurge"/>, never on
    /// Specimen or Hunt surges, and a cooldown in surges between events. Rolled in the lull before a surge so the HUD
    /// can warn the squad.
    /// </summary>
    public sealed class EventScheduler
    {
        public const int FirstSurge = 4;
        public const int CooldownSurges = 3;
        public const float Chance = 0.3f;
        public const RunEventKind Last = RunEventKind.ToxicLeak;
        /// <summary>Frenzy: enemy move speed and damage multipliers.</summary>
        public const float FrenzySpeed = 1.25f;
        public const float FrenzyDamage = 1.15f;

        private int _lastEventSurge = int.MinValue / 2;

        public void Reset() => _lastEventSurge = int.MinValue / 2;

        public static float Weight(RunEventKind k, bool hasMap, int openSectors) => k switch
        {
            RunEventKind.Blackout => 3f,
            RunEventKind.Stalker => 2f,
            RunEventKind.Lockdown => hasMap && openSectors >= 2 ? 2f : 0f,
            RunEventKind.Frenzy => 2f,
            RunEventKind.ToxicLeak => hasMap ? 2f : 0f,
            _ => 0f,
        };

        public RunEventKind Roll(DeterministicRandom rng, int surge, SurgeType type, bool hasMap, int openSectors)
        {
            if (rng == null || surge < FirstSurge) return RunEventKind.None;
            if (type == SurgeType.Specimen || type == SurgeType.Hunt) return RunEventKind.None;
            if (surge - _lastEventSurge <= CooldownSurges) return RunEventKind.None;
            if (rng.NextFloat() >= Chance) return RunEventKind.None;
            float total = 0f;
            for (var k = RunEventKind.Blackout; k <= Last; k++) total += Weight(k, hasMap, openSectors);
            if (total <= 0f) return RunEventKind.None;
            float roll = rng.NextFloat() * total;
            for (var k = RunEventKind.Blackout; k <= Last; k++)
            {
                roll -= Weight(k, hasMap, openSectors);
                if (roll <= 0f && Weight(k, hasMap, openSectors) > 0f)
                {
                    _lastEventSurge = surge;
                    return k;
                }
            }
            return RunEventKind.None;
        }

        public static string Name(RunEventKind k) => k switch
        {
            RunEventKind.Blackout => "BLACKOUT",
            RunEventKind.Stalker => "THE STALKER",
            RunEventKind.Lockdown => "EMERGENCY LOCKDOWN",
            RunEventKind.Frenzy => "FRENZY",
            RunEventKind.ToxicLeak => "TOXIC LEAK",
            _ => string.Empty,
        };

        public static string Hint(RunEventKind k) => k switch
        {
            RunEventKind.Blackout => "The lights are failing. Flashlight on (L); restart the generators to restore power",
            RunEventKind.Stalker => "Something is hunting you. Stay together; it drops a rare weapon",
            RunEventKind.Lockdown => "Sector doors are sealing. Hack the terminal to lift the lockdown",
            RunEventKind.Frenzy => "They're faster and hit harder this surge. Hold a choke point",
            RunEventKind.ToxicLeak => "Gas is venting into two rooms. Stay out of the green haze",
            _ => string.Empty,
        };
    }
}
