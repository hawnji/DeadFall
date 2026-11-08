using System;
using Deadfall.Classes;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Encounters;
using Deadfall.Player;
using Deadfall.Rules.Common;
using Deadfall.Rules.Progression;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// The first time you reach FOB Halcyon each session (after the title screen), a class picker comes up: one card
    /// per class with its role, ability and passives. Locked classes show the operator level they open at. The last
    /// class you played is highlighted. Picking one sets it (and its saved loadout); Esc keeps the current class.
    /// </summary>
    public sealed class ClassPickPanel : MonoBehaviour
    {
        private const string ModalId = "class_pick";

        /// <summary>True while something else owns the screen (the title); the picker waits until it's gone.</summary>
        public static Func<bool> Blocked;

        private bool _done, _visible;
        private UnityEngine.InputSystem.InputAction _esc;

        private void OnEnable()
        {
            _esc = new UnityEngine.InputSystem.InputAction("ClassPickClose", UnityEngine.InputSystem.InputActionType.Button, "<Keyboard>/escape");
            _esc.Enable();
        }
        private float _readyAt = -1f;

        private static PlayerCursorLock Cursor() => PlayerIdentity.Local != null ? PlayerIdentity.Local.GetComponent<PlayerCursorLock>() : null;

        private void Update()
        {
            if (_done) return;
            if (_visible)
            {
                PlayerCursorLock cur = Cursor();
                if ((_esc != null && _esc.WasPressedThisFrame()) || (cur != null && cur.IsLocked)) Finish();
                return;
            }
            bool blocked = Blocked != null && Blocked();
            bool atBase = WorldControl.IsAtBase && (WaveManager.Current == null || !WaveManager.Current.IsRunning);
            if (blocked || !atBase || PlayerClass.Local == null || ProfileService.Current == null) { _readyAt = -1f; return; }
            // A short beat after arriving so the fade from the title finishes first.
            if (_readyAt < 0f) { _readyAt = Time.unscaledTime + 0.8f; return; }
            if (Time.unscaledTime < _readyAt || UiModal.Active) return;
            if (!UiModal.TryOpen(ModalId)) return;
            _visible = true;
            PlayerCursorLock c = Cursor();
            if (c != null) c.SetLocked(false);
        }

        private void Finish()
        {
            _done = true;
            if (!_visible) return;
            _visible = false;
            UiModal.Close(ModalId);
            PlayerCursorLock c = Cursor();
            if (c != null) c.SetLocked(true);
        }

        private void OnDisable()
        {
            _esc?.Disable();
            _esc?.Dispose();
            _esc = null;
            if (_visible) Finish();
        }

        private void OnGUI()
        {
            if (!_visible) return;
            ProfileService svc = ProfileService.Current;
            PlayerClass cls = PlayerClass.Local;
            if (svc == null || cls == null) { Finish(); return; }
            PlayerProfile p = svc.Data;
            HudKit.Begin();
            GUI.depth = -25;
            HudKit.Backdrop(0.7f);
            float sw = HudKit.W, sh = HudKit.H;
            var classes = PrototypeClasses.All;
            const float cardW = 300f, cardH = 470f, gap = 22f;
            float total = classes.Count * cardW + (classes.Count - 1) * gap;
            float scale = Mathf.Min(1f, (sw - 60f) / total);
            float cw = cardW * scale, g = gap * scale;
            float x = (sw - (classes.Count * cw + (classes.Count - 1) * g)) * 0.5f, y = (sh - cardH) * 0.5f;
            HudKit.Label(new Rect(0f, y - 80f, sw, 44f), "CHOOSE YOUR CLASS", 34, Color.white, TextAnchor.MiddleCenter, head: true);
            HudKit.Label(new Rect(0f, y - 38f, sw, 22f), "You can change it later at the Armory.  ·  Esc keeps your current class", 13, HudKit.TextDim, TextAnchor.MiddleCenter);
            for (int i = 0; i < classes.Count; i++)
            {
                ClassDefinition c = classes[i];
                bool unlocked = p.ClassUnlocked(c.ContentId);
                bool current = cls.Class == c;
                var r = new Rect(x + i * (cw + g), y, cw, cardH);
                bool hover = unlocked && r.Contains(Event.current.mousePosition);
                if (hover) r.y -= 6f;
                HudKit.AccentPanel(r, unlocked ? c.Color : new Color(0.4f, 0.4f, 0.4f), unlocked ? 1f : 0.6f);
                if (current) HudKit.Outline(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), c.Color);
                float ty = r.y + 18f;
                HudKit.Label(new Rect(r.x + 18f, ty, r.width - 36f, 34f), c.DisplayName.ToUpperInvariant(), 26, unlocked ? Color.white : HudKit.TextDim, TextAnchor.MiddleLeft, head: true);
                ty += 34f;
                HudKit.Label(new Rect(r.x + 18f, ty, r.width - 36f, 20f), c.Role, 13, unlocked ? c.Color : HudKit.TextDim, TextAnchor.MiddleLeft, head: true);
                ty += 30f;
                if (c.Ability != null)
                {
                    HudKit.Label(new Rect(r.x + 18f, ty, r.width - 36f, 18f), "ABILITY", 11, HudKit.TextDim, TextAnchor.MiddleLeft, head: true);
                    ty += 18f;
                    string ab = c.Ability.DisplayName + ": " + c.Ability.Description;
                    float abH = WrappedHeight(ab, 13, r.width - 36f);
                    HudKit.Label(new Rect(r.x + 18f, ty, r.width - 36f, abH), ab, 13, HudKit.Text, TextAnchor.UpperLeft, wrap: true);
                    ty += abH + 10f;
                }
                HudKit.Label(new Rect(r.x + 18f, ty, r.width - 36f, 18f), "PASSIVES", 11, HudKit.TextDim, TextAnchor.MiddleLeft, head: true);
                ty += 18f;
                foreach (PassiveDefinition pd in c.Passives)
                {
                    if (ty > r.yMax - 90f) break;
                    HudKit.Label(new Rect(r.x + 18f, ty, r.width - 36f, 20f), pd.DisplayName, 13, unlocked ? c.Color : HudKit.Text, TextAnchor.MiddleLeft, head: true);
                    ty += 19f;
                    if (!string.IsNullOrEmpty(pd.Description))
                    {
                        float dh = WrappedHeight(pd.Description, 12, r.width - 36f);
                        HudKit.Label(new Rect(r.x + 18f, ty, r.width - 36f, dh), pd.Description, 12, HudKit.TextDim, TextAnchor.UpperLeft, wrap: true);
                        ty += dh;
                    }
                    ty += 8f;
                }
                var pick = new Rect(r.x + 18f, r.yMax - 52f, r.width - 36f, 36f);
                string label = !unlocked ? $"OPERATOR LEVEL {OperatorLevels.ClassUnlockLevel(c.ContentId)}" : current ? "KEEP " + c.DisplayName.ToUpperInvariant() : "PLAY " + c.DisplayName.ToUpperInvariant();
                if (HudKit.Button(pick, label, current, unlocked, 14, c.Color) && unlocked)
                {
                    if (!current) cls.SetClass(c);
                    p.SelectedClass = c.ContentId;
                    svc.MarkDirty();
                    GameAudio.Play2D(SfxId.UiConfirm);
                    Finish();
                }
            }
            HudKit.End();
        }

        private static float WrappedHeight(string text, int size, float width) =>
            Mathf.Ceil(HudKit.Style(size, TextAnchor.UpperLeft, wrap: true).CalcHeight(new GUIContent(text), width));
    }
}
