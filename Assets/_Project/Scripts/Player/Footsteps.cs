using Deadfall.Core;
using Deadfall.Rules.Common;
using UnityEngine;

namespace Deadfall.Player
{
    /// <summary>
    /// The local player's footsteps: one step sound per stride of ground covered (longer strides when sprinting,
    /// shorter and quieter when crouched or downed), plus a heavier step on landing.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor))]
    public sealed class Footsteps : MonoBehaviour
    {
        private const float WalkStride = 2.1f;
        private const float SprintStride = 2.7f;
        private const float CrouchStride = 1.5f;

        private PlayerMotor _motor;
        private PlayerStatus _status;
        private float _travelled;

        private void Awake()
        {
            _motor = GetComponent<PlayerMotor>();
            _status = GetComponent<PlayerStatus>();
        }

        private void OnEnable()
        {
            if (_motor != null) _motor.Landed += OnLanded;
        }

        private void OnDisable()
        {
            if (_motor != null) _motor.Landed -= OnLanded;
        }

        private void OnLanded(float fallSpeed)
        {
            if (fallSpeed < 2f || IsDead) return;
            _travelled = 0f;
            GameAudio.Play2D(SfxId.Footstep, Mathf.Clamp(0.7f + fallSpeed / 12f, 0.7f, 1.3f), 0.85f);
        }

        private bool IsDead => _status != null && _status.IsDead;

        private void Update()
        {
            if (_motor == null || !_motor.IsGrounded || _motor.IsDashing || IsDead) return;
            float speed = _motor.HorizontalSpeed;
            if (speed < 0.5f)
            {
                // Standing still: the next step comes soon after moving again, not a full stride later.
                _travelled = Mathf.Min(_travelled, WalkStride * 0.6f);
                return;
            }
            _travelled += speed * Time.deltaTime;
            bool low = _motor.IsCrouched;
            float stride = _motor.IsSprinting ? SprintStride : low ? CrouchStride : WalkStride;
            if (_travelled < stride) return;
            _travelled -= stride;
            float volume = _motor.IsSprinting ? 1f : low ? 0.45f : 0.75f;
            GameAudio.Play2D(SfxId.Footstep, volume);
        }
    }
}
