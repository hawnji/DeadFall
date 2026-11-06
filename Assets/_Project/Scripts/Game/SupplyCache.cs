using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Rules.Combat;
using Deadfall.Rules.Common;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// A Supply Cache on a map loot slot (D-05). It sells supplies, not guns: an ammo refill, a full heal and a full
    /// set of grenades, the same on every peer. F opens the buy menu; the host takes the Field Credits
    /// (<see cref="RunEconomy"/>). Weapons come from the mystery box, wall buys and drops.
    /// </summary>
    public sealed class SupplyCache : MonoBehaviour, IInteractable
    {
        public enum Supply : byte { Ammo, Heal, Grenades }

        public struct Offer
        {
            public Supply Kind;
            public string Name;
            public string Detail;
            public int Price;
        }

        private readonly List<Offer> _stock = new List<Offer>();
        private readonly List<Material> _materials = new List<Material>();

        public int Id { get; private set; }
        public int Sector { get; private set; }
        public IReadOnlyList<Offer> Stock => _stock;

        public static SupplyCache Create(int id, int sector, ulong mapSeed, Vector3 position, float yaw, Transform parent)
        {
            var root = new GameObject($"SupplyCache #{id}");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            var c = root.AddComponent<SupplyCache>();
            c.Id = id;
            c.Sector = sector;
            c.Part(new Vector3(0f, 0.45f, 0f), new Vector3(1.6f, 0.9f, 0.8f), new Color(0.22f, 0.36f, 0.26f));
            c.Part(new Vector3(0f, 0.92f, 0f), new Vector3(1.66f, 0.06f, 0.86f), new Color(0.75f, 0.62f, 0.2f));
            c.Part(new Vector3(0f, 0.55f, -0.41f), new Vector3(0.5f, 0.18f, 0.02f), new Color(0.4f, 0.9f, 0.5f));
            // Blender model (T-58) over the placeholder parts; the parts have no gameplay role beyond looks.
            GameObject model = ModelLibrary.Spawn("supply_cache", root.transform);
            if (model != null) ModelLibrary.HideExcept(root.transform, model.transform);
            c.RollStock(mapSeed);
            if (RunEconomy.Current != null) RunEconomy.Current.Register(c);
            InteractGlow.Add(root, InteractGlow.Guns);
            return c;
        }

        private void Part(Vector3 local, Vector3 size, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = local;
            go.transform.localScale = size;
            Material m = RuntimeMaterials.CreateLit(color);
            _materials.Add(m);
            go.GetComponent<Renderer>().sharedMaterial = m;
        }

        private void RollStock(ulong mapSeed)
        {
            _stock.Add(new Offer { Kind = Supply.Ammo, Name = "Ammo refill", Detail = "Fills the reserve of every gun you carry.", Price = LootRules.AmmoRefillPrice });
            _stock.Add(new Offer { Kind = Supply.Heal, Name = "Med kit", Detail = "Restores you to full health.", Price = LootRules.CacheHealPrice });
            _stock.Add(new Offer { Kind = Supply.Grenades, Name = "Grenade bundle", Detail = "Refills your grenades to capacity.", Price = LootRules.CacheGrenadePrice });
        }

        public string InteractPrompt(GameObject player) => "Open Supply Cache";
        public bool CanInteract(GameObject player) => true;
        public void Interact(GameObject player) => SupplyCacheMenu.Open(this);

        private void OnDestroy()
        {
            if (RunEconomy.Current != null) RunEconomy.Current.Unregister(this);
            foreach (Material m in _materials) if (m != null) Destroy(m);
        }
    }
}
