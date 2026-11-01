using System.Collections.Generic;

namespace Deadfall.Rules.Common
{
    public enum ModifierKind
    {
        /// <summary>Added to the base value before percentages.</summary>
        Flat,
        /// <summary>Summed with other additive percentages: +10% and +20% give +30%.</summary>
        AddPercent,
        /// <summary>Multiplied separately: x1.1 and x1.2 give x1.32.</summary>
        Multiply,
    }

    public readonly struct StatModifier
    {
        public readonly ModifierKind Kind;
        public readonly float Value;
        /// <summary>Identifies who added this modifier (class passive, Field Mod, attachment) so it can be removed.</summary>
        public readonly object Source;

        public StatModifier(ModifierKind kind, float value, object source)
        {
            Kind = kind;
            Value = value;
            Source = source;
        }
    }

    /// <summary>
    /// Computes (base + flat) * (1 + sum of AddPercent) * product of Multiply.
    /// Used by classes, attachments, and Field Mods so stacking rules live in one place.
    /// </summary>
    public sealed class ModifierStack
    {
        private readonly List<StatModifier> _modifiers = new List<StatModifier>(4);

        public int Count => _modifiers.Count;

        public void Add(StatModifier modifier) => _modifiers.Add(modifier);

        public int RemoveAllFromSource(object source)
        {
            int removed = 0;
            for (int i = _modifiers.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_modifiers[i].Source, source))
                {
                    _modifiers.RemoveAt(i);
                    removed++;
                }
            }
            return removed;
        }

        public void Clear() => _modifiers.Clear();

        public float Evaluate(float baseValue)
        {
            float flat = 0f, percent = 0f, multiply = 1f;
            for (int i = 0; i < _modifiers.Count; i++)
            {
                StatModifier m = _modifiers[i];
                switch (m.Kind)
                {
                    case ModifierKind.Flat: flat += m.Value; break;
                    case ModifierKind.AddPercent: percent += m.Value; break;
                    case ModifierKind.Multiply: multiply *= m.Value; break;
                }
            }
            return (baseValue + flat) * (1f + percent) * multiply;
        }
    }
}
