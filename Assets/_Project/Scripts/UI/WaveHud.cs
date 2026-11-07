using System.Collections.Generic;
using Deadfall.Core;
using Deadfall.Encounters;
using Deadfall.Rules.Encounters;
using UnityEngine;

namespace Deadfall.UI
{
    /// <summary>
    /// Survival HUD (<see cref="HudKit"/> style): surge panel at the top centre with a phase-coloured rule, a
    /// credits pill under it, big announcements when a surge starts or ends, and "+15" credit pops by the crosshair.
    /// </summary>
    public sealed class WaveHud : MonoBehaviour
    {
        private const float AnnouncementSeconds = 3f;
        private const float CreditPopSeconds = 1.6f;

        private WaveManager _waves;
        private WaveAnnouncement _announcement;
        private float _announcementAt = -100f;
        private readonly List<(int amount, string reason, float at)> _pops = new List<(int, string, float)>();

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

        private void OnGUI()
        {
            if (_waves == null) return;
            HudKit.Begin();
            float w = HudKit.W, h = HudKit.H;
            float now = Time.unscaledTime;

            if (_waves.Phase != RunPhase.NotStarted)
            {
                Color accent = PhaseColor();
                var box = new Rect(w * 0.5f - 210f, 18f, 420f, 70f);
                HudKit.Panel(box);
                HudKit.Fill(new Rect(box.x + 18f, box.y, box.width - 36f, 3f), accent);
                HudKit.Label(new Rect(box.x, box.y + 8f, box.width, 32f), PanelLine(), 24, Color.white, TextAnchor.MiddleCenter, head: true);
                HudKit.Label(new Rect(box.x, box.y + 40f, box.width, 22f), SubLine(), 14, HudKit.TextDim, TextAnchor.MiddleCenter);

                string credits = _waves.LocalCredits.ToString("N0");
                float cw = HudKit.TextWidth(credits, 18, head: true) + 110f;
                var pill = new Rect(w * 0.5f - cw * 0.5f, box.yMax + 8f, cw, 30f);
                HudKit.Panel(pill, 0.9f);
                HudKit.Label(new Rect(pill.x + 14f, pill.y, 90f, pill.height), "CREDITS", 12, HudKit.TextDim, TextAnchor.MiddleLeft, head: true);
                HudKit.Label(new Rect(pill.x, pill.y, pill.width - 14f, pill.height), credits, 18, HudKit.Accent, TextAnchor.MiddleRight, head: true);
            }

            // Big announcement: slides in, holds, fades out.
            float age = now - _announcementAt;
            if (age >= 0f && age < AnnouncementSeconds && !string.IsNullOrEmpty(_announcement.Title))
            {
                float alpha = Mathf.Clamp01(age / 0.15f) * Mathf.Clamp01((AnnouncementSeconds - age) / 0.6f);
                float slide = (1f - Mathf.Clamp01(age / 0.25f)) * 18f;
                Color c = _waves.Phase == RunPhase.Failed ? HudKit.Bad : _waves.Phase == RunPhase.Surge ? HudKit.Accent : Color.white;
                float y = h * 0.22f + slide;
                float tw = HudKit.TextWidth(_announcement.Title, 46, head: true);
                HudKit.Fill(new Rect(w * 0.5f - tw * 0.5f - 90f, y + 27f, 64f, 2f), new Color(c.r, c.g, c.b, alpha * 0.8f));
                HudKit.Fill(new Rect(w * 0.5f + tw * 0.5f + 26f, y + 27f, 64f, 2f), new Color(c.r, c.g, c.b, alpha * 0.8f));
                HudKit.Label(new Rect(0f, y, w, 56f), _announcement.Title, 46, new Color(c.r, c.g, c.b, alpha), TextAnchor.MiddleCenter, head: true);
                if (!string.IsNullOrEmpty(_announcement.Subtitle))
                    HudKit.Label(new Rect(0f, y + 54f, w, 28f), _announcement.Subtitle, 18, new Color(0.9f, 0.92f, 0.94f, alpha), TextAnchor.MiddleCenter);
            }

            // Credit pops: just right of centre and below the crosshair, so they never sit on the aim point (scrap pops
            // mirror them on the left).
            for (int i = _pops.Count - 1; i >= 0; i--)
            {
                float t = (now - _pops[i].at) / CreditPopSeconds;
                if (t >= 1f)
                {
                    _pops.RemoveAt(i);
                    continue;
                }
                int slot = _pops.Count - 1 - i;
                float a = 1f - t * t;
                var r = new Rect(w * 0.5f + 60f, h * 0.5f + 78f + slot * 22f - t * 16f, 320f, 22f);
                HudKit.Label(r, $"+{_pops[i].amount}", 18, new Color(HudKit.Accent.r, HudKit.Accent.g, HudKit.Accent.b, a), TextAnchor.MiddleLeft, head: true);
                if (!string.IsNullOrEmpty(_pops[i].reason))
                    HudKit.Label(new Rect(r.x + HudKit.TextWidth($"+{_pops[i].amount}", 18, head: true) + 8f, r.y, 280f, 22f), _pops[i].reason, 13,
                        new Color(0.9f, 0.9f, 0.9f, a * 0.9f), TextAnchor.MiddleLeft);
            }
            HudKit.End();
        }

        private Color PhaseColor()
        {
            switch (_waves.Phase)
            {
                case RunPhase.Surge: return HudKit.Bad;
                case RunPhase.Lull: return HudKit.Info;
                case RunPhase.Reward:
                case RunPhase.Complete: return HudKit.Good;
                case RunPhase.Failed: return HudKit.Bad;
                default: return HudKit.Accent;
            }
        }

        private string PanelLine()
        {
            string final = _waves.IsEndless ? "  ·  ENDLESS" : _waves.FinalSurge > 0 ? $" / {_waves.FinalSurge}" : string.Empty;
            switch (_waves.Phase)
            {
                // Operations show the objective clock; Survival/Endless just warn of the next surge (random 10-25 s lull).
                case RunPhase.Lull: return _waves.IsOperation ? $"LULL  {Clock(_waves.TimeLeft)}" : $"SURGE {_waves.Surge} INCOMING";
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
                    return _waves.IsOperation ? $"Surge {_waves.Surge} next  ·  work the objectives" : "Restock and get in position";
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
