using System;
using System.Collections.Generic;
using Deadfall.Core;
using Deadfall.Encounters;
using Deadfall.Net;
using Deadfall.Player;
using Deadfall.Rules.Combat;
using Deadfall.Rules.Common;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// D-16 drops and rejoins (M11).
    ///
    /// Host: every client says who it is with df.hello (a token kept in PlayerPrefs). When a client drops mid-run its
    /// character stays downed in place and the host holds its slot for 90 seconds (<see cref="ReconnectLedger{T}"/>).
    /// A rejoin with the same token inside the window gets its Field Credits moved to the new connection and its
    /// position, health and life state sent back (df.restore). Otherwise the slot expires and the player is out.
    ///
    /// Client: losing the host mid-run opens the Connection Lost panel with a 90 second Rejoin window (it also retries
    /// on its own). Run rewards are held, not banked, until the player is back in the same run, gives up, or the
    /// window runs out; then they bank exactly once with the failure share (D-03) if the run was not finished.
    /// There is no host migration: if the host is gone, rejoining fails and the window simply runs out.
    /// </summary>
    public sealed class ReconnectDirector : MonoBehaviour
    {
        private const string MsgHello = "df.hello";
        private const string MsgRestore = "df.restore";
        private const string TokenKey = "deadfall.session.token";
        private const float RetryEvery = 8f;

        public static ReconnectDirector Current { get; private set; }

        /// <summary>Host: a held slot was claimed or expired, so the stand-in can go.</summary>
        public static event Action<ulong> GhostReleased;
        /// <summary>Client: the player gave up on rejoining, or the window ran out. Held rewards bank now.</summary>
        public static event Action RejoinAbandoned;
        /// <summary>Client: back in the run the player dropped from.</summary>
        public static event Action Rejoined;

        private readonly ReconnectLedger<SquadLife.MemberRecord> _ledger = new ReconnectLedger<SquadLife.MemberRecord>();
        private readonly Dictionary<ulong, string> _tokens = new Dictionary<ulong, string>();
        private SessionService _session;
        private GameObject _player;
        private PlayerHealth _health;
        private PlayerMotor _motor;
        private bool _wasInRun;
        private string _token;

        // Client drop state.
        private bool _dropped;
        private float _dropDeadline;
        private float _nextRetry;
        private string _dropReason;

        /// <summary>Client: the Connection Lost window is open.</summary>
        public bool Dropped => _dropped;
        public float SecondsLeft => _dropped ? Mathf.Max(0f, _dropDeadline - Time.unscaledTime) : 0f;
        public int HeldSlots => _ledger.Count;

        public void Bind(GameObject player)
        {
            _player = player;
            _health = player.GetComponent<PlayerHealth>();
            _motor = player.GetComponent<PlayerMotor>();
        }

        private string Token
        {
            get
            {
                if (!string.IsNullOrEmpty(_token)) return _token;
                _token = PlayerPrefs.GetString(TokenKey, string.Empty);
                if (_token.Length != 32)
                {
                    _token = Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(TokenKey, _token);
                    PlayerPrefs.Save();
                }
                return _token;
            }
        }

        private void Awake()
        {
            Current = this;
            _session = SessionService.Instance;
            _session.RegisterHandler(MsgHello, OnHello);
            _session.RegisterHandler(MsgRestore, OnRestore);
            _session.Started += OnStarted;
            _session.Stopped += OnStopped;
            _session.PeerDisconnected += OnPeerDisconnected;
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            if (_session == null) return;
            _session.UnregisterHandler(MsgHello);
            _session.UnregisterHandler(MsgRestore);
            _session.Started -= OnStarted;
            _session.Stopped -= OnStopped;
            _session.PeerDisconnected -= OnPeerDisconnected;
        }

        private static bool RunActive => WaveManager.Current != null && WaveManager.Current.IsRunning;

        /// <summary>Host: true when <paramref name="clientId"/> dropping now gets a held slot.</summary>
        public bool WillHold(ulong clientId) => _session.IsHost && RunActive && _tokens.ContainsKey(clientId);

        private void Update()
        {
            // Read before Stopped fires: by then the run state has already been cleared on a client.
            if (_session.IsConnectedClient) _wasInRun = RunActive;

            if (_session.IsHost || !_session.IsActive)
            {
                List<ulong> gone = _ledger.Expire(Time.unscaledTimeAsDouble);
                foreach (ulong id in gone)
                {
                    GhostReleased?.Invoke(id);
                    RunToasts.Show($"Player {id} did not reconnect", HudKit.Warn, 4f);
                }
                if (!RunActive && _ledger.Count > 0)
                {
                    foreach (ulong id in HeldIds()) GhostReleased?.Invoke(id);
                    _ledger.Clear();
                }
            }

            if (!_dropped) return;
            if (Time.unscaledTime >= _dropDeadline) { Abandon("The rejoin window ran out"); return; }
            if (!_session.IsActive && Time.unscaledTime >= _nextRetry)
            {
                _nextRetry = Time.unscaledTime + RetryEvery;
                _session.Rejoin();
            }
        }

        private readonly List<ulong> _heldIds = new List<ulong>();
        private List<ulong> HeldIds()
        {
            // The ledger hides its keys; expiring everything "now + window" returns them all.
            _heldIds.Clear();
            _heldIds.AddRange(_ledger.Expire(double.MaxValue));
            return _heldIds;
        }

        // ---------------------------------------------------------------- host

        private void OnStarted(SessionRole role)
        {
            if (role != SessionRole.Client) { _tokens.Clear(); _ledger.Clear(); return; }
            using (var w = new FastBufferWriter(64, Allocator.Temp))
            {
                w.WriteValueSafe(Token);
                _session.SendToServer(MsgHello, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnHello(ulong sender, FastBufferReader r)
        {
            if (!_session.IsHost) return;
            r.ReadValueSafe(out string token);
            if (string.IsNullOrEmpty(token) || token.Length > 64) return;
            _tokens[sender] = token;
            if (!_ledger.TryClaim(token, Time.unscaledTimeAsDouble, out ulong oldId, out SquadLife.MemberRecord rec)) return;
            if (!RunActive) { GhostReleased?.Invoke(oldId); return; }
            if (WaveManager.Current != null) WaveManager.Current.TransferCredits(oldId, sender);
            GhostReleased?.Invoke(oldId);
            using (var w = new FastBufferWriter(64, Allocator.Temp))
            {
                w.WriteValueSafe(rec.Position);
                w.WriteValueSafe(rec.Yaw);
                w.WriteValueSafe((byte)rec.Life);
                w.WriteValueSafe(rec.Health01);
                w.WriteValueSafe((byte)Mathf.Clamp(rec.Downs, 0, 255));
                w.WriteValueSafe(rec.BleedOutLeft);
                _session.SendToClient(MsgRestore, sender, w, NetworkDelivery.ReliableSequenced);
            }
            GameLog.Info("Session", $"Player {oldId} rejoined as {sender}");
            RunToasts.Show($"Player {oldId} reconnected (now player {sender})", HudKit.Good, 4f);
        }

        private void OnPeerDisconnected(ulong clientId)
        {
            // The token stays mapped (NGO never reuses client ids), so WillHold answers the same whichever
            // PeerDisconnected handler runs first.
            if (!_tokens.TryGetValue(clientId, out string token) || !RunActive) return;
            SquadLife.MemberRecord rec = default;
            if (SquadLife.Current != null) SquadLife.Current.TryGetRecord(clientId, out rec);
            // D-16: the character is downed in place; an eliminated player stays eliminated.
            if (rec.Life == LifeState.Alive) { rec.Life = LifeState.Downed; rec.BleedOutLeft = DownedRules.BleedOutSeconds(Mathf.Max(1, rec.Downs + 1)); rec.Downs++; }
            _ledger.Hold(token, clientId, rec, Time.unscaledTimeAsDouble);
            RunToasts.Show($"Player {clientId} dropped. Holding their spot for 90 s", HudKit.Warn, 5f);
        }

        // ---------------------------------------------------------------- client

        private void OnRestore(ulong sender, FastBufferReader r)
        {
            if (_session.IsHost || _health == null) return;
            r.ReadValueSafe(out Vector3 pos);
            r.ReadValueSafe(out float yaw);
            r.ReadValueSafe(out byte life);
            r.ReadValueSafe(out float health01);
            r.ReadValueSafe(out byte downs);
            r.ReadValueSafe(out float bleed);
            if (life > (byte)LifeState.Eliminated || float.IsNaN(pos.x) || float.IsInfinity(pos.x)) return;
            _health.Restore((LifeState)life, health01, downs, bleed);
            if (_motor != null) _motor.Teleport(pos, yaw);
            RunToasts.Show("Reconnected. Back in the fight", HudKit.Good, 4f);
        }

        private void OnStopped()
        {
            if (_session.WasDropped && _wasInRun && !_dropped)
            {
                _dropped = true;
                _dropDeadline = Time.unscaledTime + (float)ReconnectLedger<int>.WindowSeconds;
                _nextRetry = Time.unscaledTime + 3f;
                _dropReason = _session.LastError;
                GameLog.Warn("Session", "Lost the host mid-run. Rejoin window open.");
            }
            _wasInRun = false;
        }

        /// <summary>Client: the phase sync says we are back inside a run after a drop.</summary>
        public void NotifyBackInRun()
        {
            if (!_dropped) return;
            _dropped = false;
            Rejoined?.Invoke();
        }

        private void Abandon(string why)
        {
            if (!_dropped) return;
            _dropped = false;
            if (_session.IsActive && !_session.IsConnectedClient) _session.Stop();
            GameLog.Info("Session", why);
            RejoinAbandoned?.Invoke();
        }

        private void OnGUI()
        {
            if (!_dropped) return;
            HudKit.Begin();
            Rect win = HudKit.Window(520f, 230f, "CONNECTION LOST", $"{Mathf.CeilToInt(SecondsLeft)}s", HudKit.Bad, backdrop: true);
            HudKit.Label(new Rect(win.x + 24f, win.y + 56f, win.width - 48f, 44f),
                string.IsNullOrEmpty(_dropReason) ? "The connection to the host dropped." : _dropReason, 14, HudKit.Text, TextAnchor.UpperLeft, wrap: true);
            string status = _session.IsConnecting ? "Reconnecting..." : $"Your spot is held for {Mathf.CeilToInt(SecondsLeft)} more seconds. Run rewards are kept until then.";
            HudKit.Label(new Rect(win.x + 24f, win.y + 104f, win.width - 48f, 40f), status, 13, HudKit.TextDim, TextAnchor.UpperLeft, wrap: true);
            if (HudKit.Button(new Rect(win.x + 24f, win.y + win.height - 60f, 220f, 38f), "Rejoin", on: true, enabled: !_session.IsActive))
            {
                _nextRetry = Time.unscaledTime + RetryEvery;
                _session.Rejoin();
            }
            if (HudKit.Button(new Rect(win.x + win.width - 244f, win.y + win.height - 60f, 220f, 38f), "Return to FOB"))
                Abandon("Gave up rejoining");
            HudKit.End();
        }
    }
}
