using System;
using Deadfall.Core;
using Deadfall.Rules.Progression;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// The wall screen in FOB Halcyon opposite Command Center: the last run's result, map and mode, its stat tiles, the XP it paid,
    /// the MVP and your own headshots, revives and downs. Recorded on this machine when a run is banked and kept in PlayerPrefs,
    /// so it survives a restart. Text is <see cref="WorldText"/>.
    /// </summary>
    public sealed class LastRunBoard : MonoBehaviour
    {
        [Serializable]
        private sealed class Record
        {
            public bool Won, Aborted;
            public string Map, Mode, When;
            public int Surges, Kills, Objectives, Specimens;
            public bool Gauntlet;
            public long GauntletMs, Xp;
            public string Mvp;
            public bool HasSelf;
            public int Headshots, Revives, Downs;
        }

        private const string PrefsKey = "deadfall.lastrun";
        public const float ScreenW = 5.7f, ScreenH = 3.1f;

        private static Record _last;
        private static bool _loaded;
        private static int _version;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _last = null; _loaded = false; _version = 0; }

        /// <summary>Called when a run is banked (<see cref="ProgressionTracker"/>), before the squad returns to the FOB.</summary>
        public static void RecordRun(RunResult r, RunSummary s, bool aborted)
        {
            var rec = new Record
            {
                Won = r.Won,
                Aborted = aborted,
                Mode = r.Gauntlet ? "GAUNTLET" : r.Escort ? "ESCORT" : r.Endless ? "ENDLESS" : r.Operation ? "OPERATION" : "SURVIVAL",
                Map = MapName(r),
                When = DateTime.Now.ToString("MMM d  HH:mm"),
                Surges = r.SurgesCleared,
                Kills = r.Kills,
                Objectives = r.Objectives,
                Specimens = r.Specimens,
                Gauntlet = r.Gauntlet,
                GauntletMs = r.GauntletMs,
                Xp = s.Reward.OperatorXp,
            };
            RunStatsDirector stats = RunStatsDirector.Current;
            if (stats != null)
            {
                rec.Mvp = stats.MvpName();
                if (stats.TryLocalRow(out RunStatsDirector.Row row))
                {
                    rec.HasSelf = true;
                    rec.Headshots = row.Headshots;
                    rec.Revives = row.Revives;
                    rec.Downs = row.Downs;
                }
            }
            _last = rec;
            _loaded = true;
            _version++;
            try { PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(rec)); PlayerPrefs.Save(); }
            catch (Exception) { /* the screen still shows it this session */ }
        }

        private static string MapName(RunResult r)
        {
            if (r.Gauntlet) return Deadfall.Rules.World.EnvironmentCatalog.At(r.GauntletEnvironment).ShortName;
            MapSession m = MapSession.Current;
            return m != null ? Deadfall.Rules.World.EnvironmentCatalog.ForSeed(m.Seed).ShortName : null;
        }

        private static Record Last
        {
            get
            {
                if (_loaded) return _last;
                _loaded = true;
                try
                {
                    string json = PlayerPrefs.GetString(PrefsKey, null);
                    if (!string.IsNullOrEmpty(json)) _last = JsonUtility.FromJson<Record>(json);
                }
                catch (Exception) { _last = null; }
                return _last;
            }
        }

        // ---------------------------------------------------------------- building

        /// <summary>Builds the screen under <paramref name="parent"/> at <paramref name="local"/>, facing along <paramref name="yaw"/> (the side players read from is -forward).</summary>
        public static LastRunBoard Build(Transform parent, Vector3 local, float yaw)
        {
            var go = new GameObject("LastRunScreen");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Transform t = go.transform;
            // Bezel against the wall (+z), glowing panel in front of it.
            FobBuilder.Box(t, "TvBezel", new Vector3(0f, 0f, 0.1f), new Vector3(ScreenW + 0.3f, ScreenH + 0.3f, 0.16f), new Color(0.12f, 0.13f, 0.14f), surface: Surface.Metal);
            Panel(t, "TvScreen", Vector3.zero, new Vector2(ScreenW, ScreenH), new Color(0.03f, 0.045f, 0.06f), 0.8f);
            Panel(t, "TvRule", new Vector3(0f, ScreenH * 0.5f - 0.45f, -0.012f), new Vector2(ScreenW - 0.6f, 0.025f), HudKit.Accent, 1.2f);
            for (int i = 0; i < 4; i++)
                Panel(t, "TvTile", new Vector3(TileX(i), -0.42f, -0.012f), new Vector2(1.25f, 0.82f), new Color(0.07f, 0.09f, 0.11f), 0.8f);
            Panel(t, "TvTileRule", new Vector3(0f, -0.84f, -0.013f), new Vector2(ScreenW - 0.4f, 0.015f), new Color(0.3f, 0.34f, 0.38f), 0.8f);
            var board = go.AddComponent<LastRunBoard>();
            board.Create();
            return board;
        }

        private static float TileX(int i) => -2.1f + i * 1.4f;

        private static void Panel(Transform parent, string name, Vector3 local, Vector2 size, Color c, float glow)
        {
            GameObject p = FobBuilder.Box(parent, name, local, new Vector3(size.x, size.y, 0.02f), c, collider: false);
            p.GetComponent<Renderer>().sharedMaterial = RuntimeMaterials.CreateEmissive(c, glow);
        }

        private TextMesh _title, _when, _head, _sub, _foot;
        private readonly TextMesh[] _tileLabel = new TextMesh[4], _tileValue = new TextMesh[4];
        private int _shown = -1;

        private const float Left = -ScreenW * 0.5f + 0.3f, Right = ScreenW * 0.5f - 0.3f, Top = ScreenH * 0.5f - 0.18f, Z = -0.03f;

        private void Create()
        {
            _title = Text("Title", new Vector3(Left, Top, Z), TextAnchor.UpperLeft, head: true);
            _when = Text("When", new Vector3(Right, Top, Z), TextAnchor.UpperRight, head: false);
            _head = Text("Result", new Vector3(Left, Top - 0.42f, Z), TextAnchor.UpperLeft, head: true);
            _sub = Text("Sub", new Vector3(Left, Top - 0.98f, Z), TextAnchor.UpperLeft, head: false);
            for (int i = 0; i < 4; i++)
            {
                _tileLabel[i] = Text("TileLabel", new Vector3(TileX(i), -0.1f, Z), TextAnchor.UpperCenter, head: false);
                _tileValue[i] = Text("TileValue", new Vector3(TileX(i), -0.36f, Z), TextAnchor.UpperCenter, head: true);
            }
            _foot = Text("Foot", new Vector3(Left, -ScreenH * 0.5f + 0.42f, Z), TextAnchor.UpperLeft, head: false);
            Refresh();
        }

        private void Update()
        {
            if (_shown != _version) Refresh();
        }

        private void Refresh()
        {
            _shown = _version;
            Record r = Last;
            Set(_title, "LAST DEPLOYMENT", 0.17f, 3.2f, HudKit.Accent);
            if (r == null)
            {
                Set(_when, string.Empty, 0.13f, 2f, HudKit.TextDim);
                Set(_head, "NO DEPLOYMENTS YET", 0.42f, ScreenW - 0.6f, HudKit.Text);
                Set(_sub, "Deploy from Command Center. Your last run shows here.", 0.15f, ScreenW - 0.6f, HudKit.TextDim);
                for (int i = 0; i < 4; i++) { Set(_tileLabel[i], string.Empty, 0.1f, 1f, HudKit.TextDim); Set(_tileValue[i], "--", 0.32f, 1.1f, HudKit.TextDim); }
                Set(_foot, string.Empty, 0.14f, 1f, HudKit.TextDim);
                return;
            }
            string result = r.Won
                ? (r.Mode == "OPERATION" ? "OPERATION COMPLETE" : r.Gauntlet ? "GAUNTLET CLEARED" : r.Mode == "ESCORT" ? "CART DELIVERED" : "DEPLOYMENT COMPLETE")
                : r.Aborted ? "DEPLOYMENT ABORTED" : "SQUAD WIPED";
            Set(_when, r.When ?? string.Empty, 0.13f, 2f, HudKit.TextDim);
            Set(_head, result, 0.42f, ScreenW - 0.6f, r.Won ? HudKit.Good : HudKit.Bad);
            string sub = string.IsNullOrEmpty(r.Map) ? r.Mode : $"{r.Map}   ·   {r.Mode}";
            Set(_sub, sub, 0.15f, ScreenW - 0.6f, HudKit.Text);

            string time = r.GauntletMs > 0 ? Deadfall.Rules.Encounters.GauntletRules.Clock(r.GauntletMs / 1000.0) : "--";
            string[] labels = { "SURGES", "KILLS", r.Gauntlet ? "TIME" : "OBJECTIVES", "SPECIMENS" };
            string[] values = { r.Surges.ToString("N0"), r.Kills.ToString("N0"), r.Gauntlet ? time : r.Objectives.ToString("N0"), r.Specimens.ToString("N0") };
            for (int i = 0; i < 4; i++)
            {
                Set(_tileLabel[i], labels[i], 0.1f, 1.1f, HudKit.TextDim);
                Set(_tileValue[i], values[i], 0.32f, 1.1f, HudKit.Text);
            }

            string foot = $"+{r.Xp:N0} XP";
            if (r.HasSelf) foot += $"     Headshots {r.Headshots}   Revives {r.Revives}   Downs {r.Downs}";
            if (!string.IsNullOrEmpty(r.Mvp)) foot += $"     MVP: {r.Mvp}";
            Set(_foot, foot, 0.14f, ScreenW - 0.6f, HudKit.Text);
        }

        private TextMesh Text(string name, Vector3 local, TextAnchor anchor, bool head) => WorldText.Create(transform, name, local, anchor, head);

        private static void Set(TextMesh tm, string text, float capHeight, float maxWidth, Color color) => WorldText.Set(tm, text, capHeight, maxWidth, color);
    }
}
