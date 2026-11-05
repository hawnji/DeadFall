using Deadfall.Classes;
using Deadfall.Core;
using Deadfall.Rules.Classes;
using UnityEngine;

namespace Deadfall.UI
{
    /// <summary>
    /// Class card above the health card (<see cref="HudKit"/> style): class name, squad doctrines (* = effect after
    /// playtests), ability bar (fills as the cooldown recovers, drains while active) with the Q key cap, passives.
    /// </summary>
    public sealed class ClassHud : MonoBehaviour
    {
        private PlayerClass _class;

        public void Bind(GameObject player) => _class = player != null ? player.GetComponent<PlayerClass>() : null;

        private void OnGUI()
        {
            if (_class == null || _class.Class == null) return;
            HudKit.Begin();
            ClassDefinition c = _class.Class;
            float h = HudKit.H;
            // Sits on top of the health card (PrototypeHud, 28 px margin, 66 px tall).
            var card = new Rect(28f, h - 28f - 66f - 10f - 86f, 340f, 86f);
            HudKit.AccentPanel(card, c.Color);
            HudKit.Label(new Rect(card.x + 16f, card.y + 6f, 200f, 24f), c.DisplayName.ToUpperInvariant(), 16, c.Color, TextAnchor.MiddleLeft, head: true);

            AbilityDefinition a = c.Ability;
            var bar = new Rect(card.x + 16f, card.y + 34f, card.width - 32f - 34f, 18f);
            string label, right = string.Empty;
            float fill;
            Color fc;
            bool ready = false;
            if (!_class.HasAbility || a == null)
            {
                label = a != null ? $"{a.DisplayName}: coming soon" : "No ability";
                fill = 0f;
                fc = HudKit.TextDim;
            }
            else
            {
                AbilityTimer t = _class.Timer;
                double now = Time.timeAsDouble;
                label = a.DisplayName;
                switch (t.Phase)
                {
                    case AbilityPhase.Active:
                        float left = t.ActiveRemaining(now);
                        fill = a.DurationSeconds > 0f ? left / a.DurationSeconds : 1f;
                        fc = c.Color;
                        right = $"{left:0.0}s";
                        break;
                    case AbilityPhase.Cooldown:
                        fill = t.CooldownProgress(now);
                        fc = new Color(0.45f, 0.48f, 0.53f);
                        right = $"{Mathf.CeilToInt(t.CooldownRemaining(now))}s";
                        break;
                    default:
                        fill = 1f;
                        fc = c.Color * 0.85f;
                        right = "READY";
                        ready = true;
                        break;
                }
            }
            HudKit.Bar(bar, fill, fc);
            HudKit.Label(new Rect(bar.x + 8f, bar.y, bar.width - 16f, bar.height), label, 12, Color.white, TextAnchor.MiddleLeft, head: true);
            HudKit.Label(new Rect(bar.x + 8f, bar.y, bar.width - 16f, bar.height), right, 12, Color.white, TextAnchor.MiddleRight, head: true);
            HudKit.KeyChip(bar.xMax + 8f, bar.y - 3f, "Q", 24f, ready);

            // Passives on one line under the bar.
            string passives = string.Empty;
            for (int i = 0; i < c.Passives.Count; i++)
            {
                bool live = i < _class.Passives.Count && _class.Passives[i].Implemented;
                passives += (passives.Length > 0 ? "  ·  " : string.Empty) + c.Passives[i].DisplayName + (live ? string.Empty : " (soon)");
            }
            HudKit.Label(new Rect(card.x + 16f, card.y + 58f, card.width - 32f, 20f), passives, 12, HudKit.TextDim, TextAnchor.MiddleLeft);

            // Squad doctrines, right of the class name.
            if (_class.ActiveDoctrines.Count > 0)
            {
                string d = string.Empty;
                foreach (string id in _class.ActiveDoctrines)
                    d += (d.Length > 0 ? "  ·  " : string.Empty) + PrototypeClasses.DoctrineName(id);
                HudKit.Label(new Rect(card.x + 16f, card.y + 6f, card.width - 32f, 24f), d, 12, HudKit.Accent, TextAnchor.MiddleRight, head: true);
            }
            HudKit.End();
        }

        private static void Fill(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
