using System.Collections.Generic;
using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Combat
{
    /// <summary>
    /// A player as the squad sees them on this machine: the local player or a remote teammate's stand-in (M11).
    /// Used for revives, spectating and the squad-wipe check.
    /// </summary>
    public interface ISquadMember
    {
        ulong ClientId { get; }
        bool IsLocalMember { get; }
        LifeState Life { get; }
        /// <summary>1 when just downed, 0 when bled out (only meaningful while downed).</summary>
        float BleedOut01 { get; }
        /// <summary>0..1 health as last known.</summary>
        float Health01 { get; }
        /// <summary>The player dropped and may rejoin (D-16). Shown downed in place; never counts as recoverable.</summary>
        bool Disconnected { get; }
        Transform Body { get; }
        Vector3 Eye { get; }
    }

    public static class SquadMembers
    {
        private static readonly List<ISquadMember> Registry = new List<ISquadMember>(4);

        public static IReadOnlyList<ISquadMember> All => Registry;

        public static void Register(ISquadMember m)
        {
            if (m != null && !Registry.Contains(m)) Registry.Add(m);
        }

        public static void Unregister(ISquadMember m) => Registry.Remove(m);

        public static ISquadMember Find(ulong clientId)
        {
            for (int i = 0; i < Registry.Count; i++)
                if (Registry[i] != null && Registry[i].ClientId == clientId) return Registry[i];
            return null;
        }

        public static ISquadMember Local
        {
            get
            {
                for (int i = 0; i < Registry.Count; i++)
                    if (Registry[i] != null && Registry[i].IsLocalMember) return Registry[i];
                return null;
            }
        }

        /// <summary>Counts connected members by state.</summary>
        public static void Count(out int alive, out int downed, out int eliminated)
        {
            alive = downed = eliminated = 0;
            for (int i = 0; i < Registry.Count; i++)
            {
                ISquadMember m = Registry[i];
                if (m == null || m.Disconnected || (m is Object o && o == null)) continue;
                if (m.Life == LifeState.Alive) alive++;
                else if (m.Life == LifeState.Downed) downed++;
                else eliminated++;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Registry.Clear();
    }
}
