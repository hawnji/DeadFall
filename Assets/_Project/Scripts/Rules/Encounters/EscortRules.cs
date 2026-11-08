using System;
using System.Collections.Generic;

namespace Deadfall.Rules.Encounters
{
    /// <summary>
    /// Escort: push a slow reactor cart from the spawn room to the far end of the map while the horde attacks it.
    /// The cart has a health pool like a nest (scaled by squad size) that infected hits wear down; it heals a little at
    /// every sector door it forces open. It only rolls while someone is within <see cref="PushRadius"/> and the
    /// infected around it don't outnumber the pushers. Delivered: the run is won. Destroyed: the run is lost.
    /// Surges keep coming until then.
    /// </summary>
    public static class EscortRules
    {
        public const float BaseHealth = 4000f;
        /// <summary>Extra health per player after the first (+35% each), like other objective pools.</summary>
        public const float HealthPerExtraPlayer = 0.35f;
        public const float PushRadius = 6f;
        /// <summary>Infected this close to the cart contest it.</summary>
        public const float ContestRadius = 3f;
        public const float BaseSpeed = 1.0f;
        public const float SpeedPerExtraPusher = 0.2f;
        public const float MaxSpeed = 1.6f;
        /// <summary>Health restored (fraction of max) each time the cart forces a sector door.</summary>
        public const float CheckpointHeal = 0.2f;
        /// <summary>How long the cart waits at a door it forces open (the door slides, the NavMesh updates).</summary>
        public const float DoorSeconds = 1.2f;
        /// <summary>The cart pulls nearby infected onto itself from this far.</summary>
        public const float AggroRadius = 6f;
        /// <summary>Lull between surges (the cart is the clock, not the lull).</summary>
        public const float LullSeconds = 12f;

        public enum Status : byte { Moving, Waiting, Contested, Blocked, Door, Delivered, Destroyed }

        public static float MaxHealth(int players) => BaseHealth * (1f + HealthPerExtraPlayer * Math.Max(0, players - 1));

        /// <summary>Metres per second with <paramref name="pushers"/> players in range (0 when nobody is pushing).</summary>
        public static float Speed(int pushers) =>
            pushers <= 0 ? 0f : Math.Min(MaxSpeed, BaseSpeed + SpeedPerExtraPusher * (pushers - 1));

        /// <summary>Why the cart is or isn't rolling, before doors and blocking players.</summary>
        public static Status Evaluate(int pushers, int nearbyInfected)
        {
            if (pushers <= 0) return Status.Waiting;
            if (nearbyInfected > pushers) return Status.Contested;
            return Status.Moving;
        }

        public static string Describe(Status s)
        {
            switch (s)
            {
                case Status.Moving: return "Rolling";
                case Status.Waiting: return "Stopped: stay near the cart to push it";
                case Status.Contested: return "Contested: clear the infected off the cart";
                case Status.Blocked: return "Blocked: get out of its way";
                case Status.Door: return "Forcing the door";
                case Status.Delivered: return "Delivered";
                case Status.Destroyed: return "Destroyed";
                default: return string.Empty;
            }
        }

        /// <summary>
        /// Rooms from the spawn room (index 0) to <paramref name="destination"/>, following the tree's parent links.
        /// Empty if the chain is broken or loops.
        /// </summary>
        public static List<int> Route(IReadOnlyList<int> parents, int destination)
        {
            var route = new List<int>();
            int r = destination;
            for (int guard = 0; guard <= parents.Count && r >= 0; guard++)
            {
                route.Add(r);
                if (r == 0)
                {
                    route.Reverse();
                    return route;
                }
                r = r < parents.Count ? parents[r] : -1;
            }
            route.Clear();
            return route;
        }
    }
}
