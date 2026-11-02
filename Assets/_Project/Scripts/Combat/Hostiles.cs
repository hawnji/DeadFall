using System.Collections.Generic;
using UnityEngine;

namespace Deadfall.Combat
{
    /// <summary>Something player-side automation (sentries, later drones) should shoot at.</summary>
    public interface IHostile
    {
        bool IsAlive { get; }
        /// <summary>Where to aim (chest height).</summary>
        Vector3 AimPoint { get; }
        IDamageable Damageable { get; }
    }

    /// <summary>Registry of living hostiles so turrets never search the scene. Enemies register while active.</summary>
    public static class Hostiles
    {
        private static readonly List<IHostile> List = new List<IHostile>(64);

        public static IReadOnlyList<IHostile> All => List;

        public static void Register(IHostile h)
        {
            if (h != null && !List.Contains(h)) List.Add(h);
        }

        public static void Unregister(IHostile h) => List.Remove(h);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => List.Clear();
    }
}
