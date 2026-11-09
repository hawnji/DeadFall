using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Encounters;
using Deadfall.Enemies;
using Deadfall.Net;
using Deadfall.Player;
using Deadfall.Rules.Combat;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// Per-player stats for the current run (kills, headshots, revives, downs, credits spent), counted on the host and
    /// sent to clients once a second ("df.stats"). Drawn as the live squad stats page while Tab is held, and on the
    /// end-of-run screen with an MVP. Reset when a new run starts; kept after it ends so the results can show them.
    /// </summary>
    public sealed class RunStatsDirector : MonoBehaviour
    {
        private const string Msg = "df.stats";
        private const float SendInterval = 1f;

        public struct Row
        {
            public ulong Id;
            public int Kills, Headshots, Revives, Downs, Spent, Credits;
            /// <summary>MVP score: kills, headshots and revives count; downs cost.</summary>
            public int Score => Kills * 10 + Headshots * 5 + Revives * 40 - Downs * 15 + Spent / 100;
        }

        private SessionService _session;
        private WaveManager _waves;
        private PlayerInputReader _input;
        private PlayerCursorLock _cursor;
        private readonly Dictionary<ulong, Row> _rows = new Dictionary<ulong, Row>();
        private readonly Dictionary<ulong, LifeState> _lastLife = new Dictionary<ulong, LifeState>();
        private readonly List<Row> _sorted = new List<Row>(4);
        private bool _wasRunning, _dirty;
        private float _nextSend;
        private float _runSeconds;
        private int _surge;

        public static RunStatsDirector Current { get; private set; }

        private bool IsAuthority => _session == null || _session.Role != SessionRole.Client;

        public void Bind(GameObject player)
        {
            _input = player != null ? player.GetComponent<PlayerInputReader>() : null;
            _cursor = player != null ? player.GetComponent<PlayerCursorLock>() : null;
        }

        private void Awake()
        {
            Current = this;
            _session = SessionService.Instance;
            _session?.RegisterHandler(Msg, OnStats);
            EventBus<EnemyDied>.Subscribe(OnEnemyDied);
            SquadLife.Revived += OnRevived;
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            _session?.UnregisterHandler(Msg);
            EventBus<EnemyDied>.Unsubscribe(OnEnemyDied);
            SquadLife.Revived -= OnRevived;
            if (_waves != null) _waves.CreditsSpent -= OnSpent;
        }

        private void Update()
        {
            if (_waves == null && WaveManager.Current != null)
            {
                _waves = WaveManager.Current;
                _waves.CreditsSpent += OnSpent;
            }
            if (_waves == null || !IsAuthority) return;
            bool running = _waves.IsRunning;
            if (running && !_wasRunning)
            {
                _rows.Clear();
                _lastLife.Clear();
                _runSeconds = 0f;
                _dirty = true;
            }
            if (running)
            {
                _runSeconds += Time.deltaTime;
                _surge = _waves.Surge;
                foreach (ulong id in _waves.Players) Touch(id);
                TrackDowns();
                // Current credits change all the time (kills, spending); refresh them with each send.
                if (Time.unscaledTime >= _nextSend)
                {
                    RefreshCredits();
                    Send();
                }
            }
            else if (_wasRunning)
            {
                RefreshCredits();
                Send(); // final numbers for the results screen
            }
            _wasRunning = running;
        }

        private Row Touch(ulong id)
        {
            if (!_rows.TryGetValue(id, out Row r))
            {
                r = new Row { Id = id };
                _rows[id] = r;
                _dirty = true;
            }
            return r;
        }

        private bool Counting => IsAuthority && _waves != null && _waves.IsRunning;

        private void OnEnemyDied(EnemyDied e)
        {
            if (!Counting) return;
            PlayerIdentity killer = PlayerIdentity.Of(e.Killer);
            if (killer == null) return;
            Row r = Touch(killer.ClientId);
            r.Kills++;
            if (e.Headshot) r.Headshots++;
            _rows[killer.ClientId] = r;
            _dirty = true;
        }

        private void OnSpent(ulong id, int amount)
        {
            if (!Counting) return;
            Row r = Touch(id);
            r.Spent += amount;
            _rows[id] = r;
            _dirty = true;
        }

        private void OnRevived(ulong reviver, ulong target)
        {
            if (!Counting) return;
            Row r = Touch(reviver);
            r.Revives++;
            _rows[reviver] = r;
            _dirty = true;
        }

        private void TrackDowns()
        {
            IReadOnlyList<ISquadMember> all = SquadMembers.All;
            for (int i = 0; i < all.Count; i++)
            {
                ISquadMember m = all[i];
                if (m == null || (m is Object o && o == null)) continue;
                ulong id = m.ClientId;
                LifeState before = _lastLife.TryGetValue(id, out LifeState b) ? b : LifeState.Alive;
                if (m.Life == LifeState.Downed && before == LifeState.Alive)
                {
                    Row r = Touch(id);
                    r.Downs++;
                    _rows[id] = r;
                    _dirty = true;
                }
                _lastLife[id] = m.Life;
            }
        }

        private void RefreshCredits()
        {
            if (_waves == null) return;
            var ids = new List<ulong>(_rows.Keys);
            foreach (ulong id in ids)
            {
                Row r = _rows[id];
                int c = _waves.CreditsOf(id);
                if (c != r.Credits) { r.Credits = c; _rows[id] = r; _dirty = true; }
            }
        }

        private void Send()
        {
            _nextSend = Time.unscaledTime + SendInterval;
            if (!_dirty || _session == null || !_session.IsHost) { _dirty = false; return; }
            _dirty = false;
            using (var w = new FastBufferWriter(16 + _rows.Count * 40, Allocator.Temp))
            {
                w.WriteValueSafe((byte)Mathf.Min(_rows.Count, 16));
                w.WriteValueSafe((ushort)Mathf.Clamp(_surge, 0, ushort.MaxValue));
                w.WriteValueSafe(_runSeconds);
                int n = 0;
                foreach (Row r in _rows.Values)
                {
                    if (n++ >= 16) break;
                    w.WriteValueSafe(r.Id);
                    w.WriteValueSafe(r.Kills);
                    w.WriteValueSafe(r.Headshots);
                    w.WriteValueSafe((ushort)Mathf.Clamp(r.Revives, 0, ushort.MaxValue));
                    w.WriteValueSafe((ushort)Mathf.Clamp(r.Downs, 0, ushort.MaxValue));
                    w.WriteValueSafe(r.Spent);
                    w.WriteValueSafe(r.Credits);
                }
                _session.SendToRemoteClients(Msg, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnStats(ulong sender, FastBufferReader r)
        {
            if (_session.IsHost) return;
            r.ReadValueSafe(out byte count);
            r.ReadValueSafe(out ushort surge);
            r.ReadValueSafe(out float seconds);
            _surge = surge;
            _runSeconds = seconds;
            _rows.Clear();
            for (int i = 0; i < count; i++)
            {
                var row = new Row();
                r.ReadValueSafe(out row.Id);
                r.ReadValueSafe(out row.Kills);
                r.ReadValueSafe(out row.Headshots);
                r.ReadValueSafe(out ushort revives);
                r.ReadValueSafe(out ushort downs);
                r.ReadValueSafe(out row.Spent);
                r.ReadValueSafe(out row.Credits);
                row.Revives = revives;
                row.Downs = downs;
                _rows[row.Id] = row;
            }
        }

        // ---------------------------------------------------------------- drawing

        /// <summary>The rows, best score first.</summary>
        public IReadOnlyList<Row> Sorted()
        {
            _sorted.Clear();
            _sorted.AddRange(_rows.Values);
            _sorted.Sort((a, b) => b.Score.CompareTo(a.Score));
            return _sorted;
        }

        public int RowCount => _rows.Count;

        /// <summary>This machine's own row, if it has one.</summary>
        public bool TryLocalRow(out Row row) => _rows.TryGetValue(LocalId, out row);

        private ulong LocalId => _session != null ? _session.LocalClientId : 0UL;

        private string NameOf(ulong id) => id == LocalId ? "You" : $"Player {id}";

        /// <summary>Draws the stats table in <paramref name="area"/> and returns the height used.</summary>
        public float DrawTable(Rect area, bool showCredits)
        {
            IReadOnlyList<Row> rows = Sorted();
            string[] heads = showCredits
                ? new[] { "PLAYER", "KILLS", "HEADSHOTS", "REVIVES", "DOWNS", "SPENT", "CREDITS" }
                : new[] { "PLAYER", "KILLS", "HEADSHOTS", "REVIVES", "DOWNS", "SPENT" };
            float first = area.width * 0.28f;
            float col = (area.width - first) / (heads.Length - 1);
            float X(int i) => i == 0 ? area.x : area.x + first + (i - 1) * col;
            float y = area.y;
            for (int i = 0; i < heads.Length; i++)
                HudKit.Label(new Rect(X(i), y, i == 0 ? first : col, 20f), heads[i], 11, HudKit.TextDim, i == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, head: true);
            y += 24f;
            bool mvp = rows.Count >= 2 && rows[0].Score > 0;
            for (int k = 0; k < rows.Count; k++)
            {
                Row r = rows[k];
                var line = new Rect(area.x - 6f, y, area.width + 12f, 28f);
                if (r.Id == LocalId) HudKit.Round(line, new Color(1f, 1f, 1f, 0.07f));
                string name = NameOf(r.Id) + (mvp && k == 0 ? "   ★ MVP" : string.Empty);
                HudKit.Label(new Rect(X(0), y, first, 28f), name, 15, mvp && k == 0 ? HudKit.Warn : HudKit.Text, TextAnchor.MiddleLeft, head: true);
                int[] v = { r.Kills, r.Headshots, r.Revives, r.Downs, r.Spent, r.Credits };
                for (int i = 1; i < heads.Length; i++)
                    HudKit.Label(new Rect(X(i), y, col, 28f), v[i - 1].ToString("N0"), 15, Color.white, TextAnchor.MiddleCenter, head: true);
                y += 30f;
            }
            if (rows.Count == 0)
            {
                HudKit.Label(new Rect(area.x, y, area.width, 24f), "No stats yet.", 13, HudKit.TextDim);
                y += 26f;
            }
            return y - area.y;
        }

        /// <summary>The MVP's name, or null (solo, or nobody scored).</summary>
        public string MvpName()
        {
            IReadOnlyList<Row> rows = Sorted();
            return rows.Count >= 2 && rows[0].Score > 0 ? NameOf(rows[0].Id) : null;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (_input == null || !_input.Current.ScoreboardHeld) return;
            if (UiModal.Active || (_cursor != null && !_cursor.IsLocked)) return;
            bool running = _waves != null && _waves.IsRunning;
            HudKit.Begin();
            float w = Mathf.Min(760f, HudKit.W - 40f);
            float h = 130f + Mathf.Max(1, _rows.Count) * 30f;
            var r = new Rect((HudKit.W - w) * 0.5f, HudKit.H * 0.2f, w, h);
            HudKit.Panel(r);
            int secs = Mathf.FloorToInt(_runSeconds);
            string sub = running || _rows.Count > 0 ? $"Surge {_surge}   ·   {secs / 60}:{secs % 60:00}" : "Start a run to track stats";
            HudKit.Label(new Rect(r.x + 24f, r.y + 14f, w - 48f, 30f), "SQUAD STATS", 22, Color.white, TextAnchor.MiddleLeft, head: true);
            HudKit.Label(new Rect(r.x + 24f, r.y + 14f, w - 48f, 30f), sub, 14, HudKit.TextDim, TextAnchor.MiddleRight, head: true);
            DrawTable(new Rect(r.x + 24f, r.y + 60f, w - 48f, h - 70f), showCredits: true);
            HudKit.End();
        }
    }
}
