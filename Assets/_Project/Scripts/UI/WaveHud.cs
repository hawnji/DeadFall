using System.Collections.Generic;
using Deadfall.Encounters;
using Deadfall.Rules.Encounters;
using UnityEngine;

namespace Deadfall.UI
{
    /// <summary>
    /// Prototype survival HUD (IMGUI until the real UI pass): surge panel at the top centre, Field Credits
    /// under it, big announcements when a surge starts or ends, and floating "+15" credit pickups.
    /// </summary>
    public sealed class WaveHud : MonoBehaviour
    {
        private const float AnnouncementSeconds = 3f;
        private const float CreditPopSeconds = 1.6f;

        private WaveManager _waves;
        private WaveAnnouncement _announcement;
        private float _announcementAt = -100f;
        private readonly List<(int amount, string reason, float at)> _pops = new List<(int, string, float)>();
        private GUIStyle _panel, _sub, _title, _subtitle, _pop;

        private void Update()
        {
            WaveManager current = WaveManager.Current;
            if (current == _waves) return;
            Unhook();
            _waves = current;
            if (_waves == null) return;
            _waves.Announced += OnAnnounced;
            _waves.LocalCreditsEarned += OnCredits;
        }

        private void OnDisable() => Unhook();

        private void Unhook()
        {
            if (_waves == null) return;
            _waves.Announced -= OnAnnounced;
            _waves.LocalCreditsEarned -= OnCredits;
            _waves = null;
        }

        private void OnAnnounced(WaveAnnouncement a)
        {
            _announcement = a;
            _announcementAt = Time.unscaledTime;
        }

        private void OnCredits(int amount, string reason)
        {
            _pops.Add((amount, reason, Time.unscaledTime));
            if (_pops.Count > 6) _pops.RemoveAt(0);
        }

        private void EnsureStyles()
        {
            if (_panel != null) return;
            _panel = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _panel.normal.textColor = Color.white;
            _sub = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
            _sub.normal.textColor = new Color(0.85f, 0.9f, 0.85f);
            _title = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _subtitle = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            _pop = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }

        private void OnGUI()
        {
            if (_waves == null) return;
            EnsureStyles();
            float w = Screen.width, h = Screen.height;
            float now = Time.unscaledTime;

            if (_waves.Phase != RunPhase.NotStarted)
            {
                Rect box = new Rect(w * 0.5f - 190f, 12f, 380f, 64f);
                GUI.color = new Color(0f, 0f, 0f, 0.5f);
                GUI.DrawTexture(box, Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(box.x, box.y + 4f, box.width, 28f), PanelLine(), _panel);
                GUI.Label(new Rect(box.x, box.y + 34f, box.width, 24f), SubLine(), _sub);
                GUI.Label(new Rect(box.x, box.yMax + 2f, box.width, 22f), $"FIELD CREDITS  {_waves.LocalCredits}", _sub);
            }

            // Big announcement: fades in fast, holds, fades out.
            float age = now - _announcementAt;
            if (age >= 0f && age < AnnouncementSeconds && !string.IsNullOrEmpty(_announcement.Title))
            {
                float alpha = Mathf.Clamp01(age / 0.15f) * Mathf.Clamp01((AnnouncementSeconds - age) / 0.6f);
                Color c = _waves.Phase == RunPhase.Failed ? new Color(1f, 0.35f, 0.3f) : _waves.Phase == RunPhase.Surge ? new Color(1f, 0.8f, 0.35f) : Color.white;
                _title.normal.textColor = new Color(c.r, c.g, c.b, alpha);
                _subtitle.normal.textColor = new Color(0.9f, 0.92f, 0.9f, alpha);
                GUI.Label(new Rect(0f, h * 0.2f, w, 50f), _announcement.Title, _title);
                if (!string.IsNullOrEmpty(_announcement.Subtitle))
                    GUI.Label(new Rect(0f, h * 0.2f + 48f, w, 28f), _announcement.Subtitle, _subtitle);
            }

            // Credit pops rise and fade under the crosshair.
            for (int i = _pops.Count - 1; i >= 0; i--)
            {
                float t = (now - _pops[i].at) / CreditPopSeconds;
                if (t >= 1f)
                {
                    _pops.RemoveAt(i);
                    continue;
                }
                _pop.normal.textColor = new Color(0.95f, 0.85f, 0.35f, 1f - t);
                string text = string.IsNullOrEmpty(_pops[i].reason) ? $"+{_pops[i].amount}" : $"+{_pops[i].amount}  {_pops[i].reason}";
                int slot = _pops.Count - 1 - i;
                GUI.Label(new Rect(0f, h * 0.5f + 70f + slot * 20f - t * 24f, w, 22f), text, _pop);
            }
        }

        private string PanelLine()
        {
            string final = _waves.FinalSurge > 0 ? $" / {_waves.FinalSurge}" : string.Empty;
            switch (_waves.Phase)
            {
                case RunPhase.Lull: return $"LULL  {Clock(_waves.TimeLeft)}";
                case RunPhase.Surge:
                    string type = _waves.SurgeType == SurgeType.Standard ? string.Empty : $"  ·  {_waves.SurgeType.ToString().ToUpperInvariant()}";
                    return $"SURGE {_waves.Surge}{final}{type}";
                case RunPhase.Reward: return $"SURGE {_waves.Surge} CLEARED";
                case RunPhase.Complete: return "DEPLOYMENT COMPLETE";
                case RunPhase.Failed: return "SQUAD WIPED";
                default: return string.Empty;
            }
        }

        private string SubLine()
        {
            switch (_waves.Phase)
            {
                case RunPhase.Lull:
                    string ready = _waves.PlayerCount > 1 ? $"  ({_waves.ReadyCount}/{_waves.PlayerCount} ready)" : string.Empty;
                    return _waves.LocalReady ? $"Ready, waiting for squad{ready}" : $"Surge {_waves.Surge} next  ·  G when ready{ready}";
                case RunPhase.Surge: return $"{_waves.EnemiesRemaining} infected left";
                case RunPhase.Reward: return "Next lull starting";
                case RunPhase.Complete:
                case RunPhase.Failed: return _waves.IsAuthority ? "N to start a new run" : "Waiting for the host";
                default: return string.Empty;
            }
        }

        private static string Clock(float seconds)
        {
            int s = Mathf.CeilToInt(seconds);
            return $"{s / 60}:{s % 60:00}";
        }
    }
}
