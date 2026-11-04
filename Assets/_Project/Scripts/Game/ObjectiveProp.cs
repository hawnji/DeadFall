using Deadfall.Core;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>What an objective prop is and how it's used.</summary>
    public enum ObjectivePropKind : byte
    {
        /// <summary>Channel: stand near it after pressing F (RestorePower).</summary>
        Generator,
        /// <summary>Channel (RetrieveDataCore).</summary>
        DataTerminal,
        /// <summary>Instant pickup (RecoverSamples).</summary>
        SampleCase,
        /// <summary>Instant pickup; the squad then has the keycard (Keycard step 1).</summary>
        Keycard,
        /// <summary>Instant, needs the keycard (Keycard step 2): ammo for everyone plus credits.</summary>
        SecurityLocker,
        /// <summary>Channel (LiftLockdown).</summary>
        LockdownTerminal,
        /// <summary>Channel (ShutDownBreach): the failing containment system's console.</summary>
        ContainmentConsole,
        /// <summary>Channel (SealContainers): a cryo container the infected try to smash until it is sealed.</summary>
        CryoContainer,
        /// <summary>Channel (VentReactor): a coolant valve.</summary>
        ReactorValve,
    }

    /// <summary>
    /// A placeholder objective object built from primitives. Pressing F asks the <see cref="ObjectiveDirector"/>
    /// (host-authoritative) to use it; the director syncs its progress and state to every peer by <see cref="Id"/>.
    /// </summary>
    public sealed class ObjectiveProp : MonoBehaviour, IInteractable
    {
        private Renderer _light;
        private Material _lightMaterial;

        public ushort Id { get; private set; }
        public byte ObjectiveId { get; private set; }
        public ObjectivePropKind Kind { get; private set; }
        /// <summary>Channel progress 0..1 (synced).</summary>
        public float Progress01 { get; private set; }
        /// <summary>Channel running (someone pressed F).</summary>
        public bool Started { get; private set; }
        public bool Done { get; private set; }
        /// <summary>Integrity 0..1 of a cryo container (synced; 1 for everything else).</summary>
        public float Health01 { get; private set; } = 1f;
        public const ObjectivePropKind LastKind = ObjectivePropKind.ReactorValve;

        public bool IsChannel => Kind == ObjectivePropKind.Generator || Kind == ObjectivePropKind.DataTerminal || Kind == ObjectivePropKind.LockdownTerminal
                                 || Kind == ObjectivePropKind.ContainmentConsole || Kind == ObjectivePropKind.CryoContainer || Kind == ObjectivePropKind.ReactorValve;

        public static ObjectiveProp Create(ushort id, byte objectiveId, ObjectivePropKind kind, Vector3 position, Transform parent)
        {
            var root = new GameObject($"Objective {kind} #{id}");
            root.transform.SetParent(parent, false);
            root.transform.position = position;
            var p = root.AddComponent<ObjectiveProp>();
            p.Id = id;
            p.ObjectiveId = objectiveId;
            p.Kind = kind;
            switch (kind)
            {
                case ObjectivePropKind.Generator:
                    Part(root.transform, new Vector3(0f, 0.6f, 0f), new Vector3(1.4f, 1.2f, 0.9f), new Color(0.35f, 0.38f, 0.3f), true);
                    Part(root.transform, new Vector3(0f, 1.3f, 0f), new Vector3(0.25f, 0.4f, 0.25f), new Color(0.2f, 0.2f, 0.2f), false);
                    p._light = Part(root.transform, new Vector3(0f, 0.9f, 0.46f), new Vector3(0.3f, 0.3f, 0.04f), Color.red, false);
                    break;
                case ObjectivePropKind.DataTerminal:
                case ObjectivePropKind.LockdownTerminal:
                    Part(root.transform, new Vector3(0f, 0.55f, 0f), new Vector3(0.8f, 1.1f, 0.5f), new Color(0.25f, 0.27f, 0.32f), true);
                    p._light = Part(root.transform, new Vector3(0f, 1.2f, 0.1f), new Vector3(0.7f, 0.45f, 0.05f),
                        kind == ObjectivePropKind.LockdownTerminal ? new Color(1f, 0.6f, 0.1f) : new Color(0.2f, 0.6f, 1f), false);
                    break;
                case ObjectivePropKind.ContainmentConsole:
                    Part(root.transform, new Vector3(0f, 0.5f, 0f), new Vector3(1.1f, 1f, 0.7f), new Color(0.22f, 0.26f, 0.3f), true);
                    Part(root.transform, new Vector3(0f, 1.05f, -0.15f), new Vector3(1.1f, 0.12f, 0.4f), new Color(0.85f, 0.65f, 0.1f), false); // hazard band
                    p._light = Part(root.transform, new Vector3(0f, 1.3f, 0.05f), new Vector3(0.9f, 0.4f, 0.05f), new Color(0.4f, 0.85f, 1f), false);
                    break;
                case ObjectivePropKind.CryoContainer:
                    Part(root.transform, new Vector3(0f, 0.15f, 0f), new Vector3(1f, 0.3f, 1f), new Color(0.25f, 0.27f, 0.3f), true);   // base
                    Part(root.transform, new Vector3(0f, 1.05f, 0f), new Vector3(0.8f, 1.5f, 0.8f), new Color(0.55f, 0.75f, 0.85f), true); // tank
                    Part(root.transform, new Vector3(0f, 1.9f, 0f), new Vector3(0.9f, 0.2f, 0.9f), new Color(0.25f, 0.27f, 0.3f), false); // cap
                    p._light = Part(root.transform, new Vector3(0f, 1.05f, 0.41f), new Vector3(0.5f, 0.9f, 0.03f), new Color(0.35f, 0.9f, 1f), false);
                    break;
                case ObjectivePropKind.ReactorValve:
                    Part(root.transform, new Vector3(0f, 0.6f, 0f), new Vector3(0.35f, 1.2f, 0.35f), new Color(0.4f, 0.42f, 0.45f), true);   // pipe
                    Part(root.transform, new Vector3(0f, 1.25f, 0.25f), new Vector3(0.8f, 0.08f, 0.08f), new Color(0.75f, 0.15f, 0.1f), false); // wheel
                    Part(root.transform, new Vector3(0f, 1.25f, 0.25f), new Vector3(0.08f, 0.8f, 0.08f), new Color(0.75f, 0.15f, 0.1f), false);
                    p._light = Part(root.transform, new Vector3(0f, 1.6f, 0f), new Vector3(0.2f, 0.2f, 0.2f), new Color(1f, 0.4f, 0.1f), false);
                    break;
                case ObjectivePropKind.SampleCase:
                    Part(root.transform, new Vector3(0f, 0.2f, 0f), new Vector3(0.5f, 0.4f, 0.35f), new Color(0.9f, 0.9f, 0.92f), true);
                    p._light = Part(root.transform, new Vector3(0f, 0.42f, 0f), new Vector3(0.2f, 0.05f, 0.2f), new Color(0.3f, 1f, 0.6f), false);
                    break;
                case ObjectivePropKind.Keycard:
                    Part(root.transform, new Vector3(0f, 0.8f, 0f), new Vector3(0.3f, 0.02f, 0.2f), new Color(1f, 0.85f, 0.1f), false);
                    Part(root.transform, new Vector3(0f, 0.38f, 0f), new Vector3(0.6f, 0.76f, 0.6f), new Color(0.3f, 0.3f, 0.32f), true); // pedestal
                    break;
                case ObjectivePropKind.SecurityLocker:
                    Part(root.transform, new Vector3(0f, 1f, 0f), new Vector3(1.2f, 2f, 0.6f), new Color(0.3f, 0.34f, 0.4f), true);
                    p._light = Part(root.transform, new Vector3(0.45f, 1.1f, 0.31f), new Vector3(0.12f, 0.2f, 0.02f), new Color(1f, 0.85f, 0.1f), false);
                    break;
            }
            if (p._light != null)
            {
                p._lightMaterial = RuntimeMaterials.CreateLit(p._light.sharedMaterial.color);
                p._light.sharedMaterial = p._lightMaterial;
            }
            InteractGlow.Add(root, InteractGlow.Objectives, () => !p.Done);
            return p;
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

        public void SetHealth01(float value) => Health01 = Mathf.Clamp01(value);

        public void SetState(float progress01, bool started, bool done)
        {
            Progress01 = Mathf.Clamp01(progress01);
            Started = started;
            if (done && !Done)
            {
                Done = true;
                if (Kind == ObjectivePropKind.SampleCase || Kind == ObjectivePropKind.Keycard) gameObject.SetActive(false);
            }
            if (_lightMaterial != null)
                RuntimeMaterials.SetColor(_lightMaterial, Done ? new Color(0.2f, 1f, 0.3f) : Started ? Color.Lerp(Color.red, Color.yellow, Progress01) : _lightMaterial.color);
        }

        public string InteractPrompt(GameObject player)
        {
            if (Done) return string.Empty;
            ObjectiveDirector d = ObjectiveDirector.Current;
            switch (Kind)
            {
                case ObjectivePropKind.Generator: return Started ? $"Restarting generator {Progress01 * 100f:0}% (stay close)" : "Restart generator";
                case ObjectivePropKind.DataTerminal: return Started ? $"Downloading data core {Progress01 * 100f:0}% (stay close)" : "Download data core";
                case ObjectivePropKind.LockdownTerminal: return Started ? $"Hacking {Progress01 * 100f:0}% (stay close)" : "Hack terminal to lift the lockdown";
                case ObjectivePropKind.ContainmentConsole: return Started ? $"Shutting down {Progress01 * 100f:0}% (stay close)" : "Shut down the containment system";
                case ObjectivePropKind.CryoContainer:
                    return Started ? $"Sealing cryo container {Progress01 * 100f:0}% (integrity {Health01 * 100f:0}%, stay close)" : $"Seal cryo container (integrity {Health01 * 100f:0}%)";
                case ObjectivePropKind.ReactorValve: return Started ? $"Opening coolant valve {Progress01 * 100f:0}% (stay close)" : "Open coolant valve";
                case ObjectivePropKind.SampleCase: return "Take sample case";
                case ObjectivePropKind.Keycard: return "Take keycard";
                case ObjectivePropKind.SecurityLocker: return d != null && d.HasKeycard ? "Open security locker" : "Security locker (needs the keycard)";
            }
            return string.Empty;
        }

        public bool CanInteract(GameObject player)
        {
            if (Done || (IsChannel && Started)) return false;
            if (Kind == ObjectivePropKind.SecurityLocker) return ObjectiveDirector.Current != null && ObjectiveDirector.Current.HasKeycard;
            return true;
        }

        public void Interact(GameObject player) => ObjectiveDirector.Current?.RequestUse(this);
    }
}
