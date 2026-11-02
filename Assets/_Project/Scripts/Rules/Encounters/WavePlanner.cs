using System;
using System.Collections.Generic;
using Deadfall.Rules.Common;

namespace Deadfall.Rules.Encounters
{
    public enum SurgeType : byte
    {
        Standard,
        /// <summary>Many weak enemies: bigger budget, cheap types only, more at once.</summary>
        Swarm,
        /// <summary>Attack from at least three directions with heavies allowed earlier.</summary>
        Siege,
    }

    /// <summary>An enemy type as the planner sees it: just ids and numbers, no engine objects.</summary>
    public sealed class EnemyOption
    {
        public string Id;
        public float ThreatCost = 1f;
        /// <summary>First surge this enemy can appear in.</summary>
        public int UnlockSurge = 1;
        /// <summary>Relative pick chance among affordable options.</summary>
        public float Weight = 1f;
        /// <summary>At most this fraction of a surge's budget goes to this type (0..1).</summary>
        public float MaxBudgetShare = 1f;

        public EnemyOption(string id, float threatCost, int unlockSurge, float weight, float maxBudgetShare = 1f)
        {
            Id = id;
            ThreatCost = threatCost;
            UnlockSurge = unlockSurge;
            Weight = weight;
            MaxBudgetShare = maxBudgetShare;
        }
    }

    /// <summary>The planner's answer for one surge: what spawns, in which order, how fast, from how many directions.</summary>
    public sealed class SurgePlan
    {
        public int Surge;
        public SurgeType Type;
        public float Budget;
        public float BudgetSpent;
        public int Concurrency;
        public int Directions;
        public float HealthScale;
        /// <summary>Enemy ids in spawn order.</summary>
        public readonly List<string> Spawns = new List<string>();

        public int Count(string id)
        {
            int n = 0;
            for (int i = 0; i < Spawns.Count; i++) if (Spawns[i] == id) n++;
            return n;
        }
    }

    /// <summary>
    /// Turns a surge number into a concrete list of enemies by spending a threat budget on unlocked types.
    /// Deterministic for a given seed, so every playtest of "seed 42, surge 7" is the same surge.
    /// </summary>
    public sealed class WavePlanner
    {
        /// <summary>Swarm surges get this much more budget, spent only on types costing at most <see cref="SwarmMaxCost"/>.</summary>
        public const float SwarmBudgetMultiplier = 1.3f;
        public const float SwarmMaxCost = 1.01f;
        public const float SwarmConcurrencyMultiplier = 1.5f;
        public const int SiegeMinDirections = 3;
        /// <summary>Siege lets heavies in this many surges before their normal unlock.</summary>
        public const int SiegeEarlyUnlock = 2;
        /// <summary>Hard stop on spawns per surge, whatever the budget (protects the frame rate and the network).</summary>
        public const int MaxSpawnsPerSurge = 400;

        private readonly DifficultyCurve _curve;
        private readonly IReadOnlyList<EnemyOption> _options;
        private readonly List<EnemyOption> _affordable = new List<EnemyOption>();
        private readonly Dictionary<string, float> _spentByType = new Dictionary<string, float>();

        public WavePlanner(DifficultyCurve curve, IReadOnlyList<EnemyOption> options)
        {
            _curve = curve ?? throw new ArgumentNullException(nameof(curve));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Surge type rotation: the first three are Standard to teach the basics, then every 5th is a Siege and
        /// every 4th (that isn't a Siege) a Swarm.
        /// </summary>
        public static SurgeType TypeFor(int surge)
        {
            if (surge <= 3) return SurgeType.Standard;
            if (surge % 5 == 0) return SurgeType.Siege;
            if (surge % 4 == 0) return SurgeType.Swarm;
            return SurgeType.Standard;
        }

        public SurgePlan Plan(int surge, int players, ulong seed, float adaptive = 0f) =>
            Plan(surge, players, seed, TypeFor(surge), adaptive);

        public SurgePlan Plan(int surge, int players, ulong seed, SurgeType type, float adaptive = 0f)
        {
            surge = Math.Max(1, surge);
            var rng = new DeterministicRandom(seed ^ ((ulong)surge * 0x9E3779B97F4A7C15UL));
            var plan = new SurgePlan
            {
                Surge = surge,
                Type = type,
                Budget = _curve.Budget(surge, players, adaptive),
                Concurrency = _curve.Concurrency(surge, players),
                Directions = _curve.Directions(surge),
                HealthScale = _curve.HealthScale(surge),
            };
            int unlockSurge = surge;
            float maxCost = float.MaxValue;
            switch (type)
            {
                case SurgeType.Swarm:
                    plan.Budget *= SwarmBudgetMultiplier;
                    plan.Concurrency = (int)Math.Ceiling(plan.Concurrency * SwarmConcurrencyMultiplier);
                    maxCost = SwarmMaxCost;
                    break;
                case SurgeType.Siege:
                    plan.Directions = Math.Max(plan.Directions, SiegeMinDirections);
                    unlockSurge = surge + SiegeEarlyUnlock;
                    break;
            }

            _spentByType.Clear();
            float remaining = plan.Budget;
            while (plan.Spawns.Count < MaxSpawnsPerSurge)
            {
                _affordable.Clear();
                float totalWeight = 0f;
                for (int i = 0; i < _options.Count; i++)
                {
                    EnemyOption o = _options[i];
                    if (o == null || o.ThreatCost <= 0f || o.Weight <= 0f) continue;
                    if (o.UnlockSurge > unlockSurge || o.ThreatCost > maxCost || o.ThreatCost > remaining) continue;
                    _spentByType.TryGetValue(o.Id, out float spent);
                    if (spent + o.ThreatCost > plan.Budget * Math.Max(0f, Math.Min(1f, o.MaxBudgetShare)) && spent > 0f) continue;
                    _affordable.Add(o);
                    totalWeight += o.Weight;
                }
                if (_affordable.Count == 0) break;

                float roll = rng.NextFloat() * totalWeight;
                EnemyOption pick = _affordable[_affordable.Count - 1];
                for (int i = 0; i < _affordable.Count; i++)
                {
                    roll -= _affordable[i].Weight;
                    if (roll <= 0f)
                    {
                        pick = _affordable[i];
                        break;
                    }
                }
                plan.Spawns.Add(pick.Id);
                remaining -= pick.ThreatCost;
                plan.BudgetSpent += pick.ThreatCost;
                _spentByType.TryGetValue(pick.Id, out float s);
                _spentByType[pick.Id] = s + pick.ThreatCost;
            }

            // A surge always has at least one enemy, even if the budget can't afford anything.
            if (plan.Spawns.Count == 0)
            {
                EnemyOption cheapest = null;
                for (int i = 0; i < _options.Count; i++)
                {
                    EnemyOption o = _options[i];
                    if (o == null || o.UnlockSurge > unlockSurge || o.ThreatCost <= 0f) continue;
                    if (cheapest == null || o.ThreatCost < cheapest.ThreatCost) cheapest = o;
                }
                if (cheapest != null)
                {
                    plan.Spawns.Add(cheapest.Id);
                    plan.BudgetSpent += cheapest.ThreatCost;
                }
            }

            Shuffle(plan.Spawns, rng);
            return plan;
        }

        private static void Shuffle(List<string> list, DeterministicRandom rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
