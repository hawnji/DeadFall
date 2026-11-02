using UnityEngine;

namespace Deadfall.Core
{
    /// <summary>
    /// Placeholder materials created from code. Builds strip any shader that no asset references, so
    /// <c>Shader.Find("Universal Render Pipeline/Lit")</c> works in the editor but returns null in a build
    /// (everything turns pink). Instead this copies the render pipeline's default material, the one Unity
    /// gives new primitives, which is always included because the pipeline asset references it.
    /// </summary>
    public static class RuntimeMaterials
    {
        private static Material _base;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _base = null;

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
