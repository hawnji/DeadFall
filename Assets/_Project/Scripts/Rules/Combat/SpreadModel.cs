using System;
using Deadfall.Rules.Common;

namespace Deadfall.Rules.Combat
{
    /// <summary>
    /// Current inaccuracy cone for a firearm: base (hip or aim) + movement penalty + shot bloom.
    /// Bloom grows per shot and recovers over time.
    /// </summary>
    public sealed class SpreadModel
    {
        private readonly WeaponStats _stats;

        public SpreadModel(WeaponStats stats) => _stats = stats;

        public float Bloom { get; private set; }

        public void OnShot() => Bloom = Math.Min(_stats.MaxBloom, Bloom + _stats.BloomPerShot);

        public void Recover(float dt) => Bloom = Math.Max(0f, Bloom - _stats.BloomRecoveryPerSecond * dt);

        public void Reset() => Bloom = 0f;

        /// <param name="aimBlend">0 = hip, 1 = fully aimed.</param>
        /// <param name="moveFactor">0 = still, 1 = full walk speed (can exceed 1 when sprinting).</param>
        /// <param name="airborne">Jumping or falling doubles the movement penalty.</param>
        public float CurrentHalfAngle(float aimBlend, float moveFactor, bool airborne)
        {
            aimBlend = Math.Min(1f, Math.Max(0f, aimBlend));
            float baseSpread = _stats.HipSpread + (_stats.AimSpread - _stats.HipSpread) * aimBlend;
            float move = _stats.MoveSpreadPenalty * Math.Max(0f, moveFactor) * (airborne ? 2f : 1f) * (1f - 0.5f * aimBlend);
            float bloom = Bloom * (1f - 0.5f * aimBlend);
            return baseSpread + move + bloom;
        }

        /// <summary>Random angular offset (degrees) inside a cone of the given half-angle.</summary>
        public static void SampleOffset(DeterministicRandom rng, float halfAngle, out float yawDegrees, out float pitchDegrees)
        {
            rng.InsideUnitDisc(out float x, out float y);
            yawDegrees = x * halfAngle;
            pitchDegrees = y * halfAngle;
        }
    }
}
