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
                if (a != null && !m.Has(a.Slot) && m.Attachments.Count < WeaponModder.SlotsFor(m.Tier)) m.Attachments.Add(a);
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
        public static WeaponMods RollMods(DeterministicRandom rng, WeaponTier tier, WeaponCategory category)
        {
            var m = new WeaponMods { Tier = tier, DamageRoll = 0.95f + rng.NextFloat() * 0.1f };
            if (category == WeaponCategory.Melee) return m;
            int want = tier == WeaponTier.Standard ? 0 : tier == WeaponTier.Refined ? 1 : 2;
            for (int tries = 0; m.Attachments.Count < want && tries < 20; tries++)
            {
                AttachmentDef a = AttachmentCatalog.All[rng.Range(0, AttachmentCatalog.All.Length)];
                if (m.Has(a.Slot)) continue;
                if (category == WeaponCategory.Heavy && a.Slot == AttachmentSlot.Optic) continue;
                m.Attachments.Add(a);
            }
            return m;
        }

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
    }
}
