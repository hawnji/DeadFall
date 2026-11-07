using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Encounters;
using Deadfall.Player;
using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// The Mod Bench menu: a turning 3D preview of the gun in your hands with the parts you've picked, one row of options
    /// per slot (only parts this gun takes, with prices), the stats before and after, and BUILD with the total. You pay
    /// only for new parts; taking parts off is free. The host charges and sends the refit back (ammo carries over).
    /// </summary>
    public sealed class ModBenchMenu : MonoBehaviour
    {
        private const string ModalId = "mod_bench";
        private static ModBench _open;
        private PlayerCursorLock _cursor;

        // What the menu is showing: the gun, its mods when we looked, and the picks per slot (null = none).
        private WeaponDefinition _def;
        private WeaponMods _seenMods;
        private readonly Dictionary<AttachmentSlot, AttachmentDef> _picks = new Dictionary<AttachmentSlot, AttachmentDef>();
        private WeaponMods _preview;
        private bool _previewDirty = true;

        // The 3D preview: a camera far below the map rendering the gun into a texture.
        private GameObject _rig;
        private Camera _cam;
        private RenderTexture _rt;
        private Transform _spin;
        private GameObject _gun;
        private float _frameDistance = 1f;

        public static bool IsOpen => _open != null;

        public static void Open(ModBench bench)
        {
            if (bench == null || !UiModal.TryOpen(ModalId)) return;
            _open = bench;
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

        private static PlayerWeapons LocalWeapons() => PlayerIdentity.Local != null ? PlayerIdentity.Local.GetComponent<PlayerWeapons>() : null;

        private void Update()
        {
            if (_open == null)
            {
                if (_rig != null) DestroyRig();
                _def = null;
                return;
            }
            PlayerIdentity me = PlayerIdentity.Local;
            if (_cursor == null && me != null) _cursor = me.GetComponent<PlayerCursorLock>();
            bool away = me == null || (me.transform.position - _open.transform.position).sqrMagnitude > 4f * 4f;
            if ((_cursor != null && _cursor.IsLocked) || away)
            {
                _open = null;
                UiModal.Close(ModalId);
                if (away && _cursor != null) _cursor.SetLocked(true);
                return;
            }
            PlayerWeapons weapons = LocalWeapons();
            WeaponDefinition def = weapons != null ? weapons.CurrentDefinition : null;
            WeaponMods mods = weapons != null ? weapons.CurrentMods : null;
            if (def != _def || mods != _seenMods) Reset(def, mods); // opened, swapped guns, or a build came back
            if (_def == null || _def.Kind != WeaponKind.Firearm) return;
            EnsureRig();
            if (_previewDirty) RebuildPreview();
            if (_spin != null) _spin.localRotation = Quaternion.Euler(0f, 90f + Mathf.Sin(Time.unscaledTime * 0.6f) * 28f, 0f);
        }

        private void OnDisable()
        {
            Close();
            DestroyRig();
        }

        private void Reset(WeaponDefinition def, WeaponMods mods)
        {
            _def = def;
            _seenMods = mods;
            _picks.Clear();
            if (mods != null)
                foreach (AttachmentDef a in mods.Attachments)
                    if (a.Slot != AttachmentSlot.Special) _picks[a.Slot] = a;
            _previewDirty = true;
        }

        private WeaponMods Desired()
        {
            if (_def == null) return null;
            var picks = new List<AttachmentDef>();
            foreach (AttachmentDef a in _picks.Values) if (a != null) picks.Add(a);
            return ModBenchRules.Build(_seenMods, picks, _def.ContentId, _def.Category);
        }

        // ---------------------------------------------------------------- preview

        private void EnsureRig()
        {
            if (_rig != null) return;
            _rig = new GameObject("[ModBenchPreview]");
            _rig.transform.position = new Vector3(0f, -3000f, 0f);
            _spin = new GameObject("Spin").transform;
            _spin.SetParent(_rig.transform, false);
            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(_rig.transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.07f, 0.08f, 0.095f, 1f);
            _cam.fieldOfView = 22f;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 20f;
            _rt = new RenderTexture(1120, 560, 24) { name = "ModBenchPreview" };
            _cam.targetTexture = _rt;
            AddLight(new Vector3(-1.2f, 1.4f, -1.5f), 2.2f, new Color(1f, 0.95f, 0.88f));
            AddLight(new Vector3(1.5f, 0.6f, 1.2f), 1.2f, new Color(0.6f, 0.75f, 1f));
            AddLight(new Vector3(0f, -1.2f, 0f), 0.5f, Color.white);
        }

        private void AddLight(Vector3 at, float intensity, Color color)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(_rig.transform, false);
            go.transform.localPosition = at;
            Light l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 6f;
            l.intensity = intensity;
            l.color = color;
            l.shadows = LightShadows.None;
        }

        private void DestroyRig()
        {
            if (_rig != null) Destroy(_rig);
            _rig = null;
            _cam = null;
            _spin = null;
            _gun = null;
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
            _rt = null;
        }

        private void RebuildPreview()
        {
            _previewDirty = false;
            _preview = Desired();
            if (_gun != null) Destroy(_gun);
            _gun = null;
            if (_spin == null || _def == null) return;
            // The gun's centre sits on the spin axis: the model is offset inside a holder.
            var holder = new GameObject("Gun");
            holder.transform.SetParent(_spin, false);
            _gun = holder;
            GameObject model = ModelLibrary.Spawn(WeaponViewmodel.ModelId(_def), holder.transform, slot => ModelLibrary.SlotFromAccent(slot, _def.PlaceholderColor));
            if (model == null) return;
            WeaponDresser.Fit(holder.transform, _def, _preview, WeaponViewmodel.MuzzlePoint(_def), 0);
            // Frame the gun with its parts (bounds in the holder's space, ignoring hidden parts).
            var b = new Bounds();
            bool any = false;
            foreach (Renderer r in holder.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            if (!any) return;
            Vector3 local = holder.transform.InverseTransformPoint(b.center);
            holder.transform.localPosition = -local;
            float size = Mathf.Max(b.size.x, b.size.y, b.size.z);
            float tan = Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float aspect = (float)_rt.width / _rt.height;
            _frameDistance = Mathf.Max(size * 0.5f / (tan * aspect), size * 0.3f / tan) * 1.15f;
            _cam.transform.localPosition = new Vector3(0f, _frameDistance * 0.18f, -_frameDistance);
            _cam.transform.localRotation = Quaternion.LookRotation(-_cam.transform.localPosition, Vector3.up);
        }

        // ---------------------------------------------------------------- menu

        private void OnGUI()
        {
            if (_open == null) return;
            HudKit.Begin();
            GUI.depth = -20;
            WaveManager waves = WaveManager.Current;
            int credits = waves != null ? waves.LocalCredits : 0;
            Rect c = HudKit.Window(1200f, 760f, "MOD BENCH", $"{credits:N0} CREDITS", HudKit.Info);
            if (_def == null || _def.Kind != WeaponKind.Firearm)
            {
                HudKit.Label(new Rect(c.x, c.y, c.width, 30f), "Hold a gun to fit attachments.", 16, HudKit.TextDim);
                Footer(c, 0, false, credits);
                HudKit.End();
                return;
            }

            // Left: the preview, then the stats.
            float leftW = 560f;
            var pv = new Rect(c.x, c.y, leftW, 280f);
            HudKit.Round(pv, new Color(0.07f, 0.08f, 0.095f, 1f));
            if (_rt != null) GUI.DrawTexture(new Rect(pv.x + 4f, pv.y + 4f, pv.width - 8f, pv.height - 8f), _rt, ScaleMode.ScaleAndCrop, false);
            WeaponTier tier = _seenMods != null ? _seenMods.Tier : WeaponTier.Standard;
            HudKit.Label(new Rect(pv.x + 14f, pv.y + 8f, pv.width - 28f, 26f), _def.DisplayName.ToUpperInvariant(), 20, Color.white, head: true);
            HudKit.Label(new Rect(pv.x + 14f, pv.y + 8f, pv.width - 28f, 26f), tier.ToString().ToUpperInvariant(), 13, SupplyCacheMenu.TierColor(tier), TextAnchor.MiddleRight, head: true);
            Stats(new Rect(c.x, pv.yMax + 14f, leftW, c.height - 280f - 14f - 50f));

            // Right: one row of options per slot.
            float x = c.x + leftW + 24f, w = c.xMax - x, y = c.y;
            foreach (AttachmentSlot slot in ModBenchRules.Slots)
            {
                List<AttachmentDef> options = AttachmentFit.For(_def.ContentId, _def.Category, slot);
                if (slot == AttachmentSlot.Optic && !WeaponSights.CanFitOptic(_def)) options.Clear();
                y = SlotRow(new Rect(x, y, w, 0f), slot, options, credits);
            }
            if (_seenMods != null)
                foreach (AttachmentDef a in _seenMods.Attachments)
                    if (a.Slot == AttachmentSlot.Special)
                    {
                        HudKit.Label(new Rect(x, y + 4f, w, 20f), $"Perk: {a.DisplayName} (from loot, stays on)", 13, HudKit.Warn);
                        y += 26f;
                    }

            WeaponMods desired = _preview ?? Desired();
            int total = ModBenchRules.Cost(_seenMods, desired);
            bool changed = !ModBenchRules.Same(_seenMods, desired);
            Footer(c, total, changed, credits);
            HudKit.End();
        }

        private float SlotRow(Rect r, AttachmentSlot slot, List<AttachmentDef> options, int credits)
        {
            HudKit.Section(new Rect(r.x, r.y, r.width, 22f), SlotName(slot));
            float y = r.y + 28f;
            if (options.Count == 0)
            {
                HudKit.Label(new Rect(r.x, y, r.width, 40f), "This gun takes nothing here.", 13, HudKit.TextDim);
                return y + 52f;
            }
            _picks.TryGetValue(slot, out AttachmentDef picked);
            int n = options.Count + 1;
            float gap = 8f, bw = (r.width - gap * (n - 1)) / n;
            for (int i = 0; i < n; i++)
            {
                AttachmentDef a = i == 0 ? null : options[i - 1];
                var br = new Rect(r.x + i * (bw + gap), y, bw, 52f);
                bool on = a == picked;
                bool owned = a != null && _seenMods != null && _seenMods.Attachments.Contains(a);
                string price = a == null ? "" : owned ? "FITTED" : ModBenchRules.Price(a).ToString("N0");
                if (HudKit.Button(br, string.Empty, on, true, 14, HudKit.Info))
                {
                    _picks[slot] = a;
                    _previewDirty = true;
                }
                HudKit.Label(new Rect(br.x, br.y + 6f, br.width, 22f), a == null ? "None" : a.DisplayName, 14, Color.white, TextAnchor.MiddleCenter, head: true);
                if (price.Length > 0)
                {
                    Color pc = owned ? HudKit.Good : ModBenchRules.Price(a) > credits ? HudKit.Bad : HudKit.Accent;
                    HudKit.Label(new Rect(br.x, br.y + 27f, br.width, 18f), price, 12, pc, TextAnchor.MiddleCenter, head: true);
                }
            }
            if (picked != null)
                HudKit.Label(new Rect(r.x, y + 56f, r.width, 18f), Effect(picked), 12, HudKit.TextDim);
            return y + 84f;
        }

        private void Footer(Rect c, int total, bool changed, int credits)
        {
            var bar = new Rect(c.x, c.yMax - 42f, c.width, 42f);
            HudKit.KeyChip(bar.x, bar.y + 9f, "Esc", 24f);
            HudKit.Label(new Rect(bar.x + 44f, bar.y + 9f, 200f, 24f), "Close", 13, HudKit.TextDim);
            bool running = WaveManager.Current != null && WaveManager.Current.IsRunning;
            string note = !running ? "Start a run to earn Field Credits." : !changed ? "Pick parts to build." : total > credits ? "Not enough credits." : total == 0 ? "Removing parts is free." : string.Empty;
            HudKit.Label(new Rect(bar.x + 120f, bar.y + 9f, 420f, 24f), note, 13, total > credits ? HudKit.Bad : HudKit.TextDim);
            HudKit.Label(new Rect(bar.xMax - 470f, bar.y, 200f, 42f), $"TOTAL  {total:N0}", 18, total > credits ? HudKit.Bad : Color.white, TextAnchor.MiddleRight, head: true);
            bool can = running && changed && total <= credits;
            if (HudKit.Button(new Rect(bar.xMax - 250f, bar.y, 130f, 42f), $"BUILD", false, can, 17, HudKit.Info))
                PerkMachineDirector.Current?.RequestModBuild(LocalWeapons(), _preview ?? Desired());
            if (HudKit.Button(new Rect(bar.xMax - 110f, bar.y, 110f, 42f), "CLOSE")) Close();
        }

        // ---------------------------------------------------------------- stats

        private void Stats(Rect r)
        {
            WeaponStats baseStats = _def.Firearm;
            if (baseStats == null) return;
            WeaponStats now = WeaponModder.Apply(baseStats, _seenMods, out float dmgNow);
            WeaponStats next = WeaponModder.Apply(baseStats, _preview ?? Desired(), out float dmgNext);
            HudKit.Section(new Rect(r.x, r.y, r.width, 22f), "STATS");
            float y = r.y + 30f, h = 25f;
            string pellets = baseStats.PelletsPerShot > 1 ? $" x{baseStats.PelletsPerShot}" : string.Empty;
            Row(ref y, h, r, "Damage", baseStats.Damage * dmgNow, baseStats.Damage * dmgNext, true, v => v.ToString("0") + pellets);
            Row(ref y, h, r, "Fire rate", now.RoundsPerMinute, next.RoundsPerMinute, true, v => v.ToString("0") + " rpm");
            Row(ref y, h, r, "Magazine", now.MagazineSize, next.MagazineSize, true, v => v.ToString("0"));
            Row(ref y, h, r, "Reload", now.TacticalReloadSeconds, next.TacticalReloadSeconds, false, v => v.ToString("0.00") + " s");
            Row(ref y, h, r, "Range", now.EffectiveRange, next.EffectiveRange, true, v => v.ToString("0") + " m");
            Row(ref y, h, r, "Hip spread", now.HipSpread, next.HipSpread, false, v => v.ToString("0.00") + "°");
            Row(ref y, h, r, "Aimed spread", now.AimSpread, next.AimSpread, false, v => v.ToString("0.00") + "°");
            Row(ref y, h, r, "Recoil", now.RecoilPitch / Mathf.Max(0.0001f, baseStats.RecoilPitch), next.RecoilPitch / Mathf.Max(0.0001f, baseStats.RecoilPitch), false, v => (v * 100f).ToString("0") + "%");
            Row(ref y, h, r, "Aim time", now.AimInSeconds, next.AimInSeconds, false, v => v.ToString("0.00") + " s");
            Row(ref y, h, r, "Move speed", now.MoveSpeedMultiplier, next.MoveSpeedMultiplier, true, v => (v * 100f).ToString("0") + "%");
            Row(ref y, h, r, "Equip time", now.EquipSeconds, next.EquipSeconds, false, v => v.ToString("0.00") + " s");
        }

        private static void Row(ref float y, float h, Rect r, string name, float now, float next, bool higherIsBetter, System.Func<float, string> fmt)
        {
            string a = fmt(now), b = fmt(next);
            bool same = a == b;
            bool better = higherIsBetter ? next > now : next < now;
            Color tc = same ? HudKit.Text : better ? HudKit.Good : HudKit.Bad;
            HudKit.Label(new Rect(r.x + 4f, y, 170f, h), name, 14, HudKit.TextDim);
            HudKit.Label(new Rect(r.x + 180f, y, 140f, h), a, 14, HudKit.Text, TextAnchor.MiddleRight, head: true);
            if (!same)
            {
                HudKit.Label(new Rect(r.x + 330f, y, 40f, h), ">", 14, HudKit.TextDim, TextAnchor.MiddleCenter);
                HudKit.Label(new Rect(r.x + 370f, y, 150f, h), b, 14, tc, TextAnchor.MiddleLeft, head: true);
            }
            HudKit.Fill(new Rect(r.x, y + h - 1f, r.width, 1f), new Color(1f, 1f, 1f, 0.05f));
            y += h;
        }

        // ---------------------------------------------------------------- text

        private static string SlotName(AttachmentSlot s) => s switch
        {
            AttachmentSlot.Optic => "OPTIC",
            AttachmentSlot.Barrel => "MUZZLE",
            AttachmentSlot.Underbarrel => "UNDERBARREL",
            AttachmentSlot.Magazine => "MAGAZINE",
            AttachmentSlot.Stock => "STOCK",
            _ => s.ToString().ToUpperInvariant(),
        };

        /// <summary>"Recoil -20%, Hip spread -15%" from an attachment's multipliers.</summary>
        private static string Effect(AttachmentDef a)
        {
            var parts = new List<string>();
            foreach (StatMod m in a.Mods)
            {
                float pct = (m.Multiplier - 1f) * 100f;
                string name = m.Stat switch
                {
                    WeaponStat.AimSpeed => "Aim speed",
                    WeaponStat.AimSpread => "Aimed spread",
                    WeaponStat.HipSpread => "Hip spread",
                    WeaponStat.ReloadSpeed => "Reload speed",
                    WeaponStat.EquipSpeed => "Equip speed",
                    WeaponStat.MoveSpeed => "Move speed",
                    WeaponStat.FireRate => "Fire rate",
                    WeaponStat.Magazine => "Magazine",
                    _ => m.Stat.ToString(),
                };
                parts.Add($"{name} {(pct >= 0 ? "+" : "")}{pct:0}%");
            }
            if (a.Id == "att.sniper_scope") parts.Add("scope view when aimed");
            return string.Join(", ", parts);
        }
    }
}
