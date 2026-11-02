using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Player;
using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// A weapon dropped by an enemy. It spins in place with a tier-coloured base. For the first 10 s only the
    /// player who got the kill can take it (D-06); after that anyone can. Taking goes through the host.
    /// </summary>
    public sealed class WeaponDrop : MonoBehaviour, IInteractable
    {
        private Transform _spin;
        private Material _body, _base;

        public ushort Id { get; private set; }
        public WeaponDefinition Definition { get; private set; }
        public WeaponMods Mods { get; private set; }
        public ulong Finder { get; private set; }
        public double FinderUntil { get; private set; }
        public double SpawnedAt { get; private set; }

        public static WeaponDrop Create(ushort id, WeaponDefinition def, WeaponMods mods, Vector3 position, ulong finder, double finderUntil, Transform parent)
        {
            var root = new GameObject($"WeaponDrop #{id} {def.DisplayName}");
            root.transform.SetParent(parent, false);
            root.transform.position = position;
            var d = root.AddComponent<WeaponDrop>();
            d.Id = id;
            d.Definition = def;
            d.Mods = mods ?? WeaponMods.Standard();
            d.Finder = finder;
            d.FinderUntil = finderUntil;
            d.SpawnedAt = Time.timeAsDouble;

            GameObject b = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            b.transform.SetParent(root.transform, false);
            b.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            b.transform.localScale = new Vector3(0.9f, 0.03f, 0.9f);
            d._base = RuntimeMaterials.CreateLit(SupplyCacheMenu.TierColor(d.Mods.Tier));
            b.GetComponent<Renderer>().sharedMaterial = d._base;
            Destroy(b.GetComponent<Collider>());

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            bool melee = def.Kind == WeaponKind.Melee;
            body.transform.localScale = melee ? new Vector3(0.08f, 0.08f, 0.9f) : new Vector3(0.1f, 0.18f, 0.75f);
            d._body = RuntimeMaterials.CreateLit(def.PlaceholderColor);
            body.GetComponent<Renderer>().sharedMaterial = d._body;
            var col = body.GetComponent<BoxCollider>();
            col.isTrigger = true; // doesn't block bullets or movement, still found by the interact ray
            col.size = new Vector3(6f, 4f, 1.4f);
            d._spin = body.transform;
            return d;
        }

        public bool CanBeTakenBy(ulong clientId) => Finder == ulong.MaxValue || clientId == Finder || Time.timeAsDouble >= FinderUntil;

        private ulong LocalId => PlayerIdentity.Local != null ? PlayerIdentity.Local.ClientId : 0UL;

        public string InteractPrompt(GameObject player)
        {
            string tier = Mods.Tier == WeaponTier.Standard ? string.Empty : $" ({Mods.Describe()})";
            if (!CanBeTakenBy(LocalId)) return $"{Definition.DisplayName}{tier}: found by another player ({FinderUntil - Time.timeAsDouble:0} s)";
            var weapons = player.GetComponent<PlayerWeapons>();
            WeaponDefinition old = weapons != null ? weapons.DefinitionAt(weapons.SlotFor(Definition)) : null;
            return old != null && old != Definition ? $"Take {Definition.DisplayName}{tier}, replaces {old.DisplayName}" : $"Take {Definition.DisplayName}{tier}";
        }

        public bool CanInteract(GameObject player) => CanBeTakenBy(LocalId);

        public void Interact(GameObject player) => RunEconomy.Current?.RequestDrop(this);

        private void Update()
        {
            if (_spin != null) _spin.localRotation = Quaternion.Euler(0f, Time.time * 90f, 0f);
        }

        private void OnDestroy()
        {
            if (_body != null) Destroy(_body);
            if (_base != null) Destroy(_base);
        }
    }
}
