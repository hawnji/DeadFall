using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Encounters;
using Deadfall.Enemies;
using Deadfall.Net;
using Deadfall.Player;
using Deadfall.Rules.Combat;
using Deadfall.Rules.Common;
using Deadfall.Rules.Encounters;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// In-run weapon economy, host-authoritative (T-36):
    /// Supply Cache purchases ("df.buy" → host spends the buyer's Field Credits → "df.grant" or "df.deny"),
    /// weapon drops from kills ("df.drop" to everyone, "df.dropreq" to take one, "df.dropgone" when taken or
    /// expired). The finder has 10 s to take a drop before anyone can (D-06). Granted weapons go into the slot
    /// <see cref="PlayerWeapons.SlotFor"/> picks.
    /// </summary>
    public sealed class RunEconomy : MonoBehaviour
    {
        private const string MsgBuy = "df.buy";
        private const string MsgGrant = "df.grant";
        private const string MsgDeny = "df.deny";
        private const string MsgDrop = "df.drop";
        private const string MsgDropRequest = "df.dropreq";
        private const string MsgDropGone = "df.dropgone";
        public const float FinderSeconds = 10f;
        public const float DropLifetime = 90f;
        public const int MaxDrops = 12;

        private const byte GrantWeapon = 0;
        private const byte GrantAmmo = 1;

        private readonly Dictionary<int, SupplyCache> _caches = new Dictionary<int, SupplyCache>();
        private readonly Dictionary<ushort, WeaponDrop> _drops = new Dictionary<ushort, WeaponDrop>();
        private SessionService _session;
        private DeterministicRandom _dropRng = new DeterministicRandom(0xD509);
        private ushort _nextDropId = 1;

        public static RunEconomy Current { get; private set; }

        private void Awake()
        {
            Current = this;
            _session = SessionService.Instance;
            _session.RegisterHandler(MsgBuy, OnBuy);
            _session.RegisterHandler(MsgGrant, OnGrant);
            _session.RegisterHandler(MsgDeny, OnDeny);
            _session.RegisterHandler(MsgDrop, OnDrop);
            _session.RegisterHandler(MsgDropRequest, OnDropRequest);
            _session.RegisterHandler(MsgDropGone, OnDropGone);
            _session.PeerConnected += OnPeerConnected;
            EventBus<EnemyDied>.Subscribe(OnEnemyDied);
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            EventBus<EnemyDied>.Unsubscribe(OnEnemyDied);
            if (_session == null) return;
            foreach (string m in new[] { MsgBuy, MsgGrant, MsgDeny, MsgDrop, MsgDropRequest, MsgDropGone }) _session.UnregisterHandler(m);
            _session.PeerConnected -= OnPeerConnected;
        }

        private bool IsAuthority => _session == null || _session.Role != SessionRole.Client;
        private static WaveManager Waves => WaveManager.Current;

        // ---------------------------------------------------------------- caches

        public void Register(SupplyCache cache) { if (cache != null) _caches[cache.Id] = cache; }
        public void Unregister(SupplyCache cache) { if (cache != null && _caches.TryGetValue(cache.Id, out SupplyCache c) && c == cache) _caches.Remove(cache.Id); }

        /// <summary>Local player buys an item (0..stock-1 weapons, stock = ammo refill).</summary>
        public void Buy(SupplyCache cache, int item)
        {
            if (cache == null) return;
            if (_session.IsConnectedClient)
            {
                using (var w = new FastBufferWriter(8, Allocator.Temp))
                {
                    w.WriteValueSafe((ushort)cache.Id);
                    w.WriteValueSafe((byte)item);
                    _session.SendToServer(MsgBuy, w, NetworkDelivery.ReliableSequenced);
                }
                return;
            }
            HandleBuy(_session.LocalClientId, cache.Id, item);
        }

        private void OnBuy(ulong sender, FastBufferReader r)
        {
            if (!_session.IsHost) return;
            r.ReadValueSafe(out ushort id);
            r.ReadValueSafe(out byte item);
            HandleBuy(sender, id, item);
        }

        private void HandleBuy(ulong buyer, int cacheId, int item)
        {
            if (!_caches.TryGetValue(cacheId, out SupplyCache cache) || cache == null) return;
            PlayerIdentity who = PlayerIdentity.Find(buyer);
            if (who != null && (who.transform.position - cache.transform.position).sqrMagnitude > 6f * 6f) { Deny(buyer, "Too far from the cache"); return; }
            WaveManager waves = Waves;
            if (waves == null || !waves.IsRunning) { Deny(buyer, "Start a run to earn Field Credits"); return; }
            if (item == cache.Stock.Count)
            {
                if (!waves.TrySpendCredits(buyer, LootRules.AmmoRefillPrice)) { Deny(buyer, "Not enough Field Credits"); return; }
                Grant(buyer, GrantAmmo, null, null);
                return;
            }
            if (item < 0 || item >= cache.Stock.Count) return;
            SupplyCache.Offer offer = cache.Stock[item];
            if (!waves.TrySpendCredits(buyer, offer.Price)) { Deny(buyer, "Not enough Field Credits"); return; }
            Grant(buyer, GrantWeapon, offer.Weapon, offer.Mods);
        }

        private void Deny(ulong to, string reason)
        {
            if (to == _session.LocalClientId || !_session.IsHost) { RunToasts.Show(reason, new Color(1f, 0.55f, 0.45f)); return; }
            using (var w = new FastBufferWriter(8 + reason.Length * 2, Allocator.Temp))
            {
                w.WriteValueSafe(reason);
                _session.SendToClient(MsgDeny, to, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnDeny(ulong sender, FastBufferReader r)
        {
            if (_session.IsHost) return;
            r.ReadValueSafe(out string reason);
            RunToasts.Show(reason, new Color(1f, 0.55f, 0.45f));
        }

        private void Grant(ulong to, byte kind, WeaponDefinition def, WeaponMods mods)
        {
            if (to == _session.LocalClientId || !_session.IsHost) { ApplyGrant(kind, def, mods); return; }
            byte[] encoded = mods != null ? mods.Encode() : new byte[0];
            using (var w = new FastBufferWriter(16 + encoded.Length, Allocator.Temp))
            {
                w.WriteValueSafe(kind);
                w.WriteValueSafe((byte)Mathf.Max(0, WeaponCatalog.IndexOf(def)));
                w.WriteValueSafe((byte)encoded.Length);
                foreach (byte b in encoded) w.WriteValueSafe(b);
                _session.SendToClient(MsgGrant, to, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnGrant(ulong sender, FastBufferReader r)
        {
            if (_session.IsHost) return;
            r.ReadValueSafe(out byte kind);
            r.ReadValueSafe(out byte index);
            WeaponMods mods = ReadMods(r);
            ApplyGrant(kind, WeaponCatalog.At(index), mods);
        }

        private static WeaponMods ReadMods(FastBufferReader r)
        {
            r.ReadValueSafe(out byte len);
            var bytes = new byte[len];
            for (int i = 0; i < len; i++) r.ReadValueSafe(out bytes[i]);
            return WeaponMods.Decode(bytes);
        }

        /// <summary>Gives the local player a weapon (or an ammo refill).</summary>
        public static void ApplyGrant(byte kind, WeaponDefinition def, WeaponMods mods)
        {
            PlayerIdentity me = PlayerIdentity.Local;
            var weapons = me != null ? me.GetComponent<PlayerWeapons>() : null;
            if (weapons == null) return;
            if (kind == GrantAmmo)
            {
                weapons.RefillAllAmmo();
                RunToasts.Show("Ammo refilled", new Color(0.6f, 0.85f, 1f));
                return;
            }
            if (def == null) return;
            int slot = weapons.SlotFor(def);
            WeaponDefinition old = weapons.DefinitionAt(slot);
            weapons.ReplaceSlot(slot, def, mods);
            string extra = mods != null && mods.Describe().Length > 0 ? $" ({mods.Describe()})" : string.Empty;
            RunToasts.Show(old != null && old != def ? $"{def.DisplayName}{extra} replaces {old.DisplayName}" : $"Got {def.DisplayName}{extra}", new Color(0.6f, 1f, 0.6f));
        }

        // ---------------------------------------------------------------- drops

        /// <summary>Events (Red Sky) scale the loot drop chance on the host. Reset when the event ends.</summary>
        public static float EventDropMultiplier = 1f;

        private void OnEnemyDied(EnemyDied e)
        {
            if (!IsAuthority || e.Definition == null) return;
            WaveManager waves = Waves;
            if (waves == null || !waves.IsRunning) return; // drops only in runs (sandbox spawns stay clean)
            // A Specimen always drops a Prototype weapon (GAME_DESIGN §7 boss reward); everything else rolls.
            bool boss = e.Definition.IsBoss;
            if (!boss && _dropRng.NextFloat() >= LootRules.DropChance(e.Definition.ThreatCost) * EventDropMultiplier) return;
            if (!boss && _drops.Count >= MaxDrops) return;
            IReadOnlyList<WeaponDefinition> all = WeaponCatalog.All;
            WeaponDefinition def = all[_dropRng.Range(0, all.Count)];
            WeaponTier tier = boss ? WeaponTier.Prototype : LootRules.RollTier(_dropRng, waves.Surge);
            WeaponMods mods = LootRules.RollMods(_dropRng, tier, def.Category);
            PlayerIdentity killer = PlayerIdentity.Of(e.Killer);
            ulong finder = killer != null && !boss ? killer.ClientId : ulong.MaxValue; // the boss drop is anyone's
            ushort id = _nextDropId++;
            if (_nextDropId == 0) _nextDropId = 1;
            SpawnDrop(id, def, mods, e.Position, finder, Time.timeAsDouble + FinderSeconds);
            if (_session.IsHost) SendDrop(_drops[id], ulong.MaxValue);
        }

        /// <summary>Host/offline: a reward weapon anyone can take (keycard locker, Stalker event) of at least <paramref name="minTier"/>.</summary>
        public void SpawnRewardDrop(Vector3 position, WeaponTier minTier)
        {
            if (!IsAuthority) return;
            IReadOnlyList<WeaponDefinition> all = WeaponCatalog.All;
            WeaponDefinition def = all[_dropRng.Range(0, all.Count)];
            WaveManager waves = Waves;
            WeaponTier tier = LootRules.RollTier(_dropRng, waves != null ? waves.Surge : 1);
            if (tier < minTier) tier = minTier;
            WeaponMods mods = LootRules.RollMods(_dropRng, tier, def.Category);
            ushort id = _nextDropId++;
            if (_nextDropId == 0) _nextDropId = 1;
            SpawnDrop(id, def, mods, position, ulong.MaxValue, 0);
            if (_session.IsHost && _drops.TryGetValue(id, out WeaponDrop d)) SendDrop(d, ulong.MaxValue);
        }

        private void SpawnDrop(ushort id, WeaponDefinition def, WeaponMods mods, Vector3 position, ulong finder, double finderUntil)
        {
            if (_drops.ContainsKey(id) || def == null) return;
            WeaponDrop d = WeaponDrop.Create(id, def, mods, position + Vector3.up * 0.1f, finder, finderUntil, transform);
            _drops[id] = d;
        }

        private void SendDrop(WeaponDrop d, ulong onlyTo)
        {
            byte[] encoded = d.Mods.Encode();
            using (var w = new FastBufferWriter(48 + encoded.Length, Allocator.Temp))
            {
                w.WriteValueSafe(d.Id);
                w.WriteValueSafe((byte)WeaponCatalog.IndexOf(d.Definition));
                w.WriteValueSafe((byte)encoded.Length);
                foreach (byte b in encoded) w.WriteValueSafe(b);
                w.WriteValueSafe(d.transform.position);
                w.WriteValueSafe(d.Finder);
                w.WriteValueSafe((float)System.Math.Max(0.0, d.FinderUntil - Time.timeAsDouble));
                if (onlyTo == ulong.MaxValue) _session.SendToRemoteClients(MsgDrop, w, NetworkDelivery.ReliableSequenced);
                else _session.SendToClient(MsgDrop, onlyTo, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnDrop(ulong sender, FastBufferReader r)
        {
            if (_session.IsHost) return;
            r.ReadValueSafe(out ushort id);
            r.ReadValueSafe(out byte index);
            WeaponMods mods = ReadMods(r);
            r.ReadValueSafe(out Vector3 pos);
            r.ReadValueSafe(out ulong finder);
            r.ReadValueSafe(out float left);
            SpawnDrop(id, WeaponCatalog.At(index), mods, pos - Vector3.up * 0.1f, finder, Time.timeAsDouble + left);
        }

        /// <summary>Local player wants a drop.</summary>
        public void RequestDrop(WeaponDrop d)
        {
            if (d == null) return;
            if (_session.IsConnectedClient)
            {
                using (var w = new FastBufferWriter(4, Allocator.Temp))
                {
                    w.WriteValueSafe(d.Id);
                    _session.SendToServer(MsgDropRequest, w, NetworkDelivery.ReliableSequenced);
                }
                return;
            }
            HandleDropRequest(_session.LocalClientId, d.Id);
        }

        private void OnDropRequest(ulong sender, FastBufferReader r)
        {
            if (!_session.IsHost) return;
            r.ReadValueSafe(out ushort id);
            HandleDropRequest(sender, id);
        }

        private void HandleDropRequest(ulong who, ushort id)
        {
            if (!_drops.TryGetValue(id, out WeaponDrop d) || d == null) return;
            if (!d.CanBeTakenBy(who)) { Deny(who, "Another player found this first"); return; }
            PlayerIdentity p = PlayerIdentity.Find(who);
            if (p != null && (p.transform.position - d.transform.position).sqrMagnitude > 6f * 6f) return;
            WeaponDefinition def = d.Definition;
            WeaponMods mods = d.Mods;
            RemoveDrop(id);
            Grant(who, GrantWeapon, def, mods);
        }

        private void RemoveDrop(ushort id)
        {
            if (_drops.TryGetValue(id, out WeaponDrop d) && d != null) Destroy(d.gameObject);
            _drops.Remove(id);
            if (_session != null && _session.IsHost)
            {
                using (var w = new FastBufferWriter(4, Allocator.Temp))
                {
                    w.WriteValueSafe(id);
                    _session.SendToRemoteClients(MsgDropGone, w, NetworkDelivery.ReliableSequenced);
                }
            }
        }

        private void OnDropGone(ulong sender, FastBufferReader r)
        {
            if (_session.IsHost) return;
            r.ReadValueSafe(out ushort id);
            if (_drops.TryGetValue(id, out WeaponDrop d) && d != null) Destroy(d.gameObject);
            _drops.Remove(id);
        }

        /// <summary>Removes every drop (map change, run start).</summary>
        public void ClearDrops()
        {
            foreach (WeaponDrop d in _drops.Values) if (d != null) Destroy(d.gameObject);
            _drops.Clear();
        }

        private readonly List<ushort> _expired = new List<ushort>();

        private void Update()
        {
            if (!IsAuthority || _drops.Count == 0) return;
            _expired.Clear();
            double now = Time.timeAsDouble;
            foreach (KeyValuePair<ushort, WeaponDrop> kv in _drops)
                if (kv.Value == null || now - kv.Value.SpawnedAt > DropLifetime) _expired.Add(kv.Key);
            foreach (ushort id in _expired) RemoveDrop(id);
        }

        private void OnPeerConnected(ulong clientId)
        {
            if (!_session.IsHost) return;
            foreach (WeaponDrop d in _drops.Values) if (d != null) SendDrop(d, clientId);
        }
    }
}
