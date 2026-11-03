using Deadfall.Core;
using Deadfall.Rules.Encounters;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// Objective list (top right), the active or incoming event, the extraction countdown, and objective
    /// announcements. Reads <see cref="ObjectiveDirector"/>, which is in step on every machine.
    /// </summary>
    public sealed class ObjectiveHud : MonoBehaviour
    {
        private ObjectiveDirector _bound;
        private string _title = string.Empty, _subtitle;
        private float _until;

        private void Update()
        {
            ObjectiveDirector d = ObjectiveDirector.Current;
            if (d == _bound) return;
            if (_bound != null) _bound.Announced -= OnAnnounced;
            _bound = d;
            if (_bound != null) _bound.Announced += OnAnnounced;
        }

        private void OnDestroy()
        {
            if (_bound != null) _bound.Announced -= OnAnnounced;
        }

        private void OnAnnounced(string title, string subtitle)
        {
            _title = title ?? string.Empty;
            _subtitle = subtitle;
            _until = Time.unscaledTime + 4f;
        }

        private void OnGUI()
        {
            ObjectiveDirector d = _bound;
            if (d == null) return;
            HudKit.Begin();
            float sw = HudKit.W, sh = HudKit.H;

            // Objective card, top right: heading, one row per open objective, then the event line.
            var rows = new System.Collections.Generic.List<Objective>();
            foreach (Objective o in d.Objectives) if (!o.IsDone) rows.Add(o);
            bool hasEvent = d.ActiveEvent != RunEventKind.None || d.PendingEvent != RunEventKind.None;
            string head = d.IsOperation ? (d.MissionName ?? "Operation").ToUpperInvariant() : rows.Count > 0 ? "OBJECTIVES" : null;
            if (head != null || hasEvent)
            {
                const float cw = 380f;
                float ch = 16f + (head != null ? 28f : 0f) + rows.Count * 24f + (hasEvent ? 30f : 0f) + 6f;
                var card = new Rect(sw - 28f - cw, 24f, cw, ch);
                HudKit.Panel(card);
                float y = card.y + 10f;
                if (head != null)
                {
                    HudKit.Section(new Rect(card.x + 16f, y, cw - 32f, 22f), head);
                    y += 28f;
                }
                foreach (Objective o in rows)
                {
                    Color dot = o.Required ? HudKit.Accent : HudKit.Info;
                    HudKit.Round(new Rect(card.x + 18f, y + 8f, 7f, 7f), dot, true);
                    HudKit.Label(new Rect(card.x + 34f, y, cw - 50f, 22f), o.Describe(), 14, o.Required ? HudKit.Text : new Color(0.78f, 0.9f, 1f), TextAnchor.MiddleLeft);
                    y += 24f;
                }
                if (d.ActiveEvent != RunEventKind.None)
                {
                    var ev = new Rect(card.x + 12f, y + 2f, cw - 24f, 24f);
                    HudKit.Round(ev, new Color(1f, 0.45f, 0.3f, 0.22f), true);
                    HudKit.Label(new Rect(ev.x + 8f, ev.y, ev.width - 16f, ev.height), "EVENT  ·  " + EventScheduler.Name(d.ActiveEvent).ToUpperInvariant(), 13, new Color(1f, 0.6f, 0.45f), TextAnchor.MiddleLeft, head: true);
                }
                else if (d.PendingEvent != RunEventKind.None)
                {
                    HudKit.Label(new Rect(card.x + 16f, y + 2f, cw - 32f, 24f), "Next surge: " + EventScheduler.Name(d.PendingEvent), 13, HudKit.Warn, TextAnchor.MiddleLeft);
                }
            }

            if (d.ExtractionActive)
            {
                float w = Mathf.Min(460f, sw - 80f);
                var card = new Rect((sw - w) * 0.5f - 16f, sh - 210f, w + 32f, 64f);
                HudKit.Panel(card);
                int secs = Mathf.CeilToInt(d.ExtractionRemaining);
                Color c = d.ExtractionOccupied ? HudKit.Info : HudKit.Warn;
                HudKit.Label(new Rect(card.x + 16f, card.y + 6f, w, 26f), d.ExtractionOccupied ? "EXTRACTION" : "EXTRACTION PAUSED  ·  get into the zone", 15, c, TextAnchor.MiddleLeft, head: true);
                HudKit.Label(new Rect(card.x + 16f, card.y + 6f, w, 26f), $"{secs / 60}:{secs % 60:00}", 18, Color.white, TextAnchor.MiddleRight, head: true);
                float done = d.ExtractionDuration > 0f ? 1f - d.ExtractionRemaining / d.ExtractionDuration : 0f;
                HudKit.Bar(new Rect(card.x + 16f, card.y + 38f, w, 12f), done, c);
            }

            if (Time.unscaledTime < _until && _title.Length > 0)
            {
                float a = Mathf.Clamp01((_until - Time.unscaledTime) / 0.6f);
                float y = sh * 0.38f;
                HudKit.Label(new Rect(0f, y, sw, 36f), _title, 26, new Color(0.75f, 0.95f, 1f, a), TextAnchor.MiddleCenter, head: true);
                if (!string.IsNullOrEmpty(_subtitle))
                    HudKit.Label(new Rect(sw * 0.2f, y + 36f, sw * 0.6f, 44f), _subtitle, 15, new Color(0.9f, 0.93f, 0.95f, a), TextAnchor.UpperCenter, wrap: true);
            }
            HudKit.End();
        }
    }
}
