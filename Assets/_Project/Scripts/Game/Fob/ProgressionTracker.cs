using System.Collections.Generic;
using Deadfall.Classes;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Encounters;
using Deadfall.Enemies;
using Deadfall.Rules.Encounters;
using Deadfall.Rules.Progression;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// Feeds the local profile from play (M10): lifetime stats live (kills, surges, objectives, structures, Field
    /// Mods), the run result when a run ends (banked with <see cref="ProfileService.BankRun"/>, results screen),
    /// challenge toasts, and the permanent bonuses (skills + research) on the player's <see cref="FieldModRunner"/>.
    /// Everything runs on each player's own machine from events every peer sees (T-55).
    /// </summary>
    public sealed class ProgressionTracker : MonoBehaviour
    {
        private GameObject _player;
        private PlayerClass _class;
        private WaveManager _waves;
        private ObjectiveDirector _objectives;
        private EngineeringDirector _engineering;
        private FieldModRunner _mods;
        private ProfileService _profile;

        private bool _inRun;
        private RunResult _run;
        private string _runClass;
        private bool _bonusesDirty = true;

        public void Bind(GameObject player)
        {
            _player = player;
            _class = player.GetComponent<PlayerClass>();
            if (_class != null) _class.ClassChanged += OnClassChanged;
        }

        private void OnEnable()
        {
            EventBus<HitConfirmed>.Subscribe(OnHit);
            EventBus<EnemyDied>.Subscribe(OnEnemyDied);
        }

        private void OnDisable()
        {
            EventBus<HitConfirmed>.Unsubscribe(OnHit);
            EventBus<EnemyDied>.Unsubscribe(OnEnemyDied);
        }

        private void OnDestroy()
        {
            if (_class != null) _class.ClassChanged -= OnClassChanged;
            if (_waves != null) _waves.PhaseChanged -= OnPhase;
            if (_objectives != null) _objectives.Announced -= OnObjectiveSay;
            if (_engineering != null) _engineering.LocalStructureBuilt -= OnBuilt;
            if (_mods != null) _mods.Picked -= OnModPicked;
            if (_profile != null) _profile.Changed -= OnProfileChanged;
        }

        private void Update()
        {
            if (_profile == null && ProfileService.Current != null)
            {
                _profile = ProfileService.Current;
                _profile.Changed += OnProfileChanged;
                RestoreClass();
                _bonusesDirty = true;
            }
            if (_waves == null && WaveManager.Current != null) { _waves = WaveManager.Current; _waves.PhaseChanged += OnPhase; }
            if (_objectives == null && ObjectiveDirector.Current != null) { _objectives = ObjectiveDirector.Current; _objectives.Announced += OnObjectiveSay; }
            if (_engineering == null && EngineeringDirector.Current != null) { _engineering = EngineeringDirector.Current; _engineering.LocalStructureBuilt += OnBuilt; }
            if (_mods == null && _player != null && (_mods = _player.GetComponent<FieldModRunner>()) != null) { _mods.Picked += OnModPicked; _bonusesDirty = true; }
            if (_bonusesDirty && _mods != null && _profile != null)
            {
                _bonusesDirty = false;
                _mods.SetPermanentBonuses(_profile.Bonuses(ClassId));
            }
        }

        private string ClassId => _class != null && _class.Class != null ? _class.Class.ContentId : null;
        private PlayerProfile Profile => _profile != null ? _profile.Data : null;

        /// <summary>The class picked last time (Armory), if still unlocked.</summary>
        private void RestoreClass()
        {
            PlayerProfile p = Profile;
            if (p == null || _class == null || string.IsNullOrEmpty(p.SelectedClass) || !p.ClassUnlocked(p.SelectedClass)) return;
            ClassDefinition c = PrototypeClasses.ById(p.SelectedClass);
            if (c != null && _class.Class != c) _class.SetClass(c);
        }

        private void OnClassChanged(ClassDefinition c)
        {
            _bonusesDirty = true;
            PlayerProfile p = Profile;
            if (p == null || c == null || p.SelectedClass == c.ContentId) return;
            p.SelectedClass = c.ContentId;
            _profile.MarkDirty(notify: false);
        }
        private void OnProfileChanged() => _bonusesDirty = true;

        // ---------------------------------------------------------------- live stats

        private void Stat(string key, long amount = 1)
        {
            PlayerProfile p = Profile;
            if (p == null) return;
            p.AddStat(key, amount);
            _profile.MarkDirty(notify: false);
        }

        private void OnHit(HitConfirmed e)
        {
            if (!e.Killed || _player == null || e.Attacker != _player) return;
            Stat(StatKeys.Kills);
            if (e.IsMelee) Stat(StatKeys.MeleeKills);
            if (e.IsCritical) Stat(StatKeys.CritKills);
            if (_inRun) _run.Kills++;
        }

        /// <summary>Per-type kills for dossiers. Raised where the enemy is simulated (host/offline).</summary>
        private void OnEnemyDied(EnemyDied e)
        {
            if (_player == null || e.Killer != _player || e.Definition == null) return;
            Stat(StatKeys.KillsOf(e.Definition.ContentId));
        }

        private void OnObjectiveSay(string title, string subtitle)
        {
            if (title != "OBJECTIVE COMPLETE") return;
            Stat(StatKeys.Objectives);
            if (_inRun) _run.Objectives++;
            Evaluate();
        }

        private void OnBuilt(Rules.Engineering.StructureKind kind) => Stat(StatKeys.StructuresBuilt);
        private void OnModPicked(Rules.Progression.FieldModDef def) => Stat(StatKeys.FieldMods);

        // ---------------------------------------------------------------- runs

        private void OnPhase(RunPhase from, RunPhase to)
        {
            if (from == RunPhase.NotStarted && to == RunPhase.Lull)
            {
                _inRun = true;
                _run = new RunResult { Operation = ObjectiveDirector.Current != null && ObjectiveDirector.Current.IsOperation || (_waves != null && _waves.IsOperation) };
                _runClass = ClassId;
                Stat(StatKeys.RunsStarted);
                Evaluate();
                return;
            }
            if (!_inRun) return;
            if (from == RunPhase.Surge && to == RunPhase.Reward)
            {
                _run.SurgesCleared++;
                Stat(StatKeys.SurgesCleared);
                if (_waves != null && _waves.SurgeType == SurgeType.Specimen) { _run.Specimens++; Stat(StatKeys.Specimens); }
                Evaluate();
                return;
            }
            if (to == RunPhase.Complete || to == RunPhase.Failed || to == RunPhase.NotStarted)
            {
                _inRun = false;
                _run.Won = to == RunPhase.Complete;
                // Operations flag is synced after the run starts on clients; take the latest view.
                if (ObjectiveDirector.Current != null && ObjectiveDirector.Current.IsOperation) _run.Operation = true;
                if (to == RunPhase.NotStarted && _run.SurgesCleared == 0 && _run.Kills == 0) return; // aborted before anything happened
                if (_profile == null) return;
                RunSummary s = _profile.BankRun(_runClass ?? ClassId, _run);
                Toast(s.Completed);
                RunResultsPanel.Show(_run, s, aborted: to == RunPhase.NotStarted);
            }
        }

        private void Evaluate()
        {
            PlayerProfile p = Profile;
            if (p == null) return;
            List<ChallengeCompletion> done = ProfileRules.EvaluateChallenges(p, ProfileService.Challenges);
            if (done.Count == 0) return;
            _profile.MarkDirty();
            Toast(done);
        }

        private static void Toast(List<ChallengeCompletion> done)
        {
            if (done == null) return;
            foreach (ChallengeCompletion c in done)
            {
                bool ach = c.Def.Kind == ChallengeKind.Achievement;
                string reward = c.Reward.IsFree ? string.Empty : $"  +{c.Reward}";
                RunToasts.Show(ach ? $"Achievement: {c.Def.DisplayName}" : $"Challenge: {c.Def.DisplayName} {c.Tier + 1}{reward}", new Color(1f, 0.9f, 0.5f));
            }
        }
    }
}
