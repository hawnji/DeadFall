using System;

namespace Deadfall.Rules.Combat
{
    [Serializable]
    public sealed class HealthStats
    {
        public float MaxHealth = 100f;
        /// <summary>Seconds after the last damage before regeneration starts.</summary>
        public float RegenDelaySeconds = 5f;
        public float RegenPerSecond = 8f;
        /// <summary>
        /// Regeneration only refills the current segment (e.g. 25 = up to the next 25 HP mark), so
        /// heavy damage still needs healing or support. 0 regenerates to full.
        /// </summary>
        public float RegenSegmentSize = 25f;
    }

    /// <summary>Health, damage intake, and segmented regeneration. Time-driven and engine-free.</summary>
    public sealed class HealthModel
    {
        private readonly HealthStats _stats;
        private double _lastDamageTime = double.NegativeInfinity;
        private float _regenCap;

        public HealthModel(HealthStats stats)
        {
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
            Current = stats.MaxHealth;
            _regenCap = stats.MaxHealth;
        }

        public float Current { get; private set; }
        public float Max => _stats.MaxHealth;
        public float Normalized => Max > 0f ? Current / Max : 0f;
        public bool IsDead => Current <= 0f;

        /// <summary>Applies damage and returns the amount actually removed.</summary>
        public float ApplyDamage(float amount, double now)
        {
            if (IsDead || amount <= 0f) return 0f;
            float before = Current;
            Current = Math.Max(0f, Current - amount);
            _lastDamageTime = now;
            _regenCap = ComputeRegenCap(Current);
            return before - Current;
        }

        public float Heal(float amount)
        {
            if (IsDead || amount <= 0f) return 0f;
            float before = Current;
            Current = Math.Min(Max, Current + amount);
            _regenCap = Math.Max(_regenCap, ComputeRegenCap(Current));
            return Current - before;
        }

        public void Tick(double now, float dt)
        {
            if (IsDead || Current >= _regenCap) return;
            if (now - _lastDamageTime < _stats.RegenDelaySeconds) return;
            Current = Math.Min(_regenCap, Current + _stats.RegenPerSecond * dt);
        }

        public void ResetToFull()
        {
            Current = Max;
            _regenCap = Max;
            _lastDamageTime = double.NegativeInfinity;
        }

        private float ComputeRegenCap(float current)
        {
            float seg = _stats.RegenSegmentSize;
            if (seg <= 0f) return Max;
            if (current <= 0f) return 0f;
            float cap = (float)Math.Ceiling(current / seg) * seg;
            return Math.Min(Max, cap);
        }
    }
}
