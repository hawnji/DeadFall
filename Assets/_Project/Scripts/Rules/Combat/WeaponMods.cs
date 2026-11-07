using System;
using System.Collections.Generic;
using Deadfall.Rules.Common;

namespace Deadfall.Rules.Combat
{
    /// <summary>Weapon quality (GAME_DESIGN §6). Higher tiers have more attachment slots and a better damage roll.</summary>
    public enum WeaponTier : byte { Standard = 0, Refined = 1, Prototype = 2 }

    public enum AttachmentSlot : byte { Optic, Barrel, Underbarrel, Magazine, Stock, Special }

    /// <summary>Stats an attachment or tier can scale. Everything is a multiplier.</summary>
    public enum WeaponStat : byte { Damage, FireRate, Magazine, Reserve, ReloadSpeed, HipSpread, AimSpread, Recoil, Range, EquipSpeed, AimSpeed, MoveSpeed }

    public readonly struct StatMod
    {
        public readonly WeaponStat Stat;
        public readonly float Multiplier;
        public StatMod(WeaponStat stat, float multiplier) { Stat = stat; Multiplier = multiplier; }
    }

    /// <summary>One attachment: a slot and a set of multipliers (behaviour hooks come later).</summary>
    public sealed class AttachmentDef
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly AttachmentSlot Slot;
        public readonly StatMod[] Mods;
        public AttachmentDef(string id, string name, AttachmentSlot slot, params StatMod[] mods) { Id = id; DisplayName = name; Slot = slot; Mods = mods; }
    }

    /// <summary>What makes one copy of a weapon different from the base definition: tier, attachments, damage roll.</summary>
    public sealed class WeaponMods
    {
        public WeaponTier Tier;
        /// <summary>Rolled on drops: 0.95..1.05 on top of the tier bonus.</summary>
        public float DamageRoll = 1f;
        public readonly List<AttachmentDef> Attachments = new List<AttachmentDef>();

        public static WeaponMods Standard() => new WeaponMods();

        public bool Has(AttachmentSlot slot)
        {
            foreach (AttachmentDef a in Attachments) if (a.Slot == slot) return true;
            return false;
        }

        public string Describe()
        {
            string tier = Tier == WeaponTier.Standard ? string.Empty : Tier.ToString();
            if (Attachments.Count == 0) return tier;
            var names = new List<string>(Attachments.Count);
            foreach (AttachmentDef a in Attachments) names.Add(a.DisplayName);
            return (tier.Length > 0 ? tier + " · " : string.Empty) + string.Join(", ", names);
        }

        /// <summary>Compact wire form: tier, roll (per mille), attachment indices in <see cref="AttachmentCatalog.All"/>.</summary>
        public byte[] Encode()
        {
            var bytes = new byte[3 + Attachments.Count];
            bytes[0] = (byte)Tier;
            bytes[1] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round((DamageRoll - 0.9f) * 1000f)));
            bytes[2] = (byte)Attachments.Count;
            for (int i = 0; i < Attachments.Count; i++) bytes[3 + i] = (byte)AttachmentCatalog.IndexOf(Attachments[i]);
            return bytes;
        }

        public static WeaponMods Decode(byte[] bytes)
        {
            var m = new WeaponMods();
            if (bytes == null || bytes.Length < 3) return m;
            m.Tier = (WeaponTier)Math.Min(bytes[0], (byte)WeaponTier.Prototype);
            m.DamageRoll = 0.9f + bytes[1] / 1000f;
            int n = Math.Min(bytes[2], bytes.Length - 3);
            for (int i = 0; i < n; i++)
            {
                AttachmentDef a = AttachmentCatalog.At(bytes[3 + i]);
                // One per slot. Tiers limit what loot rolls, not what a copy can carry (the Mod Bench fits every slot).
                if (a != null && !m.Has(a.Slot)) m.Attachments.Add(a);
            }
            return m;
        }
    }

    /// <summary>Applies tiers and attachments to weapon stats.</summary>
    public static class WeaponModder
    {
        public static int SlotsFor(WeaponTier tier) => tier == WeaponTier.Prototype ? 3 : tier == WeaponTier.Refined ? 2 : 1;
        public static float TierDamage(WeaponTier tier) => tier == WeaponTier.Prototype ? 1.15f : tier == WeaponTier.Refined ? 1.07f : 1f;

        /// <summary>
        /// Effective stats for a copy of the weapon. Damage is left on the base value and returned as a multiplier
        /// instead, so it can travel as the (host-capped) outgoing multiplier in co-op hit claims (T-28).
        /// </summary>
        public static WeaponStats Apply(WeaponStats baseStats, WeaponMods mods, out float damageMultiplier)
        {
            WeaponStats s = baseStats.Clone();
            damageMultiplier = 1f;
            if (mods == null) return s;
            damageMultiplier = TierDamage(mods.Tier) * Clamp(mods.DamageRoll, 0.9f, 1.15f);
            float mag = 1f, reserve = 1f;
            foreach (AttachmentDef a in mods.Attachments)
            {
                foreach (StatMod m in a.Mods)
                {
                    switch (m.Stat)
                    {
                        case WeaponStat.Damage: damageMultiplier *= m.Multiplier; break;
                        case WeaponStat.FireRate: s.RoundsPerMinute *= m.Multiplier; break;
                        case WeaponStat.Magazine: mag *= m.Multiplier; break;
                        case WeaponStat.Reserve: reserve *= m.Multiplier; break;
                        case WeaponStat.ReloadSpeed:
                            s.TacticalReloadSeconds /= m.Multiplier;
                            s.EmptyReloadSeconds /= m.Multiplier;
                            break;
                        case WeaponStat.HipSpread: s.HipSpread *= m.Multiplier; break;
                        case WeaponStat.AimSpread: s.AimSpread *= m.Multiplier; break;
                        case WeaponStat.Recoil:
                            s.RecoilPitch *= m.Multiplier;
                            s.RecoilYaw *= m.Multiplier;
                            break;
                        case WeaponStat.Range:
                            s.EffectiveRange *= m.Multiplier;
                            s.MaxRange *= m.Multiplier;
                            break;
                        case WeaponStat.EquipSpeed: s.EquipSeconds /= m.Multiplier; break;
                        case WeaponStat.AimSpeed: s.AimInSeconds /= m.Multiplier; break;
                        case WeaponStat.MoveSpeed: s.MoveSpeedMultiplier *= m.Multiplier; break;
                    }
                }
            }
            if (mag != 1f) s.MagazineSize = Math.Max(1, (int)Math.Round(s.MagazineSize * mag));
            if (reserve != 1f) s.MaxReserve = Math.Max(0, (int)Math.Round(s.MaxReserve * reserve));
            return s;
        }

        private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    }

    /// <summary>The prototype attachment set (GAME_DESIGN §6). Indices are used on the wire: append only.</summary>
    public static class AttachmentCatalog
    {
        public static readonly AttachmentDef[] All =
        {
            new AttachmentDef("att.red_dot", "Red Dot", AttachmentSlot.Optic, new StatMod(WeaponStat.AimSpeed, 1.15f), new StatMod(WeaponStat.AimSpread, 0.9f)),
            new AttachmentDef("att.holo_scope", "Holo Scope", AttachmentSlot.Optic, new StatMod(WeaponStat.AimSpread, 0.7f), new StatMod(WeaponStat.Range, 1.15f), new StatMod(WeaponStat.AimSpeed, 0.9f)),
            new AttachmentDef("att.compensator", "Compensator", AttachmentSlot.Barrel, new StatMod(WeaponStat.Recoil, 0.8f)),
            new AttachmentDef("att.long_barrel", "Long Barrel", AttachmentSlot.Barrel, new StatMod(WeaponStat.Range, 1.3f), new StatMod(WeaponStat.EquipSpeed, 0.9f)),
            new AttachmentDef("att.foregrip", "Foregrip", AttachmentSlot.Underbarrel, new StatMod(WeaponStat.Recoil, 0.85f), new StatMod(WeaponStat.HipSpread, 0.85f)),
            new AttachmentDef("att.laser", "Laser Sight", AttachmentSlot.Underbarrel, new StatMod(WeaponStat.HipSpread, 0.7f)),
            new AttachmentDef("att.extended_mag", "Extended Mag", AttachmentSlot.Magazine, new StatMod(WeaponStat.Magazine, 1.4f), new StatMod(WeaponStat.ReloadSpeed, 0.9f)),
            new AttachmentDef("att.fast_mag", "Fast Mag", AttachmentSlot.Magazine, new StatMod(WeaponStat.ReloadSpeed, 1.3f)),
            new AttachmentDef("att.light_stock", "Light Stock", AttachmentSlot.Stock, new StatMod(WeaponStat.MoveSpeed, 1.05f), new StatMod(WeaponStat.AimSpeed, 1.1f), new StatMod(WeaponStat.Recoil, 1.1f)),
            new AttachmentDef("att.heavy_stock", "Heavy Stock", AttachmentSlot.Stock, new StatMod(WeaponStat.Recoil, 0.85f), new StatMod(WeaponStat.AimSpeed, 0.9f)),
            new AttachmentDef("att.hot_loads", "Hot Loads", AttachmentSlot.Special, new StatMod(WeaponStat.Damage, 1.1f), new StatMod(WeaponStat.Recoil, 1.1f)),
            new AttachmentDef("att.feeder", "Feeder", AttachmentSlot.Special, new StatMod(WeaponStat.FireRate, 1.1f), new StatMod(WeaponStat.Reserve, 1.2f)),
            // Appended (the wire form uses catalog indices).
            new AttachmentDef("att.sniper_scope", "Sniper Scope", AttachmentSlot.Optic, new StatMod(WeaponStat.AimSpread, 0.5f), new StatMod(WeaponStat.Range, 1.3f), new StatMod(WeaponStat.AimSpeed, 0.8f)),
        };

        public static int IndexOf(AttachmentDef a) => Array.IndexOf(All, a);
        public static AttachmentDef At(int index) => index >= 0 && index < All.Length ? All[index] : null;
        public static AttachmentDef ById(string id)
        {
            foreach (AttachmentDef a in All) if (a.Id == id) return a;
            return null;
        }
    }

    /// <summary>
    /// Run economy numbers (T-36; first balance pass T-77, see BALANCE.md): weapon drops, tier odds, attachment rolls and Supply Cache prices.
    /// </summary>
    /// <summary>Attachments the player fits at the Armory (one per slot, up to <see cref="MaxFitted"/>).</summary>
    public static class ArmoryAttachments
    {
        public const int MaxFitted = 3;

        /// <summary>Slots the Armory offers (Special is loot-only).</summary>
        public static readonly AttachmentSlot[] Slots = { AttachmentSlot.Optic, AttachmentSlot.Barrel, AttachmentSlot.Underbarrel, AttachmentSlot.Magazine, AttachmentSlot.Stock };

        /// <summary>The catalog's attachments for a slot, in catalog order.</summary>
        public static List<AttachmentDef> For(AttachmentSlot slot)
        {
            var list = new List<AttachmentDef>();
            foreach (AttachmentDef a in AttachmentCatalog.All) if (a.Slot == slot) list.Add(a);
            return list;
        }

        /// <summary>
        /// Mods for a set of saved attachment ids: unknown ids, repeats of a slot, and anything past
        /// <see cref="MaxFitted"/> are dropped. Null when nothing valid is left.
        /// </summary>
        public static WeaponMods Build(IEnumerable<string> ids, string weaponId = null, WeaponCategory category = WeaponCategory.AssaultRifle)
        {
            if (ids == null) return null;
            var m = new WeaponMods();
            foreach (string id in ids)
            {
                AttachmentDef a = AttachmentCatalog.ById(id);
                if (a == null || m.Has(a.Slot) || m.Attachments.Count >= MaxFitted) continue;
                if (weaponId != null && !AttachmentFit.Allows(weaponId, category, a)) continue;
                m.Attachments.Add(a);
            }
            return m.Attachments.Count > 0 ? m : null;
        }

        /// <summary>
        /// The ids after cycling <paramref name="slot"/> by <paramref name="dir"/> through none and each option.
        /// A pick that would go past <see cref="MaxFitted"/> is skipped back to none.
        /// </summary>
        public static string[] Cycle(string[] ids, AttachmentSlot slot, int dir, string weaponId = null, WeaponCategory category = WeaponCategory.AssaultRifle)
        {
            var list = new List<string>();
            string current = null;
            if (ids != null)
                foreach (string id in ids)
                {
                    AttachmentDef a = AttachmentCatalog.ById(id);
                    if (a == null) continue;
                    if (a.Slot == slot) current = id; else list.Add(id);
                }
            List<AttachmentDef> options = weaponId != null ? AttachmentFit.For(weaponId, category, slot) : For(slot);
            int at = -1; // -1 = none
            for (int i = 0; i < options.Count; i++) if (options[i].Id == current) at = i;
            int count = options.Count + 1;
            int next = ((at + 1 + dir) % count + count) % count - 1;
            if (next >= 0 && list.Count >= MaxFitted) next = -1;
            if (next >= 0) list.Add(options[next].Id);
            return list.ToArray();
        }
    }

    public static class LootRules
    {
        /// <summary>Chance an enemy of this threat cost drops a weapon.</summary>
        public static float DropChance(float threatCost) => Math.Min(0.2f, 0.012f * threatCost);

        public static WeaponTier RollTier(DeterministicRandom rng, int surge)
        {
            float prototype = Math.Min(0.2f, Math.Max(0f, (surge - 4) * 0.015f));
            float refined = Math.Min(0.5f, 0.12f + surge * 0.02f);
            float r = rng.NextFloat();
            if (r < prototype) return WeaponTier.Prototype;
            if (r < prototype + refined) return WeaponTier.Refined;
            return WeaponTier.Standard;
        }

        /// <summary>A tier's attachments: one per slot type, none for melee or launchers' magazine.</summary>
        public static WeaponMods RollMods(DeterministicRandom rng, WeaponTier tier, WeaponCategory category, string weaponId = null)
        {
            var m = new WeaponMods { Tier = tier, DamageRoll = 0.95f + rng.NextFloat() * 0.1f };
            if (category == WeaponCategory.Melee) return m;
            int want = tier == WeaponTier.Standard ? 0 : tier == WeaponTier.Refined ? 1 : 2;
            for (int tries = 0; m.Attachments.Count < want && tries < 20; tries++)
            {
                AttachmentDef a = AttachmentCatalog.All[rng.Range(0, AttachmentCatalog.All.Length)];
                if (m.Has(a.Slot) || !Fits(weaponId, category, a)) continue;
                m.Attachments.Add(a);
            }
            return m;
        }

        /// <summary>
        /// The Weapon Forge: the same weapon one tier up. Keeps its attachments, adds one for the new tier's slot,
        /// and never lowers the damage roll. Returns null at Prototype.
        /// </summary>
        public static WeaponMods Upgrade(DeterministicRandom rng, WeaponMods current, WeaponCategory category, string weaponId = null)
        {
            current = current ?? WeaponMods.Standard();
            if (current.Tier >= WeaponTier.Prototype) return null;
            var m = new WeaponMods { Tier = current.Tier + 1, DamageRoll = Math.Max(current.DamageRoll, 0.95f + rng.NextFloat() * 0.1f) };
            foreach (AttachmentDef a in current.Attachments)
                if (!m.Has(a.Slot)) m.Attachments.Add(a); // keeps everything, Mod Bench parts included
            if (category == WeaponCategory.Melee) return m;
            int want = Math.Min(WeaponModder.SlotsFor(m.Tier), m.Attachments.Count + 1);
            for (int tries = 0; m.Attachments.Count < want && tries < 30; tries++)
            {
                AttachmentDef a = AttachmentCatalog.All[rng.Range(0, AttachmentCatalog.All.Length)];
                if (m.Has(a.Slot) || !Fits(weaponId, category, a)) continue;
                m.Attachments.Add(a);
            }
            return m;
        }

        /// <summary>Loot only rolls attachments the weapon takes (no optics on unknown heavy weapons).</summary>
        private static bool Fits(string weaponId, WeaponCategory category, AttachmentDef a)
        {
            if (weaponId != null) return AttachmentFit.Allows(weaponId, category, a);
            return !(category == WeaponCategory.Heavy && a.Slot == AttachmentSlot.Optic);
        }

        /// <summary>Weapon Forge price to raise a weapon from <paramref name="current"/> (0 when it can't go higher).</summary>
        public static int ForgePrice(WeaponTier current) => current == WeaponTier.Standard ? 1000 : current == WeaponTier.Refined ? 2000 : 0;

        public static int BasePrice(WeaponCategory c)
        {
            switch (c)
            {
                case WeaponCategory.Pistol: return 250;
                case WeaponCategory.SubmachineGun: return 600;
                case WeaponCategory.AssaultRifle: return 800;
                case WeaponCategory.Shotgun: return 750;
                case WeaponCategory.LightMachineGun: return 1000;
                case WeaponCategory.Heavy: return 1300;
                case WeaponCategory.Melee: return 450;
                default: return 1500;
            }
        }

        public static int Price(WeaponCategory c, WeaponTier tier)
        {
            float mult = tier == WeaponTier.Prototype ? 2.4f : tier == WeaponTier.Refined ? 1.6f : 1f;
            return (int)(Math.Round(BasePrice(c) * mult / 50f) * 50f);
        }

        public const int AmmoRefillPrice = 200;
        public const int CacheStock = 3;
        /// <summary>Supply Cache: full heal and a full set of grenades (the cache sells supplies, not guns).</summary>
        public const int CacheHealPrice = 250;
        public const int CacheGrenadePrice = 300;
    }

    /// <summary>
    /// Which attachments each gun takes (reviewed with the attachment models, 2026-11-07). Melee takes none; the
    /// loot-only Special perks fit any firearm; a gun not listed here takes everything (new content stays usable).
    /// </summary>
    public static class AttachmentFit
    {
        private static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
        {
            ["weapon.halberd_ar"] = new[] { "red_dot", "holo_scope", "sniper_scope", "compensator", "long_barrel", "foregrip", "laser", "extended_mag", "fast_mag", "light_stock", "heavy_stock" },
            ["weapon.breacher_12"] = new[] { "red_dot", "holo_scope", "long_barrel", "laser", "fast_mag", "light_stock", "heavy_stock" },
            ["weapon.ranger_9"] = new[] { "red_dot", "compensator", "long_barrel", "laser", "extended_mag", "fast_mag" },
            ["weapon.wasp_smg"] = new[] { "red_dot", "holo_scope", "compensator", "long_barrel", "foregrip", "laser", "extended_mag", "fast_mag", "light_stock", "heavy_stock" },
            ["weapon.anvil_lmg"] = new[] { "red_dot", "holo_scope", "compensator", "long_barrel", "foregrip", "laser", "extended_mag", "fast_mag", "light_stock", "heavy_stock" },
            ["weapon.mule_launcher"] = new[] { "red_dot", "foregrip", "laser", "light_stock", "heavy_stock" },
            ["weapon.vesper_dmr"] = new[] { "red_dot", "holo_scope", "sniper_scope", "compensator", "long_barrel", "foregrip", "laser", "extended_mag", "fast_mag", "light_stock", "heavy_stock" },
            ["weapon.bulldog_44"] = new[] { "red_dot", "long_barrel", "laser", "fast_mag" },
            ["weapon.hailstorm"] = new[] { "red_dot", "holo_scope", "long_barrel", "foregrip", "laser", "extended_mag", "fast_mag", "light_stock", "heavy_stock" },
            ["weapon.kestrel_pdw"] = new[] { "red_dot", "holo_scope", "compensator", "long_barrel", "foregrip", "laser", "extended_mag" },
        };

        public static bool Allows(string weaponId, WeaponCategory category, AttachmentDef a)
        {
            if (a == null || category == WeaponCategory.Melee) return false;
            if (a.Slot == AttachmentSlot.Special) return true;
            if (weaponId == null || !Table.TryGetValue(weaponId, out string[] ids)) return true;
            string shortId = a.Id.StartsWith("att.", StringComparison.Ordinal) ? a.Id.Substring(4) : a.Id;
            return Array.IndexOf(ids, shortId) >= 0;
        }

        /// <summary>The catalog's attachments for a slot that this gun takes, in catalog order.</summary>
        public static List<AttachmentDef> For(string weaponId, WeaponCategory category, AttachmentSlot slot)
        {
            var list = new List<AttachmentDef>();
            foreach (AttachmentDef a in AttachmentCatalog.All) if (a.Slot == slot && Allows(weaponId, category, a)) list.Add(a);
            return list;
        }
    }

    /// <summary>
    /// The Mod Bench (in-run attachment shop): prices, and the copy you get after a build. A build keeps the gun's tier,
    /// damage roll and loot-only Special perk; you pay only for parts you don't already have, and taking parts off is free.
    /// </summary>
    public static class ModBenchRules
    {
        /// <summary>Slots the bench offers, in menu order.</summary>
        public static readonly AttachmentSlot[] Slots = ArmoryAttachments.Slots;

        public static int Price(AttachmentDef a)
        {
            switch (a != null ? a.Id : null)
            {
                case "att.red_dot": return 350;
                case "att.holo_scope": return 500;
                case "att.sniper_scope": return 900;
                case "att.compensator": return 450;
                case "att.long_barrel": return 500;
                case "att.foregrip": return 400;
                case "att.laser": return 300;
                case "att.extended_mag": return 600;
                case "att.fast_mag": return 450;
                case "att.light_stock": return 350;
                case "att.heavy_stock": return 400;
                default: return 0; // Special perks are loot-only
            }
        }

        /// <summary>Credits to go from <paramref name="current"/> to <paramref name="next"/>: the parts that are new.</summary>
        public static int Cost(WeaponMods current, WeaponMods next)
        {
            if (next == null) return 0;
            int total = 0;
            foreach (AttachmentDef a in next.Attachments)
                if (current == null || !current.Attachments.Contains(a)) total += Price(a);
            return total;
        }

        /// <summary>
        /// The copy after a build: <paramref name="current"/>'s tier, roll and Special perk plus the picked parts (one per
        /// slot, only ones the gun takes, never a Special). Unknown or unfit picks are dropped.
        /// </summary>
        public static WeaponMods Build(WeaponMods current, IEnumerable<AttachmentDef> picks, string weaponId, WeaponCategory category)
        {
            current = current ?? WeaponMods.Standard();
            var m = new WeaponMods { Tier = current.Tier, DamageRoll = current.DamageRoll };
            if (picks != null)
                foreach (AttachmentDef a in picks)
                {
                    if (a == null || a.Slot == AttachmentSlot.Special || m.Has(a.Slot) || !AttachmentFit.Allows(weaponId, category, a)) continue;
                    m.Attachments.Add(a);
                }
            foreach (AttachmentDef a in current.Attachments)
                if (a.Slot == AttachmentSlot.Special && !m.Has(a.Slot)) m.Attachments.Add(a);
            return m;
        }

        /// <summary>Same parts (order ignored), same tier and roll.</summary>
        public static bool Same(WeaponMods a, WeaponMods b)
        {
            a = a ?? WeaponMods.Standard();
            b = b ?? WeaponMods.Standard();
            if (a.Attachments.Count != b.Attachments.Count) return false;
            foreach (AttachmentDef x in a.Attachments) if (!b.Attachments.Contains(x)) return false;
            return true;
        }
    }
}
