using System;
using Deadfall.Core;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// Text on in-world screens (the FOB's last-run screen and booth boards). The project has no uGUI, so this is TextMesh in the
    /// HUD's fonts, drawn with the built-in font's shader (it ships in every player) and sized from measured bounds.
    /// A TextMesh reads correctly when the camera looks along its +Z.
    /// </summary>
    public static class WorldText
    {
        private static Font _builtin;
        private static bool _builtinTried;
        private static Material _headMat, _bodyMat;
        private static bool _hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _builtin = null;
            _builtinTried = false;
            _headMat = _bodyMat = null;
            if (_hooked) Font.textureRebuilt -= OnFontRebuilt;
            _hooked = false;
        }

        private static Font Builtin
        {
            get
            {
                if (_builtinTried) return _builtin;
                _builtinTried = true;
                try { _builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch (Exception) { _builtin = null; }
                if (_builtin == null) { try { _builtin = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch (Exception) { _builtin = null; } }
                return _builtin;
            }
        }

        private static Font FontFor(bool head)
        {
            Font f = head ? HudKit.HeadFont : HudKit.BodyFont;
            return f != null ? f : Builtin;
        }

        private static Material MaterialFor(Font f, bool head)
        {
            Material m = head ? _headMat : _bodyMat;
            if (m != null) return m;
            Shader sh = Builtin != null && Builtin.material != null ? Builtin.material.shader : f.material != null ? f.material.shader : null;
            if (sh == null) return f.material;
            m = new Material(sh) { name = head ? "WorldTextHead" : "WorldTextBody" };
            if (f.material != null) m.mainTexture = f.material.mainTexture;
            // GUI/Text Shader takes its depth test from this; LessEqual keeps text from showing through walls.
            m.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            if (head) _headMat = m; else _bodyMat = m;
            if (!_hooked) { Font.textureRebuilt += OnFontRebuilt; _hooked = true; }
            return m;
        }

        private static void OnFontRebuilt(Font f)
        {
            if (f == null || f.material == null) return;
            if (_headMat != null && f == FontFor(true)) _headMat.mainTexture = f.material.mainTexture;
            if (_bodyMat != null && f == FontFor(false)) _bodyMat.mainTexture = f.material.mainTexture;
        }

        public static TextMesh Create(Transform parent, string name, Vector3 local, TextAnchor anchor, bool head, Quaternion? rotation = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            if (rotation.HasValue) go.transform.localRotation = rotation.Value;
            var tm = go.AddComponent<TextMesh>();
            Font f = FontFor(head);
            if (f != null)
            {
                tm.font = f;
                go.GetComponent<MeshRenderer>().sharedMaterial = MaterialFor(f, head);
            }
            tm.fontSize = 64;
            tm.anchor = anchor;
            tm.alignment = anchor == TextAnchor.UpperRight ? TextAlignment.Right : anchor == TextAnchor.UpperCenter ? TextAlignment.Center : TextAlignment.Left;
            tm.richText = false;
            tm.characterSize = 0.01f;
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return tm;
        }

        /// <summary>Sets the text, then sizes it so a capital is <paramref name="capHeight"/> tall and the line fits <paramref name="maxWidth"/> (metres, unscaled parents).</summary>
        public static void Set(TextMesh tm, string text, float capHeight, float maxWidth, Color color)
        {
            if (tm == null) return;
            tm.color = color;
            if (string.IsNullOrEmpty(text)) { tm.text = string.Empty; return; }
            tm.characterSize = 1f;
            tm.text = "H";
            float h = Height(tm);
            float size = h > 0.0001f ? capHeight / h : capHeight / (tm.fontSize * 0.07f);
            tm.characterSize = size;
            tm.text = text;
            float w = Width(tm);
            if (w > maxWidth && w > 0.0001f) tm.characterSize = size * maxWidth / w;
        }

        private static float Height(TextMesh tm)
        {
            Renderer r = tm.GetComponent<Renderer>();
            return r != null ? r.bounds.size.y : 0f;
        }

        /// <summary>World bounds of upright text: the width is the horizontal diagonal (works at any yaw).</summary>
        private static float Width(TextMesh tm)
        {
            Renderer r = tm.GetComponent<Renderer>();
            if (r == null) return 0f;
            Vector3 s = r.bounds.size;
            return Mathf.Sqrt(s.x * s.x + s.z * s.z);
        }
    }
}
