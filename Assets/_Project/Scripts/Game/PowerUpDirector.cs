using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Encounters;
using Deadfall.Enemies;
using Deadfall.Net;
using Deadfall.Rules.Common;
using Deadfall.Rules.World;
using Deadfall.World;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deadfall.Game
{
    /// <summary>
    /// Power-ups during runs. When a player kills an infected, the host sometimes drops a power-up there (capped per
    /// surge). Any squad member who walks over it triggers it for everyone:
    /// Max Ammo refills every gun (and grenades), Double Credits doubles kill credits for 30 s, Insta-Kill makes
    /// any hit kill an infected (not Specimens) for 30 s, and Nuke kills every infected on the map and pays each
    /// player <see cref="NukeCredits"/>. The host owns drops and effects and tells every peer over "df.pu" so all
    /// see the same tokens, banners and timers.
    /// </summary>
    public sealed class PowerUpDirector : MonoBehaviour
    {
        private const string Msg = "df.pu";
        private const byte OpSpawn = 0, OpRemove = 1, OpActivate = 2;

        public const float DropChance = 0.035f;
        public const int MaxPerSurge = 4;
        public const float MinGapSeconds = 12f;
        public const float LifeSeconds = 30f;
        public const float BlinkSeconds = 8f;
        public const float PickupRadius = 1.4f;
        public const float TimedSeconds = 30f;
        public const int NukeCredits = 400;
        private const float InstaKillMultiplier = 10000f;

        private static readonly float[] Weights = { 0.3f, 0.25f, 0.25f, 0.2f }; // Max Ammo, Double, Insta, Nuke

        private SessionService _session;
        private readonly Dictionary<ushort, PowerUpDrop> _drops = new Dictionary<ushort, PowerUpDrop>();
        private readonly List<ushort> _scratch = new List<ushort>(8);
        private readonly float[] _until = new float[4];
        private DeterministicRandom _rng = new DeterministicRandom(0x5057UL);
        private ushort _nextId = 1;
        private int _surgeSeen = -1, _droppedThisSurge;
        private float _lastDropAt = -999f;
        private int _lastKind = -1;
        private bool _nuking;
        private string _banner;
        private Color _bannerColor;
        private float _bannerUntil, _flashUntil;

        public static PowerUpDirector Current { get; private set; }

        /// <summary>Raised on every peer when a Max Ammo is picked up (grenades refill too).</summary>
        public static event System.Action MaxAmmo;

        private bool IsAuthority => _session == null || _session.Role != SessionRole.Client;

        public bool IsActive(PowerUpKind k) => Time.time < _until[(int)k];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            MaxAmmo = null;
            WaveManager.PowerUpCreditMultiplier = 1f;
        }

        private void Awake()
        {
            Current = this;
            _session = SessionService.Instance;
            _session?.RegisterHandler(Msg, OnMessage);
            EventBus<EnemyDied>.Subscribe(OnEnemyDied);
            DamageModifiers.Bonus = InstaKillScale;
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            _session?.UnregisterHandler(Msg);
            EventBus<EnemyDied>.Unsubscribe(OnEnemyDied);
            if (DamageModifiers.Bonus == (System.Func<DamageInfo, GameObject, float>)InstaKillScale) DamageModifiers.Bonus = null;
            WaveManager.PowerUpCreditMultiplier = 1f;
            ClearDrops();
        }

        // ---------------------------------------------------------------- host: drops

        private void OnEnemyDied(EnemyDied e)
        {
            if (!IsAuthority || _nuking) return;
            WaveManager waves = WaveManager.Current;
            if (waves == null || !waves.IsRunning || (e.Definition != null && e.Definition.IsBoss)) return;
            if (PlayerIdentity.Of(e.Killer) == null) return; // only player kills drop power-ups
            if (waves.Surge != _surgeSeen) { _surgeSeen = waves.Surge; _droppedThisSurge = 0; }
            if (_droppedThisSurge >= MaxPerSurge || Time.time - _lastDropAt < MinGapSeconds) return;
            if (_rng.NextFloat() >= DropChance) return;
            Vector3 at = OutOfCloset(e.Position);
            if (NavMesh.SamplePosition(at, out NavMeshHit hit, 2f, NavMesh.AllAreas)) at = hit.position;
            _droppedThisSurge++;
            _lastDropAt = Time.time;
            PowerUpKind kind = RollKind();
            ushort id = _nextId++;
            if (_nextId == 0) _nextId = 1;
            Spawn(id, kind, at);
            Send(OpSpawn, w => { w.WriteValueSafe(id); w.WriteValueSafe((byte)kind); w.WriteValueSafe(at); });
        }

        /// <summary>
        /// A kill inside a breach closet drops its power-up in the room instead, a step in front of the boarded
        /// opening, where players can reach it.
        /// </summary>
        private static Vector3 OutOfCloset(Vector3 p)
        {
            BuiltMap map = MapSession.Current != null ? MapSession.Current.Map : null;
            if (map == null || map.Layout == null) return p;
            float cs = map.CellSize;
            var cell = new Cell(Mathf.FloorToInt(p.x / cs), Mathf.FloorToInt(p.z / cs));
            foreach (Breach br in map.Layout.Breaches)
            {
                if (!br.Closet.Equals(cell)) continue;
                var facing = new Vector3(br.Facing.Dx(), 0f, br.Facing.Dz());
                Vector3 inner = new Vector3((br.Inner.X + 0.5f) * cs, p.y, (br.Inner.Z + 0.5f) * cs);
                // Centre of the room-side cell, pulled a little towards the opening (still well clear of the boards).
                return inner + facing * (cs * 0.15f);
            }
            return p;
        }

        private PowerUpKind RollKind()
        {
            for (int tries = 0; tries < 4; tries++)
            {
                float r = _rng.NextFloat(), acc = 0f;
                int k = Weights.Length - 1;
                for (int i = 0; i < Weights.Length; i++) { acc += Weights[i]; if (r < acc) { k = i; break; } }
                if (k != _lastKind || tries == 3) { _lastKind = k; return (PowerUpKind)k; }
            }
            return PowerUpKind.MaxAmmo;
        }

        private void Update()
        {
            WaveManager waves = WaveManager.Current;
            WaveManager.PowerUpCreditMultiplier = IsActive(PowerUpKind.DoubleCredits) ? 2f : 1f;
            if (!IsAuthority) return;
            if (_drops.Count == 0) return;
            if (waves == null || !waves.IsRunning)
            {
                // Run over: take every drop away everywhere.
                _scratch.Clear();
                _scratch.AddRange(_drops.Keys);
                foreach (ushort id in _scratch) RemoveEverywhere(id);
                return;
            }
            _scratch.Clear();
            foreach (KeyValuePair<ushort, PowerUpDrop> kv in _drops)
            {
                PowerUpDrop d = kv.Value;
                if (d == null || Time.time >= d.ExpiresAt) { _scratch.Add(kv.Key); continue; }
                foreach (PlayerIdentity p in PlayerIdentity.All)
                {
                    if (p == null) continue;
                    ICombatTarget h = p.GetComponent<ICombatTarget>();
                    if (h != null && !h.IsTargetable) continue; // downed players can't grab power-ups
                    Vector3 delta = p.transform.position - d.transform.position;
                    if (Mathf.Abs(delta.y) > 2f || delta.x * delta.x + delta.z * delta.z > PickupRadius * PickupRadius) continue;
                    Activate(d.Kind);
                    _scratch.Add(kv.Key);
                    break;
                }
            }
            foreach (ushort id in _scratch) RemoveEverywhere(id);
        }

        private void RemoveEverywhere(ushort id)
        {
            Remove(id);
            Send(OpRemove, w => w.WriteValueSafe(id));
        }

        /// <summary>Host: carries out a picked-up power-up and tells everyone.</summary>
        private void Activate(PowerUpKind kind)
        {
            WaveManager waves = WaveManager.Current;
            switch (kind)
            {
                case PowerUpKind.MaxAmmo:
                    RunEconomy economy = RunEconomy.Current;
                    if (economy != null) foreach (PlayerIdentity p in PlayerIdentity.All) if (p != null) economy.GiveAmmo(p.ClientId);
                    break;
                case PowerUpKind.Nuke:
                    EnemyManager m = EnemyManager.Current;
                    if (m != null)
                    {
                        _nuking = true;
                        var victims = new List<Enemy>(m.Active);
                        foreach (Enemy e in victims)
                            if (e != null && e.IsAlive && (e.Definition == null || !e.Definition.IsBoss)) e.Kill();
                        _nuking = false;
                    }
                    if (waves != null) foreach (PlayerIdentity p in PlayerIdentity.All) if (p != null) waves.AwardCredits(p.ClientId, NukeCredits, "Nuke");
                    break;
            }
            float seconds = kind == PowerUpKind.DoubleCredits || kind == PowerUpKind.InstaKill ? TimedSeconds : 0f;
            Announce(kind, seconds);
            Send(OpActivate, w => { w.WriteValueSafe((byte)kind); w.WriteValueSafe(seconds); });
        }

        /// <summary>Every peer: banner, sound and the timer for a power-up someone picked up.</summary>
        private void Announce(PowerUpKind kind, float seconds)
        {
            if (seconds > 0f) _until[(int)kind] = Time.time + seconds;
            _banner = PowerUpDrop.NameOf(kind);
            _bannerColor = PowerUpDrop.ColorOf(kind);
            _bannerUntil = Time.time + 2.5f;
            Vector3 at = PlayerIdentity.Local != null ? PlayerIdentity.Local.transform.position + Vector3.up * 1.6f : Vector3.zero;
            if (kind == PowerUpKind.Nuke)
            {
                _flashUntil = Time.time + 0.6f;
                GameAudio.Play(SfxId.Explosion, at);
            }
            else GameAudio.Play(kind == PowerUpKind.DoubleCredits ? SfxId.Credits : SfxId.Pickup, at);
            if (kind == PowerUpKind.MaxAmmo) MaxAmmo?.Invoke();
        }

        // ---------------------------------------------------------------- every peer

        private void Spawn(ushort id, PowerUpKind kind, Vector3 at)
        {
            Remove(id);
            _drops[id] = PowerUpDrop.Create(id, kind, at, Time.time + LifeSeconds);
        }

        private void Remove(ushort id)
        {
            if (_drops.TryGetValue(id, out PowerUpDrop d) && d != null) Destroy(d.gameObject);
            _drops.Remove(id);
        }

        private void ClearDrops()
        {
            foreach (PowerUpDrop d in _drops.Values) if (d != null) Destroy(d.gameObject);
            _drops.Clear();
        }

        private float InstaKillScale(DamageInfo info, GameObject target)
        {
            if (!IsActive(PowerUpKind.InstaKill) || target == null) return 1f;
            Enemy e = target.GetComponentInParent<Enemy>();
            if (e == null || (e.Definition != null && e.Definition.IsBoss)) return 1f;
            return PlayerIdentity.Of(info.Source) != null ? InstaKillMultiplier : 1f;
        }

        private delegate void Writer(FastBufferWriter w);

        private void Send(byte op, Writer write)
        {
            if (_session == null || !_session.IsHost) return;
            using (var w = new FastBufferWriter(32, Allocator.Temp))
            {
                w.WriteValueSafe(op);
                write(w);
                _session.SendToRemoteClients(Msg, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnMessage(ulong sender, FastBufferReader r)
        {
            if (_session.IsHost) return;
            r.ReadValueSafe(out byte op);
            switch (op)
            {
                case OpSpawn:
                    r.ReadValueSafe(out ushort id);
                    r.ReadValueSafe(out byte kind);
                    r.ReadValueSafe(out Vector3 at);
                    Spawn(id, (PowerUpKind)kind, at);
                    break;
                case OpRemove:
                    r.ReadValueSafe(out ushort gone);
                    Remove(gone);
                    break;
                case OpActivate:
                    r.ReadValueSafe(out byte k);
                    r.ReadValueSafe(out float seconds);
                    Announce((PowerUpKind)k, seconds);
                    break;
            }
        }

        // ---------------------------------------------------------------- HUD

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            bool timers = IsActive(PowerUpKind.DoubleCredits) || IsActive(PowerUpKind.InstaKill);
            if (!timers && Time.time >= _bannerUntil && Time.time >= _flashUntil && _drops.Count == 0) return;
            HudKit.Begin();
            if (Time.time < _flashUntil)
                HudKit.Fill(new Rect(0f, 0f, HudKit.W, HudKit.H), new Color(1f, 0.95f, 0.85f, Mathf.Clamp01((_flashUntil - Time.time) / 0.6f) * 0.85f));
            if (Time.time < _bannerUntil)
            {
                float a = Mathf.Clamp01((_bannerUntil - Time.time) / 0.5f);
                Color c = _bannerColor; c.a = a;
                HudKit.Label(new Rect(0f, HudKit.H * 0.22f, HudKit.W, 50f), _banner, 40, c, TextAnchor.MiddleCenter, head: true);
            }
            // Running timers, bottom centre above the hotbar area.
            float x = HudKit.W * 0.5f - 160f, y = HudKit.H - 190f;
            for (int k = 1; k <= 2; k++)
            {
                var kind = (PowerUpKind)k;
                if (!IsActive(kind)) continue;
                float left = _until[k] - Time.time;
                var r = new Rect(x, y, 150f, 30f);
                HudKit.Panel(r);
                Color c = PowerUpDrop.ColorOf(kind);
                HudKit.Fill(new Rect(r.x + 4f, r.yMax - 5f, (r.width - 8f) * Mathf.Clamp01(left / TimedSeconds), 3f), c);
                HudKit.Label(new Rect(r.x + 10f, r.y, r.width - 20f, r.height - 4f), $"{PowerUpDrop.NameOf(kind)}  {Mathf.CeilToInt(left)}", 12, c, TextAnchor.MiddleCenter, head: true);
                x += 170f;
            }
            // Labels over nearby drops.
            Camera cam = Camera.main;
            if (cam != null)
                foreach (PowerUpDrop d in _drops.Values)
                {
                    if (d == null) continue;
                    Vector3 sp = cam.WorldToScreenPoint(d.transform.position + Vector3.up * 1.6f);
                    if (sp.z <= 0f || sp.z > 25f) continue;
                    Vector2 p = HudKit.FromScreen(sp);
                    HudKit.Label(new Rect(p.x - 80f, p.y - 10f, 160f, 20f), PowerUpDrop.NameOf(d.Kind), 12, PowerUpDrop.ColorOf(d.Kind), TextAnchor.MiddleCenter, head: true);
                }
            HudKit.End();
        }
    }
}
