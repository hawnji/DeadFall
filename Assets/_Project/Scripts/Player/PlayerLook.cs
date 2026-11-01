using Deadfall.Core;
using Deadfall.Rules.Common;
using UnityEngine;

namespace Deadfall.Player
{
    /// <summary>
    /// Mouse look. Rotation comes straight from the raw mouse delta (never multiplied by deltaTime,
    /// never smoothed), so aim is 1:1 at any frame rate. Yaw turns the body, pitch turns the camera pivot.
    /// Also owns camera recoil and FOV.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class PlayerLook : MonoBehaviour
    {
        [Tooltip("Degrees per mouse count at sensitivity 1.")]
        [SerializeField] private float _degreesPerCount = 0.08f;
        [SerializeField] private float _minPitch = -88f;
        [SerializeField] private float _maxPitch = 88f;
        [SerializeField] private float _fovSharpness = 18f;

        private Transform _pivot;
        private Camera _camera;
        private PlayerInputReader _input;
        private PlayerStatus _status;
        private ISettingsService _settings;

        private float _yaw;
        private float _pitch;
        private float _recoilPitchDebt;
        private float _recoilYawDebt;
        private float _currentHorizontalFov;

        public float Yaw => _yaw;
        public float Pitch => _pitch;
        public Camera Camera => _camera;
        public Transform Pivot => _pivot;
        /// <summary>Multiplier applied to FOV at full aim. Set by the held weapon.</summary>
        public float AimFovMultiplier { get; set; } = 0.8f;
        /// <summary>When false (dead, menus), mouse input is ignored.</summary>
        public bool LookEnabled { get; set; } = true;

        private void Awake()
        {
            _camera = GetComponentInChildren<Camera>(true);
            _pivot = _camera != null ? _camera.transform.parent : transform;
            _input = GetComponent<PlayerInputReader>();
            _status = GetComponent<PlayerStatus>();
            _yaw = transform.eulerAngles.y;
            ServiceRegistry.TryGet(out _settings);
            _currentHorizontalFov = _settings != null ? _settings.Current.FieldOfView : 90f;
        }

        private void Update()
        {
            GameSettings s = _settings?.Current;
            float sensitivity = s != null ? s.MouseSensitivity : 1f;
            float aimBlend = _status != null ? _status.AimBlend : 0f;
            float aimSens = s != null ? s.AimSensitivityMultiplier : 0.8f;
            sensitivity *= Mathf.Lerp(1f, aimSens, aimBlend);

            if (LookEnabled && _input != null && (_status == null || !_status.IsDead))
            {
                Vector2 delta = _input.Current.Look * (_degreesPerCount * sensitivity);
                if (s != null && s.InvertY) delta.y = -delta.y;
                _yaw += delta.x;
                _pitch -= delta.y;
            }

            _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);
            _yaw = Mathf.Repeat(_yaw, 360f);
            ApplyRotation();

            // FOV: settings value is horizontal (what players expect); Unity's camera FOV is vertical.
            float baseFov = s != null ? s.FieldOfView : 90f;
            float targetFov = baseFov * Mathf.Lerp(1f, AimFovMultiplier, aimBlend);
            _currentHorizontalFov = Mathf.Lerp(_currentHorizontalFov, targetFov, MotionMath.ExpBlend(_fovSharpness, Time.deltaTime));
            if (_camera != null)
                _camera.fieldOfView = HorizontalToVerticalFov(_currentHorizontalFov, _camera.aspect);
        }

        public void ApplyRotation()
        {
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (_pivot != null && _pivot != transform) _pivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        public void SetView(float yaw, float pitch)
        {
            _yaw = yaw;
            _pitch = Mathf.Clamp(pitch, _minPitch, _maxPitch);
            _recoilPitchDebt = _recoilYawDebt = 0f;
            ApplyRotation();
        }

        /// <summary>Kicks the view. Positive pitch kicks upward.</summary>
        public void AddRecoil(float pitchUp, float yaw)
        {
            float before = _pitch;
            _pitch = Mathf.Clamp(_pitch - pitchUp, _minPitch, _maxPitch);
            _recoilPitchDebt += before - _pitch;
            _yaw += yaw;
            _recoilYawDebt += yaw;
            ApplyRotation();
        }

        /// <summary>Returns the view toward where it was before recoil. Call on frames without firing.</summary>
        public void RecoverRecoil(float sharpness, float dt)
        {
            if (Mathf.Approximately(_recoilPitchDebt, 0f) && Mathf.Approximately(_recoilYawDebt, 0f)) return;
            float t = MotionMath.ExpBlend(sharpness, dt);
            float p = _recoilPitchDebt * t;
            float y = _recoilYawDebt * t;
            _recoilPitchDebt -= p;
            _recoilYawDebt -= y;
            _pitch = Mathf.Clamp(_pitch + p, _minPitch, _maxPitch);
            _yaw -= y;
            ApplyRotation();
        }

        public static float HorizontalToVerticalFov(float horizontalDegrees, float aspect)
        {
            if (aspect <= 0f) return horizontalDegrees;
            float h = horizontalDegrees * Mathf.Deg2Rad;
            return 2f * Mathf.Atan(Mathf.Tan(h * 0.5f) / aspect) * Mathf.Rad2Deg;
        }
    }
}
