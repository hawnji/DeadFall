using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Encounters;
using Deadfall.Net;
using Deadfall.Player;
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
    /// Puts the four <see cref="PerkMachine"/>s and one <see cref="WeaponForge"/> on the walls of a generated map (same
    /// spots on every peer, from the map seed), clear of the ammo stations. Purchases go to the host ("df.perk"), which
    /// takes the credits; a perk is granted back to the buyer, a forged weapon arrives through <see cref="RunEconomy"/>.
    /// Perks live on the owning client and are lost when that player goes down.
    /// </summary>
    public sealed class PerkMachineDirector : MonoBehaviour, IWeaponHandlingSource
    {
        private const string Msg = "df.perk";
        private const byte OpBuy = 0, OpGrant = 1, OpForge = 2, OpModBuild = 3;
        private const float UseRange = 4f;

        public const float SwiftReload = 1.5f, SwiftEquip = 1.3f;
        public const float IronHideHealth = 50f;
        public const float MedicReviveSpeed = 2f;
        public const float FleetSprint = 1.15f;

        /// <summary>A floor-standing machine: 0.84 m wide, 0.6 m deep, up to 2 m.</summary>
        private static readonly WallPlacement.Shape MachineShape = new WallPlacement.Shape(0.5f, 0.36f, 0.05f, 2.0f);
        /// <summary>The forge bench: 1.56 m wide, 0.76 m deep, up to 2 m.</summary>
        private static readonly WallPlacement.Shape ForgeShape = new WallPlacement.Shape(0.82f, 0.42f, 0.05f, 2.0f);

        private SessionService _session;
        private BuiltMap _builtFor;
        private readonly List<PerkMachine> _machines = new List<PerkMachine>(PerkMachine.Count);
        private WeaponForge _forge;
        private ModBench _bench;
        private readonly List<Vector3> _positions = new List<Vector3>(16);
        private DeterministicRandom _forgeRng = new DeterministicRandom(0x0F0E6EUL);

        // Local player's perks.
        private static readonly bool[] Owned = new bool[PerkMachine.Count];
        private readonly float[] _pendingUntil = new float[PerkMachine.Count];
        private PlayerWeapons _hookedWeapons;
        private PlayerHealth _health;
        private PlayerStatus _status;

        public static PerkMachineDirector Current { get; private set; }
        public IReadOnlyList<PerkMachine> Machines => _machines;
        public WeaponForge Forge => _forge;
        public ModBench Bench => _bench;

        /// <summary>Revive channel speed for the local player (Field Medic). Read by the revive prompt.</summary>
        public static float LocalReviveSpeed => Owned[(int)PerkKind.FieldMedic] ? MedicReviveSpeed : 1f;

        public static bool LocalHas(PerkKind p) => Owned[(int)p];

        private bool IsAuthority => _session == null || _session.Role != SessionRole.Client;

        private void Awake()
        {
            Current = this;
            _session = SessionService.Instance;
            _session?.RegisterHandler(Msg, OnMessage);
            ClearPerks(false);
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            _session?.UnregisterHandler(Msg);
            ClearPerks(false);
            if (_hookedWeapons != null) _hookedWeapons.RemoveHandlingSource(this);
            Clear();
        }

        private void Update()
        {
            BuiltMap map = MapSession.Current != null ? MapSession.Current.Map : null;
            if (map != _builtFor)
            {
                if (map == null) Clear();
                else EnsureBuilt(map);
            }
            TickLocalPlayer();
        }

        private void Clear()
        {
            foreach (PerkMachine m in _machines) if (m != null) Destroy(m.gameObject);
            _machines.Clear();
            if (_forge != null) Destroy(_forge.gameObject);
            _forge = null;
            if (_bench != null) Destroy(_bench.gameObject);
            _bench = null;
            _positions.Clear();
            _builtFor = null;
        }

        /// <summary>
        /// Builds this map's machines and forge if not done yet (after the ammo stations); returns the floor positions
        /// of every wall fixture so far (ammo stations, machines, forge) so the Gun Dispenser can keep clear of them.
        /// </summary>
        public IReadOnlyList<Vector3> EnsureBuilt(BuiltMap map)
        {
            if (map == null || map == _builtFor) return _positions;
            Clear();
            _builtFor = map;
            AmmoStationDirector stations = AmmoStationDirector.Current;
            if (stations != null) _positions.AddRange(stations.EnsureBuilt(map));
            var rng = new DeterministicRandom(map.Layout.Seed ^ 0x9E7C0DE5UL);
            Transform parent = map.Root != null ? map.Root : transform;
            int sectors = Mathf.Max(1, map.Layout.SectorCount);

            // Perks in shuffled order, spread across sectors starting with the first (so one is buyable early).
            var perks = new List<PerkKind> { PerkKind.SwiftHands, PerkKind.IronHide, PerkKind.FieldMedic, PerkKind.FleetFoot };
            for (int i = perks.Count - 1; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                (perks[i], perks[j]) = (perks[j], perks[i]);
            }
            var rooms = new List<int>(8);
            for (int k = 0; k < perks.Count; k++)
            {
                if (!TryPlace(map, k % sectors, MachineShape, rng, rooms, out Vector3 pos, out float yaw)
                    && !TryPlaceAnywhere(map, sectors, MachineShape, rng, rooms, out pos, out yaw)) continue;
                _machines.Add(PerkMachine.Create(parent, _machines.Count, perks[k], pos, yaw));
                _positions.Add(pos);
            }

            // The forge goes mid-map (sector 1 or later when there is one) so it is something to work towards.
            int forgeSector = sectors > 1 ? Mathf.Max(1, sectors / 2) : 0;
            if (TryPlace(map, forgeSector, ForgeShape, rng, rooms, out Vector3 fp, out float fy)
                || TryPlaceAnywhere(map, sectors, ForgeShape, rng, rooms, out fp, out fy))
            {
                _forge = WeaponForge.Create(parent, fp, fy);
                _positions.Add(fp);
            }
            // The Mod Bench goes in the first sector, so attachments can be bought from the start.
            if (TryPlace(map, 0, ForgeShape, rng, rooms, out Vector3 bp, out float by)
                || TryPlaceAnywhere(map, sectors, ForgeShape, rng, rooms, out bp, out by))
            {
                _bench = ModBench.Create(parent, bp, by);
                _positions.Add(bp);
            }
            GameLog.Info("Perks", $"{_machines.Count} perk machines placed, forge {(_forge != null ? "placed" : "missing")}, mod bench {(_bench != null ? "placed" : "missing")}.");
            return _positions;
        }

        private bool TryPlace(BuiltMap map, int sector, WallPlacement.Shape shape, DeterministicRandom rng, List<int> rooms, out Vector3 pos, out float yaw)
        {
            pos = default;
            yaw = 0f;
            WallPlacement.CandidateRooms(map, sector, rooms);
            if (rooms.Count > 1) rooms.RemoveAll(i => map.Layout.Rooms[i].Template.Role == RoomRole.Spawn);
            for (int i = rooms.Count - 1; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                (rooms[i], rooms[j]) = (rooms[j], rooms[i]);
            }
            foreach (int room in rooms)
                if (WallPlacement.Find(map, room, shape, rng, _positions, out pos, out yaw)) return true;
            return false;
        }

        private bool TryPlaceAnywhere(BuiltMap map, int sectors, WallPlacement.Shape shape, DeterministicRandom rng, List<int> rooms, out Vector3 pos, out float yaw)
        {
            for (int s = 0; s < sectors; s++)
                if (TryPlace(map, s, shape, rng, rooms, out pos, out yaw)) return true;
            pos = default;
            yaw = 0f;
            return false;
        }

        // ---------------------------------------------------------------- requests

        /// <summary>The local player pressed F on machine <paramref name="index"/>.</summary>
        public void RequestPerk(int index)
        {
            if (index < 0 || index >= _machines.Count || _machines[index] == null) return;
            PerkKind perk = _machines[index].Perk;
            if (Owned[(int)perk] || Time.time < _pendingUntil[(int)perk]) return;
            _pendingUntil[(int)perk] = Time.time + 1f;
            if (IsAuthority)
            {
                HandleBuy(_session != null ? _session.LocalClientId : 0UL, index);
                return;
            }
            using (var w = new FastBufferWriter(4, Allocator.Temp))
            {
                w.WriteValueSafe(OpBuy);
                w.WriteValueSafe((byte)index);
                _session.SendToServer(Msg, w, NetworkDelivery.ReliableSequenced);
            }
        }

        /// <summary>The local player pressed F on the forge with <paramref name="weapons"/>' current gun in hand.</summary>
        public void RequestForge(PlayerWeapons weapons)
        {
            if (weapons == null || _forge == null) return;
            WeaponDefinition def = weapons.CurrentDefinition;
            if (def == null) return;
            WeaponMods mods = weapons.CurrentMods ?? WeaponMods.Standard();
            if (LootRules.ForgePrice(mods.Tier) <= 0) return;
            if (IsAuthority)
            {
                HandleForge(_session != null ? _session.LocalClientId : 0UL, def, mods);
                return;
            }
            byte[] encoded = mods.Encode();
            using (var w = new FastBufferWriter(8 + encoded.Length, Allocator.Temp))
            {
                w.WriteValueSafe(OpForge);
                w.WriteValueSafe((byte)Mathf.Max(0, WeaponCatalog.IndexOf(def)));
                w.WriteValueSafe((byte)encoded.Length);
                foreach (byte b in encoded) w.WriteValueSafe(b);
                _session.SendToServer(Msg, w, NetworkDelivery.ReliableSequenced);
            }
        }

        /// <summary>
        /// The local player pressed BUILD at the Mod Bench: <paramref name="desired"/> is the gun in hand with the picked
        /// parts. The host charges for the new parts and sends the refit back (ammo carries over).
        /// </summary>
        public void RequestModBuild(PlayerWeapons weapons, WeaponMods desired)
        {
            if (weapons == null || _bench == null || desired == null) return;
            WeaponDefinition def = weapons.CurrentDefinition;
            if (def == null || def.Kind != WeaponKind.Firearm) return;
            WeaponMods current = weapons.CurrentMods ?? WeaponMods.Standard();
            if (IsAuthority)
            {
                HandleModBuild(_session != null ? _session.LocalClientId : 0UL, def, current, desired);
                return;
            }
            byte[] a = current.Encode(), b = desired.Encode();
            using (var w = new FastBufferWriter(8 + a.Length + b.Length, Allocator.Temp))
            {
                w.WriteValueSafe(OpModBuild);
                w.WriteValueSafe((byte)Mathf.Max(0, WeaponCatalog.IndexOf(def)));
                w.WriteValueSafe((byte)a.Length);
                foreach (byte x in a) w.WriteValueSafe(x);
                w.WriteValueSafe((byte)b.Length);
                foreach (byte x in b) w.WriteValueSafe(x);
                _session.SendToServer(Msg, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private static WeaponMods ReadMods(FastBufferReader r)
        {
            r.ReadValueSafe(out byte len);
            var bytes = new byte[len];
            for (int i = 0; i < len; i++) r.ReadValueSafe(out bytes[i]);
            return WeaponMods.Decode(bytes);
        }

        private void OnMessage(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out byte op);
            if (op == OpGrant)
            {
                if (_session.IsHost) return;
                r.ReadValueSafe(out byte perk);
                GrantLocal((PerkKind)perk);
                return;
            }
            if (!_session.IsHost) return;
            if (op == OpBuy)
            {
                r.ReadValueSafe(out byte index);
                HandleBuy(sender, index);
            }
            else if (op == OpForge)
            {
                r.ReadValueSafe(out byte defIndex);
                r.ReadValueSafe(out byte len);
                var bytes = new byte[len];
                for (int i = 0; i < len; i++) r.ReadValueSafe(out bytes[i]);
                HandleForge(sender, WeaponCatalog.At(defIndex), WeaponMods.Decode(bytes));
            }
            else if (op == OpModBuild)
            {
                r.ReadValueSafe(out byte defIndex);
                WeaponMods current = ReadMods(r);
                WeaponMods desired = ReadMods(r);
                HandleModBuild(sender, WeaponCatalog.At(defIndex), current, desired);
            }
        }

        private bool CanUse(ulong who, Vector3 at, out WaveManager waves, out RunEconomy economy)
        {
            waves = WaveManager.Current;
            economy = RunEconomy.Current;
            if (waves == null || !waves.IsRunning || economy == null) return false;
            PlayerIdentity p = PlayerIdentity.Find(who);
            return p == null || (p.transform.position - at).sqrMagnitude <= UseRange * UseRange;
        }

        private void HandleBuy(ulong who, int index)
        {
            if (index < 0 || index >= _machines.Count || _machines[index] == null) return;
            PerkMachine m = _machines[index];
            if (!CanUse(who, m.transform.position, out WaveManager waves, out RunEconomy economy)) return;
            if (!waves.TrySpendCredits(who, PerkMachine.PriceOf(m.Perk)))
            {
                economy.Notify(who, "Not enough Field Credits");
                return;
            }
            GameAudio.Play(SfxId.UiConfirm, m.transform.position + Vector3.up * 1.4f);
            if (_session == null || who == _session.LocalClientId) { GrantLocal(m.Perk); return; }
            using (var w = new FastBufferWriter(4, Allocator.Temp))
            {
                w.WriteValueSafe(OpGrant);
                w.WriteValueSafe((byte)m.Perk);
                _session.SendToClient(Msg, who, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void HandleForge(ulong who, WeaponDefinition def, WeaponMods mods)
        {
            if (_forge == null || def == null || mods == null) return;
            if (!CanUse(who, _forge.transform.position, out WaveManager waves, out RunEconomy economy)) return;
            int price = LootRules.ForgePrice(mods.Tier);
            if (price <= 0) return;
            if (!waves.TrySpendCredits(who, price))
            {
                economy.Notify(who, "Not enough Field Credits");
                return;
            }
            WeaponMods up = LootRules.Upgrade(_forgeRng, mods, def.Category, def.ContentId);
            economy.GiveWeapon(who, def, up);
            GameAudio.Play(SfxId.Explosion, _forge.transform.position + Vector3.up * 1.1f, 0.35f);
        }

        private void HandleModBuild(ulong who, WeaponDefinition def, WeaponMods current, WeaponMods desired)
        {
            if (_bench == null || def == null || def.Kind != WeaponKind.Firearm || desired == null) return;
            if (!CanUse(who, _bench.transform.position, out WaveManager waves, out RunEconomy economy))
            {
                if (economy != null && (waves == null || !waves.IsRunning)) economy.Notify(who, "Start a run to use the Mod Bench");
                return;
            }
            WeaponMods next = ModBenchRules.Build(current, desired.Attachments, def.ContentId, def.Category);
            if (ModBenchRules.Same(current, next)) return;
            int price = ModBenchRules.Cost(current, next);
            if (!waves.TrySpendCredits(who, price))
            {
                economy.Notify(who, "Not enough Field Credits");
                return;
            }
            economy.GiveRefit(who, def, next);
            GameAudio.Play(SfxId.UiConfirm, _bench.transform.position + Vector3.up * 1.1f);
        }

        // ---------------------------------------------------------------- local perks

        private void GrantLocal(PerkKind perk)
        {
            Owned[(int)perk] = true;
            _pendingUntil[(int)perk] = 0f;
            GameAudio.Play2D(SfxId.ObjectiveDone);
            RunToasts.Show($"{PerkMachine.NameOf(perk)}: {PerkMachine.EffectOf(perk)}", PerkMachine.ColorOf(perk));
            ApplyStats();
        }

        private void ClearPerks(bool announce)
        {
            bool any = false;
            for (int i = 0; i < Owned.Length; i++) { any |= Owned[i]; Owned[i] = false; }
            if (any && announce) RunToasts.Show("Perks lost", new Color(1f, 0.45f, 0.4f));
            ApplyStats();
        }

        private bool AnyOwned()
        {
            for (int i = 0; i < Owned.Length; i++) if (Owned[i]) return true;
            return false;
        }

        private void TickLocalPlayer()
        {
            PlayerIdentity me = PlayerIdentity.Local;
            if (me == null) return;
            if (_health == null || _health.gameObject != me.gameObject)
            {
                _health = me.GetComponent<PlayerHealth>();
                _status = me.GetComponent<PlayerStatus>();
                PlayerWeapons weapons = me.GetComponent<PlayerWeapons>();
                if (weapons != _hookedWeapons)
                {
                    if (_hookedWeapons != null) _hookedWeapons.RemoveHandlingSource(this);
                    _hookedWeapons = weapons;
                    if (weapons != null) weapons.AddHandlingSource(this);
                }
                ApplyStats();
            }
            // Perks are lost when you go down, and when the run ends.
            bool running = WaveManager.Current != null && WaveManager.Current.IsRunning;
            if (AnyOwned() && ((_health != null && !_health.IsAlive) || !running)) ClearPerks(running);
        }

        private void ApplyStats()
        {
            if (_health != null) _health.SetPerkBonusMaxHealth(Owned[(int)PerkKind.IronHide] ? IronHideHealth : 0f);
            if (_status != null) _status.SprintMultiplier = Owned[(int)PerkKind.FleetFoot] ? FleetSprint : 1f;
        }

        public void ModifyHandling(WeaponDefinition weapon, ref WeaponHandling handling)
        {
            if (!Owned[(int)PerkKind.SwiftHands]) return;
            handling.ReloadSpeed *= SwiftReload;
            handling.EquipSpeed *= SwiftEquip;
        }

        // ---------------------------------------------------------------- HUD

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !AnyOwned()) return;
            HudKit.Begin();
            // A row of perk badges just right of the health card.
            float x = 380f, y = HudKit.H - 28f - 38f;
            for (int i = 0; i < Owned.Length; i++)
            {
                if (!Owned[i]) continue;
                var p = (PerkKind)i;
                var r = new Rect(x, y, 38f, 38f);
                HudKit.Panel(r);
                HudKit.Fill(new Rect(r.x + 3f, r.yMax - 6f, r.width - 6f, 3f), PerkMachine.ColorOf(p));
                HudKit.Label(new Rect(r.x, r.y, r.width, r.height - 4f), PerkMachine.ShortOf(p), 14, PerkMachine.ColorOf(p), TextAnchor.MiddleCenter, head: true);
                x += 44f;
            }
        }
    }
}
