using System;
using System.Collections.Generic;

namespace Deadfall.Rules.Classes
{
    /// <summary>The math behind the prototype class passives, kept here so it can be tested and tuned in one place.</summary>
    public static class ClassRules
    {
        /// <summary>
        /// Tank "Guard": extra damage reduction while health is above a threshold (GAME_DESIGN §5: 30% above 50% HP).
        /// </summary>
        public static float GuardReduction(float health01, float threshold, float reduction) =>
            health01 > threshold ? Clamp01(reduction) : 0f;

        /// <summary>
        /// Gunner "Steady Hands": recoil shrinks the longer the trigger stays down, down to a floor.
        /// Returns the recoil multiplier (1 = normal).
        /// </summary>
        public static float SustainedRecoil(float sustainSeconds, float reductionPerSecond, float minimum) =>
            Math.Max(Clamp01(minimum), 1f - Math.Max(0f, sustainSeconds) * Math.Max(0f, reductionPerSecond));

        /// <summary>
        /// Combines two independent damage reductions so they never reach 100%: 30% and 50% give 65%, not 80%.
        /// </summary>
        public static float CombineReduction(float a, float b) => 1f - (1f - Clamp01(a)) * (1f - Clamp01(b));

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }

    /// <summary>
    /// Warrior combo chain: melee hits within <see cref="WindowSeconds"/> of each other count up 1, 2, 3; the
    /// third is a cleave (hits everything in a wider arc) and the chain starts over.
    /// </summary>
    public sealed class MeleeCombo
    {
        private double _lastHit = double.NegativeInfinity;

        public MeleeCombo(float windowSeconds = 1.2f, int length = 3)
        {
            WindowSeconds = windowSeconds;
            Length = Math.Max(1, length);
        }

        public float WindowSeconds { get; }
        public int Length { get; }
        /// <summary>Hits landed in the current chain (0 when it has lapsed).</summary>
        public int Count { get; private set; }

        /// <summary>Counts a swing that landed. Returns its place in the chain (1..Length).</summary>
        public int Register(double now)
        {
            if (now - _lastHit > WindowSeconds || Count >= Length) Count = 0;
            Count++;
            _lastHit = now;
            return Count;
        }

        /// <summary>True when the next swing, if it lands in time, finishes the chain.</summary>
        public bool NextIsFinisher(double now) => now - _lastHit <= WindowSeconds && Count == Length - 1;

        public void Reset()
        {
            Count = 0;
            _lastHit = double.NegativeInfinity;
        }
    }

    /// <summary>A Squad Doctrine: a bonus for two different classes in the squad, or for enough distinct classes.</summary>
    public sealed class DoctrineRule
    {
        public string Id;
        public string ClassA;
        public string ClassB;
        /// <summary>When above 0, the doctrine ignores the pair and needs this many distinct classes in the squad.</summary>
        public int MinDistinctClasses;

        public static DoctrineRule Pair(string id, string a, string b) => new DoctrineRule { Id = id, ClassA = a, ClassB = b };
        public static DoctrineRule Distinct(string id, int count) => new DoctrineRule { Id = id, MinDistinctClasses = count };
    }

    /// <summary>
    /// Works out which doctrines each player gets (GAME_DESIGN §5.1, D-17). A pair doctrine applies to a player of
    /// either class when another player has the other class. Each player keeps at most <c>maxPerPlayer</c>,
    /// in the order the rules are listed, so duplicates of a class still play normally.
    /// </summary>
    public static class DoctrineResolver
    {
        public static List<string>[] Resolve(IReadOnlyList<string> playerClasses, IReadOnlyList<DoctrineRule> rules, int maxPerPlayer = 2)
        {
            int n = playerClasses?.Count ?? 0;
            var result = new List<string>[n];
            var distinct = new HashSet<string>();
            for (int i = 0; i < n; i++)
            {
                result[i] = new List<string>(maxPerPlayer);
                if (!string.IsNullOrEmpty(playerClasses[i])) distinct.Add(playerClasses[i]);
            }
            if (rules == null) return result;

            for (int p = 0; p < n; p++)
            {
                string mine = playerClasses[p];
                if (string.IsNullOrEmpty(mine)) continue;
                for (int r = 0; r < rules.Count && result[p].Count < maxPerPlayer; r++)
                {
                    DoctrineRule rule = rules[r];
                    if (rule == null || string.IsNullOrEmpty(rule.Id)) continue;
                    bool applies;
                    if (rule.MinDistinctClasses > 0) applies = distinct.Count >= rule.MinDistinctClasses;
                    else if (rule.ClassA == rule.ClassB) applies = false; // a pair needs two different classes
                    else if (mine == rule.ClassA) applies = AnyOther(playerClasses, p, rule.ClassB);
                    else if (mine == rule.ClassB) applies = AnyOther(playerClasses, p, rule.ClassA);
                    else applies = false;
                    if (applies) result[p].Add(rule.Id);
                }
            }
            return result;
        }

        private static bool AnyOther(IReadOnlyList<string> classes, int self, string wanted)
        {
            for (int i = 0; i < classes.Count; i++)
                if (i != self && classes[i] == wanted) return true;
            return false;
        }
    }
}
