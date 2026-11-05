using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Encounters;
using Deadfall.Rules.Encounters;
using Deadfall.Player;
using Deadfall.Rules.Progression;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deadfall.Game
{
    /// <summary>
    /// "Field Mod ready: press T" hint, and the pick screen: three cards in rarity colours with what each does and
    /// how many stacks you have. Click one to take it. Esc closes without choosing (the offer waits).
    /// </summary>
    public sealed class FieldModPanel : MonoBehaviour
    {
        private const string ModalId = "field_mods";
        private InputAction _open;
        private bool _visible;
        /// <summary>The surge whose lull already popped the picker up (once per lull; Esc puts it away until T).</summary>
        private int _autoShownSurge = -1;

        private void OnEnable()
        {
            _open = new InputAction("FieldMods", InputActionType.Button, "<Keyboard>/t");
            _open.Enable();
        }

        private void OnDisable()
        {
            _open?.Disable();
            _open?.Dispose();
            Hide();
        }

        private static PlayerCursorLock Cursor() => PlayerIdentity.Local != null ? PlayerIdentity.Local.GetComponent<PlayerCursorLock>() : null;

        private void Update()
        {
            FieldModRunner runner = FieldModRunner.Local;
            if (_visible)
            {
                PlayerCursorLock c = Cursor();
                if (runner == null || runner.PendingOffers <= 0 || (c != null && c.IsLocked)) Hide();
                return;
            }
            if (runner == null || runner.PendingOffers <= 0 || _open == null) return;
            bool pressed = _open.WasPressedThisFrame();
            // In the lull between surges the picker pops up by itself (once per lull).
            WaveManager waves = WaveManager.Current;
            bool lull = waves != null && waves.IsRunning && waves.Phase == RunPhase.Lull;
            bool auto = lull && _autoShownSurge != waves.Surge && !UiModal.Active;
            if (!pressed && !auto) return;
            PlayerCursorLock cursor = Cursor();
            if (cursor != null && !cursor.IsLocked) return; // paused
            if (!UiModal.TryOpen(ModalId)) return;
            if (auto) _autoShownSurge = waves.Surge;
            _visible = true;
            if (cursor != null) cursor.SetLocked(false);
        }

        private void Hide()
        {
            if (!_visible) return;
            _visible = false;
            UiModal.Close(ModalId);
            PlayerCursorLock c = Cursor();
            if (c != null && !c.IsLocked) c.SetLocked(true);
        }

        private void OnGUI()
        {
            FieldModRunner runner = FieldModRunner.Local;
            if (runner == null || runner.PendingOffers <= 0) return;
            HudKit.Begin();
            float sw = HudKit.W, sh = HudKit.H;
            if (!_visible)
            {
                string n = runner.PendingOffers > 1 ? $" ({runner.PendingOffers})" : string.Empty;
                HudKit.Prompt(sw * 0.5f, sh - 28f - 84f - 60f, "T", $"Field Mod ready{n}", true);
                HudKit.End();
                return;
            }
            IReadOnlyList<FieldModDef> offer = runner.CurrentOffer();
            if (offer == null) { HudKit.End(); return; }
            GUI.depth = -20;
            HudKit.Backdrop(0.6f);
            const float cardW = 260f, cardH = 320f, gap = 22f;
            float total = offer.Count * cardW + (offer.Count - 1) * gap;
            float x = (sw - total) * 0.5f, y = (sh - cardH) * 0.5f;
            HudKit.Label(new Rect(0f, y - 70f, sw, 40f), "CHOOSE A FIELD MOD", 30, Color.white, TextAnchor.MiddleCenter, head: true);
            HudKit.Label(new Rect(0f, y - 34f, sw, 22f), "Esc to decide later", 13, HudKit.TextDim, TextAnchor.MiddleCenter);
            for (int i = 0; i < offer.Count; i++)
            {
                FieldModDef d = offer[i];
                Color rc = RarityColor(d.Rarity);
                var r = new Rect(x + i * (cardW + gap), y, cardW, cardH);
                bool hover = r.Contains(Event.current.mousePosition);
                if (hover) r.y -= 6f;
                HudKit.Round(new Rect(r.x - 2f, r.y + 6f, r.width + 4f, r.height + 2f), new Color(0f, 0f, 0f, 0.4f));
                HudKit.Round(r, new Color(0.07f, 0.08f, 0.1f, 0.97f));
                HudKit.Round(new Rect(r.x, r.y, r.width, 6f), rc, true);
                HudKit.Outline(r, new Color(rc.r, rc.g, rc.b, hover ? 0.9f : 0.35f));
                HudKit.Label(new Rect(r.x + 16f, r.y + 18f, r.width - 32f, 18f), (d.Rarity + (d.ClassId != null ? "  ·  class" : string.Empty)).ToUpperInvariant(), 12, rc, TextAnchor.MiddleCenter, head: true);
                HudKit.Label(new Rect(r.x + 16f, r.y + 42f, r.width - 32f, 56f), d.DisplayName, 22, Color.white, TextAnchor.MiddleCenter, head: true, wrap: true);
                HudKit.Fill(new Rect(r.x + r.width * 0.5f - 24f, r.y + 104f, 48f, 2f), new Color(rc.r, rc.g, rc.b, 0.7f));
                HudKit.Label(new Rect(r.x + 18f, r.y + 116f, r.width - 36f, 110f), d.Description, 14, HudKit.Text, TextAnchor.UpperCenter, wrap: true);
                int have = runner.Inventory.Stacks(d.Id);
                string stack = d.Stack == StackRule.Unique ? "Unique" : d.Stack == StackRule.Evolves ? $"Evolves at {d.EvolveAt} ({have} now)" : $"Stacks {have}/{d.MaxStacks}";
                HudKit.Label(new Rect(r.x + 16f, r.y + cardH - 82f, r.width - 32f, 20f), stack, 12, HudKit.TextDim, TextAnchor.MiddleCenter);
                if (HudKit.Button(new Rect(r.x + 24f, r.y + cardH - 54f, r.width - 48f, 38f), "TAKE", hover, true, 16, rc)) runner.Pick(i);
            }
            HudKit.End();
        }

        private static Color RarityColor(ModRarity r) =>
            r == ModRarity.Legendary ? new Color(1f, 0.7f, 0.25f) : r == ModRarity.Rare ? new Color(0.45f, 0.7f, 1f) : new Color(0.85f, 0.85f, 0.85f);
    }
}
