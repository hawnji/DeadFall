using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Net;
using Deadfall.Player;
using Deadfall.Rules.Progression;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// End-of-run screen (M10): result, what the run counted, permanent rewards banked (with the D-03 retention note
    /// on a failure), level and rank changes, challenges completed. The host returns the squad to the FOB from here.
    /// </summary>
    public sealed class RunResultsPanel : MonoBehaviour
    {
        private const string ModalId = "run_results";
        private static bool _show;
        private static RunResult _result;
        private static RunSummary _summary;
        private static bool _aborted;

        public static bool IsOpen => _show;

        public static void Show(RunResult result, RunSummary summary, bool aborted)
        {
            _result = result;
            _summary = summary;
            _aborted = aborted;
            _show = true;
        }

        private static void Hide()
        {
            _show = false;
            UiModal.Close(ModalId);
            PlayerCursorLock c = PlayerIdentity.Local != null ? PlayerIdentity.Local.GetComponent<PlayerCursorLock>() : null;
            if (c != null) c.SetLocked(true);
        }

        private void Update()
        {
            if (!_show) return;
            // Esc relocks the cursor (PlayerCursorLock): treat it as Continue.
            if (UiModal.Owner == ModalId)
            {
                PlayerCursorLock lk = PlayerIdentity.Local != null ? PlayerIdentity.Local.GetComponent<PlayerCursorLock>() : null;
                if (lk != null && lk.IsLocked) { Hide(); return; }
                return;
            }
            // Wait for any other modal (Field Mod pick, cache) to close, then take the cursor.
            if (UiModal.Owner != ModalId && UiModal.TryOpen(ModalId))
            {
                PlayerCursorLock c = PlayerIdentity.Local != null ? PlayerIdentity.Local.GetComponent<PlayerCursorLock>() : null;
                if (c != null) c.SetLocked(false);
            }
        }

        private void OnDisable() { if (_show) Hide(); }

        private void OnGUI()
        {
            if (!_show || UiModal.Owner != ModalId) return;
            HudKit.Begin();
            GUI.depth = -30;
            string head = _result.Won ? (_result.Operation ? "OPERATION COMPLETE" : "DEPLOYMENT COMPLETE") : _aborted ? "DEPLOYMENT ABORTED" : "SQUAD WIPED";
            Color hc = _result.Won ? HudKit.Good : HudKit.Bad;
            Rect c = HudKit.Window(620f, 560f, head, null, hc);

            // Stat tiles.
            string[] labels = { "SURGES", "KILLS", "OBJECTIVES", "SPECIMENS" };
            int[] values = { _result.SurgesCleared, _result.Kills, _result.Objectives, _result.Specimens };
            float tw = (c.width - 3 * 10f) / 4f;
            for (int i = 0; i < 4; i++)
            {
                var t = new Rect(c.x + i * (tw + 10f), c.y, tw, 70f);
                HudKit.Round(t, HudKit.GlassLight);
                HudKit.Label(new Rect(t.x, t.y + 8f, t.width, 34f), values[i].ToString("N0"), 28, Color.white, TextAnchor.MiddleCenter, head: true);
                HudKit.Label(new Rect(t.x, t.y + 44f, t.width, 18f), labels[i], 11, HudKit.TextDim, TextAnchor.MiddleCenter, head: true);
            }
            float y = c.y + 86f;

            RunReward r = _summary.Reward;
            HudKit.Section(new Rect(c.x, y, c.width, 22f), "Banked");
            y += 28f;
            string[] rl = { "Operator XP", "Class XP", "Requisition", "Intel" };
            long[] rv = { r.OperatorXp, r.ClassXp, r.Currency.Requisition, r.Currency.Intel };
            float half = c.width * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                float x = c.x + (i % 2) * half, ry = y + (i / 2) * 28f;
                HudKit.Label(new Rect(x, ry, half - 20f, 26f), rl[i], 15, HudKit.TextDim);
                HudKit.Label(new Rect(x, ry, half - 20f, 26f), "+" + rv[i].ToString("N0"), 17, HudKit.Accent, TextAnchor.MiddleRight, head: true);
            }
            y += 62f;
            if (r.Retained)
            {
                HudKit.Label(new Rect(c.x, y, c.width, 20f), $"Run failed: {Mathf.RoundToInt(RunRewardRules.FailureRetention * 100f)}% of rewards kept. Challenge progress always counts.", 13, HudKit.Warn);
                y += 26f;
            }
            if (_summary.OperatorLevelAfter > _summary.OperatorLevelBefore)
            {
                HudKit.Label(new Rect(c.x, y, c.width, 24f), $"Operator level {_summary.OperatorLevelAfter}!", 17, HudKit.Good, head: true);
                y += 28f;
            }
            if (_summary.RankAfter > _summary.RankBefore)
            {
                HudKit.Label(new Rect(c.x, y, c.width, 24f), $"Class rank {_summary.RankAfter}! New skill points at the Training Range.", 17, HudKit.Good, head: true);
                y += 28f;
            }
            if (_summary.Completed != null && _summary.Completed.Count > 0)
            {
                y += 4f;
                HudKit.Section(new Rect(c.x, y, c.width, 22f), "Completed");
                y += 26f;
                foreach (ChallengeCompletion cc in _summary.Completed)
                {
                    if (y > c.yMax - 70f) break;
                    HudKit.Label(new Rect(c.x + 4f, y, c.width, 22f),
                        $"★  {cc.Def.DisplayName}{(cc.Def.Kind == ChallengeKind.Challenge ? $" {cc.Tier + 1}" : string.Empty)}{(cc.Reward.IsFree ? string.Empty : $"   +{cc.Reward}")}", 14, HudKit.Text);
                    y += 22f;
                }
            }

            SessionService session = SessionService.Current;
            bool client = session != null && session.Role == SessionRole.Client;
            var br = new Rect(c.x, c.yMax - 42f, c.width, 42f);
            bool canReturn = !client && WorldControl.ReturnToFob != null && !WorldControl.IsAtBase;
            string cont = client && !WorldControl.IsAtBase ? "CONTINUE (HOST RETURNS THE SQUAD)" : "CONTINUE";
            if (canReturn)
            {
                if (HudKit.Button(new Rect(br.x, br.y, br.width * 0.5f - 6f, br.height), "RETURN TO FOB", true)) { Hide(); WorldControl.ReturnToFob(); }
                if (HudKit.Button(new Rect(br.x + br.width * 0.5f + 6f, br.y, br.width * 0.5f - 6f, br.height), cont)) Hide();
            }
            else if (HudKit.Button(br, cont, true)) Hide();
            HudKit.End();
        }
    }
}
