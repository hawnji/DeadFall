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

        // New objectives: shown big in the middle first, then they fly to their row in the card.
        private const float PopHold = 2.4f, PopFly = 0.55f;
        private readonly System.Collections.Generic.Dictionary<byte, float> _seenAt = new System.Collections.Generic.Dictionary<byte, float>();
        private readonly System.Collections.Generic.Dictionary<byte, float> _timeMax = new System.Collections.Generic.Dictionary<byte, float>();
        private readonly System.Collections.Generic.List<Objective> _rows = new System.Collections.Generic.List<Objective>(8);
        private readonly System.Collections.Generic.List<byte> _stale = new System.Collections.Generic.List<byte>(8);
        private bool _primed;

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

            // Objective card, top right: heading, one row per open objective (with a progress or timer bar), then the event line.
            var rows = _rows;
            rows.Clear();
            foreach (Objective o in d.Objectives) if (!o.IsDone) rows.Add(o);
            TrackNew(rows);
            bool hasEvent = d.ActiveEvent != RunEventKind.None || d.PendingEvent != RunEventKind.None;
            string head = d.IsOperation ? (d.MissionName ?? "Operation").ToUpperInvariant() : rows.Count > 0 ? "OBJECTIVES" : null;
            if (head != null || hasEvent)
            {
                const float cw = 380f;
                float ch = 16f + (head != null ? 28f : 0f) + RowsHeight(rows) + (hasEvent ? 30f : 0f) + 6f;
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
                    float age = Time.unscaledTime - (_seenAt.TryGetValue(o.Id, out float t0) ? t0 : -100f);
                    var rowRect = new Rect(card.x + 34f, y, cw - 50f, 22f);
                    if (age < PopHold + PopFly)
                    {
                        // Still in the middle of the screen (or flying here): leave its row empty for now.
                        if (age >= PopHold) DrawFlying(o, rowRect, (age - PopHold) / PopFly);
                        y += RowHeight(o);
                        continue;
                    }
                    Color dot = o.Required ? HudKit.Accent : HudKit.Info;
                    // A gently cycling rainbow dot marks the quest.
                    Color rainbow = Color.HSVToRGB(Mathf.Repeat(Time.unscaledTime * 0.22f + o.Id * 0.17f, 1f), 0.75f, 1f);
                    HudKit.Round(new Rect(card.x + 17f, y + 7f, 9f, 9f), o.Required ? rainbow : dot, true);
                    HudKit.Label(rowRect, o.Describe(), 14, o.Required ? HudKit.Text : new Color(0.78f, 0.9f, 1f), TextAnchor.MiddleLeft);
                    y += 24f;
                    float bar = BarValue(o, out Color barColor);
                    if (bar >= 0f)
                    {
                        HudKit.Bar(new Rect(card.x + 34f, y, cw - 50f, 6f), bar, barColor);
                        y += 12f;
                    }
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

            DrawPopups(rows, sw, sh);

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

        // ---------------------------------------------------------------- new objective pop-ups and bars

        private void TrackNew(System.Collections.Generic.List<Objective> rows)
        {
            float now = Time.unscaledTime;
            foreach (Objective o in rows)
            {
                if (!_seenAt.ContainsKey(o.Id))
                {
                    // Objectives already there when the HUD first sees the run (joining mid-run) skip the pop-up.
                    _seenAt[o.Id] = _primed ? now : -100f;
                }
                if (o.IsTimed && (!_timeMax.TryGetValue(o.Id, out float m) || o.TimeLeft > m)) _timeMax[o.Id] = o.TimeLeft;
            }
            _primed = true;
            // Forget finished ones so a reused id pops up again.
            _stale.Clear();
            foreach (byte id in _seenAt.Keys)
            {
                bool open = false;
                foreach (Objective o in rows) if (o.Id == id) { open = true; break; }
                if (!open) _stale.Add(id);
            }
            foreach (byte id in _stale) { _seenAt.Remove(id); _timeMax.Remove(id); }
        }

        /// <summary>0..1 for the bar under a row (time left on a timed objective, else progress on a counted one), or -1 for none.</summary>
        private float BarValue(Objective o, out Color color)
        {
            if (o.IsTimed && _timeMax.TryGetValue(o.Id, out float max) && max > 0f)
            {
                float t = Mathf.Clamp01(o.TimeLeft / max);
                color = t > 0.5f ? HudKit.Good : t > 0.2f ? HudKit.Warn : HudKit.Bad;
                return t;
            }
            if (o.Target > 1)
            {
                color = HudKit.Info;
                return Mathf.Clamp01(o.Progress / (float)o.Target);
            }
            color = default;
            return -1f;
        }

        private float RowHeight(Objective o) => 24f + (o.IsTimed || o.Target > 1 ? 12f : 0f);

        private float RowsHeight(System.Collections.Generic.List<Objective> rows)
        {
            float h = 0f;
            foreach (Objective o in rows) h += RowHeight(o);
            return h;
        }

        private static Rect PopRect(float sw, float sh) => new Rect((sw - 560f) * 0.5f, sh * 0.26f, 560f, 92f);

        /// <summary>The big "NEW OBJECTIVE" card in the middle, for objectives in their first seconds.</summary>
        private void DrawPopups(System.Collections.Generic.List<Objective> rows, float sw, float sh)
        {
            float now = Time.unscaledTime;
            Rect r = PopRect(sw, sh);
            foreach (Objective o in rows)
            {
                if (!_seenAt.TryGetValue(o.Id, out float t0)) continue;
                float age = now - t0;
                if (age < 0f || age >= PopHold) continue;
                float a = Mathf.Clamp01(age / 0.25f);
                float scale = 1f + 0.12f * (1f - Mathf.Clamp01(age / 0.3f));
                var card = new Rect(r.center.x - r.width * scale * 0.5f, r.center.y - r.height * scale * 0.5f, r.width * scale, r.height * scale);
                HudKit.Panel(card, a);
                Color rainbow = Color.HSVToRGB(Mathf.Repeat(now * 0.4f, 1f), 0.7f, 1f);
                rainbow.a = a;
                HudKit.Fill(new Rect(card.x, card.y, card.width, 3f), rainbow);
                HudKit.Label(new Rect(card.x, card.y + 10f, card.width, 22f), o.Required ? "NEW OBJECTIVE" : "OPTIONAL OBJECTIVE", 13, new Color(rainbow.r, rainbow.g, rainbow.b, a), TextAnchor.MiddleCenter, head: true);
                HudKit.Label(new Rect(card.x + 16f, card.y + 34f, card.width - 32f, 32f), o.Title.ToUpperInvariant(), 24, new Color(1f, 1f, 1f, a), TextAnchor.MiddleCenter, head: true);
                string sub = o.IsTimed ? $"{ObjectiveRules.Clock(o.TimeLeft)} to complete" : o.Target > 1 ? $"0/{o.Target}" : (o.RewardCredits > 0 ? $"+{o.RewardCredits:N0} credits" : string.Empty);
                if (!string.IsNullOrEmpty(sub))
                    HudKit.Label(new Rect(card.x, card.y + 64f, card.width, 20f), sub, 13, new Color(HudKit.TextDim.r, HudKit.TextDim.g, HudKit.TextDim.b, a), TextAnchor.MiddleCenter);
                return; // one at a time
            }
        }

        /// <summary>The objective's title gliding from the middle card to its row in the side card.</summary>
        private void DrawFlying(Objective o, Rect row, float t)
        {
            float e = 1f - (1f - t) * (1f - t);
            Rect from = PopRect(HudKit.W, HudKit.H);
            var start = new Rect(from.x + 16f, from.y + 34f, from.width - 32f, 32f);
            var r = new Rect(Mathf.Lerp(start.x, row.x, e), Mathf.Lerp(start.y, row.y, e), Mathf.Lerp(start.width, row.width, e), Mathf.Lerp(start.height, row.height, e));
            int size = Mathf.RoundToInt(Mathf.Lerp(24f, 14f, e));
            HudKit.Label(r, e < 0.6f ? o.Title.ToUpperInvariant() : o.Describe(), size, Color.white, e < 0.5f ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft, head: e < 0.6f);
        }
    }
}
