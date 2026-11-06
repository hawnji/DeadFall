using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Deadfall.Rules.Progression
{
    public enum SaveLoadStatus : byte
    {
        Ok,
        /// <summary>Nothing to load (new player).</summary>
        Empty,
        /// <summary>Header or checksum wrong. The caller tries the backup.</summary>
        Corrupt,
        /// <summary>Written by a newer build. Never overwrite it.</summary>
        TooNew,
    }

    public struct SaveLoadResult
    {
        public SaveLoadStatus Status;
        public PlayerProfile Profile;
        /// <summary>Version the file was written with (before migrations).</summary>
        public int FromVersion;
        public bool Migrated => Status == SaveLoadStatus.Ok && FromVersion < SaveFormat.CurrentVersion;
    }

    /// <summary>
    /// Versioned profile save (T-54): plain "key=value" lines under a header, a version line, and an FNV-1a checksum
    /// line last. Older versions are upgraded one step at a time by <see cref="Migrations"/> before parsing, so
    /// the parser only ever reads the current layout. Engine-free; the game layer does file IO and backups.
    /// </summary>
    public static class SaveFormat
    {
        public const string Header = "deadfall-profile";
        public const int CurrentVersion = 1;

        /// <summary>
        /// Upgrade steps on the raw key/value map: entry <c>i</c> turns version <c>i + 1</c> into <c>i + 2</c>.
        /// Empty while version 1 is the only layout; add a step whenever the layout changes and bump
        /// <see cref="CurrentVersion"/>.
        /// </summary>
        public static readonly List<Action<Dictionary<string, string>>> Migrations = new List<Action<Dictionary<string, string>>>();

        public static string Write(PlayerProfile p)
        {
            var sb = new StringBuilder(1024);
            sb.Append(Header).Append('\n');
            Line(sb, "version", CurrentVersion.ToString(CultureInfo.InvariantCulture));
            if (IsId(p.SelectedClass)) Line(sb, "class", p.SelectedClass);
            Line(sb, "opxp", p.OperatorXp);
            foreach (KeyValuePair<string, long> kv in Sorted(p.Wallet.All)) if (IsId(kv.Key)) Line(sb, "cur." + kv.Key, kv.Value);
            foreach (KeyValuePair<string, long> kv in Sorted(p.ClassXp)) if (IsId(kv.Key)) Line(sb, "cxp." + kv.Key, kv.Value);
            foreach (KeyValuePair<string, int> kv in Sorted(p.Prestige)) if (IsId(kv.Key)) Line(sb, "prestige." + kv.Key, kv.Value);
            Line(sb, "skills", List(p.Skills));
            Line(sb, "research", List(p.Research));
            Line(sb, "done", List(p.Completed));
            if (p.BankedRuns.Count > 0) Line(sb, "banked", List(p.BankedRuns.ToArray()));
            foreach (KeyValuePair<string, string[]> kv in Sorted(p.Loadouts)) if (IsId(kv.Key)) Line(sb, "loadout." + kv.Key, List(kv.Value));
            foreach (KeyValuePair<string, string[]> kv in Sorted(p.Attachments)) if (IsId(kv.Key) && kv.Value != null && kv.Value.Length > 0) Line(sb, "attach." + kv.Key, List(kv.Value));
            foreach (KeyValuePair<string, long> kv in Sorted(p.Stats)) if (IsId(kv.Key)) Line(sb, "stat." + kv.Key, kv.Value);
            foreach (KeyValuePair<string, long> kv in Sorted(p.WeaponXp)) if (IsId(kv.Key)) Line(sb, "wxp." + kv.Key, kv.Value);
            string body = sb.ToString();
            return body + "sum=" + Checksum(body).ToString("x8", CultureInfo.InvariantCulture) + "\n";
        }

        public static SaveLoadResult Read(string text) => Read(text, Migrations);

        public static SaveLoadResult Read(string text, IReadOnlyList<Action<Dictionary<string, string>>> migrations)
        {
            if (string.IsNullOrWhiteSpace(text)) return new SaveLoadResult { Status = SaveLoadStatus.Empty };
            text = text.Replace("\r\n", "\n");
            int sumAt = text.LastIndexOf("sum=", StringComparison.Ordinal);
            if (!text.StartsWith(Header + "\n", StringComparison.Ordinal) || sumAt < 0 || (sumAt > 0 && text[sumAt - 1] != '\n'))
                return new SaveLoadResult { Status = SaveLoadStatus.Corrupt };
            string body = text.Substring(0, sumAt);
            string sumText = text.Substring(sumAt + 4).Trim();
            if (!uint.TryParse(sumText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint sum) || sum != Checksum(body))
                return new SaveLoadResult { Status = SaveLoadStatus.Corrupt };

            var map = new Dictionary<string, string>();
            string[] lines = body.Split('\n');
            for (int i = 1; i < lines.Length; i++)
            {
                int eq = lines[i].IndexOf('=');
                if (eq > 0) map[lines[i].Substring(0, eq)] = lines[i].Substring(eq + 1);
            }
            if (!map.TryGetValue("version", out string vs) || !int.TryParse(vs, NumberStyles.Integer, CultureInfo.InvariantCulture, out int version) || version < 1)
                return new SaveLoadResult { Status = SaveLoadStatus.Corrupt };
            int latest = 1 + (migrations?.Count ?? 0);
            if (version > latest) return new SaveLoadResult { Status = SaveLoadStatus.TooNew, FromVersion = version };
            for (int v = version; v < latest; v++) migrations[v - 1](map);

            return new SaveLoadResult { Status = SaveLoadStatus.Ok, FromVersion = version, Profile = Parse(map) };
        }

        private static PlayerProfile Parse(Dictionary<string, string> map)
        {
            var p = new PlayerProfile();
            foreach (KeyValuePair<string, string> kv in map)
            {
                string k = kv.Key, v = kv.Value;
                if (k == "class") p.SelectedClass = IsId(v) ? v : null;
                else if (k == "opxp") p.OperatorXp = Long(v);
                else if (k == "skills") Fill(p.Skills, v);
                else if (k == "research") Fill(p.Research, v);
                else if (k == "done") Fill(p.Completed, v);
                else if (k == "banked") { foreach (string id in Split(v)) if (IsId(id) && !p.BankedRuns.Contains(id)) p.BankedRuns.Add(id); }
                else if (k.StartsWith("cur.", StringComparison.Ordinal)) p.Wallet.Set(k.Substring(4), Long(v));
                else if (k.StartsWith("cxp.", StringComparison.Ordinal)) p.ClassXp[k.Substring(4)] = Long(v);
                else if (k.StartsWith("prestige.", StringComparison.Ordinal)) p.Prestige[k.Substring(9)] = (int)Math.Min(int.MaxValue, Long(v));
                else if (k.StartsWith("loadout.", StringComparison.Ordinal)) p.Loadouts[k.Substring(8)] = Split(v);
                else if (k.StartsWith("attach.", StringComparison.Ordinal)) p.Attachments[k.Substring(7)] = Split(v);
                else if (k.StartsWith("stat.", StringComparison.Ordinal)) p.Stats[k.Substring(5)] = Long(v);
                else if (k.StartsWith("wxp.", StringComparison.Ordinal)) p.WeaponXp[k.Substring(4)] = Long(v);
                // Unknown keys are ignored so an older build can still read a file it partly understands.
            }
            return p;
        }

        /// <summary>
        /// Weapon XP from before the profile existed (M6 kept it in PlayerPrefs as "id=xp;id=xp;"). Merged into the
        /// profile once, keeping the larger value per weapon.
        /// </summary>
        public static int ImportLegacyWeaponXp(PlayerProfile p, string legacy)
        {
            if (string.IsNullOrEmpty(legacy)) return 0;
            int n = 0;
            foreach (string part in legacy.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string id = part.Substring(0, eq);
                if (!IsId(id) || !long.TryParse(part.Substring(eq + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out long xp) || xp < 0) continue;
                p.WeaponXp.TryGetValue(id, out long have);
                if (xp > have) p.WeaponXp[id] = xp;
                n++;
            }
            return n;
        }

        /// <summary>FNV-1a over UTF-16 code units, enough to catch truncation and hand edits.</summary>
        public static uint Checksum(string s)
        {
            uint h = 2166136261;
            foreach (char c in s) { h ^= c; h *= 16777619; }
            return h;
        }

        /// <summary>Ids are written raw, so only safe characters are allowed.</summary>
        public static bool IsId(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > 64) return false;
            foreach (char c in s)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '.' && c != '_' && c != '-') return false;
            return true;
        }

        private static void Line(StringBuilder sb, string key, string value) => sb.Append(key).Append('=').Append(value).Append('\n');
        private static void Line(StringBuilder sb, string key, long value) => Line(sb, key, value.ToString(CultureInfo.InvariantCulture));

        private static string List(IEnumerable<string> ids)
        {
            var l = new List<string>();
            foreach (string id in ids) if (IsId(id)) l.Add(id);
            if (!(ids is string[])) l.Sort(StringComparer.Ordinal);
            return string.Join(",", l);
        }

        private static IEnumerable<KeyValuePair<string, T>> Sorted<T>(IEnumerable<KeyValuePair<string, T>> e)
        {
            var l = new List<KeyValuePair<string, T>>(e);
            l.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return l;
        }

        private static long Long(string v) => long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out long r) ? Math.Max(0, r) : 0;

        private static string[] Split(string v) => string.IsNullOrEmpty(v) ? Array.Empty<string>() : v.Split(',');

        private static void Fill(HashSet<string> set, string v)
        {
            foreach (string id in Split(v)) if (IsId(id)) set.Add(id);
        }
    }
}
