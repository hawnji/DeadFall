using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deadfall.Game
{
    /// <summary>
    /// The Blackout event's look on this machine: every light drops to a glimmer and the ambient goes nearly black.
    /// The local flashlight (a spotlight on the camera) turns on by itself in a blackout; L toggles it any time.
    /// </summary>
    public sealed class BlackoutLighting : MonoBehaviour
    {
        private const float LightFactor = 0.06f;
        private const float AmbientFactor = 0.12f;

        private static BlackoutLighting _instance;
        private readonly Dictionary<Light, float> _saved = new Dictionary<Light, float>();
        private Color _savedAmbient;
        private float _savedAmbientIntensity;
        private Light _flashlight;
        private InputAction _toggle;

        public static bool IsDark { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            IsDark = false;
        }

        private void Awake()
        {
            _instance = this;
            _toggle = new InputAction("Flashlight", InputActionType.Button, "<Keyboard>/l");
            _toggle.Enable();
        }

        private void OnDestroy()
        {
            if (IsDark) SetDark(false);
            _toggle?.Disable();
            if (_instance == this) _instance = null;
        }

        public static void SetDark(bool dark)
        {
            if (dark == IsDark) return;
            IsDark = dark;
            if (_instance != null) _instance.Apply(dark);
        }

        private void Apply(bool dark)
        {
            if (dark)
            {
                _saved.Clear();
                // Resources.FindObjectsOfTypeAll is not deprecated in any Unity version (FindObjectsByType's overloads
                // changed in Unity 6.x); it also returns assets and inactive objects, so keep live scene lights only.
                foreach (Light l in Resources.FindObjectsOfTypeAll<Light>())
                {
                    if (l == null || l == _flashlight || !l.gameObject.scene.IsValid() || !l.gameObject.activeInHierarchy) continue; // flickering lights may be briefly disabled: include them
                    _saved[l] = l.intensity;
                    l.intensity *= LightFactor;
                }
                _savedAmbient = RenderSettings.ambientLight;
                _savedAmbientIntensity = RenderSettings.ambientIntensity;
                RenderSettings.ambientLight = _savedAmbient * AmbientFactor;
                RenderSettings.ambientIntensity = _savedAmbientIntensity * AmbientFactor;
                SetFlashlight(true);
            }
            else
            {
                foreach (KeyValuePair<Light, float> kv in _saved)
                    if (kv.Key != null) kv.Key.intensity = kv.Value;
                _saved.Clear();
                RenderSettings.ambientLight = _savedAmbient;
                RenderSettings.ambientIntensity = _savedAmbientIntensity;
                SetFlashlight(false);
            }
        }

        private void Update()
        {
            if (_toggle != null && _toggle.WasPressedThisFrame()) SetFlashlight(_flashlight == null || !_flashlight.enabled);
        }

        private void SetFlashlight(bool on)
        {
            Camera cam = Camera.main;
            if (on && _flashlight == null && cam != null)
            {
                var go = new GameObject("Flashlight");
                go.transform.SetParent(cam.transform, false);
                go.transform.localPosition = new Vector3(0.15f, -0.1f, 0.1f);
                _flashlight = go.AddComponent<Light>();
                _flashlight.type = LightType.Spot;
                _flashlight.spotAngle = 48f;
                _flashlight.range = 22f;
                _flashlight.intensity = 2.2f;
                _flashlight.color = new Color(1f, 0.96f, 0.85f);
                _flashlight.shadows = LightShadows.None;
            }
            if (_flashlight != null) _flashlight.enabled = on;
        }
    }
}
