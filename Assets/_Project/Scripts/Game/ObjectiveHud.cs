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
        private GUIStyle _head, _line, _banner, _bannerSub, _center;
        private Texture2D _white;

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
            if (_head == null)
            {
                _white = Texture2D.whiteTexture;
                _head = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperRight };
                _line = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.UpperRight };
                _banner = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                _bannerSub = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                _center = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            }

            float x = Screen.width - 444f, y = 110f;
            if (d.IsOperation)
            {
                GUI.Label(new Rect(x, y, 420f, 20f), (d.MissionName ?? "Operation").ToUpperInvariant(), _head);
                y += 22f;
            }
            else if (d.Objectives.Count > 0)
            {
                GUI.Label(new Rect(x, y, 420f, 20f), "OBJECTIVES", _head);
                y += 22f;
            }
            foreach (Objective o in d.Objectives)
            {
                if (o.IsDone) continue;
                GUI.color = o.Required ? Color.white : new Color(0.75f, 0.9f, 1f);
                GUI.Label(new Rect(x, y, 420f, 20f), "• " + o.Describe(), _line);
                y += 19f;
            }
            GUI.color = Color.white;

            if (d.ActiveEvent != RunEventKind.None)
            {
                GUI.color = new Color(1f, 0.55f, 0.35f);
                GUI.Label(new Rect(x, y + 6f, 420f, 20f), "EVENT: " + EventScheduler.Name(d.ActiveEvent), _head);
                GUI.color = Color.white;
            }
            else if (d.PendingEvent != RunEventKind.None)
            {
                GUI.color = new Color(1f, 0.8f, 0.4f);
                GUI.Label(new Rect(x, y + 6f, 420f, 20f), "Next surge: " + EventScheduler.Name(d.PendingEvent), _line);
                GUI.color = Color.white;
            }

            if (d.ExtractionActive)
            {
                float w = Mathf.Min(420f, Screen.width - 80f);
                float bx = (Screen.width - w) * 0.5f, by = Screen.height - 170f;
                int secs = Mathf.CeilToInt(d.ExtractionRemaining);
                GUI.Label(new Rect(bx, by - 24f, w, 22f),
                    d.ExtractionOccupied ? $"EXTRACTION  {secs / 60}:{secs % 60:00}" : $"EXTRACTION PAUSED  {secs / 60}:{secs % 60:00}  ·  get into the zone", _center);
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.DrawTexture(new Rect(bx, by, w, 10f), _white);
                GUI.color = d.ExtractionOccupied ? new Color(0.3f, 0.9f, 1f) : new Color(0.9f, 0.6f, 0.2f);
                float done = d.ExtractionDuration > 0f ? 1f - d.ExtractionRemaining / d.ExtractionDuration : 0f;
                GUI.DrawTexture(new Rect(bx + 1f, by + 1f, (w - 2f) * done, 8f), _white);
                GUI.color = Color.white;
            }

            if (Time.unscaledTime < _until && _title.Length > 0)
            {
                GUI.color = new Color(0.75f, 0.95f, 1f, Mathf.Clamp01((_until - Time.unscaledTime) / 0.6f));
                float h = Screen.height * 0.38f;
                GUI.Label(new Rect(0f, h, Screen.width, 32f), _title, _banner);
                if (!string.IsNullOrEmpty(_subtitle)) GUI.Label(new Rect(Screen.width * 0.2f, h + 30f, Screen.width * 0.6f, 40f), _subtitle, _bannerSub);
                GUI.color = Color.white;
            }
        }
    }
}
