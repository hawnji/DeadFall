using System;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// World switching for game code (M10). The bootstrap that owns the worlds (sandbox, FOB, generated maps) fills
    /// these in, so the FOB can deploy and the results screen can return without referencing the bootstrap.
    /// </summary>
    public static class WorldControl
    {
        /// <summary>Host/offline: replace the current world with the generated map of this seed.</summary>
        public static Func<ulong, bool> EnterMap;
        /// <summary>Back to FOB Halcyon (ends any run, removes the map).</summary>
        public static Action ReturnToFob;
        /// <summary>Teleport to the firing range next to the FOB.</summary>
        public static Action EnterTrainingRange;
        /// <summary>True while no generated map is loaded (FOB and range are one world).</summary>
        public static Func<bool> AtBase;

        public static bool IsAtBase => AtBase == null || AtBase();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            EnterMap = null;
            ReturnToFob = null;
            EnterTrainingRange = null;
            AtBase = null;
        }
    }
}
