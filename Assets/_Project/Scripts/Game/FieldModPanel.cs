using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
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
        private GUIStyle _hint, _name, _desc, _rarity;

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
            if (runner == null || runner.PendingOffers <= 0 || _open == null || !_open.WasPressedThisFrame()) return;
            PlayerCursorLock cursor = Cursor();
            if (cursor != null && !cursor.IsLocked) return; // paused
            if (!UiModal.TryOpen(ModalId)) return;
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
            if (_hint == null)
            {
                _hint = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                _name = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter, wordWrap = true };
                _desc = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.UpperCenter, wordWrap = true };
                _rarity = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.UpperCenter };
            }
            if (!_visible)
            {
                GUI.color = new Color(1f, 0.85f, 0.4f);
                string n = runner.PendingOffers > 1 ? $" ({runner.PendingOffers})" : string.Empty;
                GUI.Label(new Rect(0f, Screen.height - 150f, Screen.width, 24f), $"Field Mod ready{n}: press T", _hint);
                GUI.color = Color.white;
                return;
            }
            IReadOnlyList<FieldModDef> offer = runner.CurrentOffer();
            if (offer == null) return;
            GUI.depth = -20;
            const float cardW = 220f, cardH = 230f, gap = 18f;
            float total = offer.Count * cardW + (offer.Count - 1) * gap;
            float x = (Screen.width - total) * 0.5f, y = (Screen.height - cardH) * 0.5f;
            GUI.Label(new Rect(0f, y - 44f, Screen.width, 30f), "Choose a Field Mod", _name);
            for (int i = 0; i < offer.Count; i++)
            {
                FieldModDef d = offer[i];
                var r = new Rect(x + i * (cardW + gap), y, cardW, cardH);
                GUI.color = RarityColor(d.Rarity);
                GUI.Box(r, GUIContent.none);
                GUI.Box(r, GUIContent.none);
                GUI.color = Color.white;
                GUI.Label(new Rect(r.x + 10f, r.y + 14f, r.width - 20f, 20f), d.Rarity + (d.ClassId != null ? "  ·  class" : string.Empty), _rarity);
                GUI.color = RarityColor(d.Rarity);
                GUI.Label(new Rect(r.x + 10f, r.y + 34f, r.width - 20f, 46f), d.DisplayName, _name);
                GUI.color = Color.white;
                GUI.Label(new Rect(r.x + 12f, r.y + 84f, r.width - 24f, 80f), d.Description, _desc);
                int have = runner.Inventory.Stacks(d.Id);
                string stack = d.Stack == StackRule.Unique ? "Unique" : d.Stack == StackRule.Evolves ? $"Evolves at {d.EvolveAt} ({have} now)" : $"Stacks {have}/{d.MaxStacks}";
                GUI.Label(new Rect(r.x + 10f, r.y + 164f, r.width - 20f, 20f), stack, _rarity);
                if (GUI.Button(new Rect(r.x + 30f, r.y + cardH - 40f, r.width - 60f, 28f), "Take")) runner.Pick(i);
            }
        }

        private static Color RarityColor(ModRarity r) =>
            r == ModRarity.Legendary ? new Color(1f, 0.7f, 0.25f) : r == ModRarity.Rare ? new Color(0.45f, 0.7f, 1f) : new Color(0.85f, 0.85f, 0.85f);
    }
}
