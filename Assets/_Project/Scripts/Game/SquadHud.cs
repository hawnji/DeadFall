using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Player;
using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// M11 squad HUD: the downed screen (bleed-out bar, self-revive kit prompt), the eliminated screen (who you
    /// are spectating, when you return), and a small squad roster on the left with each teammate's state.
    /// </summary>
    public sealed class SquadHud : MonoBehaviour
    {
        private void OnGUI()
        {
            SquadLife life = SquadLife.Current;
            if (life == null || life.LocalHealth == null) return;
            HudKit.Begin();
            float w = HudKit.W, h = HudKit.H;
            PlayerHealth hp = life.LocalHealth;

            if (hp.IsDowned) DrawDowned(life, hp, w, h);
            else if (hp.IsEliminated) DrawEliminated(life, w, h);
            DrawRoster(life, h);
            HudKit.End();
        }

        private static void DrawDowned(SquadLife life, PlayerHealth hp, float w, float h)
        {
            float t = hp.BleedOut01;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * (3f + (1f - t) * 6f));
            HudKit.Vignette(new Color(0.6f, 0f, 0f, 0.45f + 0.25f * pulse));
            var card = new Rect(w * 0.5f - 230f, h * 0.24f, 460f, 112f);
            HudKit.AccentPanel(card, HudKit.Bad);
            HudKit.Label(new Rect(card.x, card.y + 8f, card.width, 36f), "YOU'RE DOWN", 30, HudKit.Bad, TextAnchor.MiddleCenter, head: true);
            string sub = life.BeingRevived ? "A teammate is reviving you" : life.InRun ? "Crawl to cover. Sidearm only" : "Getting back up";
            HudKit.Label(new Rect(card.x, card.y + 44f, card.width, 22f), sub, 14, life.BeingRevived ? HudKit.Good : HudKit.TextDim, TextAnchor.MiddleCenter);
            HudKit.Bar(new Rect(card.x + 24f, card.y + 76f, card.width - 48f, 12f), t, life.BeingRevived ? HudKit.Good : HudKit.Bad);
            HudKit.Label(new Rect(card.x + 24f, card.y + 88f, card.width - 48f, 20f),
                life.BeingRevived ? "Bleed-out paused" : $"Bleeding out  {Mathf.CeilToInt(hp.BleedOutLeft)}s  ·  down {hp.DownCount}", 12, HudKit.TextDim, TextAnchor.MiddleCenter);

            if (!life.InRun) return;
            if (life.Kits > 0)
                HudKit.Prompt(w * 0.5f, h * 0.5f + 130f, "F", $"Hold  ·  Use self-revive kit  ({life.Kits} left for the squad)", true, life.SelfReviveProgress);
            else
                HudKit.Prompt(w * 0.5f, h * 0.5f + 130f, null, "No self-revive kits left. Wait for a teammate", false);
        }

        private static void DrawEliminated(SquadLife life, float w, float h)
        {
            var card = new Rect(w * 0.5f - 260f, 104f, 520f, 84f);
            HudKit.AccentPanel(card, HudKit.Warn);
            HudKit.Label(new Rect(card.x, card.y + 6f, card.width, 34f), "ELIMINATED", 26, HudKit.Warn, TextAnchor.MiddleCenter, head: true);
            HudKit.Label(new Rect(card.x, card.y + 40f, card.width, 20f), "You return at the end of this surge, or at the next objective", 13, HudKit.TextDim, TextAnchor.MiddleCenter);
            SpectatorCamera spec = SpectatorCamera.Current;
            string watching = spec != null && spec.Target != null ? $"Spectating player {spec.Target.ClientId}  ·  click to switch" : "No teammate to watch";
            HudKit.Label(new Rect(card.x, card.y + 60f, card.width, 20f), watching, 12, HudKit.Text, TextAnchor.MiddleCenter);
        }

        private static readonly List<ISquadMember> Sorted = new List<ISquadMember>(4);

        /// <summary>Teammates on the left edge: state, health or bleed-out.</summary>
        private static void DrawRoster(SquadLife life, float h)
        {
            Sorted.Clear();
            IReadOnlyList<ISquadMember> all = SquadMembers.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && !(all[i] is Object o && o == null) && !all[i].IsLocalMember) Sorted.Add(all[i]);
            if (Sorted.Count == 0) return;
            Sorted.Sort((a, b) => a.ClientId.CompareTo(b.ClientId));
            float y = h * 0.42f;
            HudKit.Label(new Rect(28f, y - 22f, 240f, 20f), life.InRun ? $"SQUAD  ·  {life.Kits} KITS" : "SQUAD", 12, HudKit.TextDim, TextAnchor.MiddleLeft, head: true);
            foreach (ISquadMember m in Sorted)
            {
                Color c = m.Disconnected ? HudKit.TextDim : m.Life == LifeState.Alive ? HudKit.Good : m.Life == LifeState.Downed ? HudKit.Bad : HudKit.Warn;
                // Hex badge with the player number, then a slim card with the name, state and a bar.
                var r = new Rect(28f + 26f, y + 2f, 240f - 26f, 36f);
                HudKit.Panel(r, 0.9f);
                var hex = new Rect(28f, y - 1f, 36f, 42f);
                HudKit.Hex(hex, new Color(HudKit.Glass.r, HudKit.Glass.g, HudKit.Glass.b, 0.95f), c);
                HudKit.Label(hex, m.ClientId.ToString(), 15, Color.white, TextAnchor.MiddleCenter, head: true);
                string state = m.Disconnected ? "RECONNECTING" : m.Life == LifeState.Alive ? "" : m.Life == LifeState.Downed ? "DOWN" : "OUT";
                HudKit.Label(new Rect(r.x + 18f, r.y + 1f, 150f, 20f), $"PLAYER {m.ClientId}", 13, HudKit.Text, TextAnchor.MiddleLeft, head: true);
                HudKit.Label(new Rect(r.x + 90f, r.y + 1f, r.width - 102f, 20f), state, 12, c, TextAnchor.MiddleRight, head: true);
                float bar = m.Life == LifeState.Downed && !m.Disconnected ? m.BleedOut01 : m.Life == LifeState.Alive ? m.Health01 : 0f;
                HudKit.Bar(new Rect(r.x + 18f, r.y + 22f, r.width - 30f, 6f), bar, c);
                y += 46f;
            }
        }
    }
}
