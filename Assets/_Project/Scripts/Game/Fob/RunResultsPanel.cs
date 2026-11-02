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
        private GUIStyle _title, _small, _bold;

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
            if (_title == null)
            {
                _title = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                _small = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
                _bold = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            }
            GUI.depth = -30;
            const float w = 460f, h = 400f;
            var area = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.Box(area, GUIContent.none);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 16f, area.y + 12f, w - 32f, h - 24f));
            string head = _result.Won ? (_result.Operation ? "OPERATION COMPLETE" : "DEPLOYMENT COMPLETE") : _aborted ? "DEPLOYMENT ABORTED" : "SQUAD WIPED";
            GUI.color = _result.Won ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.6f, 0.5f);
            GUILayout.Label(head, _title);
            GUI.color = Color.white;
            GUILayout.Space(6f);
            GUILayout.Label($"Surges cleared: {_result.SurgesCleared}    Kills: {_result.Kills}    Objectives: {_result.Objectives}    Specimens: {_result.Specimens}", _small);
            GUILayout.Space(6f);
            RunReward r = _summary.Reward;
            GUILayout.Label("Banked", _bold);
            GUILayout.Label($"+{r.OperatorXp} operator XP    +{r.ClassXp} class XP\n+{r.Currency.Requisition} Requisition    +{r.Currency.Intel} Intel", _small);
            if (r.Retained) GUILayout.Label($"Run failed: {Mathf.RoundToInt(RunRewardRules.FailureRetention * 100f)}% of rewards kept. Challenge progress always counts.", _small);
            if (_summary.OperatorLevelAfter > _summary.OperatorLevelBefore) GUILayout.Label($"Operator level {_summary.OperatorLevelAfter}!", _bold);
            if (_summary.RankAfter > _summary.RankBefore) GUILayout.Label($"Class rank {_summary.RankAfter}! New skill points at the Training Range.", _bold);
            if (_summary.Completed != null && _summary.Completed.Count > 0)
            {
                GUILayout.Space(4f);
                GUILayout.Label("Completed", _bold);
                foreach (ChallengeCompletion c in _summary.Completed)
                    GUILayout.Label($"{c.Def.DisplayName}{(c.Def.Kind == ChallengeKind.Challenge ? $" {c.Tier + 1}" : string.Empty)}{(c.Reward.IsFree ? string.Empty : $"  +{c.Reward}")}", _small);
            }
            GUILayout.FlexibleSpace();
            SessionService session = SessionService.Current;
            bool client = session != null && session.Role == SessionRole.Client;
            GUILayout.BeginHorizontal();
            if (!client && WorldControl.ReturnToFob != null && !WorldControl.IsAtBase)
            {
                if (GUILayout.Button("Return to FOB", GUILayout.Height(30f))) { Hide(); WorldControl.ReturnToFob(); }
            }
            if (GUILayout.Button(client && !WorldControl.IsAtBase ? "Continue (the host returns the squad)" : "Continue", GUILayout.Height(30f))) Hide();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
