using System.Collections.Generic;
using System.Text;
using Deadfall.Classes;
using Deadfall.Core;
using Deadfall.Enemies;
using Deadfall.Rules.Combat;
using Deadfall.Rules.Encounters;
using Deadfall.Rules.Engineering;
using Deadfall.Rules.Progression;
using Deadfall.Rules.World;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// The info board on the screen behind each FOB console: what the facility does, then its live status (level and XP,
    /// classes, research done and the next project, dossiers, records), or when it opens if it's still locked.
    /// Refreshes once a second from the profile.
    /// </summary>
    public sealed class FacilityBoard : MonoBehaviour
    {
        private const int Lines = 3;
        private const float RefreshSeconds = 1f;

        private FacilityId _facility;
        private float _w, _h;
        private Color _accent;
        private TextMesh _title, _tag, _status;
        private readonly TextMesh[] _lines = new TextMesh[Lines];
        private readonly string[] _text = new string[Lines];
        private string _shown;
        private float _next;

        /// <summary>
        /// Builds the board under a booth over its screen (booth space: the screen faces +z, centred at
        /// <paramref name="centre"/>, <paramref name="width"/> × <paramref name="height"/>).
        /// </summary>
        public static FacilityBoard Build(Transform booth, FacilityId facility, Vector3 centre, float width, float height)
        {
            var go = new GameObject("InfoBoard");
            go.transform.SetParent(booth, false);
            go.transform.localPosition = centre;
            // Turned to face the console, so its +z runs the way a player at the console looks.
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Color c = FobBuilder.ColorOf(facility);
            Color bg = new Color(0.025f, 0.035f, 0.045f);
            GameObject panel = FobBuilder.Box(go.transform, "BoardGlass", new Vector3(0f, 0f, 0.006f), new Vector3(width - 0.08f, height - 0.08f, 0.004f), bg, collider: false);
            panel.GetComponent<Renderer>().sharedMaterial = RuntimeMaterials.CreateEmissive(bg, 0.8f);
            GameObject rule = FobBuilder.Box(go.transform, "BoardRule", new Vector3(0f, height * 0.5f - 0.43f, 0.002f), new Vector3(width - 0.4f, 0.018f, 0.004f), c, collider: false);
            rule.GetComponent<Renderer>().sharedMaterial = RuntimeMaterials.CreateEmissive(c, 1.2f);

            var b = go.AddComponent<FacilityBoard>();
            b._facility = facility;
            b._w = width;
            b._h = height;
            b._accent = c;
            float left = -width * 0.5f + 0.2f, top = height * 0.5f - 0.13f, z = -0.01f;
            b._title = WorldText.Create(go.transform, "Title", new Vector3(left, top, z), TextAnchor.UpperLeft, head: true);
            b._status = WorldText.Create(go.transform, "Status", new Vector3(width * 0.5f - 0.2f, top, z), TextAnchor.UpperRight, head: true);
            b._tag = WorldText.Create(go.transform, "Tagline", new Vector3(left, top - 0.4f, z), TextAnchor.UpperLeft, head: false);
            for (int i = 0; i < Lines; i++)
                b._lines[i] = WorldText.Create(go.transform, "Line", new Vector3(left, top - 0.62f - i * 0.2f, z), TextAnchor.UpperLeft, head: false);
            b.Refresh(force: true);
            return b;
        }

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + RefreshSeconds;
            Refresh(force: false);
        }

        private void Refresh(bool force)
        {
            PlayerProfile p = ProfileService.Profile;
            int level = p != null ? p.OperatorLevel : 1;
            bool open = Facilities.IsOpen(_facility, level);
            for (int i = 0; i < Lines; i++) _text[i] = null;
            if (!open)
            {
                _text[0] = $"Opens at operator level {Facilities.UnlockLevel(_facility)}.";
                _text[1] = $"You're level {level}. Every run earns operator XP.";
            }
            else if (p != null) Fill(p);
            else _text[0] = "Loading profile...";

            string status = open ? StatusOf(p) : "LOCKED";
            var sig = new StringBuilder(128).Append(open).Append('|').Append(status);
            for (int i = 0; i < Lines; i++) sig.Append('|').Append(_text[i]);
            string s = sig.ToString();
            if (!force && s == _shown) return;
            _shown = s;

            float maxW = _w - 0.4f;
            Color dim = HudKit.TextDim;
            WorldText.Set(_title, Facilities.Name(_facility).ToUpperInvariant(), 0.17f, maxW * 0.62f, open ? _accent : dim);
            WorldText.Set(_status, status, 0.1f, maxW * 0.36f, open ? HudKit.Text : HudKit.Bad);
            WorldText.Set(_tag, Tagline(_facility), 0.085f, maxW, open ? HudKit.Text : dim);
            for (int i = 0; i < Lines; i++)
                WorldText.Set(_lines[i], _text[i], 0.075f, maxW, open ? HudKit.Text : dim);
        }

        private static string Tagline(FacilityId f) => f switch
        {
            FacilityId.CommandCenter => "Deploy the squad: pick a map and a mode.",
            FacilityId.Armory => "Choose your class and loadout.",
            FacilityId.TrainingRange => "Class ranks, skill trees and prestige.",
            FacilityId.ResearchLab => "Permanent upgrades for every run.",
            FacilityId.EngineeringWorkshop => "Field structures you build with Scrap.",
            FacilityId.IntelligenceCenter => "Enemy dossiers and intel research.",
            FacilityId.TrophyRoom => "Records, challenges and achievements.",
            _ => string.Empty,
        };

        private string StatusOf(PlayerProfile p)
        {
            if (p == null) return string.Empty;
            switch (_facility)
            {
                case FacilityId.CommandCenter: return $"OPERATOR L{p.OperatorLevel}";
                case FacilityId.ResearchLab: return ResearchStatus(p, ResearchCategory.Survival, ResearchCategory.Combat, ResearchCategory.Arsenal);
                case FacilityId.EngineeringWorkshop: return ResearchStatus(p, ResearchCategory.Engineering);
                case FacilityId.IntelligenceCenter: return ResearchStatus(p, ResearchCategory.Intel);
                default: return string.Empty;
            }
        }

        // ---------------------------------------------------------------- content

        private void Fill(PlayerProfile p)
        {
            switch (_facility)
            {
                case FacilityId.CommandCenter: CommandCenter(p); break;
                case FacilityId.Armory: Armory(p); break;
                case FacilityId.TrainingRange: Training(p); break;
                case FacilityId.ResearchLab: Research(p, 0, ResearchCategory.Survival, ResearchCategory.Combat, ResearchCategory.Arsenal); break;
                case FacilityId.EngineeringWorkshop:
                    _text[0] = $"{StructureCatalog.All.Length} structures: build them in the field with B.";
                    Research(p, 1, ResearchCategory.Engineering);
                    break;
                case FacilityId.IntelligenceCenter: Intel(p); break;
                case FacilityId.TrophyRoom: Trophies(p); break;
            }
        }

        private void CommandCenter(PlayerProfile p)
        {
            int level = p.OperatorLevel;
            _text[0] = level >= OperatorLevels.MaxLevel
                ? "Operator level maxed."
                : $"{p.OperatorXp - OperatorLevels.XpForLevel(level):N0} / {OperatorLevels.XpForLevel(level + 1) - OperatorLevels.XpForLevel(level):N0} XP to level {level + 1}.";
            _text[1] = NextUnlock(level);
            _text[2] = $"{EnvironmentCatalog.All.Length} maps  ·  Survival, Endless, Operation, Gauntlet, Escort";
        }

        /// <summary>What the next operator level that unlocks anything brings: facilities, classes, research.</summary>
        private static string NextUnlock(int level)
        {
            int next = int.MaxValue;
            foreach (FacilityId f in Facilities.All) { int l = Facilities.UnlockLevel(f); if (l > level && l < next) next = l; }
            foreach (ClassDefinition c in PrototypeClasses.All) { int l = OperatorLevels.ClassUnlockLevel(c.ContentId); if (l > level && l < next) next = l; }
            foreach (ResearchDef r in ProfileService.ResearchProjects) { int l = r.RequiredOperatorLevel; if (l > level && l < next) next = l; }
            if (next == int.MaxValue) return "Everything is unlocked.";
            var names = new List<string>(4);
            foreach (FacilityId f in Facilities.All) if (Facilities.UnlockLevel(f) == next) names.Add(Facilities.Name(f));
            foreach (ClassDefinition c in PrototypeClasses.All) if (OperatorLevels.ClassUnlockLevel(c.ContentId) == next) names.Add(c.DisplayName);
            int research = 0;
            foreach (ResearchDef r in ProfileService.ResearchProjects) if (r.RequiredOperatorLevel == next) research++;
            if (research > 0) names.Add(research == 1 ? "1 research project" : $"{research} research projects");
            return $"Next unlock at level {next}: {string.Join(", ", names)}.";
        }

        private void Armory(PlayerProfile p)
        {
            PlayerClass cls = PlayerClass.Local;
            string id = cls != null && cls.Class != null ? cls.Class.ContentId : null;
            _text[0] = id != null ? $"Deploying as {cls.Class.DisplayName}, rank {p.RankOf(id)}." : "No class picked yet.";
            int unlocked = 0;
            ClassDefinition nextClass = null;
            foreach (ClassDefinition c in PrototypeClasses.All)
            {
                if (p.ClassUnlocked(c.ContentId)) unlocked++;
                else if (nextClass == null || OperatorLevels.ClassUnlockLevel(c.ContentId) < OperatorLevels.ClassUnlockLevel(nextClass.ContentId)) nextClass = c;
            }
            _text[1] = nextClass != null
                ? $"Classes {unlocked}/{PrototypeClasses.All.Count}  ·  next: {nextClass.DisplayName} at operator level {OperatorLevels.ClassUnlockLevel(nextClass.ContentId)}"
                : $"All {PrototypeClasses.All.Count} classes unlocked.";
            int weapons = 0, weaponsDone = 0;
            foreach (ResearchDef r in ProfileService.ResearchProjects)
            {
                if (string.IsNullOrEmpty(r.UnlocksWeapon)) continue;
                weapons++;
                if (p.Research.Contains(r.Id)) weaponsDone++;
            }
            if (weapons > 0) _text[2] = $"Researched weapons {weaponsDone}/{weapons} (Research Lab).";
        }

        private void Training(PlayerProfile p)
        {
            PlayerClass cls = PlayerClass.Local;
            if (cls == null || cls.Class == null) { _text[0] = "Pick a class at the Armory first."; return; }
            string id = cls.Class.ContentId;
            int rank = p.RankOf(id);
            long xp = p.XpOf(id);
            _text[0] = $"{cls.Class.DisplayName}: rank {rank}/{ClassRanks.MaxRank}{(p.PrestigeOf(id) > 0 ? $", prestige {p.PrestigeOf(id)}" : string.Empty)}";
            _text[1] = rank >= ClassRanks.MaxRank
                ? (ClassRanks.CanPrestige(xp, p.PrestigeOf(id)) ? "Max rank: prestige is ready." : "Max rank.")
                : $"{xp - ClassRanks.XpForRank(rank):N0} / {ClassRanks.XpForRank(rank + 1) - ClassRanks.XpForRank(rank):N0} XP to rank {rank + 1}.";
            int points = SkillTreeRules.Available(ProfileService.Skills, id, rank, p.Skills);
            _text[2] = points > 0 ? $"{points} skill point{(points == 1 ? string.Empty : "s")} to spend." : "No skill points to spend.";
        }

        private static string ResearchStatus(PlayerProfile p, params ResearchCategory[] cats)
        {
            int total = 0, done = 0;
            foreach (ResearchDef r in ProfileService.ResearchProjects)
            {
                if (System.Array.IndexOf(cats, r.Category) < 0) continue;
                total++;
                if (p.Research.Contains(r.Id)) done++;
            }
            return total > 0 ? $"{done}/{total} RESEARCHED" : string.Empty;
        }

        /// <summary>Research lines from <paramref name="line"/> on: how many are ready now, then the next project and what it needs.</summary>
        private void Research(PlayerProfile p, int line, params ResearchCategory[] cats)
        {
            int ready = 0;
            ResearchDef nextReady = null, nextOther = null;
            ResearchError otherWhy = ResearchError.Ok;
            foreach (ResearchDef r in ProfileService.ResearchProjects)
            {
                if (System.Array.IndexOf(cats, r.Category) < 0) continue;
                ResearchError e = ResearchRules.CanResearch(r, p.Research, p.OperatorLevel, p.Wallet);
                if (e == ResearchError.AlreadyDone) continue;
                if (e == ResearchError.Ok) { ready++; if (nextReady == null) nextReady = r; }
                else if (nextOther == null || e == ResearchError.CannotAfford && otherWhy != ResearchError.CannotAfford) { nextOther = r; otherWhy = e; }
            }
            if (nextReady == null && nextOther == null) { _text[line] = "All research here is done."; return; }
            _text[line] = ready > 0 ? $"{ready} project{(ready == 1 ? string.Empty : "s")} ready to research." : "Nothing affordable right now.";
            if (line + 1 >= Lines) return;
            if (nextReady != null) _text[line + 1] = $"Next: {nextReady.DisplayName} ({nextReady.Cost})";
            else
            {
                string need = otherWhy switch
                {
                    ResearchError.LevelTooLow => $"operator level {nextOther.RequiredOperatorLevel}",
                    ResearchError.CannotAfford => nextOther.Cost.ToString(),
                    _ => "a prerequisite",
                };
                _text[line + 1] = $"Next: {nextOther.DisplayName} (needs {need})";
            }
        }

        private void Intel(PlayerProfile p)
        {
            int tier = ResearchRules.DossierTier(ProfileService.ResearchProjects, p.Research);
            int total = 0, revealed = 0;
            foreach (EnemyDefinition e in PrototypeEnemies.All)
            {
                int enemyTier = e.Role switch { EnemyRole.Boss => 3, EnemyRole.Elite => 2, EnemyRole.Hunter => 2, _ => 1 };
                total++;
                if (DossierRules.Revealed(enemyTier, tier, p.Stat(StatKeys.KillsOf(e.ContentId)))) revealed++;
            }
            _text[0] = $"Dossiers revealed {revealed}/{total}. Kill {DossierRules.KillsToReveal} of a type to reveal it.";
            Research(p, 1, ResearchCategory.Intel);
        }

        private void Trophies(PlayerProfile p)
        {
            _text[0] = $"Runs won {p.Stat(StatKeys.RunsWon):N0} of {p.Stat(StatKeys.RunsStarted):N0}  ·  kills {p.Stat(StatKeys.Kills):N0}";
            int ach = 0, achDone = 0;
            foreach (ChallengeDef c in ProfileService.Challenges)
            {
                if (c.Kind != ChallengeKind.Achievement) continue;
                ach++;
                if (p.Completed.Contains(c.Id)) achDone++;
            }
            _text[1] = $"Achievements {achDone}/{ach}.";
            long best = 0;
            string where = null;
            foreach (EnvironmentDef env in EnvironmentCatalog.All)
            {
                long ms = p.Stat(GauntletRecords.BestKey(env.Index));
                if (ms > 0 && (best == 0 || ms < best)) { best = ms; where = env.ShortName; }
            }
            _text[2] = best > 0 ? $"Best Gauntlet: {GauntletRules.Clock(best / 1000.0)} on {where}" : "No Gauntlet cleared yet.";
        }
    }
}
