using Deadfall.Core;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>The four power-ups a kill can drop.</summary>
    public enum PowerUpKind : byte { MaxAmmo, DoubleCredits, InstaKill, Nuke }

    /// <summary>
    /// A power-up floating above the floor: a glowing, spinning token for its kind with a light under it. Blinks
    /// before it expires. Pure visuals on every peer; <see cref="PowerUpDirector"/> owns pickups and timing.
    /// </summary>
    public sealed class PowerUpDrop : MonoBehaviour
    {
        public ushort Id { get; private set; }
        public PowerUpKind Kind { get; private set; }
        public float ExpiresAt { get; set; }

        private Transform _spin;
        private Renderer[] _renderers;
        private Vector3 _base;

        public static Color ColorOf(PowerUpKind k) => k switch
        {
            PowerUpKind.MaxAmmo => new Color(1f, 0.78f, 0.25f),
            PowerUpKind.DoubleCredits => new Color(0.35f, 1f, 0.45f),
            PowerUpKind.InstaKill => new Color(0.95f, 0.95f, 1f),
            _ => new Color(1f, 0.35f, 0.2f),
        };

        public static string NameOf(PowerUpKind k) => k switch
        {
            PowerUpKind.MaxAmmo => "MAX AMMO",
            PowerUpKind.DoubleCredits => "DOUBLE CREDITS",
            PowerUpKind.InstaKill => "INSTA-KILL",
            _ => "NUKE",
        };

        public static PowerUpDrop Create(ushort id, PowerUpKind kind, Vector3 floor, float expiresAt)
        {
            var root = new GameObject($"PowerUp {NameOf(kind)} #{id}");
            root.transform.position = floor;
            var d = root.AddComponent<PowerUpDrop>();
            d.Id = id;
            d.Kind = kind;
            d.ExpiresAt = expiresAt;
            d._base = floor;
            d._spin = new GameObject("Token").transform;
            d._spin.SetParent(root.transform, false);
            d._spin.localPosition = Vector3.up * 1f;
            Color c = ColorOf(kind);
            Material glow = RuntimeMaterials.CreateEmissive(c, 2.2f);
            Material dark = RuntimeMaterials.CreateLit(new Color(0.08f, 0.08f, 0.09f));
            switch (kind)
            {
                case PowerUpKind.MaxAmmo:
                    // Three rounds standing in a row.
                    for (int i = -1; i <= 1; i++)
                    {
                        Part(d._spin, PrimitiveType.Cylinder, new Vector3(i * 0.14f, 0f, 0f), new Vector3(0.1f, 0.18f, 0.1f), glow);
                        Part(d._spin, PrimitiveType.Sphere, new Vector3(i * 0.14f, 0.18f, 0f), new Vector3(0.1f, 0.12f, 0.1f), glow);
                    }
                    break;
                case PowerUpKind.DoubleCredits:
                    // Two coins side by side, facing out.
                    for (int i = -1; i <= 1; i += 2)
                    {
                        GameObject coin = Part(d._spin, PrimitiveType.Cylinder, new Vector3(i * 0.15f, 0f, 0f), new Vector3(0.26f, 0.025f, 0.26f), glow);
                        coin.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    }
                    break;
                case PowerUpKind.InstaKill:
                    // A skull: round cranium, jaw, dark eye sockets.
                    Part(d._spin, PrimitiveType.Sphere, Vector3.zero, new Vector3(0.36f, 0.34f, 0.34f), glow);
                    Part(d._spin, PrimitiveType.Cube, new Vector3(0f, -0.17f, 0.04f), new Vector3(0.2f, 0.12f, 0.2f), glow);
                    Part(d._spin, PrimitiveType.Sphere, new Vector3(-0.08f, 0.02f, 0.15f), new Vector3(0.1f, 0.1f, 0.06f), dark);
                    Part(d._spin, PrimitiveType.Sphere, new Vector3(0.08f, 0.02f, 0.15f), new Vector3(0.1f, 0.1f, 0.06f), dark);
                    Part(d._spin, PrimitiveType.Cube, new Vector3(0f, -0.07f, 0.16f), new Vector3(0.04f, 0.06f, 0.03f), dark);
                    break;
                default:
                    // A bomb: body, nose, tail fins.
                    Part(d._spin, PrimitiveType.Sphere, Vector3.zero, new Vector3(0.22f, 0.38f, 0.22f), glow);
                    Part(d._spin, PrimitiveType.Cube, new Vector3(0f, -0.24f, 0f), new Vector3(0.3f, 0.08f, 0.03f), dark);
                    Part(d._spin, PrimitiveType.Cube, new Vector3(0f, -0.24f, 0f), new Vector3(0.03f, 0.08f, 0.3f), dark);
                    break;
            }
            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = Vector3.up * 0.7f;
            Light l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.range = 4f;
            l.intensity = 2f;
            l.shadows = LightShadows.None;
            d._renderers = root.GetComponentsInChildren<Renderer>();
            return d;
        }

        private static GameObject Part(Transform parent, PrimitiveType type, Vector3 local, Vector3 scale, Material m)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = scale;
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        private void Update()
        {
            float t = Time.time;
            _spin.localRotation = Quaternion.Euler(0f, t * 120f, 0f);
            _spin.localPosition = Vector3.up * (1f + Mathf.Sin(t * 2.4f) * 0.12f);
            // Blink faster as it runs out.
            float left = ExpiresAt - t;
            bool show = left > PowerUpDirector.BlinkSeconds || Mathf.Repeat(t * (left < 3f ? 8f : 4f), 1f) < 0.6f;
            foreach (Renderer r in _renderers) if (r != null) r.enabled = show;
        }
    }
}
