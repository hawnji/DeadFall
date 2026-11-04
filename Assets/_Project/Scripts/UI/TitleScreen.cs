using System;
using Deadfall.Core;
using Deadfall.Net;
using Deadfall.Player;
using Deadfall.Rules.Common;
using UnityEngine;

namespace Deadfall.UI
{
    /// <summary>
    /// The title screen shown when the game starts: a slow camera drift through FOB Halcyon behind the DEADFALL logo,
    /// with Play solo, Host co-op, Join co-op, Settings and Quit. The game world is already built underneath; the
    /// title just holds the cursor, swaps in its own camera and hides the in-game HUD until the player picks an option.
    /// Plays the title theme (Resources/Music/TitleTheme) on the Music slider. "Quit to title" on the pause screen
    /// leaves co-op, returns to the FOB and shows it again (<see cref="ReturnToTitle"/>).
    /// The first showing is skipped with <c>-skiptitle</c>, <c>-deadfall-host</c> or <c>-deadfall-join</c> on the command line.
    /// </summary>
    public sealed class TitleScreen : MonoBehaviour
    {
        private const string Owner = "title";
        private const string PrefAddress = "deadfall.net.address";
        private const string PrefPort = "deadfall.net.port";
        private const float FadeIn = 1.4f;
        private const float FadeOut = 0.45f;
        private const string Theme = "Music/TitleTheme";

        private GameObject _player;
        private GameObject _hud;
        private PlayerCursorLock _cursor;
        private Camera[] _playerCameras = Array.Empty<Camera>();
        private Camera _camera;
        private SettingsPanel _settings;
        private Vector3 _center;
        private Action _returnToBase;
        private bool _returnRequested;

        private bool _open;
        private enum Panel { None, Online, Lan }
        private Panel _panel;
        private bool _joining;
        private bool _hostingOnline;
        private string _code = string.Empty;
        private string _address = SessionService.DefaultAddress;
        private string _port = SessionService.DefaultPort.ToString();
        private string _error = string.Empty;
        private float _openedAt;
        private float _closingAt = -1f;

        public static bool Showing { get; private set; }
        public static TitleScreen Current { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Showing = false; Current = null; }

        /// <param name="player">The local player, frozen while the title is up.</param>
        /// <param name="hud">The in-game HUD root, hidden while the title is up.</param>
        /// <param name="fobOrigin">FOB Halcyon's origin; the title camera drifts inside it.</param>
        /// <param name="returnToBase">Ends any run or map and puts the player back at the FOB (Quit to title).</param>
        public void Bind(GameObject player, GameObject hud, Vector3 fobOrigin, Action returnToBase)
        {
            Current = this;
            _returnToBase = returnToBase;
            _player = player;
            _hud = hud;
            _center = fobOrigin;
            _cursor = player != null ? player.GetComponent<PlayerCursorLock>() : null;
        }

        public static bool SkippedByCommandLine()
        {
            foreach (string a in Environment.GetCommandLineArgs())
                if (a == "-skiptitle" || a == "-deadfall-host" || a == "-deadfall-join") return true;
            return false;
        }

        private void Start()
        {
            _address = PlayerPrefs.GetString(PrefAddress, SessionService.DefaultAddress);
            _port = PlayerPrefs.GetInt(PrefPort, SessionService.DefaultPort).ToString();
            _settings = gameObject.AddComponent<SettingsPanel>();
            _settings.Bind(_cursor);
            if (!SkippedByCommandLine()) Open();
        }

        /// <summary>Leaves any co-op session, returns to the FOB and shows the title again (pause screen).</summary>
        public void ReturnToTitle()
        {
            // Deferred to Update: this is called from the pause screen's OnGUI, which Open() hides.
            if (!_open) _returnRequested = true;
        }

        private void DoReturnToTitle()
        {
            _returnRequested = false;
            if (_open) return;
            SessionService s = SessionService.Current;
            if (s != null && s.IsActive) s.Stop();
            try { _returnToBase?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
            Open();
        }

        private void Open()
        {
            if (_open) return;
            _open = true;
            Showing = true;
            _openedAt = Time.unscaledTime;
            _closingAt = -1f;
            _panel = Panel.None;
            _joining = _hostingOnline = false;
            _error = string.Empty;
            GameAudio.PlayMusic(Theme, 2f);
            UiModal.TryOpen(Owner);
            if (_hud != null) _hud.SetActive(false);

            if (_player != null) _playerCameras = _player.GetComponentsInChildren<Camera>();
            foreach (Camera c in _playerCameras) if (c != null) { c.enabled = false; c.tag = "Untagged"; }
            var go = new GameObject("[TitleCamera]");
            go.tag = "MainCamera";
            _camera = go.AddComponent<Camera>();
            _camera.fieldOfView = 55f;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 200f;
            PlaceCamera(0f);
        }

        private void Close()
        {
            if (!_open) return;
            _open = false;
            _closingAt = Time.unscaledTime;
            if (_settings != null) _settings.Close();
            UiModal.Close(Owner);
            if (_hud != null) _hud.SetActive(true);
            foreach (Camera c in _playerCameras) if (c != null) { c.enabled = true; c.tag = "MainCamera"; }
            if (_camera != null) Destroy(_camera.gameObject);
            Showing = false;
            GameAudio.StopMusic(2.5f);
            if (_cursor != null) _cursor.SetLocked(true);
            GameAudio.Play2D(SfxId.UiConfirm);
        }

        private void OnDestroy()
        {
            if (_open) Close();
            if (Current == this) Current = null;
        }

        /// <summary>A slow sideways drift along the south end of the hall, looking up the lane at Command Center.</summary>
        private void PlaceCamera(float t)
        {
            if (_camera == null) return;
            float sway = Mathf.Sin(t * 0.07f);
            Vector3 pos = _center + new Vector3(sway * 9f, 3.4f + Mathf.Sin(t * 0.11f) * 0.25f, -11.5f);
            Vector3 look = _center + new Vector3(-sway * 4f, 1.6f, 9f);
            _camera.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
        }

        private void Update()
        {
            if (_returnRequested) DoReturnToTitle();
            if (!_open) return;
            PlaceCamera(Time.unscaledTime - _openedAt);
            // The cursor stays free (Esc would otherwise lock it and wake up gameplay input).
            if (_cursor != null && _cursor.IsLocked) _cursor.SetLocked(false);
            // Settings borrows the modal slot; take it back when that window closes.
            if (_settings != null && !_settings.IsOpen) UiModal.TryOpen(Owner);

            SessionService s = SessionService.Current;
            if (_joining && s != null)
            {
                if (s.IsConnectedClient) { _joining = false; Close(); }
                else if (!s.IsActive) { _joining = false; _error = string.IsNullOrEmpty(s.LastError) ? "Could not connect." : s.LastError; }
            }
        }

        // ------------------------------------------------------------------------------------------ drawing

        private void OnGUI()
        {
            float now = Time.unscaledTime;
            if (!_open)
            {
                // A short fade from black into the game after the title closes.
                if (_closingAt < 0f || now - _closingAt > FadeOut) return;
                HudKit.Begin();
                GUI.depth = -60;
                float a = 1f - Mathf.Clamp01((now - _closingAt) / FadeOut);
                HudKit.Fill(new Rect(0f, 0f, HudKit.W, HudKit.H), new Color(0f, 0f, 0f, a * 0.85f));
                HudKit.End();
                return;
            }
            if (_settings != null && _settings.IsOpen) return;

            HudKit.Begin();
            GUI.skin = HudKit.Skin;
            GUI.depth = -50;
            float w = HudKit.W, h = HudKit.H;

            // Darken the left side for the menu and edge the whole frame in a dim red.
            const int strips = 24;
            float band = Mathf.Min(900f, w * 0.62f);
            for (int i = 0; i < strips; i++)
            {
                float t = i / (float)strips;
                HudKit.Fill(new Rect(band * t, 0f, band / strips + 1f, h), new Color(0.01f, 0.012f, 0.02f, Mathf.Lerp(0.88f, 0f, t * t)));
            }
            HudKit.Vignette(new Color(0.25f, 0.02f, 0.02f, 0.55f));
            HudKit.Fill(new Rect(0f, h - 70f, w, 70f), new Color(0f, 0f, 0f, 0.35f));

            float x = Mathf.Max(48f, w * 0.07f);
            float y = h * 0.2f;
            HudKit.Label(new Rect(x - 4f, y, 900f, 130f), "DEADFALL", 118, Color.white, TextAnchor.MiddleLeft, head: true);
            HudKit.Fill(new Rect(x, y + 132f, 96f, 5f), HudKit.Accent);
            HudKit.Fill(new Rect(x + 104f, y + 134f, 300f, 1f), new Color(1f, 1f, 1f, 0.15f));
            HudKit.Label(new Rect(x, y + 146f, 700f, 30f), "CO-OP ZOMBIE SURVIVAL", 20, HudKit.TextDim, TextAnchor.MiddleLeft, head: true);

            float by = y + 220f;
            const float bw = 300f, bh = 50f, gap = 12f;
            bool busy = _joining || _hostingOnline || OnlineRelay.Busy;
            bool online = OnlineRelay.Available;
            if (MenuButton(new Rect(x, by, bw, bh), "PLAY SOLO", !busy)) Close();
            by += bh + gap;
            if (MenuButton(new Rect(x, by, bw, bh), _hostingOnline ? "GOING ONLINE..." : "HOST ONLINE", online && !busy)) HostOnline();
            by += bh + gap;
            if (MenuButton(new Rect(x, by, bw, bh), "JOIN ONLINE", online && !busy, _panel == Panel.Online)) Toggle(Panel.Online);
            float onlineY = by;
            by += bh + gap;
            if (MenuButton(new Rect(x, by, bw, bh), "LAN / IP", !busy, _panel == Panel.Lan)) Toggle(Panel.Lan);
            float lanY = by;
            by += bh + gap;
            if (MenuButton(new Rect(x, by, bw, bh), "SETTINGS", !busy))
            {
                UiModal.Close(Owner);
                _settings.Open();
            }
            by += bh + gap;
            if (MenuButton(new Rect(x, by, bw, bh), Application.isEditor ? "QUIT (BUILDS ONLY)" : "QUIT", !Application.isEditor && !busy))
                Application.Quit();

            if (_panel == Panel.Online) DrawJoinOnline(new Rect(x + bw + 24f, onlineY - 40f, 380f, 190f));
            else if (_panel == Panel.Lan) DrawLan(new Rect(x + bw + 24f, lanY - 90f, 380f, 270f));
            if (!online)
                HudKit.Label(new Rect(x, by + 4f, 520f, 40f), "Online play needs the Multiplayer Services package (Window > Package Manager).", 12, HudKit.TextDim, TextAnchor.UpperLeft, wrap: true);
            else if (_panel == Panel.None && !string.IsNullOrEmpty(_error))
                HudKit.Label(new Rect(x, by + 4f, 520f, 40f), _error, 13, HudKit.Bad, TextAnchor.UpperLeft, wrap: true);

            HudKit.Label(new Rect(x, h - 50f, 600f, 30f), "WASD move  ·  Mouse aim  ·  F interact  ·  Esc pause", 14, HudKit.TextDim, TextAnchor.MiddleLeft, head: true);
            HudKit.Label(new Rect(w - 420f, h - 50f, 400f, 30f), BuildInfo.Label, 14, HudKit.TextDim, TextAnchor.MiddleRight, head: true);

            // Fade in from black when the game first opens.
            float fade = 1f - Mathf.Clamp01((now - _openedAt) / FadeIn);
            if (fade > 0f) HudKit.Fill(new Rect(0f, 0f, w, h), new Color(0f, 0f, 0f, fade));
            HudKit.End();
        }

        private static bool MenuButton(Rect r, string text, bool enabled, bool on = false)
        {
            bool hover = enabled && r.Contains(Event.current.mousePosition);
            bool clicked = HudKit.Button(r, string.Empty, on, enabled, 18);
            if (hover || on) HudKit.Fill(new Rect(r.x, r.y + 8f, 4f, r.height - 16f), HudKit.Accent);
            HudKit.Label(new Rect(r.x + 22f, r.y, r.width - 30f, r.height), text, 19, enabled ? Color.white : HudKit.TextDim, TextAnchor.MiddleLeft, head: true);
            return clicked;
        }

        private void Toggle(Panel p)
        {
            _panel = _panel == p ? Panel.None : p;
            _error = string.Empty;
        }

        private void DrawJoinOnline(Rect r)
        {
            HudKit.Panel(r);
            float x = r.x + 18f, y = r.y + 14f, iw = r.width - 36f;
            HudKit.Label(new Rect(x, y, iw, 26f), "JOIN WITH A CODE", 17, HudKit.Accent, TextAnchor.MiddleLeft, head: true);
            y += 40f;
            GUI.enabled = !_joining;
            _code = OnlineRelay.CleanCode(GUI.TextField(new Rect(x, y, iw, 40f), _code, 12));
            GUI.enabled = true;
            y += 52f;
            if (_joining || OnlineRelay.Busy) Connecting(r, x, y, iw, _code);
            else
            {
                if (HudKit.Button(new Rect(x, y, 140f, 36f), "Join", on: true, enabled: _code.Length >= 4)) JoinOnline();
                string hint = string.IsNullOrEmpty(_error) ? "Ask the host for the code shown on their screen." : _error;
                HudKit.Label(new Rect(x + 152f, y - 4f, iw - 152f, 44f), hint, 12, string.IsNullOrEmpty(_error) ? HudKit.TextDim : HudKit.Bad, TextAnchor.MiddleLeft, wrap: true);
            }
        }

        private void DrawLan(Rect r)
        {
            HudKit.Panel(r);
            float x = r.x + 18f, y = r.y + 14f, iw = r.width - 36f;
            HudKit.Label(new Rect(x, y, iw, 26f), "LAN / DIRECT IP", 17, HudKit.Accent, TextAnchor.MiddleLeft, head: true);
            y += 38f;
            HudKit.Label(new Rect(x, y, 80f, 30f), "Address", 14, HudKit.Text);
            GUI.enabled = !_joining;
            _address = GUI.TextField(new Rect(x + 84f, y, iw - 84f, 30f), _address, 64);
            y += 40f;
            HudKit.Label(new Rect(x, y, 80f, 30f), "Port", 14, HudKit.Text);
            _port = GUI.TextField(new Rect(x + 84f, y, 100f, 30f), _port, 5);
            GUI.enabled = true;
            y += 44f;
            if (_joining) Connecting(r, x, y, iw, _address);
            else
            {
                if (HudKit.Button(new Rect(x, y, 160f, 36f), "Join", on: true)) Join();
                if (HudKit.Button(new Rect(x + 172f, y, 160f, 36f), "Host on LAN")) Host();
                y += 46f;
                string hint = string.IsNullOrEmpty(_error) ? "Same PC: 127.0.0.1. LAN: the host's IP. Over the internet the host must forward the port." : _error;
                HudKit.Label(new Rect(x, y, iw, 44f), hint, 12, string.IsNullOrEmpty(_error) ? HudKit.TextDim : HudKit.Bad, TextAnchor.UpperLeft, wrap: true);
            }
        }

        private void Connecting(Rect r, float x, float y, float iw, string target)
        {
            HudKit.Label(new Rect(x, y, iw - 120f, 36f), $"Connecting to {target}...", 14, HudKit.Info);
            if (HudKit.Button(new Rect(r.xMax - 128f, y, 110f, 36f), "Cancel"))
            {
                _joining = false;
                SessionService.Current?.Stop();
            }
        }

        // ------------------------------------------------------------------------------------------ actions

        private ushort Port()
        {
            if (!ushort.TryParse(_port, out ushort port) || port == 0) port = SessionService.DefaultPort;
            _port = port.ToString();
            PlayerPrefs.SetString(PrefAddress, _address);
            PlayerPrefs.SetInt(PrefPort, port);
            return port;
        }

        private void Host()
        {
            SessionService s = SessionService.Instance;
            if (s.IsActive) s.Stop();
            if (s.StartHost(Port())) Close();
            else { _panel = Panel.Lan; _error = string.IsNullOrEmpty(s.LastError) ? "Could not start hosting." : s.LastError; }
        }

        private void HostOnline()
        {
            SessionService s = SessionService.Instance;
            if (s.IsActive) s.Stop();
            _error = string.Empty;
            _hostingOnline = true;
            OnlineRelay.Host((code, error) =>
            {
                _hostingOnline = false;
                if (!_open) return;
                if (error != null) { _panel = Panel.None; _error = error; return; }
                Close(); // the join code stays on screen in game (bottom line and pause screen)
            });
        }

        private void JoinOnline()
        {
            SessionService s = SessionService.Instance;
            if (s.IsActive) s.Stop();
            _error = string.Empty;
            string code = _code;
            OnlineRelay.Join(code, error =>
            {
                if (!_open) return;
                if (error != null) _error = error;
                else _joining = true; // Update closes the title once the host accepts us
            });
        }

        private void Join()
        {
            SessionService s = SessionService.Instance;
            if (s.IsActive) s.Stop();
            _error = string.Empty;
            if (string.IsNullOrWhiteSpace(_address)) _address = SessionService.DefaultAddress;
            _address = _address.Trim();
            if (s.StartClient(_address, Port())) _joining = true;
            else _error = string.IsNullOrEmpty(s.LastError) ? "Could not connect." : s.LastError;
        }
    }
}
