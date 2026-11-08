using System;
using System.Collections.Generic;

namespace Deadfall.Classes
{
    /// <summary>Things abilities put into the world. The byte value goes over the network: append only.</summary>
    public enum SpawnKind : byte
    {
        Shield = 0,
        Sentry = 1,
        ClusterCharge = 2,
    }

    /// <summary>A world object created by an ability, tracked by <see cref="ClassNetwork"/>.</summary>
    public interface IAbilitySpawn
    {
        ushort NetId { get; set; }
        ulong OwnerClientId { get; }
    }

    /// <summary>An ability spawn with health (shield, sentry): the host syncs it so clients' mirrors show the damage.</summary>
    public interface IAbilityHealth
    {
        float Health01 { get; }
        /// <summary>Clients: health from the host (mirrors only).</summary>
        void SetMirrorHealth01(float value);
    }

    /// <summary>
    /// Which class every player in the session plays, keyed by client id (0 offline and for the host). Kept in step
    /// by <see cref="ClassNetwork"/>; used for doctrines, remote player labels and host-side class effects.
    /// </summary>
    public static class ClassRoster
    {
        private static readonly Dictionary<ulong, string> Classes = new Dictionary<ulong, string>();

        public static IReadOnlyDictionary<ulong, string> All => Classes;
        public static event Action Changed;

        public static string ClassOf(ulong clientId) => Classes.TryGetValue(clientId, out string c) ? c : null;

        public static void Set(ulong clientId, string classId)
        {
            if (Classes.TryGetValue(clientId, out string old) && old == classId) return;
            if (string.IsNullOrEmpty(classId)) Classes.Remove(clientId);
            else Classes[clientId] = classId;
            Changed?.Invoke();
        }

        public static void Remove(ulong clientId)
        {
            if (Classes.Remove(clientId)) Changed?.Invoke();
        }

        public static void Clear()
        {
            if (Classes.Count == 0) return;
            Classes.Clear();
            Changed?.Invoke();
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Classes.Clear();
            Changed = null;
        }
    }
}
