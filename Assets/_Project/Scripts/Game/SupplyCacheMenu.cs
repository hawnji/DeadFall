using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Encounters;
using Deadfall.Player;
using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>The Supply Cache buy menu: three weapons with tier, attachments and price, an ammo refill, your credits.</summary>
    public sealed class SupplyCacheMenu : MonoBehaviour
    {
        private const string ModalId = "supply_cache";
        private static SupplyCache _open;
        private PlayerCursorLock _cursor;
        private GUIStyle _title, _small, _price;

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
            if (_title == null)
            {
                _title = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
                _small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
                _price = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            }
            GUI.depth = -20;
            const float w = 460f, h = 360f;
            var area = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.Box(area, GUIContent.none);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 14f, area.y + 10f, w - 28f, h - 20f));
            WaveManager waves = WaveManager.Current;
            int credits = waves != null ? waves.LocalCredits : 0;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Supply Cache", _title);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{credits} credits", _price);
            GUILayout.EndHorizontal();
            if (waves == null || !waves.IsRunning) GUILayout.Label("Field Credits come from kills during a run (press N).", _small);
            GUILayout.Space(4f);

            var weapons = PlayerIdentity.Local != null ? PlayerIdentity.Local.GetComponent<PlayerWeapons>() : null;
            for (int i = 0; i < cache.Stock.Count; i++)
            {
                SupplyCache.Offer o = cache.Stock[i];
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.BeginVertical();
                GUI.color = TierColor(o.Mods.Tier);
                GUILayout.Label($"{o.Weapon.DisplayName}  ·  {o.Mods.Tier}", _title);
                GUI.color = Color.white;
                string replaces = weapons != null && weapons.DefinitionAt(weapons.SlotFor(o.Weapon)) is WeaponDefinition old ? $"Replaces {old.DisplayName}. " : string.Empty;
                string att = o.Mods.Attachments.Count > 0 ? string.Join(", ", o.Mods.Attachments.ConvertAll(a => a.DisplayName)) + ". " : string.Empty;
                GUILayout.Label($"{o.Weapon.Category}. {att}{replaces}", _small);
                GUILayout.EndVertical();
                GUI.enabled = credits >= o.Price;
                if (GUILayout.Button($"{o.Price}", GUILayout.Width(80f), GUILayout.Height(40f))) RunEconomy.Current?.Buy(cache, i);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label("Refill all ammo", _title);
            GUILayout.FlexibleSpace();
            GUI.enabled = credits >= LootRules.AmmoRefillPrice;
            if (GUILayout.Button($"{LootRules.AmmoRefillPrice}", GUILayout.Width(80f), GUILayout.Height(30f))) RunEconomy.Current?.Buy(cache, cache.Stock.Count);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close (Esc)", GUILayout.Height(26f))) Close();
            GUILayout.EndArea();
        }

        public static Color TierColor(WeaponTier t) =>
            t == WeaponTier.Prototype ? new Color(0.85f, 0.55f, 1f) : t == WeaponTier.Refined ? new Color(0.45f, 0.75f, 1f) : Color.white;
    }
}
