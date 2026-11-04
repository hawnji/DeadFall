using Deadfall.Core;
using Deadfall.Player;
using Deadfall.Rules.Common;
using Deadfall.Rules.World;
using UnityEngine;

namespace Deadfall.RunSystems
{
    /// <summary>
    /// A one-use pickup placed on a map loot slot: ammo crate, medkit or secret stash. Interact (F) asks
    /// <see cref="LootNetwork"/> to take it; the host decides who gets it, so two players can't both take one.
    /// </summary>
    public sealed class LootPickup : MonoBehaviour, IInteractable
    {
        public const float MedkitHeal = 50f;

        public int Id { get; private set; }
        public LootKind Kind { get; private set; }
        public bool Taken { get; private set; }

        public static LootPickup Create(int id, LootKind kind, Vector3 position, float yaw, Transform parent)
        {
            var root = new GameObject($"Loot {kind} #{id}");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            var p = root.AddComponent<LootPickup>();
            p.Id = id;
            p.Kind = kind;
            switch (kind)
            {
                case LootKind.Medkit:
                    Part(root.transform, new Vector3(0f, 0.18f, 0f), new Vector3(0.6f, 0.36f, 0.4f), new Color(0.9f, 0.9f, 0.88f));
                    Part(root.transform, new Vector3(0f, 0.37f, 0f), new Vector3(0.36f, 0.02f, 0.1f), new Color(0.85f, 0.15f, 0.15f));
                    Part(root.transform, new Vector3(0f, 0.37f, 0f), new Vector3(0.1f, 0.02f, 0.3f), new Color(0.85f, 0.15f, 0.15f));
                    break;
                case LootKind.SecretStash:
                    Part(root.transform, new Vector3(0f, 0.25f, 0f), new Vector3(0.8f, 0.5f, 0.5f), new Color(0.85f, 0.68f, 0.2f));
                    break;
                default:
                    Part(root.transform, new Vector3(0f, 0.25f, 0f), new Vector3(0.9f, 0.5f, 0.5f), new Color(0.28f, 0.42f, 0.62f));
                    break;
            }
            LootNetwork.Register(p);
            InteractGlow.Add(p.gameObject, InteractGlow.PowerUps, () => !p.Taken);
            return p;
        }

        private static void Part(Transform parent, Vector3 local, Vector3 size, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = RuntimeMaterials.CreateLit(color);
        }

        public string InteractPrompt(GameObject player)
        {
            if (Taken) return string.Empty;
            switch (Kind)
            {
                case LootKind.Medkit: return $"Take medkit (+{MedkitHeal:0} health)";
                case LootKind.SecretStash: return "Open secret stash";
                default: return "Take ammo";
            }
        }

        public bool CanInteract(GameObject player) => !Taken;

        public void Interact(GameObject player) => LootNetwork.RequestTake(this);

        /// <summary>Hides the pickup (every peer, once the host says it was taken).</summary>
        public void MarkTaken()
        {
            if (Taken) return;
            Taken = true;
            gameObject.SetActive(false);
        }

        /// <summary>Gives the item to the local player.</summary>
        public static void ApplyTo(GameObject player, LootKind kind)
        {
            if (player == null) return;
            GameAudio.Play2D(SfxId.Pickup);
            var health = player.GetComponent<PlayerHealth>();
            var weapons = player.GetComponent<PlayerWeapons>();
            switch (kind)
            {
                case LootKind.AmmoCrate:
                    if (weapons != null) weapons.RefillAllAmmo();
                    break;
                case LootKind.Medkit:
                    if (health != null) health.Heal(MedkitHeal);
                    break;
                case LootKind.SecretStash:
                    if (weapons != null) weapons.RefillAllAmmo();
                    if (health != null) health.Heal(health.Max);
                    break;
            }
        }

        private void OnDestroy()
        {
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                if (r.sharedMaterial != null) Destroy(r.sharedMaterial);
            LootNetwork.Unregister(this);
        }
    }
}
