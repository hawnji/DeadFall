using Deadfall.Rules.Common;
using System;
using System.Collections;
using Deadfall.Combat;
using Deadfall.Core;
using UnityEngine;
using UnityEngine.AI;

namespace Deadfall.World
{
    /// <summary>
    /// A boarded opening between a room and the sealed closet behind it, where wave enemies appear. While any board
    /// is up it carves the NavMesh, so enemies in the closet can't path in and attack it instead (it is an
    /// <see cref="IBlocker"/>); each <see cref="HealthPerBoard"/> of damage tears off a board. Players repair one
    /// board per use (F, repeating while held, and usable from anywhere in front of the opening without aiming at it)
    /// and the map session pays credits for it. An invisible wall on the Ignore Raycast layer keeps
    /// players out of the closet even with every board gone; bullets and enemies pass it.
    /// </summary>
    public sealed class BreachBarricade : MonoBehaviour, IBlocker, IDamageable, IHoldInteractable, INearbyInteractable
    {
        public const int MaxBoards = 5;
        public const float HealthPerBoard = 60f;
        public const float OpeningWidth = 2.2f;
        public const float OpeningHeight = 2.5f;

        private readonly GameObject[] _boards = new GameObject[MaxBoards];
        private NavMeshObstacle _obstacle;
        private float _health = MaxBoards * HealthPerBoard;
        private Vector3 _along;

        public int Index { get; private set; }
        public int Room { get; private set; }
        public int Boards { get; private set; } = MaxBoards;
        /// <summary>True where enemies are simulated (host/offline): only then is it a target.</summary>
        public bool Authority { get; set; } = true;

        /// <summary>Where enemies stand while breaking in (closet side).</summary>
        public Vector3 ClosetSide { get; private set; }

        /// <summary>Board count changed by enemy damage (host/offline). The map session syncs it.</summary>
        public static event Action<BreachBarricade> BrokenBoard;
        /// <summary>Filled in by the map session: prompt text, and what using it does.</summary>
        public static Func<BreachBarricade, GameObject, string> PromptFor;
        public static Action<BreachBarricade, GameObject> Use;

        public Transform TargetTransform => transform;
        public bool IsTargetable => Authority && Boards > 0 && isActiveAndEnabled;
        public IDamageable Damageable => this;
        public bool IsAlive => Boards > 0;
        public float DamageReduction => 0f;

        /// <param name="edge">Floor centre of the wall edge the opening is in.</param>
        /// <param name="outward">Unit vector from the room into the closet.</param>
        public static BreachBarricade Create(int index, int room, Vector3 edge, Vector3 outward, Material wood, Material frame, Transform parent)
        {
            var root = new GameObject($"Breach #{index}");
            root.transform.SetParent(parent, false);
            root.transform.position = edge;
            root.transform.rotation = Quaternion.LookRotation(outward, Vector3.up);
            var b = root.AddComponent<BreachBarricade>();
            b.Index = index;
            b.Room = room;
            b._along = root.transform.right;
            b.ClosetSide = edge + outward * 1.2f;

            // Frame posts and header, room side.
            Part(root.transform, new Vector3(-OpeningWidth * 0.5f - 0.08f, OpeningHeight * 0.5f, -0.12f), new Vector3(0.16f, OpeningHeight, 0.16f), frame);
            Part(root.transform, new Vector3(OpeningWidth * 0.5f + 0.08f, OpeningHeight * 0.5f, -0.12f), new Vector3(0.16f, OpeningHeight, 0.16f), frame);
            // Boards across the opening, bottom to top, slightly tilted for the boarded-up look.
            for (int i = 0; i < MaxBoards; i++)
            {
                float y = 0.35f + i * (OpeningHeight - 0.6f) / (MaxBoards - 1);
                GameObject board = Part(root.transform, new Vector3(0f, y, -0.05f), new Vector3(OpeningWidth + 0.25f, 0.22f, 0.07f), wood);
                board.transform.localRotation = Quaternion.Euler(0f, 0f, (i % 2 == 0 ? 1f : -1f) * (4f + i * 1.3f));
                b._boards[i] = board;
            }
            // Interaction volume on the room side (trigger: no blocking, bullets ignore triggers).
            var use = new GameObject("Use");
            use.transform.SetParent(root.transform, false);
            use.transform.localPosition = new Vector3(0f, 1.2f, -0.4f);
            var trigger = use.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(OpeningWidth, 2.2f, 0.6f);
            // Player wall inside the closet mouth: Ignore Raycast layer, so bullets (and NavMesh agents) pass.
            var wall = new GameObject("PlayerBlocker");
            wall.layer = 2;
            wall.transform.SetParent(root.transform, false);
            wall.transform.localPosition = new Vector3(0f, OpeningHeight * 0.5f, 0.45f);
            var wallBox = wall.AddComponent<BoxCollider>();
            wallBox.size = new Vector3(OpeningWidth + 0.2f, OpeningHeight + 0.2f, 0.3f);

            b._obstacle = root.AddComponent<NavMeshObstacle>();
            b._obstacle.shape = NavMeshObstacleShape.Box;
            b._obstacle.center = new Vector3(0f, OpeningHeight * 0.5f, 0.1f);
            b._obstacle.size = new Vector3(OpeningWidth + 0.4f, OpeningHeight, 0.5f);
            b._obstacle.carving = true;
            b._obstacle.carveOnlyStationary = true;
            Blockers.Register(b);
            NearbyInteractables.Register(b);
            return b;
        }

        private static GameObject Part(Transform parent, Vector3 local, Vector3 size, Material m)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = size;
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        private void OnDestroy()
        {
            Blockers.Unregister(this);
            NearbyInteractables.Unregister(this);
        }

        /// <summary>A little slower than the host's repair cooldown, so held repairs never get refused.</summary>
        public float RepeatSeconds => 0.7f / Mathf.Max(0.5f, LocalRepairSpeed != null ? LocalRepairSpeed() : 1f);

        /// <summary>Repair speed of the local player (Engineer Handy: 1.5). Set by the map session.</summary>
        public static Func<float> LocalRepairSpeed;

        /// <summary>Room side, in front of the opening: up to <see cref="RepairReach"/> m out and a little past each post.</summary>
        public bool InReach(Vector3 feet, out float distance)
        {
            distance = float.MaxValue;
            if (!isActiveAndEnabled) return false;
            Vector3 local = transform.InverseTransformPoint(feet); // +z points into the closet
            if (local.z > 0.2f || local.z < -RepairReach) return false;
            if (Mathf.Abs(local.x) > OpeningWidth * 0.5f + 0.9f) return false;
            if (local.y < -0.6f || local.y > 1.5f) return false;
            distance = new Vector2(Mathf.Max(0f, Mathf.Abs(local.x) - OpeningWidth * 0.5f), -local.z).magnitude;
            return true;
        }

        public const float RepairReach = 2.6f;

        public float DistanceFrom(Vector3 point)
        {
            Vector3 d = point - transform.position;
            d.y = 0f;
            float along = Mathf.Clamp(Vector3.Dot(d, _along), -OpeningWidth * 0.5f, OpeningWidth * 0.5f);
            Vector3 closest = transform.position + _along * along;
            closest.y = point.y;
            return Vector3.Distance(point, closest);
        }

        /// <summary>Only from the closet side (+z); enemies already in the room never tear a closet open.</summary>
        public bool BreakableFrom(Vector3 point) => transform.InverseTransformPoint(point).z > -0.3f;

        public float ApplyDamage(in DamageInfo info)
        {
            if (!Authority || Boards <= 0 || info.Amount <= 0f) return 0f;
            // Only enemies break boards; player fire passes through the gaps.
            if (info.Source != null && Deadfall.Combat.PlayerIdentity.Of(info.Source) != null) return 0f;
            float applied = Mathf.Min(_health, info.Amount);
            _health -= applied;
            int boards = Mathf.CeilToInt(_health / HealthPerBoard - 0.0001f);
            if (boards != Boards)
            {
                SetBoards(boards);
                GameAudio.Play(SfxId.BarricadeBreak, transform.position);
                BrokenBoard?.Invoke(this);
            }
            return applied;
        }

        /// <summary>Sets the board count (repairs, run reset, and clients mirroring the host).</summary>
        public void SetBoards(int boards)
        {
            boards = Mathf.Clamp(boards, 0, MaxBoards);
            Boards = boards;
            _health = Mathf.Max(Mathf.Min(_health, boards * HealthPerBoard), (boards - 1) * HealthPerBoard + 0.01f);
            if (boards == 0) _health = 0f;
            if (boards == MaxBoards) _health = MaxBoards * HealthPerBoard;
            for (int i = 0; i < MaxBoards; i++) if (_boards[i] != null) _boards[i].SetActive(i < boards);
            if (_obstacle != null) _obstacle.enabled = boards > 0;
        }

        /// <summary>Puts one board back. Returns false when it is already whole.</summary>
        public bool RepairOne()
        {
            if (Boards >= MaxBoards) return false;
            _health = (Boards + 1) * HealthPerBoard;
            SetBoards(Boards + 1);
            PlayNailing();
            return true;
        }

        /// <summary>Hits played for each board put back.</summary>
        public const int NailHits = 3;
        private const float NailGapSeconds = 0.21f;

        /// <summary>Hammers the board in: the nail sound three times. Every peer calls this on a repair.</summary>
        public void PlayNailing()
        {
            if (isActiveAndEnabled) StartCoroutine(Nailing());
        }

        private IEnumerator Nailing()
        {
            var wait = new WaitForSeconds(NailGapSeconds);
            for (int i = 0; i < NailHits; i++)
            {
                GameAudio.Play(SfxId.NailBoard, transform.position);
                if (i < NailHits - 1) yield return wait;
            }
        }

        public string InteractPrompt(GameObject player) => PromptFor != null ? PromptFor(this, player) : (Boards < MaxBoards ? "Repair barricade" : string.Empty);
        public bool CanInteract(GameObject player) => Boards < MaxBoards;
        public void Interact(GameObject player) => Use?.Invoke(this, player);
    }
}
