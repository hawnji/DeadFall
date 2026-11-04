using Deadfall.Core;
using Deadfall.Rules.Engineering;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// One generator of the map's power grid. Pressing F on a tripped one asks the <see cref="PowerGrid"/> (host) to
    /// start the restart channel; the grid syncs its state to every peer by <see cref="Index"/>.
    /// </summary>
    public sealed class GridGenerator : MonoBehaviour, IInteractable
    {
        private Material _lamp;
        private Transform _fan;
        private Light _glow;

        public int Index { get; private set; }
        public bool Online { get; private set; } = true;
        public bool Restarting { get; private set; }
        public float Progress01 { get; private set; }

        public static GridGenerator Create(int index, Vector3 position, float yaw, Transform parent)
        {
            var root = new GameObject($"Grid generator {index}");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            var g = root.AddComponent<GridGenerator>();
            g.Index = index;
            Part(root.transform, new Vector3(0f, 0.15f, 0f), new Vector3(1.9f, 0.3f, 1.2f), new Color(0.2f, 0.2f, 0.22f), true);   // skid
            Part(root.transform, new Vector3(0f, 0.85f, 0f), new Vector3(1.6f, 1.1f, 1f), new Color(0.75f, 0.6f, 0.15f), true);   // housing
            Part(root.transform, new Vector3(0f, 1.45f, 0f), new Vector3(1.4f, 0.1f, 0.85f), new Color(0.12f, 0.12f, 0.13f), false); // stripe
            Part(root.transform, new Vector3(0.55f, 1.75f, -0.25f), new Vector3(0.18f, 0.6f, 0.18f), new Color(0.25f, 0.25f, 0.27f), false); // exhaust
            g._fan = Part(root.transform, new Vector3(-0.81f, 0.9f, 0f), new Vector3(0.04f, 0.7f, 0.12f), new Color(0.3f, 0.3f, 0.32f), false).transform;
            Renderer lamp = Part(root.transform, new Vector3(0.45f, 1.1f, 0.51f), new Vector3(0.3f, 0.3f, 0.04f), Color.green, false);
            g._lamp = RuntimeMaterials.CreateEmissive(Color.green, 1.5f);
            lamp.sharedMaterial = g._lamp;
            var lg = new GameObject("Glow");
            lg.transform.SetParent(root.transform, false);
            lg.transform.localPosition = new Vector3(0.45f, 1.2f, 0.8f);
            g._glow = lg.AddComponent<Light>();
            g._glow.type = LightType.Point;
            g._glow.range = 4f;
            g._glow.intensity = 0.8f;
            g.Apply();
            return g;
        }

        private static Renderer Part(Transform parent, Vector3 local, Vector3 size, Color color, bool solid)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = RuntimeMaterials.CreateLit(color);
            if (!solid) Destroy(go.GetComponent<Collider>());
            return go.GetComponent<Renderer>();
        }

        public void SetState(bool online, bool restarting, float progress01)
        {
            if (online == Online && restarting == Restarting && Mathf.Abs(progress01 - Progress01) < 0.001f) return;
            Online = online;
            Restarting = restarting && !online;
            Progress01 = Mathf.Clamp01(progress01);
            Apply();
        }

        private void Apply()
        {
            Color c = Online ? new Color(0.3f, 1f, 0.35f) : Restarting ? Color.Lerp(Color.red, Color.yellow, Progress01) : Color.red;
            RuntimeMaterials.SetColor(_lamp, c);
            if (_glow != null) _glow.color = c;
        }

        private void Update()
        {
            if (Online && _fan != null) _fan.localRotation = Quaternion.Euler(Time.time * 720f % 360f, 0f, 0f);
            if (!Online && !Restarting && _glow != null) _glow.intensity = Mathf.Repeat(Time.time, 1f) < 0.5f ? 1.2f : 0.2f; // blinking fault
            else if (_glow != null) _glow.intensity = 0.8f;
        }

        public string InteractPrompt(GameObject player)
        {
            if (Online) return "Grid generator: online";
            return Restarting ? $"Restarting grid generator {Progress01 * 100f:0}% (stay close)" : $"Restart grid generator ({PowerGridRules.RestartSeconds:0} s)";
        }

        public bool CanInteract(GameObject player) => !Online && !Restarting;

        public void Interact(GameObject player) => PowerGrid.Current?.RequestRestart(Index);
    }
}
