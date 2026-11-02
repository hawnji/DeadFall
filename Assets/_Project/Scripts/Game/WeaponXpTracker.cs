using System.Collections.Generic;
using System.Text;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Player;
using Deadfall.Rules.Progression;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// Weapon XP foundation (GAME_DESIGN §9.2): kills by the local player give the weapon in hand XP; levels use
    /// <see cref="WeaponXp"/>. Stored in PlayerPrefs for now; the versioned save (M10) takes it over.
    /// </summary>
    public sealed class WeaponXpTracker : MonoBehaviour
    {
        private const string PrefKey = "deadfall.weaponxp";
        private static readonly Dictionary<string, long> Xp = new Dictionary<string, long>();
        private static bool _loaded;
        private PlayerWeapons _weapons;
        private bool _dirty;
        private float _saveAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Xp.Clear(); _loaded = false; }

        public static long XpOf(string weaponId) { Load(); return weaponId != null && Xp.TryGetValue(weaponId, out long v) ? v : 0; }
        public static int LevelOf(string weaponId) => WeaponXp.LevelForXp(XpOf(weaponId));
        public static IReadOnlyDictionary<string, long> All { get { Load(); return Xp; } }

        /// <summary>Replaces the stored XP (save system import).</summary>
        public static void Import(IDictionary<string, long> values)
        {
            Xp.Clear();
            foreach (KeyValuePair<string, long> kv in values) Xp[kv.Key] = kv.Value;
            _loaded = true;
        }

        public void Bind(GameObject player) => _weapons = player != null ? player.GetComponent<PlayerWeapons>() : null;

        private void OnEnable() => EventBus<HitConfirmed>.Subscribe(OnHit);

        private void OnDisable()
        {
            EventBus<HitConfirmed>.Unsubscribe(OnHit);
            Save();
        }

        private void OnHit(HitConfirmed e)
        {
            if (!e.Killed || _weapons == null || e.Attacker != _weapons.gameObject) return;
            WeaponDefinition def = _weapons.CurrentDefinition;
            if (def == null) return;
            Load();
            long before = XpOf(def.ContentId);
            long after = before + WeaponXp.XpForKill(1f);
            Xp[def.ContentId] = after;
            int lb = WeaponXp.LevelForXp(before), la = WeaponXp.LevelForXp(after);
            if (la > lb) RunToasts.Show($"{def.DisplayName} reached level {la}", new Color(0.7f, 0.9f, 1f));
            _dirty = true;
        }

        private void Update()
        {
            if (_dirty && Time.unscaledTime >= _saveAt) { Save(); _saveAt = Time.unscaledTime + 10f; }
        }

        private void OnApplicationQuit() => Save();

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            string s = PlayerPrefs.GetString(PrefKey, string.Empty);
            foreach (string part in s.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0 && long.TryParse(part.Substring(eq + 1), out long v)) Xp[part.Substring(0, eq)] = v;
            }
        }

        private void Save()
        {
            if (!_dirty) return;
            _dirty = false;
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, long> kv in Xp) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
            PlayerPrefs.SetString(PrefKey, sb.ToString());
        }
    }
}
