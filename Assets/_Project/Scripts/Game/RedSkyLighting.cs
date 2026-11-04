using System.Collections.Generic;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// The Red Sky event's look on this machine: every light and the ambient shift to a bloody red for the surge.
    /// Colours are saved and put back when the event ends.
    /// </summary>
    public static class RedSkyLighting
    {
        private static readonly Color Red = new Color(1f, 0.22f, 0.16f);
        private const float Blend = 0.7f;
        private static readonly Dictionary<Light, Color> Saved = new Dictionary<Light, Color>();
        private static Color _savedAmbient;
        private static bool _savedFog;
        private static Color _savedFogColor;

        public static bool IsRed { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Saved.Clear();
            IsRed = false;
        }

        public static void Set(bool red)
        {
            if (red == IsRed) return;
            IsRed = red;
            if (red)
            {
                Saved.Clear();
                foreach (Light l in Resources.FindObjectsOfTypeAll<Light>())
                {
                    if (l == null || !l.gameObject.scene.IsValid() || l.type == LightType.Spot) continue; // flashlights keep their white beam
                    Saved[l] = l.color;
                    l.color = Color.Lerp(l.color, Red, Blend);
                }
                _savedAmbient = RenderSettings.ambientLight;
                _savedFog = RenderSettings.fog;
                _savedFogColor = RenderSettings.fogColor;
                RenderSettings.ambientLight = Color.Lerp(_savedAmbient, Red * 0.5f, Blend);
                RenderSettings.fogColor = new Color(0.25f, 0.03f, 0.02f);
                RenderSettings.fog = true;
            }
            else
            {
                foreach (KeyValuePair<Light, Color> kv in Saved)
                    if (kv.Key != null) kv.Key.color = kv.Value;
                Saved.Clear();
                RenderSettings.ambientLight = _savedAmbient;
                RenderSettings.fog = _savedFog;
                RenderSettings.fogColor = _savedFogColor;
            }
        }
    }
}
