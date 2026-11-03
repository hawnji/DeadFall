using UnityEngine;

namespace Deadfall.Core
{
    /// <summary>
    /// Placeholder materials created from code. Builds strip any shader that no asset references, and in a
    /// URP player build primitives made at runtime get a material URP can't draw, so both
    /// <c>Shader.Find("Universal Render Pipeline/Lit")</c> and the primitive's own material render pink.
    /// The fix is a real material asset in Resources (<c>Resources/Materials/RuntimeLit.mat</c>, URP Lit):
    /// everything in Resources ships with the build, so its shader does too. The other paths are fallbacks
    /// for the editor and for the built-in pipeline.
    /// </summary>
    public static class RuntimeMaterials
    {
        private const string ResourcePath = "Materials/RuntimeLit";
        private const string EmissivePath = "Materials/RuntimeLitEmissive";
        private static Material _base, _emissive;
        private static bool _emissiveLoaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _base = null;
            _emissive = null;
            _emissiveLoaded = false;
        }

        /// <summary>
        /// A lit material that also glows with <paramref name="color"/> × <paramref name="intensity"/> (lamps, screens,
        /// status lights, specimen fluid). Uses <c>Resources/Materials/RuntimeLitEmissive.mat</c> so the build keeps the
        /// shader's emission variant; without it the keyword is set on a plain copy (fine in the editor).
        /// </summary>
        public static Material CreateEmissive(Color color, float intensity = 1.6f)
        {
            if (!_emissiveLoaded)
            {
                _emissiveLoaded = true;
                bool srpActive = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
                Material asset = srpActive ? Resources.Load<Material>(EmissivePath) : null;
                if (asset != null) _emissive = new Material(asset) { name = "RuntimeLitEmissive" };
            }
            var m = new Material(_emissive != null ? _emissive : Base);
            SetColor(m, color);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", color * intensity);
            return m;
        }

        /// <summary>A new lit material tinted with <paramref name="color"/>. Works with URP and the built-in pipeline.</summary>
        public static Material CreateLit(Color color)
        {
            var m = new Material(Base);
            SetColor(m, color);
            return m;
        }

        public static void SetColor(Material m, Color color)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        }

        private static Material Base
        {
            get
            {
                if (_base != null) return _base;
                bool srpActive = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
                Material asset = srpActive ? Resources.Load<Material>(ResourcePath) : null;
                if (asset != null)
                {
                    _base = new Material(asset) { name = "RuntimeLit" };
                    return _base;
                }
                GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Material pipelineDefault = probe.GetComponent<Renderer>().sharedMaterial;
                Object.Destroy(probe);
                if (pipelineDefault != null && pipelineDefault.shader != null && pipelineDefault.shader.isSupported)
                {
                    _base = new Material(pipelineDefault) { name = "RuntimeLit" };
                }
                else
                {
                    bool srp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
                    Shader shader = Shader.Find(srp ? "Universal Render Pipeline/Lit" : "Standard");
                    _base = new Material(shader != null ? shader : Shader.Find("Hidden/InternalErrorShader")) { name = "RuntimeLit" };
                }
                return _base;
            }
        }
    }
}
