using System.Collections.Generic;
using Deadfall.Enemies;
using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// Health bar at the top of the screen for a Specimen (name, phase, modifiers) or a Hunt pursuer. Works on every
    /// machine: clients read the host's snapshots (health fraction, phase byte). Also flashes the phase change.
    /// </summary>
    public sealed class BossHud : MonoBehaviour
    {
        private Enemy _boss;
        private float _nextScan;
        private string _banner = string.Empty;
        private float _bannerUntil;
        private GUIStyle _name, _sub, _bannerStyle;
        private Texture2D _white;

        private void OnEnable() => Enemy.PhaseChanged += OnPhaseChanged;
        private void OnDisable() => Enemy.PhaseChanged -= OnPhaseChanged;

        private void OnPhaseChanged(Enemy e, SpecimenPhase phase)
        {
            _banner = phase == SpecimenPhase.Rampage ? "IT'S ENRAGED · INFECTED INCOMING"
                : phase == SpecimenPhase.ExposedCore ? "THE CORE IS EXPOSED · SHOOT THE CHEST" : string.Empty;
            _bannerUntil = Time.unscaledTime + 3f;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextScan && _boss != null && _boss.isActiveAndEnabled) return;
            _nextScan = Time.unscaledTime + 0.25f;
            _boss = null;
            EnemyManager m = EnemyManager.Current;
            if (m == null) return;
            IReadOnlyList<Enemy> active = m.Active;
            for (int i = 0; i < active.Count; i++)
            {
                Enemy e = active[i];
                if (e == null || !e.IsAlive || e.Definition == null) continue;
                if (e.Definition.IsBoss) { _boss = e; break; }
                if (e.Definition.Role == EnemyRole.Hunter && _boss == null) _boss = e;
            }
        }

        private void OnGUI()
        {
            if (_white == null)
            {
                _white = Texture2D.whiteTexture;
                _name = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                _sub = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
                _bannerStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            }
            if (Time.unscaledTime < _bannerUntil && _banner.Length > 0)
            {
                GUI.color = new Color(1f, 0.45f, 0.35f, Mathf.Clamp01((_bannerUntil - Time.unscaledTime) / 0.5f));
                GUI.Label(new Rect(0f, Screen.height * 0.3f, Screen.width, 32f), _banner, _bannerStyle);
                GUI.color = Color.white;
            }
            Enemy b = _boss;
            if (b == null || !b.IsAlive) return;
            float w = Mathf.Min(560f, Screen.width - 80f);
            float x = (Screen.width - w) * 0.5f, y = 54f;
            GUI.Label(new Rect(x, y - 26f, w, 24f), b.Definition.DisplayName.ToUpperInvariant(), _name);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(x, y, w, 12f), _white);
            GUI.color = b.Definition.IsBoss ? new Color(0.85f, 0.2f, 0.25f) : new Color(0.75f, 0.45f, 0.95f);
            GUI.DrawTexture(new Rect(x + 1f, y + 1f, (w - 2f) * b.Health01, 10f), _white);
            GUI.color = Color.white;
            if (b.Definition.IsBoss)
            {
                // Phase thresholds as ticks on the bar.
                GUI.DrawTexture(new Rect(x + w * SpecimenRules.RampageAt, y, 2f, 12f), _white);
                GUI.DrawTexture(new Rect(x + w * SpecimenRules.ExposedAt, y, 2f, 12f), _white);
                string mods = SpecimenRules.Describe(b.BossModifiers);
                GUI.Label(new Rect(x, y + 14f, w, 18f), SpecimenRules.Name(b.Phase) + (mods.Length > 0 ? "  ·  " + mods : string.Empty), _sub);
            }
            else
            {
                GUI.Label(new Rect(x, y + 14f, w, 18f), "Hunting the squad", _sub);
            }
        }
    }
}
