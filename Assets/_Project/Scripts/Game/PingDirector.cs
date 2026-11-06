using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Enemies;
using Deadfall.Net;
using Deadfall.Player;
using Deadfall.Rules.Common;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// Squad pings (middle mouse by default): marks the infected, item or spot you're looking at with an on-screen
    /// marker everyone in the squad sees for a few seconds. An enemy ping follows that enemy. Clients send "df.ping"
    /// to the host, which relays it to the other clients.
    /// </summary>
    public sealed class PingDirector : MonoBehaviour
    {
        private const string Msg = "df.ping";
        private const float Range = 80f;
        private const float LifeSeconds = 8f;
        private const float EnemyLifeSeconds = 6f;
        private const float Cooldown = 0.35f;
        private const int MaxPerPlayer = 1;
        private const int Mask = ~(1 << 2);

        public enum Kind : byte { Spot, Enemy, Item }

        private struct Ping
        {
            public ulong Owner;
            public Kind Kind;
            public Vector3 Position;
            public string Label;
            public float Until;
            public Transform Follow;
        }

        private SessionService _session;
        private PlayerInputReader _input;
        private GameObject _player;
        private float _nextAllowed;
        private readonly List<Ping> _pings = new List<Ping>(12);
        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        public static PingDirector Current { get; private set; }

        public void Bind(GameObject player)
        {
            _player = player;
            _input = player != null ? player.GetComponent<PlayerInputReader>() : null;
        }

        private void Awake()
        {
            Current = this;
            _session = SessionService.Instance;
            _session?.RegisterHandler(Msg, OnPing);
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            _session?.UnregisterHandler(Msg);
        }

        private ulong LocalId => _session != null ? _session.LocalClientId : 0UL;

        private void Update()
        {
            for (int i = _pings.Count - 1; i >= 0; i--)
            {
                Ping p = _pings[i];
                if (Time.time >= p.Until) { _pings.RemoveAt(i); continue; }
                if (p.Kind == Kind.Enemy)
                {
                    // Track the marked enemy; drop the ping when it dies or vanishes.
                    Enemy e = p.Follow != null ? p.Follow.GetComponent<Enemy>() : null;
                    if (e == null || !e.IsAlive || !e.isActiveAndEnabled) { _pings.RemoveAt(i); continue; }
                    p.Position = p.Follow.position;
                    _pings[i] = p;
                }
            }
            if (_input != null && _input.Current.PingPressed && Time.time >= _nextAllowed) TryPing();
        }

        private void TryPing()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            PlayerHealth health = _player != null ? _player.GetComponent<PlayerHealth>() : null;
            if (health != null && health.IsEliminated) return;
            _nextAllowed = Time.time + Cooldown;
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            int n = Physics.SphereCastNonAlloc(ray, 0.25f, Hits, Range, Mask, QueryTriggerInteraction.Collide);
            // Nearest useful hit: an enemy or an interactable beats the first solid surface behind it.
            float bestWall = float.MaxValue;
            Vector3 wallPoint = ray.origin + ray.direction * 20f;
            Enemy enemy = null;
            float enemyDist = float.MaxValue;
            IInteractable item = null;
            Vector3 itemPoint = default;
            float itemDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = Hits[i];
                if (h.collider == null || (_player != null && h.collider.transform.IsChildOf(_player.transform))) continue;
                float d = h.distance;
                Vector3 point = d > 0f ? h.point : ray.origin;
                var en = h.collider.GetComponentInParent<Enemy>();
                if (en != null && en.IsAlive) { if (d < enemyDist) { enemyDist = d; enemy = en; } continue; }
                var it = h.collider.GetComponentInParent<IInteractable>();
                if (it != null && d < itemDist) { itemDist = d; item = it; itemPoint = ((Component)it).transform.position + Vector3.up * 1.2f; continue; }
                if (!h.collider.isTrigger && d < bestWall && d > 0f) { bestWall = d; wallPoint = point; }
            }
            if (enemy != null && enemyDist <= bestWall + 0.5f)
                Send(Kind.Enemy, enemy.transform.position, enemy.name);
            else if (item != null && itemDist <= bestWall + 0.5f)
                Send(Kind.Item, itemPoint, Shorten(item.InteractPrompt(_player)));
            else
                Send(Kind.Spot, bestWall < float.MaxValue ? wallPoint : ray.origin + ray.direction * 20f, string.Empty);
        }

        /// <summary>"Buy Swift Hands: faster reloads (1,200 credits)" becomes "Buy Swift Hands".</summary>
        private static string Shorten(string prompt)
        {
            if (string.IsNullOrEmpty(prompt)) return "Item";
            int cut = prompt.IndexOfAny(new[] { '(', ':' });
            string s = (cut > 0 ? prompt.Substring(0, cut) : prompt).Trim();
            return s.Length > 32 ? s.Substring(0, 32) : s;
        }

        private void Send(Kind kind, Vector3 at, string label)
        {
            ulong me = LocalId;
            Add(me, kind, at, label);
            GameAudio.Play2D(SfxId.UiClick, 0.8f, 1.4f);
            if (_session == null || !_session.IsActive) return;
            using (var w = new FastBufferWriter(512, Allocator.Temp))
            {
                w.WriteValueSafe(me);
                w.WriteValueSafe((byte)kind);
                w.WriteValueSafe(at);
                w.WriteValueSafe(label ?? string.Empty);
                if (_session.IsHost) _session.SendToRemoteClients(Msg, w, NetworkDelivery.ReliableSequenced);
                else _session.SendToServer(Msg, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnPing(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out ulong owner);
            r.ReadValueSafe(out byte kind);
            r.ReadValueSafe(out Vector3 at);
            r.ReadValueSafe(out string label);
            if (_session.IsHost)
            {
                owner = sender; // the host vouches for who sent it
                using (var w = new FastBufferWriter(512, Allocator.Temp))
                {
                    w.WriteValueSafe(owner);
                    w.WriteValueSafe(kind);
                    w.WriteValueSafe(at);
                    w.WriteValueSafe(label ?? string.Empty);
                    _session.SendToRemoteClients(Msg, w, NetworkDelivery.ReliableSequenced, sender);
                }
            }
            if (owner == LocalId) return;
            Add(owner, (Kind)Mathf.Min(kind, (byte)Kind.Item), at, label);
            GameAudio.Play2D(SfxId.UiClick, 0.6f, 1.2f);
        }

        private void Add(ulong owner, Kind kind, Vector3 at, string label)
        {
            int mine = 0, oldest = -1;
            for (int i = 0; i < _pings.Count; i++)
            {
                if (_pings[i].Owner != owner) continue;
                mine++;
                if (oldest < 0 || _pings[i].Until < _pings[oldest].Until) oldest = i;
            }
            if (mine >= MaxPerPlayer && oldest >= 0) _pings.RemoveAt(oldest);
            Transform follow = null;
            if (kind == Kind.Enemy)
            {
                // Each peer has its own enemy objects: follow whichever living one is nearest the marked spot.
                EnemyManager em = EnemyManager.Current;
                float best = 3f * 3f;
                if (em != null)
                    foreach (Enemy e in em.Active)
                    {
                        if (e == null || !e.IsAlive) continue;
                        float d = (e.transform.position - at).sqrMagnitude;
                        if (d < best) { best = d; follow = e.transform; }
                    }
                if (follow == null) kind = Kind.Spot;
            }
            _pings.Add(new Ping
            {
                Owner = owner, Kind = kind, Position = at, Label = label ?? string.Empty, Follow = follow,
                Until = Time.time + (kind == Kind.Enemy ? EnemyLifeSeconds : LifeSeconds),
            });
        }

        private static Color ColorOf(Kind k) => k == Kind.Enemy ? new Color(1f, 0.3f, 0.25f) : k == Kind.Item ? new Color(1f, 0.85f, 0.25f) : new Color(0.55f, 0.85f, 1f);

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || _pings.Count == 0) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            HudKit.Begin();
            Vector3 eye = cam.transform.position;
            ulong me = LocalId;
            foreach (Ping p in _pings)
            {
                Vector3 world = p.Position + (p.Kind == Kind.Enemy ? Vector3.up * 2.1f : Vector3.up * 0.3f);
                Vector3 sp = cam.WorldToScreenPoint(world);
                bool behind = sp.z <= 0f;
                Vector2 g = HudKit.FromScreen(sp);
                if (behind) g = new Vector2(HudKit.W - g.x, HudKit.H - 40f);
                // Off screen: pin to the edge so you can still find it.
                g.x = Mathf.Clamp(g.x, 30f, HudKit.W - 30f);
                g.y = Mathf.Clamp(g.y, 30f, HudKit.H - 40f);
                float a = Mathf.Clamp01((p.Until - Time.time) / 0.6f);
                Color c = ColorOf(p.Kind); c.a = a;
                float pulse = 1f + 0.15f * Mathf.Sin(Time.time * 8f);
                float s = 10f * pulse;
                HudKit.Round(new Rect(g.x - s, g.y - s, s * 2f, s * 2f), new Color(0f, 0f, 0f, 0.45f * a));
                HudKit.Round(new Rect(g.x - s * 0.55f, g.y - s * 0.55f, s * 1.1f, s * 1.1f), c, true);
                int metres = Mathf.RoundToInt(Vector3.Distance(eye, p.Position));
                string who = p.Owner == me ? string.Empty : $"P{p.Owner}  ";
                string what = p.Kind == Kind.Enemy ? "ENEMY" : p.Kind == Kind.Item ? p.Label.ToUpperInvariant() : "HERE";
                HudKit.Label(new Rect(g.x - 120f, g.y + 12f, 240f, 18f), $"{who}{what}  {metres} m", 12, c, TextAnchor.UpperCenter, head: true);
            }
            HudKit.End();
        }
    }
}
