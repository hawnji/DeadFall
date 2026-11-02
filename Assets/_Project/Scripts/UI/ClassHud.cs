using Deadfall.Classes;
using Deadfall.Rules.Classes;
using UnityEngine;

namespace Deadfall.UI
{
    /// <summary>
    /// Prototype class HUD (IMGUI until the real UI pass): class name and ability bar above the health bar.
    /// The bar fills as the cooldown recovers, shows the remaining effect time while active, and reads READY [Q].
    /// </summary>
    public sealed class ClassHud : MonoBehaviour
    {
        private PlayerClass _class;
        private GUIStyle _name, _small;

        public void Bind(GameObject player) => _class = player != null ? player.GetComponent<PlayerClass>() : null;

        private void OnGUI()
        {
            if (_class == null || _class.Class == null) return;
            if (_name == null)
            {
                _name = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold };
                _small = new GUIStyle(GUI.skin.label) { fontSize = 13 };
                _small.normal.textColor = new Color(0.85f, 0.9f, 0.85f);
            }
            ClassDefinition c = _class.Class;
            float h = Screen.height;
            const float x = 24f, width = 260f;
            float y = h - 140f;

            _name.normal.textColor = c.Color;
            GUI.Label(new Rect(x, y - 22f, width, 22f), c.DisplayName.ToUpperInvariant(), _name);

            AbilityDefinition a = c.Ability;
            Rect bar = new Rect(x, y, width, 18f);
            Fill(bar, new Color(0f, 0f, 0f, 0.55f));
            string label;
            if (!_class.HasAbility || a == null)
            {
                label = a != null ? $"{a.DisplayName}: coming soon" : "No ability";
            }
            else
            {
                AbilityTimer t = _class.Timer;
                double now = Time.timeAsDouble;
                switch (t.Phase)
                {
                    case AbilityPhase.Active:
                        float left = t.ActiveRemaining(now);
                        Fill(new Rect(bar.x + 2f, bar.y + 2f, (bar.width - 4f) * (a.DurationSeconds > 0f ? left / a.DurationSeconds : 1f), bar.height - 4f), c.Color);
                        label = $"{a.DisplayName}  {left:0.0}s";
                        break;
                    case AbilityPhase.Cooldown:
                        Fill(new Rect(bar.x + 2f, bar.y + 2f, (bar.width - 4f) * t.CooldownProgress(now), bar.height - 4f), new Color(0.45f, 0.45f, 0.5f));
                        label = $"{a.DisplayName}  {Mathf.CeilToInt(t.CooldownRemaining(now))}s";
                        break;
                    default:
                        Fill(new Rect(bar.x + 2f, bar.y + 2f, bar.width - 4f, bar.height - 4f), c.Color * 0.8f);
                        label = $"{a.DisplayName}  READY [Q]";
                        break;
                }
            }
            GUI.Label(new Rect(bar.x + 6f, bar.y, bar.width, bar.height), label, _small);

            // Passives, one line each, under the bar's label line.
            float py = y + 20f;
            for (int i = 0; i < c.Passives.Count; i++)
            {
                PassiveDefinition pd = c.Passives[i];
                bool live = i < _class.Passives.Count && _class.Passives[i].Implemented;
                GUI.Label(new Rect(x, py, 420f, 18f), $"• {pd.DisplayName}{(live ? "" : " (soon)")}", _small);
                py += 16f;
            }
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
