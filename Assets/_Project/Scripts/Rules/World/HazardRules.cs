using System.Collections.Generic;
using Deadfall.Rules.Common;

namespace Deadfall.Rules.World
{
    /// <summary>
    /// Toxic Leak event (M12): which rooms fill with gas. Every peer computes the same rooms from the map seed, the
    /// surge and the open sectors, so nothing extra is sent. Never the spawn room, corridors, the boss room or secrets.
    /// </summary>
    public static class HazardRules
    {
        public const int ToxicRooms = 2;
        public const float ToxicDamagePerSecond = 6f;
        /// <summary>Containment Breach floor: a cycle of calm, a warning flicker, then an arc that hurts whoever stands on it.</summary>
        public const float BreachCycleSeconds = 3.2f;
        public const float BreachWarnSeconds = 0.8f;
        public const float BreachArcSeconds = 0.7f;
        public const float BreachPlayerDamage = 16f;
        public const float BreachEnemyDamage = 45f;

        public static List<int> PickToxicRooms(Layout layout, int openSectors, ulong mapSeed, int surge, int count = ToxicRooms)
        {
            var pool = new List<int>();
            if (layout == null) return pool;
            foreach (PlacedRoom r in layout.Rooms)
            {
                if (r.Index == 0 || r.Sector >= openSectors) continue;
                RoomRole role = r.Template.Role;
                if (role == RoomRole.Room || role == RoomRole.Objective) pool.Add(r.Index);
            }
            var rng = new DeterministicRandom(mapSeed ^ ((ulong)surge * 0xD1B54A32D192ED03UL) ^ 0x70C1CUL);
            var picked = new List<int>(count);
            while (picked.Count < count && pool.Count > 0)
            {
                int k = rng.Range(0, pool.Count);
                picked.Add(pool[k]);
                pool.RemoveAt(k);
            }
            picked.Sort();
            return picked;
        }

        /// <summary>Containment Breach: the room whose floor arcs (same pool as Toxic Leak, its own roll). -1 if none.</summary>
        public static int PickBreachRoom(Layout layout, int openSectors, ulong mapSeed, int surge)
        {
            List<int> rooms = PickToxicRooms(layout, openSectors, mapSeed ^ 0xB4EAC4UL, surge, 1);
            return rooms.Count > 0 ? rooms[0] : -1;
        }

        public enum BreachPhase { Calm, Warning, Arc }

        /// <summary>Where the breach floor is in its cycle, from seconds since the event began (every peer runs the same clock).</summary>
        public static BreachPhase BreachPhaseAt(float secondsSinceStart)
        {
            if (secondsSinceStart < 0f) return BreachPhase.Calm;
            float t = secondsSinceStart % BreachCycleSeconds;
            float arcStart = BreachCycleSeconds - BreachArcSeconds;
            if (t >= arcStart) return BreachPhase.Arc;
            if (t >= arcStart - BreachWarnSeconds) return BreachPhase.Warning;
            return BreachPhase.Calm;
        }

        /// <summary>Which arc (counting from 0) a moment falls in, so each arc hurts a target once.</summary>
        public static int BreachArcIndex(float secondsSinceStart) =>
            secondsSinceStart < 0f ? -1 : (int)(secondsSinceStart / BreachCycleSeconds);
    }
}
