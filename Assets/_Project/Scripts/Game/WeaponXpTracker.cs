using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Player;
using Deadfall.Rules.Progression;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// Weapon XP (GAME_DESIGN §9.2): kills by the local player give the weapon in hand XP; levels use
    /// <see cref="WeaponXp"/>. Stored in the player profile (M10); the old PlayerPrefs store is imported once by
    /// <see cref="ProfileService"/>.
    /// </summary>
    public sealed class WeaponXpTracker : MonoBehaviour
    {
        private static readonly Dictionary<string, long> Empty = new Dictionary<string, long>();
        private PlayerWeapons _weapons;

        private static Dictionary<string, long> Store => ProfileService.Profile != null ? ProfileService.Profile.WeaponXp : Empty;

        public static long XpOf(string weaponId) => weaponId != null && Store.TryGetValue(weaponId, out long v) ? v : 0;
        public static int LevelOf(string weaponId) => WeaponXp.LevelForXp(XpOf(weaponId));
        public static IReadOnlyDictionary<string, long> All => Store;

        public void Bind(GameObject player) => _weapons = player != null ? player.GetComponent<PlayerWeapons>() : null;

        private void OnEnable() => EventBus<HitConfirmed>.Subscribe(OnHit);
        private void OnDisable() => EventBus<HitConfirmed>.Unsubscribe(OnHit);

        private void OnHit(HitConfirmed e)
        {
            if (!e.Killed || _weapons == null || e.Attacker != _weapons.gameObject || ProfileService.Current == null) return;
            WeaponDefinition def = _weapons.CurrentDefinition;
            if (def == null) return;
            long before = XpOf(def.ContentId);
            long after = before + WeaponXp.XpForKill(1f);
            Store[def.ContentId] = after;
            int lb = WeaponXp.LevelForXp(before), la = WeaponXp.LevelForXp(after);
            if (la > lb) RunToasts.Show($"{def.DisplayName} reached level {la}", new Color(0.7f, 0.9f, 1f));
            ProfileService.Current.MarkDirty(notify: false);
        }
    }
}
