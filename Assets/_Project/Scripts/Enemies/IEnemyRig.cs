using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Enemies
{
    /// <summary>What <see cref="Enemy"/> drives on its model each frame: limb swings (<see cref="EnemyRig"/>) or
    /// animation clips (<see cref="ZombieAnimator"/>).</summary>
    public interface IEnemyRig
    {
        /// <summary>True when the rig animates wind-ups, staggers and death itself, so the enemy skips its placeholder
        /// body tilt (it would double up with the clips).</summary>
        bool HandlesPose { get; }

        /// <summary>Back to the rest pose (pooled enemies being reused).</summary>
        void Reset();

        /// <param name="position">The enemy's world position (speed is measured from it, so mirrors work too).</param>
        /// <param name="progress">0..1 through the current brain state.</param>
        void Tick(Vector3 position, EnemyState state, float progress, bool alive, float now, float dt, Vector3 viewer);
    }
}
