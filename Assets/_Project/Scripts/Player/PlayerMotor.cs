using System;
using Deadfall.Rules.Common;
using UnityEngine;

namespace Deadfall.Player
{
    [Serializable]
    public sealed class MovementConfig
    {
        [Header("Speeds (m/s)")]
        public float WalkSpeed = 4.5f;
        public float SprintSpeed = 7f;
        public float CrouchSpeed = 2.4f;
        public float BackwardMultiplier = 0.85f;
        public float AimSpeedMultiplier = 0.65f;
        [Tooltip("Sprint only when the move input is within this many degrees of forward.")]
        public float SprintMaxAngle = 50f;

        [Header("Responsiveness")]
        [Tooltip("Reaches 90% of target speed in ~0.12 s.")]
        public float GroundAccelSharpness = 19f;
        [Tooltip("Stops in ~0.1 s.")]
        public float GroundDecelSharpness = 32f;
        public float AirSharpness = 2.5f;

        [Header("Jump and gravity")]
        public float Gravity = 22f;
        public float JumpHeight = 0.9f;
        public float CoyoteTime = 0.1f;
        public float JumpBufferTime = 0.1f;
        public float GroundStickSpeed = 3f;

        [Header("Body")]
        public float Radius = 0.35f;
        public float StandHeight = 1.8f;
        public float CrouchHeight = 1.15f;
        public float EyeBelowTop = 0.12f;
        public float CrouchSharpness = 14f;
        public float StepOffset = 0.35f;
        public float SlopeLimit = 46f;
    }

    /// <summary>Per-frame movement intent, separate from devices so tests and AI-driven bots can feed it.</summary>
    public struct MotorInput
    {
        public Vector2 Move;
        public bool SprintHeld;
        public bool JumpPressed;
        public bool CrouchHeld;
    }

    /// <summary>
    /// Kinematic first-person movement on a CharacterController. Runs in Update with exact,
    /// frame-rate independent integration (see MotionMath), so the same input covers the same
    /// distance at 30, 60, or 144 FPS. The camera is a child, so there is no interpolation jitter.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        [SerializeField] private MovementConfig _config = new MovementConfig();

        private CharacterController _controller;
        private PlayerInputReader _input;
        private PlayerStatus _status;
        private PlayerLook _look;

        private Vector3 _horizontalVelocity;
        private float _verticalVelocity;
        private float _timeSinceGrounded = 999f;
        private float _jumpBufferTimer = 999f;
        private float _currentHeight;
        private bool _isCrouched;

        public MovementConfig Config => _config;
        public Vector3 Velocity => _horizontalVelocity + Vector3.up * _verticalVelocity;
        public float HorizontalSpeed => new Vector2(_horizontalVelocity.x, _horizontalVelocity.z).magnitude;
        public bool IsGrounded { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool IsCrouched => _isCrouched;
        /// <summary>Horizontal speed relative to walk speed, used for weapon spread.</summary>
        public float MoveFactor => _config.WalkSpeed > 0f ? HorizontalSpeed / _config.WalkSpeed : 0f;
        /// <summary>When false (dead), input is ignored but gravity still applies.</summary>
        public bool InputEnabled { get; set; } = true;

        /// <summary>True during an ability dash (Warrior Lunge): normal movement input is ignored.</summary>
        public bool IsDashing => _dashRemaining > 0f;
        public event Action DashEnded;
        public event Action Jumped;
        public event Action<float> Landed;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _input = GetComponent<PlayerInputReader>();
            _status = GetComponent<PlayerStatus>();
            _look = GetComponent<PlayerLook>();
            ApplyBodyConfig();
        }

        public void ApplyBodyConfig()
        {
            if (_controller == null) _controller = GetComponent<CharacterController>();
            _currentHeight = _config.StandHeight;
            _controller.radius = _config.Radius;
            _controller.stepOffset = _config.StepOffset;
            _controller.slopeLimit = _config.SlopeLimit;
            _controller.skinWidth = Mathf.Max(0.01f, _config.Radius * 0.1f);
            _controller.minMoveDistance = 0f;
            SetHeight(_currentHeight);
        }

        private void Update()
        {
            var mi = new MotorInput();
            if (InputEnabled && _input != null && (_status == null || !_status.IsDead))
            {
                PlayerInputFrame f = _input.Current;
                mi.Move = f.Move;
                mi.SprintHeld = f.SprintHeld;
                mi.JumpPressed = f.JumpPressed;
                mi.CrouchHeld = f.CrouchHeld;
                if (_status != null && _status.IsDowned)
                {
                    // Downed (D-01): stay low and crawl; no sprint or jump.
                    mi.SprintHeld = false;
                    mi.JumpPressed = false;
                    mi.CrouchHeld = true;
                }
            }
            Simulate(mi, Time.deltaTime);
        }

        /// <summary>Advances movement by dt. Public so PlayMode tests can drive exact frame times.</summary>
        public void Simulate(in MotorInput input, float dt)
        {
            if (dt <= 0f) return;
            MovementConfig c = _config;

            UpdateCrouch(input.CrouchHeld, dt);

            // Sprint: hold, forward-ish only, not crouched, not blocked by weapon actions.
            bool forwardish = input.Move.y > 0.1f && Vector2.Angle(Vector2.up, input.Move) <= c.SprintMaxAngle;
            bool blocked = _status != null && _status.SprintBlocked;
            IsSprinting = input.SprintHeld && forwardish && !_isCrouched && !blocked;

            float speed = IsSprinting ? c.SprintSpeed : _isCrouched ? c.CrouchSpeed : c.WalkSpeed;
            if (_status != null && _status.IsDowned) speed = c.CrouchSpeed * Deadfall.Rules.Combat.DownedRules.CrawlMultiplier;
            if (_status != null)
            {
                speed *= _status.WeaponMoveMultiplier * _status.BonusMoveMultiplier;
                if (IsSprinting) speed *= _status.SprintMultiplier;
                speed *= Mathf.Lerp(1f, c.AimSpeedMultiplier, _status.AimBlend);
            }
            if (input.Move.y < 0f) speed *= Mathf.Lerp(1f, c.BackwardMultiplier, -input.Move.y);

            Vector3 forward = transform.forward;
            Vector3 right = transform.right;
            Vector3 wish = forward * input.Move.y + right * input.Move.x;
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            Vector3 target = wish * speed;

            float sharpness;
            if (IsGrounded)
                sharpness = target.sqrMagnitude >= _horizontalVelocity.sqrMagnitude - 0.0001f && target.sqrMagnitude > 0.0001f
                    ? c.GroundAccelSharpness
                    : c.GroundDecelSharpness;
            else
                sharpness = c.AirSharpness;

            float dx, dz;
            if (_dashRemaining > 0f)
            {
                // Dash: a fixed-speed burst along the dash direction, ending when its distance is covered.
                float step = Mathf.Min(_dashRemaining, _dashSpeed * dt);
                _dashRemaining -= step;
                dx = _dashDirection.x * step;
                dz = _dashDirection.z * step;
                _horizontalVelocity = _dashDirection * (_dashRemaining > 0f ? _dashSpeed : c.SprintSpeed);
                if (_dashRemaining <= 0f) _dashEndPending = true;
            }
            else
            {
                _horizontalVelocity.x = MotionMath.ExpApproach(_horizontalVelocity.x, target.x, sharpness, dt, out dx);
                _horizontalVelocity.z = MotionMath.ExpApproach(_horizontalVelocity.z, target.z, sharpness, dt, out dz);
            }

            // Jump with coyote time and input buffering so jumps feel reliable at the edge of timing.
            _jumpBufferTimer = input.JumpPressed ? 0f : _jumpBufferTimer + dt;
            _timeSinceGrounded = IsGrounded ? 0f : _timeSinceGrounded + dt;

            float dy;
            bool canJump = _timeSinceGrounded <= c.CoyoteTime && !_isCrouched;
            if (_jumpBufferTimer <= c.JumpBufferTime && canJump)
            {
                _verticalVelocity = MotionMath.JumpVelocity(c.JumpHeight, c.Gravity);
                _jumpBufferTimer = 999f;
                _timeSinceGrounded = 999f;
                IsGrounded = false;
                Jumped?.Invoke();
            }

            if (IsGrounded && _verticalVelocity <= 0f)
            {
                // Small downward push keeps the controller glued to slopes and stairs.
                _verticalVelocity = -c.GroundStickSpeed;
                dy = _verticalVelocity * dt;
            }
            else
            {
                _verticalVelocity = MotionMath.Ballistic(_verticalVelocity, -c.Gravity, dt, out dy);
            }

            Vector3 before = transform.position;
            CollisionFlags flags = _controller.Move(new Vector3(dx, dy, dz));
            Vector3 moved = transform.position - before;
            if (_dashRemaining > 0f && (flags & CollisionFlags.Sides) != 0 && new Vector2(moved.x, moved.z).sqrMagnitude < 0.25f * new Vector2(dx, dz).sqrMagnitude)
            {
                // Ran into a wall: the dash stops here instead of grinding along it.
                _dashRemaining = 0f;
                _horizontalVelocity = Vector3.zero;
                _dashEndPending = true;
            }
            if (_dashEndPending)
            {
                _dashEndPending = false;
                DashEnded?.Invoke();
            }

            if ((flags & CollisionFlags.Above) != 0 && _verticalVelocity > 0f) _verticalVelocity = 0f;

            bool wasGrounded = IsGrounded;
            IsGrounded = _controller.isGrounded;
            if (IsGrounded && !wasGrounded)
            {
                Landed?.Invoke(-_verticalVelocity);
                if (_verticalVelocity < 0f) _verticalVelocity = -c.GroundStickSpeed;
            }

            // Don't store momentum into walls: if we moved less than intended, keep the smaller velocity.
            Vector3 actual = new Vector3(moved.x, 0f, moved.z) / dt;
            if (actual.sqrMagnitude < _horizontalVelocity.sqrMagnitude && (flags & CollisionFlags.Sides) != 0)
                _horizontalVelocity = actual;
        }

        private void UpdateCrouch(bool crouchHeld, float dt)
        {
            MovementConfig c = _config;
            if (crouchHeld)
            {
                _isCrouched = true;
            }
            else if (_isCrouched && HasHeadroom(c.StandHeight))
            {
                _isCrouched = false;
            }

            float targetHeight = _isCrouched ? c.CrouchHeight : c.StandHeight;
            if (!Mathf.Approximately(_currentHeight, targetHeight))
            {
                _currentHeight = Mathf.Lerp(_currentHeight, targetHeight, MotionMath.ExpBlend(c.CrouchSharpness, dt));
                if (Mathf.Abs(_currentHeight - targetHeight) < 0.005f) _currentHeight = targetHeight;
                SetHeight(_currentHeight);
            }
        }

        private bool HasHeadroom(float height)
        {
            float radius = _controller.radius * 0.95f;
            Vector3 bottom = transform.position + Vector3.up * (radius + 0.05f);
            Vector3 top = transform.position + Vector3.up * (height - radius);
            return !Physics.CheckCapsule(bottom, top, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        private void SetHeight(float height)
        {
            // Pivot is at the feet, so the capsule grows upward and the feet stay planted.
            _controller.height = height;
            _controller.center = new Vector3(0f, height * 0.5f, 0f);
            Transform pivot = _look != null ? _look.Pivot : null;
            if (pivot != null && pivot != transform)
                pivot.localPosition = new Vector3(0f, height - _config.EyeBelowTop, 0f);
        }

        /// <summary>Moves the player instantly (spawn, respawn) and clears momentum.</summary>
        /// <summary>
        /// Moves <paramref name="distance"/> metres along <paramref name="direction"/> (flattened) over
        /// <paramref name="seconds"/>, with collisions. Gravity still applies, so dashing off a ledge falls.
        /// </summary>
        public void Dash(Vector3 direction, float distance, float seconds)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f || distance <= 0f) return;
            _dashDirection = direction.normalized;
            _dashRemaining = distance;
            _dashSpeed = distance / Mathf.Max(0.05f, seconds);
        }

        public void CancelDash()
        {
            if (_dashRemaining <= 0f) return;
            _dashRemaining = 0f;
            DashEnded?.Invoke();
        }

        private Vector3 _dashDirection;
        private float _dashRemaining, _dashSpeed;
        private bool _dashEndPending;

        public void Teleport(Vector3 position, float yaw)
        {
            _dashRemaining = 0f;
            _controller.enabled = false;
            transform.position = position;
            _controller.enabled = true;
            _horizontalVelocity = Vector3.zero;
            _verticalVelocity = 0f;
            if (_look != null) _look.SetView(yaw, 0f);
        }
    }
}
