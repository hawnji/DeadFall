using System;
using Deadfall.Core;
using Deadfall.Rules.World;
using UnityEngine;

namespace Deadfall.World
{
    /// <summary>
    /// The control that opens a <see cref="DoorLock.Switch"/> or <see cref="DoorLock.Power"/> door: a security switch
    /// on a wall, or a power relay cabinet. The map session decides what using it does and keeps peers in step.
    /// </summary>
    public sealed class DoorSwitch : MonoBehaviour, IInteractable
    {
        private Renderer _lamp;
        private Material _lampMat;
        private Transform _lever;

        public int ConnectionIndex { get; private set; }
        public DoorLock Kind { get; private set; }
        public bool Used { get; private set; }

        public static Func<DoorSwitch, GameObject, string> PromptFor;
        public static Action<DoorSwitch, GameObject> Use;

        public static DoorSwitch Create(int connection, DoorLock kind, Vector3 floorPoint, float yaw, Material body, Transform parent)
        {
            var root = new GameObject(kind == DoorLock.Power ? $"PowerRelay #{connection}" : $"SecuritySwitch #{connection}");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(floorPoint, Quaternion.Euler(0f, yaw, 0f));
            var s = root.AddComponent<DoorSwitch>();
            s.ConnectionIndex = connection;
            s.Kind = kind;
            if (kind == DoorLock.Power)
            {
                Part(root.transform, new Vector3(0f, 0.8f, 0f), new Vector3(1.3f, 1.6f, 0.7f), body, true);
                Part(root.transform, new Vector3(0f, 1.62f, 0f), new Vector3(1.4f, 0.06f, 0.8f), body, false);
                s._lamp = Part(root.transform, new Vector3(0f, 1.2f, 0.36f), new Vector3(0.5f, 0.3f, 0.04f), null, false).GetComponent<Renderer>();
            }
            else
            {
                Part(root.transform, new Vector3(0f, 1.3f, 0f), new Vector3(0.7f, 0.9f, 0.2f), body, true);
                s._lever = Part(root.transform, new Vector3(0f, 1.4f, 0.16f), new Vector3(0.1f, 0.45f, 0.1f), body, false).transform;
                s._lever.localRotation = Quaternion.Euler(35f, 0f, 0f);
                s._lamp = Part(root.transform, new Vector3(0f, 1.0f, 0.11f), new Vector3(0.18f, 0.12f, 0.04f), null, false).GetComponent<Renderer>();
            }
            s._lampMat = RuntimeMaterials.CreateLit(new Color(0.9f, 0.2f, 0.15f));
            s._lamp.sharedMaterial = s._lampMat;
            return s;
        }

        private static GameObject Part(Transform parent, Vector3 local, Vector3 size, Material m, bool collider)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = size;
            if (!collider) UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            if (m != null) go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        private void OnDestroy() { if (_lampMat != null) Destroy(_lampMat); }

        /// <summary>Marks it used (lamp green, lever down). Also applied on clients from the host's state.</summary>
        public void SetUsed(bool used)
        {
            Used = used;
            if (_lampMat != null) _lampMat.color = used ? new Color(0.25f, 0.9f, 0.35f) : new Color(0.9f, 0.2f, 0.15f);
            if (_lever != null) _lever.localRotation = Quaternion.Euler(used ? -35f : 35f, 0f, 0f);
        }

        public string InteractPrompt(GameObject player) => PromptFor != null ? PromptFor(this, player) : string.Empty;
        public bool CanInteract(GameObject player) => !Used;
        public void Interact(GameObject player) => Use?.Invoke(this, player);
    }
}
