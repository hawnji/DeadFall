using UnityEngine;

namespace Deadfall.World
{
    /// <summary>
    /// A failing ceiling light: mostly on, with short stutters off now and then. Toggles <see cref="Light.enabled"/>
    /// rather than intensity, so it never fights <c>BlackoutLighting</c>, which scales and restores intensities.
    /// Which lights flicker is deterministic from the map seed; the timing is local (purely visual).
    /// </summary>
    [RequireComponent(typeof(Light))]
    public sealed class LightFlicker : MonoBehaviour
    {
        private Light _light;
        private float _next;
        private int _stutters;

        /// <summary>About one light in seven, never in the start room.</summary>
        public static bool ShouldFlicker(ulong seed, int room, int light)
        {
            if (room == 0) return false;
            ulong h = seed ^ ((ulong)(room + 1) * 0x9E3779B97F4A7C15UL) ^ ((ulong)(light + 1) * 0xBF58476D1CE4E5B9UL);
            h ^= h >> 31;
            h *= 0x94D049BB133111EBUL;
            h ^= h >> 29;
            return h % 7 == 0;
        }

        private void Awake()
        {
            _light = GetComponent<Light>();
            _next = Time.time + Random.Range(1f, 6f);
        }

        private void Update()
        {
            if (Time.time < _next) return;
            if (_stutters > 0)
            {
                _light.enabled = !_light.enabled;
                _stutters--;
                _next = Time.time + Random.Range(0.03f, 0.12f);
                return;
            }
            _light.enabled = true;
            _stutters = Random.Range(1, 4) * 2; // an even count ends with the light on
            _next = Time.time + Random.Range(2f, 9f);
        }

        private void OnDisable()
        {
            if (_light != null) _light.enabled = true;
        }
    }
}
