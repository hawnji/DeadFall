using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Encounters;
using Deadfall.Net;
using Deadfall.Rules.Combat;
using Deadfall.Rules.Common;
using Deadfall.Rules.World;
using Deadfall.World;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// Puts one wall-mounted <see cref="AmmoStation"/> in each sector of a generated map (same spots on every peer:
    /// they come from the map seed and geometry). Pressing F sends "df.ammost" to the host, which takes
    /// <see cref="Price"/> Field Credits and grants a full ammo refill through <see cref="RunEconomy"/>.
    /// </summary>
    public sealed class AmmoStationDirector : MonoBehaviour
    {
        private const string MsgUse = "df.ammost";
        /// <summary>Same as an ammo refill at a supply cache.</summary>
        public const int Price = LootRules.AmmoRefillPrice;
        private const float UseRange = 4f;

        /// <summary>Self-revive syringe from the dispenser beside the cabinet: one more kit for the squad's pool.</summary>
        public const int SyringePrice = 1500;

        /// <summary>
        /// A wall cabinet 0.84 m wide and 0.3 m deep (plus the wall plate) with the syringe dispenser on its right,
        /// hung 0.85 to 2.2 m up.
        /// </summary>
        private static readonly WallPlacement.Shape StationShape = new WallPlacement.Shape(0.8f, 0.2f, 0.85f, 2.2f);

        private SessionService _session;
        private BuiltMap _builtFor;
        private readonly List<AmmoStation> _stations = new List<AmmoStation>(8);
        private readonly List<Vector3> _positions = new List<Vector3>(8);

        public static AmmoStationDirector Current { get; private set; }
        public IReadOnlyList<AmmoStation> Stations => _stations;

        private bool IsAuthority => _session == null || _session.Role != SessionRole.Client;

        private void Awake()
        {
            Current = this;
            _session = SessionService.Instance;
            _session?.RegisterHandler(MsgUse, OnUse);
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            _session?.UnregisterHandler(MsgUse);
            Clear();
        }

        private void Update()
        {
            BuiltMap map = MapSession.Current != null ? MapSession.Current.Map : null;
            if (map == _builtFor) return;
            if (map == null) Clear();
            else EnsureBuilt(map);
        }

        private void Clear()
        {
            foreach (AmmoStation s in _stations) if (s != null) Destroy(s.gameObject);
            _stations.Clear();
            _positions.Clear();
            _builtFor = null;
        }

        /// <summary>Builds this map's stations if not done yet; returns their floor positions (others avoid them).</summary>
        public IReadOnlyList<Vector3> EnsureBuilt(BuiltMap map)
        {
            if (map == null || map == _builtFor) return _positions;
            Clear();
            _builtFor = map;
            var rng = new DeterministicRandom(map.Layout.Seed ^ 0xA3307A71UL);
            var rooms = new List<int>(8);
            for (int sector = 0; sector < map.Layout.SectorCount; sector++)
            {
                WallPlacement.CandidateRooms(map, sector, rooms);
                for (int i = rooms.Count - 1; i > 0; i--)
                {
                    int j = rng.Range(0, i + 1);
                    (rooms[i], rooms[j]) = (rooms[j], rooms[i]);
                }
                foreach (int room in rooms)
                {
                    if (!WallPlacement.Find(map, room, StationShape, rng, _positions, out Vector3 pos, out float yaw)) continue;
                    _stations.Add(AmmoStation.Create(map.Root != null ? map.Root : transform, _stations.Count, pos, yaw));
                    _positions.Add(pos);
                    break;
                }
            }
            GameLog.Info("Ammo", $"{_stations.Count} ammo stations placed.");
            return _positions;
        }

        /// <summary>The local player pressed F on station <paramref name="index"/>.</summary>
        public void RequestUse(int index)
        {
            if (IsAuthority)
            {
                HandleUse(_session != null ? _session.LocalClientId : 0UL, index);
                return;
            }
            using (var w = new FastBufferWriter(4, Allocator.Temp))
            {
                w.WriteValueSafe((byte)index);
                _session.SendToServer(MsgUse, w, NetworkDelivery.ReliableSequenced);
            }
        }

        /// <summary>The local player pressed F on station <paramref name="index"/>'s syringe dispenser.</summary>
        public void RequestSyringe(int index)
        {
            if (IsAuthority)
            {
                HandleSyringe(_session != null ? _session.LocalClientId : 0UL, index);
                return;
            }
            using (var w = new FastBufferWriter(4, Allocator.Temp))
            {
                w.WriteValueSafe((byte)(index | 0x80));
                _session.SendToServer(MsgUse, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnUse(ulong sender, FastBufferReader r)
        {
            if (!_session.IsHost) return;
            r.ReadValueSafe(out byte index);
            if ((index & 0x80) != 0) HandleSyringe(sender, index & 0x7F);
            else HandleUse(sender, index);
        }

        private void HandleUse(ulong who, int index)
        {
            if (index < 0 || index >= _stations.Count || _stations[index] == null) return;
            WaveManager waves = WaveManager.Current;
            RunEconomy economy = RunEconomy.Current;
            if (waves == null || !waves.IsRunning || economy == null) return;
            Vector3 at = _stations[index].transform.position;
            PlayerIdentity p = PlayerIdentity.Find(who);
            if (p != null && (p.transform.position - at).sqrMagnitude > UseRange * UseRange) return;
            if (!waves.TrySpendCredits(who, Price))
            {
                economy.Notify(who, "Not enough Field Credits");
                return;
            }
            economy.GiveAmmo(who);
            GameAudio.Play(SfxId.Reload, at + Vector3.up * 1.4f);
        }

        private void HandleSyringe(ulong who, int index)
        {
            if (index < 0 || index >= _stations.Count || _stations[index] == null) return;
            WaveManager waves = WaveManager.Current;
            RunEconomy economy = RunEconomy.Current;
            SquadLife squad = SquadLife.Current;
            if (waves == null || !waves.IsRunning || economy == null || squad == null) return;
            Vector3 at = _stations[index].transform.position;
            PlayerIdentity p = PlayerIdentity.Find(who);
            if (p != null && (p.transform.position - at).sqrMagnitude > UseRange * UseRange) return;
            if (squad.Kits >= SquadLife.MaxKits)
            {
                economy.Notify(who, $"The squad already carries {SquadLife.MaxKits} self-revive kits");
                return;
            }
            if (!waves.TrySpendCredits(who, SyringePrice))
            {
                economy.Notify(who, "Not enough Field Credits");
                return;
            }
            squad.HostAddKit();
            GameAudio.Play(SfxId.Pickup, at + Vector3.up * 1.4f);
        }
    }
}
