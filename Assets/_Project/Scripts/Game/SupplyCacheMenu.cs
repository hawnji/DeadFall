using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Encounters;
using Deadfall.Player;
using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>The Supply Cache buy menu: ammo refill, med kit and grenade bundle with prices, plus your credits.</summary>
    public sealed class SupplyCacheMenu : MonoBehaviour
    {
        private const string ModalId = "supply_cache";
        private static SupplyCache _open;
        private PlayerCursorLock _cursor;

        public static bool IsOpen => _open != null;

        public static void Open(SupplyCache cache)
        {
            if (cache == null || !UiModal.TryOpen(ModalId)) return;
            _open = cache;
            PlayerCursorLock cursor = Cursor();
            if (cursor != null) cursor.SetLocked(false);
        }

        public static void Close()
        {
            if (_open == null) return;
            _open = null;
            UiModal.Close(ModalId);
            PlayerCursorLock cursor = Cursor();
            if (cursor != null) cursor.SetLocked(true);
        }

        private static PlayerCursorLock Cursor() => PlayerIdentity.Local != null ? PlayerIdentity.Local.GetComponent<PlayerCursorLock>() : null;

        private void Update()
        {
            if (_open == null) return;
            PlayerIdentity me = PlayerIdentity.Local;
            if (_cursor == null && me != null) _cursor = me.GetComponent<PlayerCursorLock>();
            // Esc relocks the cursor (PlayerCursorLock), which closes the menu; walking away does too.
            bool away = me == null || (me.transform.position - _open.transform.position).sqrMagnitude > 4f * 4f;
            if ((_cursor != null && _cursor.IsLocked) || away)
            {
                _open = null;
                UiModal.Close(ModalId);
                if (away && _cursor != null) _cursor.SetLocked(true);
            }
        }

        private void OnDisable() => Close();

        private void OnGUI()
        {
            SupplyCache cache = _open;
            if (cache == null) return;
            HudKit.Begin();
            GUI.depth = -20;
            WaveManager waves = WaveManager.Current;
            int credits = waves != null ? waves.LocalCredits : 0;
            int rows = cache.Stock.Count;
            Rect c = HudKit.Window(520f, 140f + rows * 78f + 40f, "SUPPLY CACHE", $"{credits:N0} CREDITS");
            float y = c.y;
            if (waves == null || !waves.IsRunning)
            {
                HudKit.Label(new Rect(c.x, y, c.width, 20f), "Field Credits come from kills during a run (press N).", 13, HudKit.TextDim);
                y += 26f;
            }

            PlayerIdentity me = PlayerIdentity.Local;
            var health = me != null ? me.GetComponent<PlayerHealth>() : null;
            var thrower = me != null ? me.GetComponent<GrenadeThrower>() : null;
            for (int i = 0; i < cache.Stock.Count; i++)
            {
                SupplyCache.Offer o = cache.Stock[i];
                Color tc = KindColor(o.Kind);
                string state = string.Empty;
                bool useful = true;
                if (o.Kind == SupplyCache.Supply.Heal && health != null)
                {
                    useful = health.Current < health.Max - 0.5f;
                    state = useful ? $"{Mathf.CeilToInt(health.Current)}/{Mathf.CeilToInt(health.Max)} HP" : "Full health";
                }
                else if (o.Kind == SupplyCache.Supply.Grenades && thrower != null)
                {
                    useful = thrower.Count < thrower.Capacity;
                    state = $"{thrower.Count}/{thrower.Capacity} carried";
                }
                var row = new Rect(c.x, y, c.width, 70f);
                HudKit.Round(row, HudKit.GlassLight);
                HudKit.Round(new Rect(row.x, row.y + 8f, 3f, row.height - 16f), tc, true);
                HudKit.Label(new Rect(row.x + 16f, row.y + 8f, row.width - 150f, 24f), o.Name, 18, Color.white, TextAnchor.MiddleLeft, head: true);
                if (state.Length > 0)
                {
                    float nw = HudKit.TextWidth(o.Name, 18, head: true);
                    HudKit.Label(new Rect(row.x + 26f + nw, row.y + 8f, 160f, 24f), state.ToUpperInvariant(), 12, tc, TextAnchor.MiddleLeft, head: true);
                }
                HudKit.Label(new Rect(row.x + 16f, row.y + 34f, row.width - 150f, 30f), o.Detail, 12, HudKit.TextDim, TextAnchor.UpperLeft, wrap: true);
                if (HudKit.Button(new Rect(row.xMax - 118f, row.y + 15f, 104f, 40f), o.Price.ToString("N0"), false, useful && credits >= o.Price, 17)) RunEconomy.Current?.Buy(cache, i);
                y += 78f;
            }

            HudKit.KeyChip(c.x, c.yMax - 26f, "Esc", 24f);
            HudKit.Label(new Rect(c.x + 44f, c.yMax - 26f, 200f, 24f), "Close", 13, HudKit.TextDim);
            if (HudKit.Button(new Rect(c.xMax - 120f, c.yMax - 34f, 120f, 34f), "CLOSE")) Close();
            HudKit.End();
        }

        private static Color KindColor(SupplyCache.Supply k) =>
            k == SupplyCache.Supply.Heal ? new Color(0.45f, 1f, 0.55f) : k == SupplyCache.Supply.Grenades ? new Color(0.85f, 0.9f, 0.45f) : new Color(0.55f, 0.8f, 1f);

        public static Color TierColor(WeaponTier t) =>
            t == WeaponTier.Prototype ? new Color(0.85f, 0.55f, 1f) : t == WeaponTier.Refined ? new Color(0.45f, 0.75f, 1f) : new Color(0.85f, 0.87f, 0.9f);
    }
}
