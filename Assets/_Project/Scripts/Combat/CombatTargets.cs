using System.Collections.Generic;
using UnityEngine;

namespace Deadfall.Combat
{
    /// <summary>Something enemies can hunt: players now, decoys and engineer structures later.</summary>
    public interface ICombatTarget
    {
        Transform TargetTransform { get; }
        /// <summary>False while dead or otherwise not worth chasing.</summary>
        bool IsTargetable { get; }
        IDamageable Damageable { get; }
    }

    /// <summary>A target that pulls enemies toward itself when they are within its radius (Tank shield).</summary>
    public interface IAggroTarget : ICombatTarget
    {
        float AggroRadius { get; }
    }

    /// <summary>
    /// Registry of everything enemies can target, so the AI never searches the scene. Targets register
    /// themselves when enabled. Reset when play mode starts (safe with fast enter-play-mode).
    /// </summary>
    public static class CombatTargets
    {
        private static readonly List<ICombatTarget> Targets = new List<ICombatTarget>(8);

        public static IReadOnlyList<ICombatTarget> All => Targets;

        public static void Register(ICombatTarget target)
        {
            if (target != null && !Targets.Contains(target)) Targets.Add(target);
        }

        public static void Unregister(ICombatTarget target) => Targets.Remove(target);

        /// <summary>
        /// The target an enemy at <paramref name="from"/> should go for: the nearest aggro target whose radius it is
        /// inside, otherwise the nearest targetable target (straight-line distance). Null when there is none.
        /// </summary>
        public static ICombatTarget Nearest(Vector3 from, out float distance)
        {
            ICombatTarget taunt = null;
            float tauntSq = float.MaxValue;
            for (int i = 0; i < Targets.Count; i++)
            {
                if (!(Targets[i] is IAggroTarget a) || !a.IsTargetable || a.TargetTransform == null) continue;
                float sq = (a.TargetTransform.position - from).sqrMagnitude;
                if (sq <= a.AggroRadius * a.AggroRadius && sq < tauntSq)
                {
                    tauntSq = sq;
                    taunt = a;
                }
            }
            if (taunt != null)
            {
                distance = Mathf.Sqrt(tauntSq);
                return taunt;
            }

            ICombatTarget best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < Targets.Count; i++)
            {
                ICombatTarget t = Targets[i];
                if (t == null || !t.IsTargetable || t.TargetTransform == null) continue;
                float sq = (t.TargetTransform.position - from).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = t;
                }
            }
            distance = best != null ? Mathf.Sqrt(bestSq) : float.MaxValue;
            return best;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Targets.Clear();
    }
}
