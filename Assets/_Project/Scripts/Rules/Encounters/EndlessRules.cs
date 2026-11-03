using System;
using System.Text;

namespace Deadfall.Rules.Encounters
{
    /// <summary>Escalation modifiers that stack in Endless (D-15).</summary>
    [Flags]
    public enum EndlessModifier
    {
        None = 0,
        /// <summary>Enemies have more health.</summary>
        Armored = 1,
        /// <summary>Bigger surges, more enemies at once.</summary>
        Swarming = 2,
        /// <summary>More enemies at once from more directions.</summary>
        Frenzied = 4,
        /// <summary>Many more mutations (variants).</summary>
        EliteTide = 8,
        /// <summary>Shorter lulls.</summary>
        Relentless = 16,
    }

    /// <summary>
    /// Endless mode (M12, D-15): no final surge. The normal curve makes it comfortable to 10 and hard by 20; from
    /// surge 21 an escalation modifier is added every 5 surges in a fixed order, and once all five are active they
    /// start stacking again, so 40+ is extreme for a full squad. Pure functions so the ramp is tested.
    /// </summary>
    public static class EndlessRules
    {
        public const int FirstModifierSurge = 21;
        public const int SurgesPerModifier = 5;
        public static readonly EndlessModifier[] Order =
        {
            EndlessModifier.Armored, EndlessModifier.Swarming, EndlessModifier.Frenzied, EndlessModifier.EliteTide, EndlessModifier.Relentless,
        };

        /// <summary>How many modifier steps have been reached by <paramref name="surge"/>.</summary>
        public static int Steps(int surge) => surge < FirstModifierSurge ? 0 : 1 + (surge - FirstModifierSurge) / SurgesPerModifier;

        /// <summary>True on the surge a new step is added (for the announcement).</summary>
        public static bool StepsUpAt(int surge) => surge >= FirstModifierSurge && (surge - FirstModifierSurge) % SurgesPerModifier == 0;

        /// <summary>Stack count of one modifier at <paramref name="surge"/>.</summary>
        public static int Stacks(int surge, EndlessModifier m)
        {
            int idx = Array.IndexOf(Order, m);
            int steps = Steps(surge);
            if (idx < 0 || steps <= idx) return 0;
            return 1 + (steps - 1 - idx) / Order.Length;
        }

        public static EndlessModifier Active(int surge)
        {
            EndlessModifier m = EndlessModifier.None;
            foreach (EndlessModifier o in Order) if (Stacks(surge, o) > 0) m |= o;
            return m;
        }

        public static float HealthMultiplier(int surge) => 1f + 0.25f * Stacks(surge, EndlessModifier.Armored);
        public static float BudgetMultiplier(int surge) => 1f + 0.2f * Stacks(surge, EndlessModifier.Swarming);
        public static float ConcurrencyMultiplier(int surge) =>
            (1f + 0.15f * Stacks(surge, EndlessModifier.Swarming)) * (1f + 0.25f * Stacks(surge, EndlessModifier.Frenzied));
        public static int ExtraDirections(int surge) => Stacks(surge, EndlessModifier.Frenzied);
        public static float VariantMultiplier(int surge) => 1f + 1f * Stacks(surge, EndlessModifier.EliteTide);
        public static float LullMultiplier(int surge) => Math.Max(0.35f, (float)Math.Pow(0.7, Stacks(surge, EndlessModifier.Relentless)));

        /// <summary>Applies the modifiers for <paramref name="plan"/>'s surge (the caller handles variants and lulls).</summary>
        public static void Apply(SurgePlan plan, int maxDirections, int maxConcurrency)
        {
            int s = plan.Surge;
            plan.Endless = Active(s);
            plan.HealthScale *= HealthMultiplier(s);
            plan.Budget *= BudgetMultiplier(s);
            plan.Concurrency = Math.Min(maxConcurrency, (int)Math.Ceiling(plan.Concurrency * ConcurrencyMultiplier(s)));
            plan.Directions = Math.Min(maxDirections, plan.Directions + ExtraDirections(s));
        }

        /// <summary>"Armored ×2 · Swarming · Frenzied" (empty before surge 21).</summary>
        public static string Describe(int surge)
        {
            var sb = new StringBuilder();
            foreach (EndlessModifier o in Order)
            {
                int n = Stacks(surge, o);
                if (n <= 0) continue;
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(Name(o));
                if (n > 1) sb.Append(" x").Append(n);
            }
            return sb.ToString();
        }

        public static string Name(EndlessModifier m) => m switch
        {
            EndlessModifier.Armored => "Armored",
            EndlessModifier.Swarming => "Swarming",
            EndlessModifier.Frenzied => "Frenzied",
            EndlessModifier.EliteTide => "Elite Tide",
            EndlessModifier.Relentless => "Relentless",
            _ => m.ToString(),
        };
    }
}
