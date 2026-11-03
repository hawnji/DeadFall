using System;
using System.Collections.Generic;
using Deadfall.Rules.Combat;
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
        /// <summary>Guaranteed elites (Alpha, later Goliath) and double the variant chance; smaller budget.</summary>
        Elite,
        /// <summary>One relentless pursuer (Stalker) with light support.</summary>
        Hunt,
        /// <summary>A normal surge, but supply crates drop into the map while it runs.</summary>
        Supply,
        /// <summary>A Specimen (boss) fight with light support.</summary>
        Specimen,
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
        public EnemyRole Role;
        /// <summary>False for types a Splitting variant breaks into (no endless chains).</summary>
        public bool CanSplit = true;

        public EnemyOption(string id, float threatCost, int unlockSurge, float weight, float maxBudgetShare = 1f,
            EnemyRole role = EnemyRole.Regular, bool canSplit = true)
        {
            Id = id;
            ThreatCost = threatCost;
            UnlockSurge = unlockSurge;
            Weight = weight;
            MaxBudgetShare = maxBudgetShare;
            Role = role;
            CanSplit = canSplit;
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
        /// <summary>Mutation traits for each entry of <see cref="Spawns"/> (same index).</summary>
        public readonly List<EnemyTraits> Traits = new List<EnemyTraits>();
        /// <summary>Supply surges: crates dropped into the map while it runs.</summary>
        public int SupplyCrates;
        /// <summary>Specimen surges: the boss id (also Spawns[0]) and its rolled modifiers.</summary>
        public string BossId;
        public SpecimenModifiers BossModifiers;
        /// <summary>Endless escalation modifiers active this surge (M12).</summary>
        public EndlessModifier Endless;
        /// <summary>Hunt surges: the pursuer id (also Spawns[0]).</summary>
        public string HunterId;

        public EnemyTraits TraitsAt(int index) => index >= 0 && index < Traits.Count ? Traits[index] : EnemyTraits.None;

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
        public const float EliteBudgetMultiplier = 0.8f;
        public const float EliteVariantMultiplier = 2f;
        /// <summary>Elite surges may field elites this many surges before their normal unlock.</summary>
        public const int EliteEarlyUnlock = 6;
        public const float HuntBudgetMultiplier = 0.6f;
        public const float HuntConcurrencyMultiplier = 0.75f;
        public const float SpecimenBudgetMultiplier = 0.5f;

        private readonly DifficultyCurve _curve;
        private readonly IReadOnlyList<EnemyOption> _options;
        private readonly List<EnemyOption> _affordable = new List<EnemyOption>();
        private readonly Dictionary<string, float> _spentByType = new Dictionary<string, float>();

        /// <summary>Endless runs add escalation modifiers from surge 21 (<see cref="EndlessRules"/>).</summary>
        public bool Endless { get; set; }
        /// <summary>Boss Specimen surges use here when it exists (the environment's boss, M12). Null: any boss.</summary>
        public string PreferredBossId { get; set; }

        public WavePlanner(DifficultyCurve curve, IReadOnlyList<EnemyOption> options)
        {
            _curve = curve ?? throw new ArgumentNullException(nameof(curve));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Surge type rotation: the first three are Standard to teach the basics. After that, in priority order:
        /// every 10th is a Specimen, every 5th a Siege, every 7th an Elite, every 6th a Hunt, every 4th a Swarm and
        /// every 9th a Supply surge. Surges 1-10: S S S Swarm Siege Hunt Elite Swarm Supply Specimen.
        /// </summary>
        public static SurgeType TypeFor(int surge)
        {
            if (surge <= 3) return SurgeType.Standard;
            if (surge % 10 == 0) return SurgeType.Specimen;
            if (surge % 5 == 0) return SurgeType.Siege;
            if (surge % 7 == 0) return SurgeType.Elite;
            if (surge % 6 == 0) return SurgeType.Hunt;
            if (surge % 4 == 0) return SurgeType.Swarm;
            if (surge % 9 == 0) return SurgeType.Supply;
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
            float variantMultiplier = 1f;
            EnemyOption boss = null, hunter = null;
            var forced = new List<EnemyOption>();
            switch (type)
            {
                case SurgeType.Elite:
                {
                    plan.Budget *= EliteBudgetMultiplier;
                    variantMultiplier = EliteVariantMultiplier;
                    int elites = 1 + Math.Max(1, Math.Min(4, players)) / 2;
                    for (int i = 0; i < elites; i++)
                    {
                        EnemyOption e = PickRole(EnemyRole.Elite, surge + EliteEarlyUnlock, rng);
                        if (e != null) forced.Add(e);
                    }
                    break;
                }
                case SurgeType.Hunt:
                    hunter = PickRole(EnemyRole.Hunter, surge, rng);
                    if (hunter != null)
                    {
                        plan.Budget *= HuntBudgetMultiplier;
                        plan.Concurrency = Math.Max(2, (int)Math.Ceiling(plan.Concurrency * HuntConcurrencyMultiplier));
                    }
                    break;
                case SurgeType.Supply:
                    plan.SupplyCrates = 2 + Math.Max(1, Math.Min(4, players)) / 2;
                    break;
                case SurgeType.Specimen:
                    boss = PickRole(EnemyRole.Boss, int.MaxValue, rng);
                    if (boss != null)
                    {
                        plan.Budget *= SpecimenBudgetMultiplier;
                        plan.BossModifiers = SpecimenRules.RollModifiers(rng, surge);
                    }
                    break;
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
            if (Endless)
            {
                EndlessRules.Apply(plan, Math.Max(SiegeMinDirections, _curve.Settings.MaxDirections) + 1, _curve.Settings.MaxConcurrency);
                variantMultiplier *= EndlessRules.VariantMultiplier(surge);
            }

            _spentByType.Clear();
            float remaining = plan.Budget;
            // Guaranteed elites are paid for first (they may use up the whole budget).
            foreach (EnemyOption e in forced)
            {
                plan.Spawns.Add(e.Id);
                remaining -= e.ThreatCost;
                plan.BudgetSpent += e.ThreatCost;
                _spentByType.TryGetValue(e.Id, out float fs);
                _spentByType[e.Id] = fs + e.ThreatCost;
            }
            while (plan.Spawns.Count < MaxSpawnsPerSurge)
            {
                _affordable.Clear();
                float totalWeight = 0f;
                for (int i = 0; i < _options.Count; i++)
                {
                    EnemyOption o = _options[i];
                    if (o == null || o.ThreatCost <= 0f || o.Weight <= 0f) continue;
                    if (o.Role == EnemyRole.Hunter || o.Role == EnemyRole.Boss) continue;
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
                    if (o.Role != EnemyRole.Regular) continue;
                    if (cheapest == null || o.ThreatCost < cheapest.ThreatCost) cheapest = o;
                }
                if (cheapest != null)
                {
                    plan.Spawns.Add(cheapest.Id);
                    plan.BudgetSpent += cheapest.ThreatCost;
                }
            }

            Shuffle(plan.Spawns, rng);
            // The pursuer or the Specimen arrives first, so the fight is about it from the start.
            if (hunter != null)
            {
                plan.Spawns.Insert(0, hunter.Id);
                plan.HunterId = hunter.Id;
            }
            if (boss != null)
            {
                plan.Spawns.Insert(0, boss.Id);
                plan.BossId = boss.Id;
            }
            for (int i = 0; i < plan.Spawns.Count; i++)
            {
                EnemyOption o = Find(plan.Spawns[i]);
                plan.Traits.Add(o == null ? EnemyTraits.None : VariantRules.Roll(rng, surge, o.Role, o.CanSplit, variantMultiplier));
            }
            return plan;
        }

        private EnemyOption Find(string id)
        {
            for (int i = 0; i < _options.Count; i++) if (_options[i] != null && _options[i].Id == id) return _options[i];
            return null;
        }

        /// <summary>A weighted pick among options of <paramref name="role"/> unlocked by <paramref name="unlockSurge"/>.</summary>
        private EnemyOption PickRole(EnemyRole role, int unlockSurge, DeterministicRandom rng)
        {
            if (role == EnemyRole.Boss && PreferredBossId != null)
            {
                for (int i = 0; i < _options.Count; i++)
                    if (_options[i] != null && _options[i].Role == role && _options[i].Id == PreferredBossId) { rng.NextFloat(); return _options[i]; }
            }
            float total = 0f;
            for (int i = 0; i < _options.Count; i++)
            {
                EnemyOption o = _options[i];
                if (o != null && o.Role == role && o.UnlockSurge <= unlockSurge) total += Math.Max(0.01f, o.Weight);
            }
            if (total <= 0f) return null;
            float roll = rng.NextFloat() * total;
            EnemyOption last = null;
            for (int i = 0; i < _options.Count; i++)
            {
                EnemyOption o = _options[i];
                if (o == null || o.Role != role || o.UnlockSurge > unlockSurge) continue;
                last = o;
                roll -= Math.Max(0.01f, o.Weight);
                if (roll <= 0f) return o;
            }
            return last;
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
