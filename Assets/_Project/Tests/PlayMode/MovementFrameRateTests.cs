using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Player;
using NUnit.Framework;
using UnityEngine;

namespace Deadfall.Tests.PlayMode
{
    /// <summary>The same input must cover the same ground at 30, 60, and 144 FPS (M1 acceptance criterion).</summary>
    public class MovementFrameRateTests
    {
        [TearDown]
        public void TearDown() => PlayModeTestUtil.Cleanup();

        private static float DistanceAfter(float dt, float seconds, MotorInput input, Vector3 start)
        {
            GameObject player = PlayModeTestUtil.Track(PlayerFactory.CreateLocalPlayer(start, 0f, new List<WeaponDefinition>()));
            var motor = player.GetComponent<PlayerMotor>();
            motor.enabled = false; // drive it manually with exact frame times
            Physics.SyncTransforms();

            var idle = new MotorInput();
            for (int i = 0; i < 30; i++) { motor.Simulate(idle, 1f / 60f); Physics.SyncTransforms(); } // settle on the ground

            Vector3 from = player.transform.position;
            int steps = Mathf.RoundToInt(seconds / dt);
            for (int i = 0; i < steps; i++) { motor.Simulate(input, dt); Physics.SyncTransforms(); }
            Vector3 delta = player.transform.position - from;
            delta.y = 0f;
            return delta.magnitude;
        }

        [Test]
        public void WalkDistanceMatchesAcrossFrameRates()
        {
            PlayModeTestUtil.Ground();
            var walk = new MotorInput { Move = new Vector2(0f, 1f) };
            float d30 = DistanceAfter(1f / 30f, 2f, walk, new Vector3(-20f, 0.02f, 0f));
            float d60 = DistanceAfter(1f / 60f, 2f, walk, new Vector3(0f, 0.02f, 0f));
            float d144 = DistanceAfter(1f / 144f, 2f, walk, new Vector3(20f, 0.02f, 0f));
            Assert.That(d30, Is.EqualTo(d60).Within(d60 * 0.02f), "30 vs 60 FPS");
            Assert.That(d144, Is.EqualTo(d60).Within(d60 * 0.02f), "144 vs 60 FPS");
            Assert.That(d60, Is.GreaterThan(7.5f), "Walking 2 s at 4.5 m/s should cover about 8.8 m");
        }

        [Test]
        public void SprintIsFasterThanWalkAndStrafeDoesNotSprint()
        {
            PlayModeTestUtil.Ground();
            float walk = DistanceAfter(1f / 60f, 2f, new MotorInput { Move = Vector2.up }, new Vector3(-20f, 0.02f, 0f));
            float sprint = DistanceAfter(1f / 60f, 2f, new MotorInput { Move = Vector2.up, SprintHeld = true }, new Vector3(0f, 0.02f, 0f));
            float strafeSprint = DistanceAfter(1f / 60f, 2f, new MotorInput { Move = Vector2.right, SprintHeld = true }, new Vector3(20f, 0.02f, 0f));
            Assert.Greater(sprint, walk * 1.3f);
            Assert.That(strafeSprint, Is.EqualTo(walk).Within(walk * 0.05f));
        }

        [Test]
        public void StopsQuicklyWhenInputReleased()
        {
            PlayModeTestUtil.Ground();
            GameObject player = PlayModeTestUtil.Track(PlayerFactory.CreateLocalPlayer(new Vector3(0f, 0.02f, 0f), 0f, new List<WeaponDefinition>()));
            var motor = player.GetComponent<PlayerMotor>();
            motor.enabled = false;
            Physics.SyncTransforms();
            for (int i = 0; i < 60; i++) { motor.Simulate(new MotorInput { Move = Vector2.up }, 1f / 60f); Physics.SyncTransforms(); }
            for (int i = 0; i < 9; i++) { motor.Simulate(new MotorInput(), 1f / 60f); Physics.SyncTransforms(); } // 0.15 s
            Assert.Less(motor.HorizontalSpeed, 0.1f);
        }
    }
}
