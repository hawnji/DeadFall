using System;
using Deadfall.Net;
using Deadfall.Player;
using UnityEngine;

namespace Deadfall.DebugTools
{
    /// <summary>
    /// Host / Join / Leave panel for the networking spike, shown on the pause screen (Esc). A one-line
    /// session status stays in the top-right corner during play. Placeholder IMGUI until the real
    /// lobby UI (M11).
    ///
    /// Command line, for testing with a build: <c>-deadfall-host [port]</c> or <c>-deadfall-join [address] [port]</c>.
    /// </summary>
    public sealed class NetSpikePanel : MonoBehaviour
    {
        private const string PrefAddress = "deadfall.net.address";
        private const string PrefPort = "deadfall.net.port";

        private NetSpikeSync _sync;
        private PlayerCursorLock _cursor;
        private string _address = SessionService.DefaultAddress;
        private string _port = SessionService.DefaultPort.ToString();
        private GUIStyle _box, _label, _status;

        public void Bind(NetSpikeSync sync, GameObject player)
        {
            _sync = sync;
            _cursor = player != null ? player.GetComponent<PlayerCursorLock>() : null;
        }

        private void Start()
        {
            _address = PlayerPrefs.GetString(PrefAddress, SessionService.DefaultAddress);
            _port = PlayerPrefs.GetInt(PrefPort, SessionService.DefaultPort).ToString();
            ApplyCommandLine();
        }

        private void ApplyCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-deadfall-host")
                {
                    ushort port = i + 1 < args.Length && ushort.TryParse(args[i + 1], out ushort p) ? p : SessionService.DefaultPort;
                    SessionService.Instance.StartHost(port);
                    return;
                }
                if (args[i] == "-deadfall-join")
                {
                    string address = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : SessionService.DefaultAddress;
                    ushort port = i + 2 < args.Length && ushort.TryParse(args[i + 2], out ushort p) ? p : SessionService.DefaultPort;
                    SessionService.Instance.StartClient(address, port);
                    return;
                }
            }
        }

        private void OnGUI()
        {
            Deadfall.Core.HudKit.Begin();
            GUI.skin = Deadfall.Core.HudKit.Skin;
            DrawGui();
            Deadfall.Core.HudKit.End();
        }

        private void DrawGui()
        {
            if (_sync == null || _sync.Session == null) return;
            EnsureStyles();
            GUI.depth = -10;
            SessionService s = _sync.Session;

            // Always-visible status line.
            string line = StatusText(s);
            if (!string.IsNullOrEmpty(line))
                Deadfall.Core.HudKit.Label(new Rect(0f, Deadfall.Core.HudKit.H - 26f, Deadfall.Core.HudKit.W, 20f), line, 12, Deadfall.Core.HudKit.Info, TextAnchor.MiddleCenter, head: true);

            bool paused = _cursor == null || !_cursor.IsLocked;
            if (!paused || Deadfall.Core.UiModal.Active) return;

            float w = 360f, h = 230f;
            var area = new Rect((Deadfall.Core.HudKit.W - w) * 0.5f, Deadfall.Core.HudKit.H * 0.4f + 50f, w, h);
            Deadfall.Core.HudKit.Panel(area);
            GUILayout.BeginArea(new Rect(area.x + 14f, area.y + 10f, w - 28f, h - 20f));
            GUILayout.Label("CO-OP", _label);

            if (!s.IsActive)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Address", GUILayout.Width(60f));
                _address = GUILayout.TextField(_address, 64);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Port", GUILayout.Width(60f));
                _port = GUILayout.TextField(_port, 5);
                GUILayout.EndHorizontal();
                GUILayout.Space(6f);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Host", GUILayout.Height(30f))) Host(s);
                if (GUILayout.Button("Join", GUILayout.Height(30f))) Join(s);
                GUILayout.EndHorizontal();
                if (!string.IsNullOrEmpty(s.LastError)) GUILayout.Label(s.LastError);
                else GUILayout.Label("Host on one copy, then Join from the other.\nSame PC: keep 127.0.0.1. LAN: use the host's IP.");
            }
            else
            {
                GUILayout.Label(StatusText(s));
                GUILayout.Label(s.Stats.Summary(3));
                GUILayout.Space(8f);
                if (GUILayout.Button(s.IsHost ? "Close session" : s.IsConnecting ? "Cancel" : "Leave", GUILayout.Height(30f))) s.Stop();
            }
            GUILayout.EndArea();
        }

        private string StatusText(SessionService s)
        {
            switch (s.Role)
            {
                case SessionRole.Host:
                    int peers = s.RemotePeerCount;
                    return $"HOST on port {s.Port}  ·  {peers} other player{(peers == 1 ? "" : "s")} connected";
                case SessionRole.Client:
                    return s.IsConnecting
                        ? $"Connecting to {s.Address}:{s.Port}..."
                        : $"CLIENT (player {s.LocalClientId})  ·  connected to {s.Address}:{s.Port}";
                default:
                    return string.Empty;
            }
        }

        private void Host(SessionService s)
        {
            if (!TryPort(out ushort port)) return;
            Save(port);
            s.StartHost(port);
        }

        private void Join(SessionService s)
        {
            if (!TryPort(out ushort port)) return;
            Save(port);
            s.StartClient(_address, port);
        }

        private bool TryPort(out ushort port)
        {
            if (ushort.TryParse(_port, out port) && port > 0) return true;
            _port = SessionService.DefaultPort.ToString();
            port = SessionService.DefaultPort;
            return true;
        }

        private void Save(ushort port)
        {
            PlayerPrefs.SetString(PrefAddress, _address);
            PlayerPrefs.SetInt(PrefPort, port);
        }

        private void EnsureStyles()
        {
            if (_box != null) return;
            var bg = new Texture2D(1, 1);
            bg.SetPixel(0, 0, new Color(0.08f, 0.09f, 0.1f, 0.92f));
            bg.Apply();
            _box = new GUIStyle(GUI.skin.box);
            _box.normal.background = bg;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            if (Deadfall.Core.HudKit.HeadFont != null) _label.font = Deadfall.Core.HudKit.HeadFont;
            _label.normal.textColor = Deadfall.Core.HudKit.Accent;
            _status = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.UpperRight };
            _status.normal.textColor = new Color(0.6f, 0.85f, 1f);
        }
    }
}
