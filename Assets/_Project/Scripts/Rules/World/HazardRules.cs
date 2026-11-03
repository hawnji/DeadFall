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
    }
}
