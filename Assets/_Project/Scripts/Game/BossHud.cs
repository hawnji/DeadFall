using System.Collections.Generic;
using Deadfall.Core;
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

        private void OnEnable() => Enemy.PhaseChanged += OnPhaseChanged;
        private void OnDisable() => Enemy.PhaseChanged -= OnPhaseChanged;

        private void OnPhaseChanged(Enemy e, SpecimenPhase phase)
        {
            _banner = phase == SpecimenPhase.Rampage ? "IT'S ENRAGED · INFECTED INCOMING"
                : phase == SpecimenPhase.ExposedCore ? "THE CORE IS EXPOSED · SHOOT THE GLOWING CORE" : string.Empty;
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
            Enemy b = _boss;
            bool banner = Time.unscaledTime < _bannerUntil && _banner.Length > 0;
            if (!banner && (b == null || !b.IsAlive)) return;
            HudKit.Begin();
            float sw = HudKit.W;
            if (banner)
            {
                float a = Mathf.Clamp01((_bannerUntil - Time.unscaledTime) / 0.5f);
                HudKit.Label(new Rect(0f, HudKit.H * 0.3f + 60f, sw, 36f), _banner, 24, new Color(HudKit.Bad.r, HudKit.Bad.g, HudKit.Bad.b, a), TextAnchor.MiddleCenter, head: true);
            }
            if (b != null && b.IsAlive)
            {
                float w = Mathf.Min(640f, sw - 80f);
                float x = (sw - w) * 0.5f, y = 182f;
                Color c = b.Definition.IsBoss ? new Color(0.9f, 0.22f, 0.25f) : new Color(0.72f, 0.45f, 0.95f);
                var card = new Rect(x - 16f, y - 34f, w + 32f, 72f);
                HudKit.Panel(card);
                HudKit.Label(new Rect(x, y - 30f, w, 26f), b.Definition.DisplayName.ToUpperInvariant(), 18, Color.white, TextAnchor.MiddleLeft, head: true);
                HudKit.Label(new Rect(x, y - 30f, w, 26f), $"{Mathf.CeilToInt(b.Health01 * 100f)}%", 15, c, TextAnchor.MiddleRight, head: true);
                HudKit.Bar(new Rect(x, y, w, 14f), b.Health01, c);
                if (b.Definition.IsBoss)
                {
                    // Phase thresholds as ticks on the bar.
                    HudKit.Fill(new Rect(x + w * SpecimenRules.RampageAt - 1f, y - 2f, 2f, 18f), Color.white);
                    HudKit.Fill(new Rect(x + w * SpecimenRules.ExposedAt - 1f, y - 2f, 2f, 18f), Color.white);
                    string mods = SpecimenRules.Describe(b.BossModifiers);
                    HudKit.Label(new Rect(x, y + 16f, w, 20f), SpecimenRules.Name(b.Phase) + (mods.Length > 0 ? "  ·  " + mods : string.Empty), 12, HudKit.TextDim, TextAnchor.MiddleLeft);
                }
                else
                {
                    HudKit.Label(new Rect(x, y + 16f, w, 20f), "Hunting the squad", 12, HudKit.TextDim, TextAnchor.MiddleLeft);
                }
            }
            HudKit.End();
        }
    }
}
