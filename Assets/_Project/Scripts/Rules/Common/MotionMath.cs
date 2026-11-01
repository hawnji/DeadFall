using System;

namespace Deadfall.Rules.Common
{
    /// <summary>
    /// Frame-rate independent smoothing. Velocity approaches a target exponentially with a given
    /// sharpness (1/s). Displacement is integrated exactly, so ten 10 ms steps and one 100 ms step
    /// land in the same place. This is what keeps movement identical at 30, 60, and 144 FPS.
    /// </summary>
    public static class MotionMath
    {
        /// <summary>
        /// Advances one axis. Returns the new velocity and writes the exact distance travelled during dt.
        /// </summary>
        public static float ExpApproach(float velocity, float target, float sharpness, float dt, out float displacement)
        {
            if (dt <= 0f)
            {
                displacement = 0f;
                return velocity;
            }
            if (sharpness <= 0f)
            {
                displacement = velocity * dt;
                return velocity;
            }
            float decay = (float)Math.Exp(-sharpness * dt);
            float delta = velocity - target;
            displacement = target * dt + delta * (1f - decay) / sharpness;
            return target + delta * decay;
        }

        /// <summary>Blend factor for exponential smoothing of non-velocity values (camera, UI).</summary>
        public static float ExpBlend(float sharpness, float dt)
        {
            if (sharpness <= 0f || dt <= 0f) return 0f;
            return 1f - (float)Math.Exp(-sharpness * dt);
        }

        /// <summary>Sharpness that reaches the given fraction (e.g. 0.9) of the target after the given time.</summary>
        public static float SharpnessFor(float fraction, float seconds)
        {
            if (seconds <= 0f) return 0f;
            fraction = Math.Min(Math.Max(fraction, 0.0001f), 0.9999f);
            return -(float)Math.Log(1f - fraction) / seconds;
        }

        /// <summary>Exact vertical step under constant acceleration. Returns new velocity.</summary>
        public static float Ballistic(float velocity, float acceleration, float dt, out float displacement)
        {
            displacement = velocity * dt + 0.5f * acceleration * dt * dt;
            return velocity + acceleration * dt;
        }

        /// <summary>Launch speed needed to reach a height against gravity (gravity given as a positive number).</summary>
        public static float JumpVelocity(float height, float gravity) => (float)Math.Sqrt(2f * Math.Abs(gravity) * Math.Max(0f, height));
    }
}
