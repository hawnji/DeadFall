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

        /// <summary>Nearest targetable target to <paramref name="from"/> (straight-line distance), or null.</summary>
        public static ICombatTarget Nearest(Vector3 from, out float distance)
        {
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
