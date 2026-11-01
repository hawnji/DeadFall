using Deadfall.Rules.Common;
using NUnit.Framework;

namespace Deadfall.Tests.EditMode.Rules
{
    public class MotionMathTests
    {
        private static float Simulate(float dt, float seconds, float target, float sharpness)
        {
            float v = 0f, x = 0f;
            int steps = (int)System.Math.Round(seconds / dt);
            for (int i = 0; i < steps; i++)
            {
                v = MotionMath.ExpApproach(v, target, sharpness, dt, out float dx);
                x += dx;
            }
            return x;
        }

        [Test]
        public void DistanceIsIdenticalAcrossFrameRates()
        {
            float at30 = Simulate(1f / 30f, 2f, 4.5f, 19f);
            float at60 = Simulate(1f / 60f, 2f, 4.5f, 19f);
            float at144 = Simulate(1f / 144f, 2f, 4.5f, 19f);
            Assert.AreEqual(at60, at30, at60 * 0.001f);
            Assert.AreEqual(at60, at144, at60 * 0.001f);
        }

        [Test]
        public void SharpnessForReachesFraction()
        {
            float k = MotionMath.SharpnessFor(0.9f, 0.12f);
            float v = MotionMath.ExpApproach(0f, 1f, k, 0.12f, out _);
            Assert.AreEqual(0.9f, v, 1e-4);
        }

        [Test]
        public void BallisticMatchesClosedFormAtAnyStep()
        {
            foreach (float dt in new[] { 1f / 30f, 1f / 144f })
            {
                float v = 10f, y = 0f;
                int steps = (int)System.Math.Round(1f / dt);
                for (int i = 0; i < steps; i++) { v = MotionMath.Ballistic(v, -20f, dt, out float dy); y += dy; }
                Assert.AreEqual(0f, y, 1e-3, $"dt={dt}"); // 10*1 - 0.5*20*1 = 0
            }
        }

        [Test]
        public void JumpVelocityReachesHeight()
        {
            float v = MotionMath.JumpVelocity(0.9f, 20f);
            Assert.AreEqual(0.9f, v * v / (2f * 20f), 1e-4);
        }
    }
}
