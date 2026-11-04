using System;
using Deadfall.Core;
using Deadfall.Rules.World;
using UnityEngine;
using UnityEngine.AI;

namespace Deadfall.World
{
    /// <summary>
    /// A locked door: between sectors, or into a side branch. Closed, it blocks players and bullets and carves the
    /// NavMesh so enemies path around it (T-32). Opening slides it into the ceiling and removes the carve. What opens
    /// it (<see cref="Lock"/>: credits at the door, a switch or a power relay elsewhere) is handled by the map
    /// session, which also opens it on every peer. Interact (F) at the door asks the session to buy it.
    /// </summary>
    public sealed class SectorDoor : MonoBehaviour, IInteractable
    {
        public static Func<SectorDoor, GameObject, string> PromptFor;
        public static Action<SectorDoor, GameObject> Use;

        public DoorLock Lock { get; set; }
        public int Cost { get; set; }
        /// <summary>A side door into a branch rather than a door between sectors.</summary>
        public bool IsSideDoor => Sector <= 0;

        public string InteractPrompt(GameObject player) => !IsOpen && PromptFor != null ? PromptFor(this, player) : string.Empty;
        public bool CanInteract(GameObject player) => !IsOpen && Lock == DoorLock.Credits;
        public void Interact(GameObject player) => Use?.Invoke(this, player);

        private const float OpenSeconds = 1.4f;

        private Transform _slab;
        private NavMeshObstacle _obstacle;
        private Collider _collider;
        private float _height;
        private float _openT; // 0 closed .. 1 open
        private bool _opening;

        public int Sector { get; private set; }
        public int ConnectionIndex { get; set; }
        public bool IsOpen => _opening || _openT > 0f;

        public static SectorDoor Create(Vector3 floorCenter, bool alongX, float width, float height, int sector, Material material, Transform parent)
        {
            var root = new GameObject($"SectorDoor S{sector}");
            root.transform.SetParent(parent, false);
            root.transform.position = floorCenter;
            var door = root.AddComponent<SectorDoor>();
            door.Sector = sector;
            door._height = height;

            GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = "Slab";
            slab.transform.SetParent(root.transform, false);
            slab.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            slab.transform.localScale = alongX ? new Vector3(width, height, 0.25f) : new Vector3(0.25f, height, width);
            slab.GetComponent<Renderer>().sharedMaterial = material;
            door._slab = slab.transform;
            ModelLibrary.Skin(slab, "prop_door", material.color, longAlongX: true);
            door._collider = slab.GetComponent<Collider>();

            door._obstacle = root.AddComponent<NavMeshObstacle>();
            door._obstacle.shape = NavMeshObstacleShape.Box;
            door._obstacle.center = new Vector3(0f, height * 0.5f, 0f);
            door._obstacle.size = alongX ? new Vector3(width, height, 0.6f) : new Vector3(0.6f, height, width);
            door._obstacle.carving = true;
            door._obstacle.carveOnlyStationary = true;
            InteractGlow.Add(door.gameObject, InteractGlow.Objectives, () => door.CanInteract(null));
            return door;
        }

        public void Open()
        {
            if (IsOpen) return;
            _opening = true;
            _obstacle.enabled = false;
        }

        /// <summary>Closes at once (run restart).</summary>
        public void CloseImmediate()
        {
            _opening = false;
            _openT = 0f;
            _slab.localPosition = new Vector3(0f, _height * 0.5f, 0f);
            _slab.gameObject.SetActive(true);
            _collider.enabled = true;
            _obstacle.enabled = true;
        }

        private void Update()
        {
            if (!_opening) return;
            _openT = Mathf.MoveTowards(_openT, 1f, Time.deltaTime / OpenSeconds);
            float eased = _openT * _openT * (3f - 2f * _openT);
            _slab.localPosition = new Vector3(0f, _height * 0.5f + eased * (_height - 0.15f), 0f);
            if (_openT >= 1f)
            {
                _opening = false;
                _slab.gameObject.SetActive(false); // tucked in the ceiling
            }
        }
    }
}
