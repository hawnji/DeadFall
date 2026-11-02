using UnityEngine;

namespace Deadfall.Player
{
    /// <summary>
    /// Small shared state that player components read and write each frame, so motor, look, and
    /// weapons coordinate without referencing each other's internals.
    /// </summary>
    public sealed class PlayerStatus : MonoBehaviour
    {
        /// <summary>Set by weapons when aiming, firing, meleeing, or recovering from sprint, so the motor won't sprint.</summary>
        public bool SprintBlocked;
        /// <summary>Movement multiplier from the held weapon and aim state.</summary>
        public float WeaponMoveMultiplier = 1f;
        /// <summary>Move speed bonus from Field Mods and skills (1 = none).</summary>
        public float BonusMoveMultiplier = 1f;
        /// <summary>0..1 aim-down-sights blend, read by look (sensitivity, FOV) and motor (speed).</summary>
        public float AimBlend;
        public bool IsDead;
    }
}
