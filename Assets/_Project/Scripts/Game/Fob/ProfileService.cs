using System;
using System.Collections.Generic;
using System.IO;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Rules.Combat;
using Deadfall.Rules.Progression;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// The local player's permanent profile (M10): loads it at startup, saves it (debounced, plus on quit) as a
    /// versioned, checksummed text file with a backup, and owns the progression catalogs. Weapon XP that M6 kept in
    /// PlayerPrefs is imported once on first load (T-54).
    /// </summary>
    public sealed class ProfileService : MonoBehaviour
    {
        public const string FileName = "deadfall_profile.txt";
        public const string LegacyWeaponXpKey = "deadfall.weaponxp";

        public static readonly List<SkillNodeDef> Skills = SkillTreeCatalog.Prototype();
        public static readonly List<ResearchDef> ResearchProjects = ResearchCatalog.Prototype();
        public static readonly List<ChallengeDef> Challenges = ChallengeCatalog.Prototype();

        private static ProfileService _current;
        private PlayerProfile _profile;
        private bool _dirty;
        private float _saveAt;
        private bool _readOnly;

        public static ProfileService Current => _current;
        /// <summary>The live profile. Never null once the service exists.</summary>
        public static PlayerProfile Profile => _current != null ? _current.Data : null;
        public PlayerProfile Data { get { EnsureLoaded(); return _profile; } }
        public string StatusNote { get; private set; } = string.Empty;
        public string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>Something in the profile changed (UI refresh, bonus re-apply).</summary>
        public event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _current = null;

        private void Awake()
        {
            _current = this;
            EnsureLoaded();
        }

        private void OnDestroy()
        {
            Save();
            if (_current == this) _current = null;
        }

        private void OnApplicationQuit() => Save();

        private void Update()
        {
            if (_dirty && Time.unscaledTime >= _saveAt) Save();
        }

        /// <summary>Call after any change. Saves within a few seconds.</summary>
        public void MarkDirty(bool notify = true)
        {
            if (!_dirty) _saveAt = Time.unscaledTime + 3f;
            _dirty = true;
            if (notify) Changed?.Invoke();
        }

        private void EnsureLoaded()
        {
            if (_profile != null) return;
            _profile = LoadFromDisk(out string note);
            StatusNote = note;
            string legacy = PlayerPrefs.GetString(LegacyWeaponXpKey, string.Empty);
            if (!string.IsNullOrEmpty(legacy))
            {
                int n = SaveFormat.ImportLegacyWeaponXp(_profile, legacy);
                if (!_readOnly)
                {
                    PlayerPrefs.DeleteKey(LegacyWeaponXpKey);
                    _dirty = true;
                    Save();
                }
                GameLog.Info("Profile", $"Imported weapon XP for {n} weapons from the old PlayerPrefs store.");
            }
        }

        private PlayerProfile LoadFromDisk(out string note)
        {
            note = string.Empty;
            string path = FilePath;
            SaveLoadResult r = TryRead(path);
            if (r.Status == SaveLoadStatus.Corrupt)
            {
                GameLog.Warn("Profile", "Profile file is damaged; trying the backup.");
                SaveLoadResult b = TryRead(path + ".bak");
                if (b.Status == SaveLoadStatus.Ok) { note = "Profile restored from backup."; r = b; }
                else
                {
                    // Keep the damaged file for inspection and start fresh.
                    TryCopy(path, path + ".damaged");
                    note = "Profile was damaged; started a new one (old file kept as .damaged).";
                    return new PlayerProfile();
                }
            }
            if (r.Status == SaveLoadStatus.TooNew)
            {
                _readOnly = true;
                note = $"Profile is from a newer build (v{r.FromVersion}); progress this session will not be saved.";
                GameLog.Warn("Profile", note);
                return new PlayerProfile();
            }
            if (r.Status == SaveLoadStatus.Empty) return new PlayerProfile();
            if (r.Migrated) { note = $"Profile upgraded from v{r.FromVersion}."; _dirty = true; }
            return r.Profile;
        }

        private static SaveLoadResult TryRead(string path)
        {
            try
            {
                return File.Exists(path) ? SaveFormat.Read(File.ReadAllText(path)) : new SaveLoadResult { Status = SaveLoadStatus.Empty };
            }
            catch (Exception e)
            {
                GameLog.Warn("Profile", $"Could not read {path}: {e.Message}");
                return new SaveLoadResult { Status = SaveLoadStatus.Corrupt };
            }
        }

        public void Save()
        {
            if (!_dirty || _profile == null || _readOnly) return;
            _dirty = false;
            string path = FilePath, tmp = path + ".tmp";
            try
            {
                File.WriteAllText(tmp, SaveFormat.Write(_profile));
                if (File.Exists(path))
                {
                    File.Copy(path, path + ".bak", true);
                    File.Delete(path);
                }
                File.Move(tmp, path);
            }
            catch (Exception e)
            {
                GameLog.Warn("Profile", $"Save failed: {e.Message}");
                _dirty = true;
                _saveAt = Time.unscaledTime + 10f;
            }
        }

        private static void TryCopy(string from, string to)
        {
            try { if (File.Exists(from)) File.Copy(from, to, true); } catch (Exception) { }
        }

        // ---------- queries used by the FOB and the run ----------

        /// <summary>Permanent bonuses for the class in play (skills + research).</summary>
        public float[] Bonuses(string classId) => ProfileRules.PermanentTotals(Data, classId, Skills, ResearchProjects);

        public bool WeaponResearched(string weaponId) => ResearchRules.WeaponUnlocked(ResearchProjects, Data.Research, weaponId);

        /// <summary>Every catalog weapon as a loadout option. Heavy weapons need research unless the class starts with them.</summary>
        public static List<LoadoutOption> LoadoutOptions()
        {
            var list = new List<LoadoutOption>();
            foreach (WeaponDefinition w in WeaponCatalog.All)
            {
                bool gated = false;
                foreach (ResearchDef r in ResearchProjects) if (r.UnlocksWeapon == w.ContentId) gated = true;
                list.Add(new LoadoutOption(w.ContentId, SlotOf(w), gated));
            }
            return list;
        }

        public static LoadoutSlot SlotOf(WeaponDefinition w)
        {
            if (w.Category == WeaponCategory.Melee) return LoadoutSlot.Melee;
            if (w.Category == WeaponCategory.Pistol) return LoadoutSlot.Sidearm;
            return LoadoutSlot.Primary;
        }

        /// <summary>The class's saved loadout if still legal, otherwise its default.</summary>
        public string[] LoadoutFor(string classId, string[] classDefault)
        {
            Data.Loadouts.TryGetValue(classId ?? string.Empty, out string[] saved);
            return LoadoutRules.Resolve(saved, LoadoutOptions(), classDefault, WeaponResearched);
        }

        /// <summary>Banks a finished run and pays challenges.</summary>
        public RunSummary BankRun(string classId, RunResult result)
        {
            RunSummary s = ProfileRules.ApplyRun(Data, classId, result, Challenges);
            MarkDirty();
            Save();
            return s;
        }

        /// <summary>Dev: enough currency and XP to open every facility and try research and skills.</summary>
        public void GrantDev()
        {
            Data.Wallet.Add(new Cost(20000, 100));
            Data.OperatorXp = Math.Max(Data.OperatorXp, OperatorLevels.XpForLevel(6));
            MarkDirty();
        }
    }
}
