using System;
using System.Collections.Generic;
using UnityEngine;

namespace Deadfall.Core
{
    /// <summary>
    /// Loads the Blender-built models in <c>Resources/Models</c> (made by <c>Tools/Blender/build_models.py</c>) and
    /// dresses the game's placeholder primitives with them. Placeholders keep their colliders, hurtboxes and NavMesh
    /// role; only their renderers are hidden, so swapping art never changes gameplay. When a model is missing (or
    /// <see cref="Enabled"/> is off) callers keep the primitives.
    /// <para>
    /// Materials are rebuilt at runtime from the material <i>names</i> (T-58): "C_RRGGBB" / "G_RRGGBB" are fixed
    /// colours ("G_" ones glow), and the slots Skin, SkinDark, Accent and AccentDim are tinted by the caller. That keeps models
    /// working in URP builds (see <see cref="RuntimeMaterials"/>) whatever material the importer made.
    /// </para>
    /// </summary>
    public static class ModelLibrary
    {
        private const string Folder = "Models/";

        /// <summary>Off: every caller keeps its primitive placeholder (debug comparison).</summary>
        public static bool Enabled = true;

        private static readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>();
        private static readonly Dictionary<Color32, Material> Materials = new Dictionary<Color32, Material>();
        private static readonly Dictionary<Color32, Material> Glows = new Dictionary<Color32, Material>();
        private static readonly List<Material> Scratch = new List<Material>(8);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Prefabs.Clear();
            Materials.Clear();
            Glows.Clear();
            Enabled = true;
        }

        public static bool Has(string id) => Load(id) != null;

        /// <summary>
        /// Instantiates model <paramref name="id"/> under <paramref name="parent"/> at the local origin (no colliders).
        /// <paramref name="tint"/> maps a tint slot name (Skin, SkinDark, Accent, AccentDim) to a colour; null keeps
        /// the slot's default. Returns null when the model isn't available.
        /// </summary>
        public static GameObject Spawn(string id, Transform parent, Func<string, Color?> tint = null)
        {
            GameObject prefab = Load(id);
            if (prefab == null) return null;
            GameObject go = UnityEngine.Object.Instantiate(prefab, parent, false);
            go.name = "Model_" + id;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(c);
            foreach (Animator a in go.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.Destroy(a);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.GetSharedMaterials(Scratch);
                var mats = new Material[Scratch.Count];
                for (int i = 0; i < mats.Length; i++) mats[i] = MaterialFor(Scratch[i] != null ? Scratch[i].name : string.Empty, tint);
                r.sharedMaterials = mats;
            }
            return go;
        }

        /// <summary>
        /// Dresses a placeholder cube with fit model <paramref name="id"/> (authored as a 1x1x1 box): the model becomes a
        /// child of the cube, so it takes the cube's size, and the cube's own renderer is hidden. A model authored long
        /// along X is turned 90° when the cube is long along Z (doors in east/west walls).
        /// </summary>
        public static GameObject Skin(GameObject box, string id, Color? accent = null, bool longAlongX = false)
        {
            if (box == null) return null;
            Color a = accent ?? Color.gray;
            GameObject go = Spawn(id, box.transform, slot => SlotFromAccent(slot, a));
            if (go == null) return null;
            Vector3 s = box.transform.localScale;
            if (longAlongX && s.z > s.x) go.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            go.isStatic = box.isStatic;
            Renderer own = box.GetComponent<Renderer>();
            if (own != null) own.enabled = false;
            return go;
        }

        /// <summary>Hides every renderer under <paramref name="root"/> except those under <paramref name="keep"/>.</summary>
        public static void HideExcept(Transform root, Transform keep, Predicate<Renderer> alsoKeep = null)
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (keep != null && r.transform.IsChildOf(keep)) continue;
                if (alsoKeep != null && alsoKeep(r)) continue;
                r.enabled = false;
            }
        }

        /// <summary>Accent and AccentDim from one colour (facility, sector, class...).</summary>
        public static Color? SlotFromAccent(string slot, Color accent)
        {
            if (slot == "Accent") return accent;
            if (slot == "AccentDim") return new Color(accent.r * 0.6f, accent.g * 0.6f, accent.b * 0.6f, 1f);
            return null;
        }

        /// <summary>Parses a model material name into its colour (exposed for tests).</summary>
        public static Color ColorForName(string materialName, Func<string, Color?> tint)
        {
            string n = Clean(materialName);
            if (n.Length >= 8 && (n.StartsWith("C_", StringComparison.Ordinal) || n.StartsWith("G_", StringComparison.Ordinal))
                && TryHex(n.Substring(2, 6), out Color c)) return c;
            Color? t = tint?.Invoke(n);
            if (t.HasValue) return t.Value;
            switch (n)
            {
                case "Skin": return new Color(0.45f, 0.52f, 0.4f);
                case "SkinDark": return new Color(0.27f, 0.31f, 0.24f);
                case "Accent": return new Color(0.95f, 0.65f, 0.2f);
                case "AccentDim": return new Color(0.57f, 0.39f, 0.12f);
                default: return new Color(0.6f, 0.6f, 0.6f);
            }
        }

        /// <summary>"G_RRGGBB" materials glow (emissive); everything else is plain lit.</summary>
        public static bool IsGlow(string materialName) => Clean(materialName).StartsWith("G_", StringComparison.Ordinal);

        private static Material MaterialFor(string name, Func<string, Color?> tint)
        {
            Color32 key = ColorForName(name, tint);
            bool glow = IsGlow(name);
            Dictionary<Color32, Material> cache = glow ? Glows : Materials;
            if (cache.TryGetValue(key, out Material m) && m != null) return m;
            m = glow ? RuntimeMaterials.CreateEmissive(key) : RuntimeMaterials.CreateLit(key);
            m.name = "Model " + Clean(name);
            cache[key] = m;
            return m;
        }

        /// <summary>Importers may add " (Instance)", ".001" or a file prefix; keep the part we wrote.</summary>
        private static string Clean(string n)
        {
            if (string.IsNullOrEmpty(n)) return string.Empty;
            int cut = n.IndexOfAny(new[] { ' ', '.' });
            if (cut > 0) n = n.Substring(0, cut);
            return n;
        }

        private static bool TryHex(string hex, out Color c)
        {
            c = default;
            if (!int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int v)) return false;
            c = new Color(((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f, 1f);
            return true;
        }

        private static GameObject Load(string id)
        {
            if (!Enabled || string.IsNullOrEmpty(id)) return null;
            if (Prefabs.TryGetValue(id, out GameObject cached)) return cached;
            GameObject prefab = Resources.Load<GameObject>(Folder + id);
            if (prefab == null) GameLog.Warn("Models", $"Model '{id}' not found in Resources/{Folder}; using the placeholder.");
            Prefabs[id] = prefab;
            return prefab;
        }
    }
}
