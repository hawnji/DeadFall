using System.Collections.Generic;
using Deadfall.Classes;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Encounters;
using Deadfall.Enemies;
using Deadfall.Net;
using Deadfall.Player;
using Deadfall.Rules.Combat;
using Deadfall.Rules.Engineering;
using Deadfall.Rules.Progression;
using Deadfall.Rules.World;
using Deadfall.Rules.Encounters;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// The FOB screen (M10): one modal with a tab per facility. Command Center deploys, Armory picks class and
    /// loadout, Training Range holds class rank, skill tree and prestige, Research Lab / Engineering Workshop /
    /// Intelligence Center sell research (the last one also shows dossiers), Trophy Room lists challenges,
    /// achievements and lifetime stats. IMGUI in the <see cref="HudKit"/> style (window, facility tabs, skinned GUILayout body); the facility logic all lives in Rules.Progression.
    /// </summary>
    public sealed class FobPanel : MonoBehaviour
    {
        private const string ModalId = "fob";
        private static FobPanel _instance;
        private static FacilityId _tab;
        private static bool _open;
        private static Vector3 _openedAt;

        private Vector2 _scroll;
        private string _seedText = "0";
        private string _note = string.Empty;
        private float _noteUntil;
        private GUIStyle _title, _small, _bold, _right, _tabOn;

        public static bool IsOpen => _open;

        private void Awake() => _instance = this;
        private void OnDestroy() { if (_instance == this) _instance = null; }
        private void OnDisable() => Close();

        public static void Open(FobTerminal terminal) => Open(terminal != null ? terminal.Facility : FacilityId.CommandCenter, terminal != null ? terminal.transform.position : Vector3.zero);

        public static void Open(FacilityId tab, Vector3 at)
        {
            if (!UiModal.TryOpen(ModalId)) return;
            _tab = tab;
            _open = true;
            _openedAt = at;
            if (_instance != null) _instance._scroll = Vector2.zero;
            PlayerCursorLock c = Cursor();
            if (c != null) c.SetLocked(false);
        }

        public static void Close()
        {
            if (!_open) return;
            _open = false;
            UiModal.Close(ModalId);
            PlayerCursorLock c = Cursor();
            if (c != null) c.SetLocked(true);
        }

        private static PlayerCursorLock Cursor() => PlayerIdentity.Local != null ? PlayerIdentity.Local.GetComponent<PlayerCursorLock>() : null;

        private void Update()
        {
            if (!_open) return;
            PlayerIdentity me = PlayerIdentity.Local;
            PlayerCursorLock c = Cursor();
            bool away = me == null || (_openedAt != Vector3.zero && (me.transform.position - _openedAt).sqrMagnitude > 6f * 6f);
            if ((c != null && c.IsLocked) || away)
            {
                _open = false;
                UiModal.Close(ModalId);
                if (away && c != null) c.SetLocked(true);
            }
        }

        private void Say(string text)
        {
            _note = text;
            _noteUntil = Time.unscaledTime + 4f;
        }

        private void OnGUI()
        {
            if (!_open) return;
            ProfileService svc = ProfileService.Current;
            if (svc == null) return;
            PlayerProfile p = svc.Data;
            HudKit.Begin();
            GUI.skin = HudKit.Skin;
            if (_title == null)
            {
                _title = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
                _small = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
                _small.normal.textColor = HudKit.TextDim;
                _bold = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
                if (HudKit.HeadFont != null) _bold.font = HudKit.HeadFont;
                _right = new GUIStyle(_bold) { alignment = TextAnchor.MiddleRight, wordWrap = false };
                _right.normal.textColor = HudKit.Accent;
                _tabOn = new GUIStyle(GUI.skin.button);
            }
            GUI.depth = -20;
            Color accent = FobBuilder.ColorOf(_tab);
            string wallet = $"OPERATOR {p.OperatorLevel}  ·  {p.Wallet.Requisition:N0} REQ  ·  {p.Wallet.Intel:N0} INTEL";
            Rect c = HudKit.Window(1040f, 700f, $"FOB HALCYON  ·  {Facilities.Name(_tab).ToUpperInvariant()}", wallet, accent);

            // Operator level progress under the title.
            float lv = OperatorLevels.Progress(p.OperatorXp);
            HudKit.Bar(new Rect(c.xMax - 220f, c.y - 18f, 220f, 6f), lv, HudKit.Accent);

            // Facility tabs.
            int count = 0;
            foreach (FacilityId f in Facilities.All) count++;
            float tabW = (c.width - (count - 1) * 6f) / count;
            int ti = 0;
            foreach (FacilityId f in Facilities.All)
            {
                bool open = Facilities.IsOpen(f, p.OperatorLevel);
                string label = open ? Facilities.Name(f) : $"{Facilities.Name(f)}  ·  L{Facilities.UnlockLevel(f)}";
                var tr = new Rect(c.x + ti * (tabW + 6f), c.y, tabW, 38f);
                if (HudKit.Button(tr, label, f == _tab, open, 13, FobBuilder.ColorOf(f))) { _tab = f; _scroll = Vector2.zero; }
                ti++;
            }
            float top = c.y + 48f;
            if (!string.IsNullOrEmpty(svc.StatusNote))
            {
                HudKit.Label(new Rect(c.x, top, c.width, 20f), svc.StatusNote, 13, HudKit.Warn);
                top += 24f;
            }

            GUILayout.BeginArea(new Rect(c.x, top, c.width, c.yMax - top - 48f));
            _scroll = GUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case FacilityId.CommandCenter: CommandCenter(svc, p); break;
                case FacilityId.Armory: Armory(svc, p); break;
                case FacilityId.TrainingRange: TrainingRange(svc, p); break;
                case FacilityId.ResearchLab: ResearchList(svc, p, ResearchCategory.Survival, ResearchCategory.Combat, ResearchCategory.Arsenal); break;
                case FacilityId.EngineeringWorkshop: Workshop(svc, p); break;
                case FacilityId.IntelligenceCenter: Intelligence(svc, p); break;
                case FacilityId.TrophyRoom: Trophies(p); break;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // Footer: note or hint, Close.
            HudKit.Fill(new Rect(c.x, c.yMax - 44f, c.width, 1f), new Color(1f, 1f, 1f, 0.08f));
            bool note = Time.unscaledTime < _noteUntil;
            if (note) HudKit.Label(new Rect(c.x, c.yMax - 36f, c.width - 140f, 34f), _note, 14, HudKit.Good, head: true);
            else
            {
                HudKit.KeyChip(c.x, c.yMax - 31f, "Esc", 24f);
                HudKit.Label(new Rect(c.x + 44f, c.yMax - 36f, 300f, 34f), "or walk away to close", 13, HudKit.TextDim);
            }
            if (HudKit.Button(new Rect(c.xMax - 120f, c.yMax - 36f, 120f, 34f), "CLOSE")) Close();
            HudKit.End();
        }

        // ---------------------------------------------------------------- Command Center

        private void CommandCenter(ProfileService svc, PlayerProfile p)
        {
            SessionService session = SessionService.Current;
            bool client = session != null && session.Role == SessionRole.Client;
            PlayerClass cls = PlayerClass.Local;
            string classId = cls != null && cls.Class != null ? cls.Class.ContentId : null;
            GUILayout.Label("DEPLOYMENT BOARD", Section());
            GUILayout.Label($"Deploying as {(cls != null && cls.Class != null ? cls.Class.DisplayName : "no class")} (rank {p.RankOf(classId)}). Change class and weapons at the Armory.", _small);
            int players = WaveManager.Current != null ? Mathf.Max(1, WaveManager.Current.PlayerCount) : 1;
            GUILayout.Label(session != null && session.IsHost ? $"Squad: {players} in session. Everyone deploys with the host." : client ? "You are in someone's squad. The host launches." : "Solo. Host from the pause screen to bring a squad.", _small);
            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Map seed (0 = random)", GUILayout.Width(150f));
            _seedText = GUILayout.TextField(_seedText ?? "0", 18, GUILayout.Width(160f));
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);

            // M12: pick the environment, then the mode.
            GUILayout.BeginHorizontal();
            foreach (EnvironmentDef env in EnvironmentCatalog.All)
                if (GUILayout.Toggle(_environment == env.Index, env.DisplayName, GUI.skin.button, GUILayout.Height(28f)) && _environment != env.Index)
                {
                    _environment = env.Index;
                    _missionId = null; // back to that environment's own Operation
                }
            GUILayout.EndHorizontal();
            EnvironmentDef picked = EnvironmentCatalog.At(_environment);
            GUILayout.Label(picked.Blurb, _small);
            MissionTemplate op = PickedMission(picked);
            // Any Operation can be played on any environment; the environment's own one is the default.
            GUILayout.BeginHorizontal();
            GUILayout.Label("Operation", GUILayout.Width(70f));
            for (int i = 0; i < ObjectiveRules.Missions.Length; i++)
            {
                MissionTemplate m = ObjectiveRules.Missions[i];
                // Only a click on an unselected one changes the pick (a selected toggle keeps returning true every frame,
                // which used to overwrite the new pick and lock out earlier Operations in the row).
                bool on = op == m;
                if (GUILayout.Toggle(on, m.Name.Replace("Operation ", string.Empty), GUI.skin.button, GUILayout.Height(24f)) && !on) _missionId = m.Id;
            }
            GUILayout.EndHorizontal();
            op = PickedMission(picked);
            GUI.enabled = !client && WorldControl.EnterMap != null;
            Mission(op.Name.Replace("Operation ", "Operation: "), op.Briefing + " Surges continue until you extract. Pays the most Intel.", () => Deploy(DeployMode.Operation));
            Mission("Survival", "Hold out through every surge. Buy doors with credits to open new sectors; a Specimen waits at the end.", () => Deploy(DeployMode.Survival));
            long best = p.Stat(StatKeys.EndlessBest);
            Mission("Endless", "No final surge. From surge 21 a new escalation stacks every 5 surges (Armored, Swarming, Frenzied, Elite Tide, Relentless). Rewards keep growing." +
                    (best > 0 ? $" Your best: surge {best}." : string.Empty), () => Deploy(DeployMode.Endless));
            GUI.enabled = true;
            if (client) GUILayout.Label("Waiting for the host to deploy.", _small);

            GUILayout.Space(10f);
            GUILayout.Label("Rewards: XP, Requisition and Intel are paid when a run ends. A failed run keeps half (D-03); challenge progress always counts.", _small);
        }

        private GUIStyle _section;

        /// <summary>Small caps accent heading for GUILayout sections.</summary>
        private GUIStyle Section()
        {
            if (_section != null) return _section;
            _section = new GUIStyle(_bold) { fontSize = 13, wordWrap = false, margin = new RectOffset(4, 4, 10, 4) };
            _section.normal.textColor = HudKit.Accent;
            return _section;
        }

        private void Mission(string name, string desc, System.Action deploy)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, _title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("DEPLOY", GUILayout.Width(130f), GUILayout.Height(34f))) deploy();
            GUILayout.EndHorizontal();
            GUILayout.Label(desc, _small);
            GUILayout.EndVertical();
        }

        private enum DeployMode { Operation, Survival, Endless }
        private int _environment;
        /// <summary>The Operation picked in the deploy list (null = the environment's own).</summary>
        private string _missionId;

        private MissionTemplate PickedMission(EnvironmentDef env) =>
            ObjectiveRules.MissionById(string.IsNullOrEmpty(_missionId) ? env.MissionId : _missionId);

        private void Deploy(DeployMode mode)
        {
            if (!ulong.TryParse(_seedText, out ulong seed) || seed == 0) seed = (ulong)Random.Range(1, int.MaxValue);
            seed = EnvironmentCatalog.Encode(seed, _environment);
            Close();
            if (WorldControl.EnterMap == null || !WorldControl.EnterMap(seed)) { RunToasts.Show("Deployment failed: the map would not generate.", new Color(1f, 0.5f, 0.4f)); return; }
            if (mode == DeployMode.Operation)
            {
                if (ObjectiveDirector.Current == null || !ObjectiveDirector.Current.StartOperation(PickedMission(EnvironmentCatalog.At(_environment))))
                    RunToasts.Show("Could not start the Operation.", new Color(1f, 0.5f, 0.4f));
            }
            else if (WaveManager.Current != null)
            {
                if (mode == DeployMode.Endless) WaveManager.Current.StartEndless();
                else WaveManager.Current.StartRun();
            }
        }

        // ---------------------------------------------------------------- Armory

        private void Armory(ProfileService svc, PlayerProfile p)
        {
            PlayerClass cls = PlayerClass.Local;
            bool running = WaveManager.Current != null && WaveManager.Current.IsRunning;
            GUILayout.Label("CLASS", Section());
            GUILayout.BeginHorizontal();
            foreach (ClassDefinition c in PrototypeClasses.All)
            {
                bool unlocked = p.ClassUnlocked(c.ContentId);
                bool selected = cls != null && cls.Class == c;
                GUI.enabled = unlocked && !running;
                GUI.color = selected ? c.Color : Color.white;
                string label = unlocked ? $"{c.DisplayName}\nrank {p.RankOf(c.ContentId)}{Prestige(p, c.ContentId)}" : $"{c.DisplayName}\noperator L{OperatorLevels.ClassUnlockLevel(c.ContentId)}";
                if (GUILayout.Button(label, GUILayout.Height(40f)) && cls != null)
                {
                    cls.SetClass(c);
                    p.SelectedClass = c.ContentId;
                    svc.MarkDirty();
                }
            }
            GUI.color = Color.white;
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (running) GUILayout.Label("Class and loadout are locked during a run.", _small);
            if (cls == null || cls.Class == null) return;

            string classId = cls.Class.ContentId;
            string[] def = RunLoadouts.DefaultIds(classId);
            string[] current = (string[])svc.LoadoutFor(classId, def).Clone();
            List<LoadoutOption> options = ProfileService.LoadoutOptions();
            GUILayout.Space(6f);
            GUILayout.Label($"{cls.Class.DisplayName.ToUpperInvariant()} LOADOUT  ·  TWO PRIMARIES, SIDEARM, MELEE", Section());
            bool changed = false;
            int touched = -1;
            bool attachmentsChanged = false;
            string[] slotNames = { "Primary", "Primary", "Sidearm", "Melee" };
            for (int i = 0; i < LoadoutRules.Size; i++)
            {
                WeaponDefinition wd = WeaponCatalog.ById(current[i]);
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label(slotNames[i], GUILayout.Width(70f));
                GUI.enabled = !running;
                if (GUILayout.Button("<", GUILayout.Width(28f)) && Cycle(current, i, -1, options, def, svc)) { changed = true; touched = i; }
                GUILayout.Label(wd != null ? $"{wd.DisplayName}  ·  level {WeaponXpTracker.LevelOf(wd.ContentId)}" : current[i], _bold, GUILayout.Width(260f));
                if (GUILayout.Button(">", GUILayout.Width(28f)) && Cycle(current, i, 1, options, def, svc)) { changed = true; touched = i; }
                GUI.enabled = true;
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                if (wd != null && wd.Kind == WeaponKind.Firearm && AttachmentRow(p, wd, !running)) { attachmentsChanged = true; touched = i; }
            }
            var locked = new List<string>();
            foreach (LoadoutOption o in options)
                if (!LoadoutRules.Allowed(o, def, svc.WeaponResearched)) locked.Add(WeaponCatalog.ById(o.WeaponId)?.DisplayName ?? o.WeaponId);
            if (locked.Count > 0) GUILayout.Label($"Locked until researched: {string.Join(", ", locked)}.", _small);
            GUI.enabled = !running;
            if (GUILayout.Button("Reset to class default", GUILayout.Width(200f))) { p.Loadouts.Remove(classId); changed = true; }
            GUI.enabled = true;
            if (changed || attachmentsChanged)
            {
                if (changed && LoadoutRules.IsValid(current, options, def, svc.WeaponResearched)) p.Loadouts[classId] = current;
                svc.MarkDirty();
                PlayerWeapons weapons = PlayerIdentity.Local != null ? PlayerIdentity.Local.GetComponent<PlayerWeapons>() : null;
                if (weapons != null && !running)
                {
                    weapons.ResetLoadout(RunLoadouts.For(classId));
                    // Hand over the weapon just picked (or just re-fitted) so it can be tried straight away.
                    if (touched >= 0) weapons.Equip(touched);
                }
            }
        }

        /// <summary>
        /// One button per attachment slot under a firearm's row ("Optic: Red Dot"); clicking cycles through none and
        /// each option, right-click goes back. Returns true when something changed.
        /// </summary>
        private bool AttachmentRow(PlayerProfile p, WeaponDefinition wd, bool enabled)
        {
            p.Attachments.TryGetValue(wd.ContentId, out string[] ids);
            ids = ids ?? System.Array.Empty<string>();
            bool changed = false;
            GUILayout.BeginHorizontal();
            GUILayout.Space(78f);
            int fitted = 0;
            foreach (string id in ids) if (AttachmentCatalog.ById(id) != null) fitted++;
            foreach (AttachmentSlot slot in ArmoryAttachments.Slots)
            {
                if (slot == AttachmentSlot.Optic && !WeaponSights.CanFitOptic(wd)) continue;
                string name = "none";
                foreach (string id in ids)
                {
                    AttachmentDef a = AttachmentCatalog.ById(id);
                    if (a != null && a.Slot == slot) name = a.DisplayName;
                }
                GUI.enabled = enabled;
                GUI.color = name == "none" ? new Color(1f, 1f, 1f, 0.7f) : new Color(0.75f, 1f, 0.8f);
                if (GUILayout.Button($"{slot}\n{name}", _small, GUILayout.Width(104f), GUILayout.Height(34f)))
                {
                    int dir = Event.current.button == 1 ? -1 : 1;
                    p.Attachments[wd.ContentId] = ArmoryAttachments.Cycle(ids, slot, dir);
                    changed = true;
                }
                GUI.color = Color.white;
            }
            GUI.enabled = true;
            GUILayout.Label($"{fitted}/{ArmoryAttachments.MaxFitted}", _small, GUILayout.Width(40f));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return changed;
        }

        private static bool Cycle(string[] loadout, int slot, int dir, List<LoadoutOption> options, string[] def, ProfileService svc)
        {
            LoadoutSlot want = LoadoutRules.SlotAt(slot);
            var fits = new List<string>();
            foreach (LoadoutOption o in options)
                if (o.Slot == want && LoadoutRules.Allowed(o, def, svc.WeaponResearched)) fits.Add(o.WeaponId);
            if (fits.Count <= 1) return false;
            int at = Mathf.Max(0, fits.IndexOf(loadout[slot]));
            for (int n = 0; n < fits.Count; n++)
            {
                at = (at + dir + fits.Count) % fits.Count;
                string pick = fits[at];
                // Two primaries must differ.
                if (slot <= 1 && loadout[1 - slot] == pick) continue;
                loadout[slot] = pick;
                return true;
            }
            return false;
        }

        private static string Prestige(PlayerProfile p, string classId)
        {
            int pr = p.PrestigeOf(classId);
            return pr > 0 ? $" · {ClassRanks.PrestigeTitle(pr)}" : string.Empty;
        }

        // ---------------------------------------------------------------- Training Range

        private void TrainingRange(ProfileService svc, PlayerProfile p)
        {
            PlayerClass cls = PlayerClass.Local;
            if (cls == null || cls.Class == null) { GUILayout.Label("Pick a class at the Armory first.", _small); return; }
            string classId = cls.Class.ContentId;
            int rank = p.RankOf(classId);
            long xp = p.XpOf(classId);
            int prestige = p.PrestigeOf(classId);
            int points = SkillTreeRules.Available(ProfileService.Skills, classId, rank, p.Skills);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{cls.Class.DisplayName} · rank {rank}/{ClassRanks.MaxRank}{Prestige(p, classId)}", _bold);
            GUILayout.FlexibleSpace();
            string next = rank >= ClassRanks.MaxRank ? "max rank" : $"{xp - ClassRanks.XpForRank(rank)} / {ClassRanks.XpForRank(rank + 1) - ClassRanks.XpForRank(rank)} XP to rank {rank + 1}";
            GUILayout.Label($"{next}  ·  {points} skill points", _right);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            for (int b = 0; b < SkillTreeRules.Branches; b++)
            {
                GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(250f));
                string branchName = null;
                foreach (SkillNodeDef n in ProfileService.Skills)
                {
                    if (n.ClassId != classId || n.Branch != b) continue;
                    if (branchName == null) { branchName = n.BranchName; GUILayout.Label(branchName, _bold); }
                    bool have = p.Skills.Contains(n.Id);
                    SkillUnlockError e = SkillTreeRules.CanUnlock(ProfileService.Skills, n, rank, p.Skills);
                    GUILayout.Label($"{(have ? "✔ " : string.Empty)}T{n.Tier} {n.DisplayName}", have ? _bold : GUI.skin.label);
                    GUILayout.Label(n.Description, _small);
                    if (!have)
                    {
                        GUI.enabled = e == SkillUnlockError.Ok;
                        string why = e switch
                        {
                            SkillUnlockError.RankTooLow => $"Needs rank {n.RequiredRank}",
                            SkillUnlockError.MissingPrerequisite => "Needs the tier below",
                            SkillUnlockError.NotEnoughPoints => $"Needs {n.Cost} points",
                            _ => $"Unlock ({n.Cost} pt)",
                        };
                        if (GUILayout.Button(why, GUILayout.Height(22f)) && ProfileRules.UnlockSkill(p, ProfileService.Skills, n.Id) == SkillUnlockError.Ok)
                        {
                            svc.MarkDirty();
                            Say($"{n.DisplayName} unlocked.");
                        }
                        GUI.enabled = true;
                    }
                    GUILayout.Space(4f);
                }
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            const int respec = 500;
            GUI.enabled = p.Wallet.Requisition >= respec && SkillTreeRules.Spent(ProfileService.Skills, classId, p.Skills) > 0;
            if (GUILayout.Button($"Respec ({respec} REQ)", GUILayout.Height(24f)) && p.Wallet.TrySpend(new Cost(respec, 0)))
            {
                SkillTreeRules.Reset(ProfileService.Skills, classId, p.Skills);
                svc.MarkDirty();
                Say("Skill points refunded.");
            }
            GUI.enabled = ClassRanks.CanPrestige(xp, prestige);
            if (GUILayout.Button(prestige >= ClassRanks.MaxPrestige ? "Max prestige" : $"Prestige (rank {ClassRanks.MaxRank})", GUILayout.Height(24f)) && ProfileRules.Prestige(p, ProfileService.Skills, classId))
            {
                svc.MarkDirty();
                Say($"{cls.Class.DisplayName} is now {ClassRanks.PrestigeTitle(p.PrestigeOf(classId))}. Back to rank 1, skill points refunded.");
            }
            GUI.enabled = WorldControl.EnterTrainingRange != null;
            if (GUILayout.Button("Go to the firing range", GUILayout.Height(24f))) { Close(); WorldControl.EnterTrainingRange(); }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label("Prestige resets the class to rank 1 and refunds its skills. Research, weapons, currencies and unlocks stay. Class XP comes from runs played as that class.", _small);
        }

        // ---------------------------------------------------------------- research

        private void ResearchList(ProfileService svc, PlayerProfile p, params ResearchCategory[] categories)
        {
            foreach (ResearchCategory cat in categories)
            {
                GUILayout.Label(cat.ToString().ToUpperInvariant(), Section());
                foreach (ResearchDef r in ProfileService.ResearchProjects)
                {
                    if (r.Category != cat) continue;
                    ResearchError e = ResearchRules.CanResearch(r, p.Research, p.OperatorLevel, p.Wallet);
                    GUILayout.BeginHorizontal(GUI.skin.box);
                    GUILayout.BeginVertical();
                    GUILayout.Label(e == ResearchError.AlreadyDone ? $"✔ {r.DisplayName}" : r.DisplayName, _bold);
                    GUILayout.Label(r.Description, _small);
                    GUILayout.EndVertical();
                    GUILayout.FlexibleSpace();
                    if (e != ResearchError.AlreadyDone)
                    {
                        string label = e switch
                        {
                            ResearchError.LevelTooLow => $"Operator L{r.RequiredOperatorLevel}",
                            ResearchError.MissingPrerequisite => $"Needs {PrereqName(r)}",
                            _ => r.Cost.ToString(),
                        };
                        GUI.enabled = e == ResearchError.Ok;
                        if (GUILayout.Button(label, GUILayout.Width(200f), GUILayout.Height(30f)) && ProfileRules.Research(p, ProfileService.ResearchProjects, r.Id) == ResearchError.Ok)
                        {
                            ProfileRules.EvaluateChallenges(p, ProfileService.Challenges);
                            svc.MarkDirty();
                            Say($"{r.DisplayName} researched.");
                        }
                        GUI.enabled = true;
                    }
                    GUILayout.EndHorizontal();
                }
                GUILayout.Space(4f);
            }
        }

        private static string PrereqName(ResearchDef r)
        {
            foreach (string id in r.Prerequisites)
            {
                ResearchDef d = ResearchRules.Find(ProfileService.ResearchProjects, id);
                if (d != null && ProfileService.Profile != null && !ProfileService.Profile.Research.Contains(id)) return d.DisplayName;
            }
            return "prerequisite";
        }

        private void Workshop(ProfileService svc, PlayerProfile p)
        {
            GUILayout.Label("STRUCTURES  ·  BUILT IN THE FIELD WITH SCRAP (B)", Section());
            foreach (StructureDef d in StructureCatalog.All)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label($"{d.Name}  ·  {ScrapRules.BuildCost(d, true)} Scrap{(d.EngineerOnly ? "  ·  Engineer only" : $"  ·  others {ScrapRules.BuildCost(d, false)} Scrap")}  ·  {d.MaxTier} tiers", _bold);
                GUILayout.Label(d.Description, _small);
                GUILayout.EndVertical();
            }
            GUILayout.Space(6f);
            ResearchList(svc, p, ResearchCategory.Engineering);
        }

        // ---------------------------------------------------------------- intelligence

        private void Intelligence(ProfileService svc, PlayerProfile p)
        {
            int tier = ResearchRules.DossierTier(ProfileService.ResearchProjects, p.Research);
            GUILayout.Label("DOSSIERS", Section());
            GUILayout.Label($"Research reveals dossiers by tier; killing {DossierRules.KillsToReveal} of a type reveals its file too.", _small);
            foreach (EnemyDefinition e in PrototypeEnemies.All)
            {
                int enemyTier = e.Role switch { EnemyRole.Boss => 3, EnemyRole.Elite => 2, EnemyRole.Hunter => 2, _ => 1 };
                long kills = p.Stat(StatKeys.KillsOf(e.ContentId));
                GUILayout.BeginVertical(GUI.skin.box);
                if (DossierRules.Revealed(enemyTier, tier, kills))
                {
                    EnemyStats s = e.Stats;
                    GUILayout.Label($"{e.DisplayName}  ·  {e.Role}  ·  {kills} killed", _bold);
                    string armor = e.ArmorMultiplier < 0.99f ? $"armored body (takes {Mathf.RoundToInt(e.ArmorMultiplier * 100f)}% damage, use armor-piercing or explosives)" : "unarmored";
                    GUILayout.Label($"Health {s.MaxHealth:0}, speed {s.ChaseSpeed:0.0} m/s, hits for {s.AttackDamage:0}. Weak point ×{e.WeakPointMultiplier:0.0}; {armor}. First seen from surge {e.UnlockSurge}.", _small);
                }
                else
                {
                    GUILayout.Label($"Classified ({e.Role})  ·  {kills}/{DossierRules.KillsToReveal} kills", _bold);
                    GUILayout.Label(enemyTier switch { 1 => "Research Field Dossiers.", 2 => "Research Elite Dossiers.", _ => "Research Specimen Files." }, _small);
                }
                GUILayout.EndVertical();
            }
            GUILayout.Space(6f);
            ResearchList(svc, p, ResearchCategory.Intel);
        }

        // ---------------------------------------------------------------- trophies

        private void Trophies(PlayerProfile p)
        {
            GUILayout.Label("LIFETIME", Section());
            GUILayout.Label($"Runs: {p.Stat(StatKeys.RunsStarted)} started, {p.Stat(StatKeys.RunsWon)} won ({p.Stat(StatKeys.OperationsWon)} Operations), {p.Stat(StatKeys.RunsFailed)} failed. " +
                            $"Kills: {p.Stat(StatKeys.Kills)} ({p.Stat(StatKeys.CritKills)} critical, {p.Stat(StatKeys.MeleeKills)} melee). Surges cleared: {p.Stat(StatKeys.SurgesCleared)}. " +
                            $"Objectives: {p.Stat(StatKeys.Objectives)}. Specimens: {p.Stat(StatKeys.Specimens)}. Structures: {p.Stat(StatKeys.StructuresBuilt)}.", _small);
            GUILayout.Space(4f);
            GUILayout.Label("CHALLENGES", Section());
            foreach (ChallengeDef c in ProfileService.Challenges)
            {
                if (c.Kind != ChallengeKind.Challenge) continue;
                int done = ChallengeRules.TiersDone(c, p.Completed);
                int t = Mathf.Min(done, c.TierCount - 1);
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label($"{c.DisplayName} {Roman(done)}", _bold, GUILayout.Width(170f));
                GUILayout.Label(string.Format(c.Description, c.Targets[t]), _small, GUILayout.Width(260f));
                GUILayout.FlexibleSpace();
                string reward = done < c.TierCount ? c.Rewards[done].ToString() : string.Empty;
                GUILayout.Label($"{ChallengeRules.ProgressText(c, p.Stat(c.Stat), p.Completed)}   {reward}", _right);
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(4f);
            GUILayout.Label("ACHIEVEMENTS", Section());
            foreach (ChallengeDef c in ProfileService.Challenges)
            {
                if (c.Kind != ChallengeKind.Achievement) continue;
                bool done = p.Completed.Contains(c.Id);
                GUI.color = done ? new Color(1f, 0.9f, 0.5f) : new Color(0.7f, 0.7f, 0.7f);
                GUILayout.Label($"{(done ? "★" : "☆")} {c.DisplayName}: {string.Format(c.Description, c.Targets[0])}", _small);
            }
            GUI.color = Color.white;
            GUILayout.Space(4f);
            GUILayout.Label("PRESTIGE", Section());
            foreach (ClassDefinition c in PrototypeClasses.All)
            {
                int pr = p.PrestigeOf(c.ContentId);
                GUILayout.Label($"{c.DisplayName}: rank {p.RankOf(c.ContentId)}{(pr > 0 ? $", prestige {pr} ({ClassRanks.PrestigeTitle(pr)})" : string.Empty)}", _small);
            }
        }

        private static string Roman(int tiersDone) => tiersDone switch { 0 => string.Empty, 1 => "I", 2 => "II", 3 => "III", _ => tiersDone.ToString() };
    }
}
