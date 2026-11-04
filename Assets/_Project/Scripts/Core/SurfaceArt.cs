using System.Collections.Generic;
using UnityEngine;

namespace Deadfall.Core
{
    /// <summary>What a surface is made of; picks its texture set (Resources/Textures/&lt;name&gt;_albedo/_normal).</summary>
    public enum Surface : byte { None, Concrete, Plaster, Metal, Wood, Dirt }

    /// <summary>
    /// Textured surfaces for the code-built world (maps, FOB, sandbox). Textures come from
    /// <c>Tools/Textures/make_textures.py</c>: near-white detail that the object's own colour tints, so every existing
    /// colour choice still reads. Boxes get a cube mesh whose UVs are in metres, so a 40 m wall repeats its texture
    /// instead of stretching one copy (meshes are shared per size; colliders and the NavMesh are untouched).
    /// </summary>
    public static class SurfaceArt
    {
        /// <summary>Off: flat colours as before (debug comparison, or if textures are missing).</summary>
        public static bool Enabled = true;

        private struct Spec
        {
            public string Name;
            public float Tile, Smoothness, Bump;
            public Spec(string name, float tile, float smoothness, float bump) { Name = name; Tile = tile; Smoothness = smoothness; Bump = bump; }
        }

        private static Spec SpecOf(Surface s) => s switch
        {
            Surface.Concrete => new Spec("concrete", 2.5f, 0.18f, 0.8f),
            Surface.Plaster => new Spec("plaster", 3f, 0.12f, 0.5f),
            Surface.Metal => new Spec("metal", 1.6f, 0.45f, 1f),
            Surface.Wood => new Spec("wood", 1.4f, 0.22f, 0.9f),
            Surface.Dirt => new Spec("dirt", 4f, 0.05f, 0.9f),
            _ => default,
        };

        private static readonly Dictionary<Surface, Texture2D[]> Textures = new Dictionary<Surface, Texture2D[]>();
        private static readonly Dictionary<long, Material> Materials = new Dictionary<long, Material>();
        private static readonly Dictionary<Vector3Int, Mesh> Meshes = new Dictionary<Vector3Int, Mesh>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Textures.Clear();
            Materials.Clear();
            Meshes.Clear();
            Enabled = true;
        }

        /// <summary>A shared textured material for <paramref name="surface"/> tinted <paramref name="tint"/>, or null (use flat).</summary>
        public static Material Material(Surface surface, Color tint)
        {
            if (!Enabled || surface == Surface.None) return null;
            Color32 c = tint;
            long key = ((long)surface << 32) | ((long)c.r << 24) | ((long)c.g << 16) | ((long)c.b << 8) | c.a;
            if (Materials.TryGetValue(key, out Material m) && m != null) return m;
            Texture2D[] tex = TexturesOf(surface);
            if (tex[0] == null) return null;
            Spec spec = SpecOf(surface);
            m = RuntimeMaterials.CreateSurface(tex[0], tex[1], tint, spec.Tile, spec.Smoothness, spec.Bump);
            m.name = $"Surface_{spec.Name}_{ColorUtility.ToHtmlStringRGB(tint)}";
            Materials[key] = m;
            return m;
        }

        private static Texture2D[] TexturesOf(Surface s)
        {
            if (Textures.TryGetValue(s, out Texture2D[] t)) return t;
            string n = SpecOf(s).Name;
            t = new[] { Resources.Load<Texture2D>($"Textures/{n}_albedo"), Resources.Load<Texture2D>($"Textures/{n}_normal") };
            Textures[s] = t;
            return t;
        }

        /// <summary>
        /// Textures a primitive cube: swaps in a metre-UV cube mesh for its scale and a textured material. Returns false
        /// (and leaves the box alone) when the surface is None, art is off, or textures are missing.
        /// </summary>
        public static bool Apply(GameObject box, Surface surface, Color tint)
        {
            if (box == null) return false;
            Material m = Material(surface, tint);
            if (m == null) return false;
            var filter = box.GetComponent<MeshFilter>();
            var renderer = box.GetComponent<Renderer>();
            if (filter == null || renderer == null) return false;
            filter.sharedMesh = BoxMesh(box.transform.lossyScale);
            renderer.sharedMaterial = m;
            return true;
        }

        /// <summary><see cref="Apply"/> with the surface guessed from the object's name and colour.</summary>
        public static bool ApplyAuto(GameObject box, Color tint) => box != null && Apply(box, Guess(box.name, tint), tint);

        private static readonly string[] Flat = { "glow", "screen", "header", "lane", "hazard", "mark", "lamp", "light", "sign", "stripe", "zone", "beacon", "pad" };
        private static readonly string[] Dirt = { "ground", "pit", "sand", "dirt", "earth" };
        private static readonly string[] Wood = { "crate", "board", "plank", "wood", "barricade", "pallet", "table", "bench" };
        private static readonly string[] Metal = { "door", "console", "rack", "beam", "frame", "panel", "trim", "barrel", "rail", "pipe", "vent",
                                                   "locker", "fixture", "transport", "shelf", "threshold", "switch", "relay", "generator", "post", "girder", "truck", "car", "container", "duct" };
        private static readonly string[] Plaster = { "wall", "lining", "partition", "side" };
        private static readonly string[] Concrete = { "floor", "ceiling", "roof", "corridor", "column", "pillar", "cover", "lintel", "tunnel", "step", "ramp",
                                                      "stair", "block", "box", "platform", "curb", "slab", "foundation" };

        /// <summary>Best guess from a box's name ("WallN", "Crates1", "Floor") and colour; None for markings and lights.</summary>
        public static Surface Guess(string name, Color color)
        {
            string n = string.IsNullOrEmpty(name) ? string.Empty : name.ToLowerInvariant();
            if (Has(n, Flat)) return Surface.None;
            if (Has(n, Dirt)) return Surface.Dirt;
            if (Has(n, Wood)) return Surface.Wood;
            if (Has(n, Metal)) return Surface.Metal;
            if (Has(n, Plaster)) return Surface.Plaster;
            if (Has(n, Concrete)) return Surface.Concrete;
            // Very saturated colours are signage or paint: keep them flat. Browns read as wood.
            Color.RGBToHSV(color, out float h, out float sat, out float v);
            if (sat > 0.6f && v > 0.5f) return Surface.None;
            if (h > 0.04f && h < 0.13f && sat > 0.3f) return Surface.Wood;
            return Surface.Concrete;
        }

        private static bool Has(string n, string[] words)
        {
            foreach (string w in words) if (n.Contains(w)) return true;
            return false;
        }

        // ---------------------------------------------------------------------------------- box mesh

        /// <summary>A unit cube (same shape as Unity's) whose UVs are in metres for a box of this world size.</summary>
        public static Mesh BoxMesh(Vector3 size)
        {
            size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
            var key = new Vector3Int(Mathf.RoundToInt(size.x * 100f), Mathf.RoundToInt(size.y * 100f), Mathf.RoundToInt(size.z * 100f));
            if (Meshes.TryGetValue(key, out Mesh mesh) && mesh != null) return mesh;

            var verts = new Vector3[24];
            var normals = new Vector3[24];
            var uvs = new Vector2[24];
            var tris = new int[36];
            // Per face: normal, then u and v axes with cross(u, v) = -normal (clockwise front faces in Unity).
            Vector3[] n = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            Vector3[] u = { Vector3.forward, Vector3.back, Vector3.right, Vector3.right, Vector3.left, Vector3.right };
            Vector3[] v = { Vector3.up, Vector3.up, Vector3.forward, Vector3.back, Vector3.up, Vector3.up };
            for (int f = 0; f < 6; f++)
            {
                float su = Vector3.Scale(u[f], size).magnitude, sv = Vector3.Scale(v[f], size).magnitude;
                int b = f * 4;
                // Bottom-left, top-left, top-right, bottom-right as seen from outside.
                Vector2[] corners = { new Vector2(-1, -1), new Vector2(-1, 1), new Vector2(1, 1), new Vector2(1, -1) };
                for (int k = 0; k < 4; k++)
                {
                    Vector2 c = corners[k];
                    verts[b + k] = (n[f] + u[f] * c.x + v[f] * c.y) * 0.5f;
                    normals[b + k] = n[f];
                    uvs[b + k] = new Vector2((c.x + 1f) * 0.5f * su, (c.y + 1f) * 0.5f * sv);
                }
                int t = f * 6;
                tris[t] = b; tris[t + 1] = b + 1; tris[t + 2] = b + 2;
                tris[t + 3] = b; tris[t + 4] = b + 2; tris[t + 5] = b + 3;
            }
            mesh = new Mesh { name = $"Box_{key.x}x{key.y}x{key.z}" };
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            Meshes[key] = mesh;
            return mesh;
        }
    }
}
