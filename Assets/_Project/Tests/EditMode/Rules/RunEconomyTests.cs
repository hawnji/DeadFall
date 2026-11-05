using System.Collections.Generic;
using Deadfall.Rules.Combat;
using Deadfall.Rules.Common;
using Deadfall.Rules.Progression;
using NUnit.Framework;

namespace Deadfall.Tests.EditMode.Rules
{
    public class WeaponModTests
    {
        private static WeaponStats Rifle() => new WeaponStats { MagazineSize = 30, MaxReserve = 180, RoundsPerMinute = 600f, TacticalReloadSeconds = 2f, EmptyReloadSeconds = 2.5f, RecoilPitch = 1f };

        [Test]
        public void StandardWithoutAttachmentsChangesNothing()
        {
            WeaponStats b = Rifle();
            WeaponStats s = WeaponModder.Apply(b, WeaponMods.Standard(), out float dmg);
            Assert.AreEqual(1f, dmg, 1e-4f);
            Assert.AreEqual(30, s.MagazineSize);
            Assert.AreNotSame(b, s);
        }

        [Test]
        public void TiersAndAttachmentsStack()
        {
            var mods = new WeaponMods { Tier = WeaponTier.Refined };
            mods.Attachments.Add(AttachmentCatalog.ById("att.extended_mag"));
            mods.Attachments.Add(AttachmentCatalog.ById("att.hot_loads"));
            WeaponStats b = Rifle();
            WeaponStats s = WeaponModder.Apply(b, mods, out float dmg);
            Assert.AreEqual(42, s.MagazineSize);
            Assert.AreEqual(1.07f * 1.1f, dmg, 1e-3f);
            Assert.Greater(s.TacticalReloadSeconds, b.TacticalReloadSeconds);
            Assert.Greater(s.RecoilPitch, b.RecoilPitch);
            Assert.AreEqual(30, b.MagazineSize, "base stats untouched");
        }

        [Test]
        public void ModsSurviveTheWire()
        {
            var rng = new DeterministicRandom(5);
            for (int i = 0; i < 200; i++)
            {
                WeaponMods m = LootRules.RollMods(rng, (WeaponTier)(i % 3), WeaponCategory.AssaultRifle);
                WeaponMods back = WeaponMods.Decode(m.Encode());
                Assert.AreEqual(m.Tier, back.Tier);
                Assert.AreEqual(m.DamageRoll, back.DamageRoll, 0.002f);
                Assert.AreEqual(m.Attachments.Count, back.Attachments.Count);
                for (int k = 0; k < m.Attachments.Count; k++) Assert.AreSame(m.Attachments[k], back.Attachments[k]);
            }
        }

        [Test]
        public void RolledModsRespectSlotsAndCategory()
        {
            var rng = new DeterministicRandom(9);
            for (int i = 0; i < 300; i++)
            {
                WeaponTier t = (WeaponTier)(i % 3);
                WeaponMods m = LootRules.RollMods(rng, t, WeaponCategory.Heavy);
                Assert.LessOrEqual(m.Attachments.Count, WeaponModder.SlotsFor(t));
                var slots = new HashSet<AttachmentSlot>();
                foreach (AttachmentDef a in m.Attachments) { Assert.IsTrue(slots.Add(a.Slot)); Assert.AreNotEqual(AttachmentSlot.Optic, a.Slot); }
            }
            Assert.IsEmpty(LootRules.RollMods(rng, WeaponTier.Prototype, WeaponCategory.Melee).Attachments);
        }

        [Test]
        public void ForgeRaisesTierAndKeepsAttachments()
        {
            var rng = new DeterministicRandom(21);
            for (int i = 0; i < 200; i++)
            {
                WeaponCategory cat = i % 4 == 0 ? WeaponCategory.Heavy : WeaponCategory.AssaultRifle;
                WeaponMods start = LootRules.RollMods(rng, (WeaponTier)(i % 2), cat);
                WeaponMods up = LootRules.Upgrade(rng, start, cat);
                Assert.AreEqual(start.Tier + 1, up.Tier);
                Assert.GreaterOrEqual(up.DamageRoll, start.DamageRoll);
                Assert.LessOrEqual(up.Attachments.Count, WeaponModder.SlotsFor(up.Tier));
                Assert.Greater(up.Attachments.Count, start.Attachments.Count);
                foreach (AttachmentDef a in start.Attachments) Assert.Contains(a, up.Attachments);
                if (cat == WeaponCategory.Heavy) foreach (AttachmentDef a in up.Attachments) Assert.AreNotEqual(AttachmentSlot.Optic, a.Slot);
            }
            Assert.IsNull(LootRules.Upgrade(rng, new WeaponMods { Tier = WeaponTier.Prototype }, WeaponCategory.Pistol));
            Assert.IsEmpty(LootRules.Upgrade(rng, null, WeaponCategory.Melee).Attachments);
            Assert.Less(LootRules.ForgePrice(WeaponTier.Standard), LootRules.ForgePrice(WeaponTier.Refined));
            Assert.AreEqual(0, LootRules.ForgePrice(WeaponTier.Prototype));
        }

        [Test]
        public void BetterTiersGetLikelierAndCostMore()
        {
            int Count(int surge)
            {
                var rng = new DeterministicRandom(1);
                int n = 0;
                for (int i = 0; i < 2000; i++) if (LootRules.RollTier(rng, surge) != WeaponTier.Standard) n++;
                return n;
            }
            Assert.Greater(Count(15), Count(1));
            Assert.Less(LootRules.Price(WeaponCategory.Pistol, WeaponTier.Standard), LootRules.Price(WeaponCategory.Pistol, WeaponTier.Refined));
            Assert.Less(LootRules.Price(WeaponCategory.AssaultRifle, WeaponTier.Refined), LootRules.Price(WeaponCategory.AssaultRifle, WeaponTier.Prototype));
            Assert.LessOrEqual(LootRules.DropChance(100f), 0.2f);
        }
    }

    public class FieldModTests
    {
        private static readonly List<FieldModDef> Catalog = FieldModCatalog.Prototype();
        private static FieldModDef Get(string id) => Catalog.Find(d => d.Id == id);

        [Test]
        public void StacksAddUpAndCap()
        {
            var inv = new FieldModInventory(Catalog);
            FieldModDef hp = Get("mod.hollow_points");
            for (int i = 0; i < 5; i++) Assert.IsNotNull(inv.Add(hp));
            Assert.IsNull(inv.Add(hp));
            Assert.AreEqual(0.4f, inv.Total(ModStat.Damage), 1e-4f);
        }

        [Test]
        public void UniqueModsOnlyOnce()
        {
            var inv = new FieldModInventory(Catalog);
            FieldModDef d = Get("mod.last_stand");
            Assert.IsTrue(inv.CanTake(d));
            inv.Add(d);
            Assert.IsFalse(inv.CanTake(d));
        }

        [Test]
        public void EvolvingModTurnsIntoItsEvolution()
        {
            var inv = new FieldModInventory(Catalog);
            FieldModDef v = Get("mod.vampiric_rounds");
            Assert.AreEqual("mod.vampiric_rounds", inv.Add(v).Id);
            inv.Add(v);
            Assert.AreEqual(2f, inv.Total(ModStat.HealOnKill), 1e-4f);
            Assert.AreEqual("mod.blood_harvest", inv.Add(v).Id);
            Assert.AreEqual(0, inv.Stacks("mod.vampiric_rounds"));
            Assert.AreEqual(4f, inv.Total(ModStat.HealOnKill), 1e-4f);
            Assert.AreEqual(0.1f, inv.Total(ModStat.Damage), 1e-4f);
            Assert.IsFalse(inv.CanTake(v));
            Assert.IsFalse(inv.CanTake(Get("mod.blood_harvest")), "evolutions are never offered");
        }

        [Test]
        public void OffersAreDistinctTakeableAndFromTheRightClass()
        {
            var rng = new DeterministicRandom(3);
            var inv = new FieldModInventory(Catalog);
            for (int i = 0; i < 500; i++)
            {
                List<FieldModDef> offer = FieldModOffers.Roll(rng, Catalog, inv, "class.gunner", i % 25);
                int takeable = Catalog.FindAll(d => inv.CanTake(d) && (d.ClassId == null || d.ClassId == "class.gunner")).Count;
                Assert.AreEqual(System.Math.Min(3, takeable), offer.Count);
                Assert.AreEqual(offer.Count, new HashSet<FieldModDef>(offer).Count);
                foreach (FieldModDef d in offer)
                {
                    Assert.IsTrue(inv.CanTake(d));
                    Assert.IsTrue(d.ClassId == null || d.ClassId == "class.gunner", d.Id);
                }
                if (i % 7 == 0 && offer.Count > 0) inv.Add(offer[0]);
            }
        }

        [Test]
        public void OffersEverySecondThirdSurgeAndAfterSpecimens()
        {
            Assert.IsFalse(FieldModOffers.OfferAfterSurge(2, false));
            Assert.IsTrue(FieldModOffers.OfferAfterSurge(3, false));
            Assert.IsTrue(FieldModOffers.OfferAfterSurge(4, true));
            Assert.IsFalse(FieldModOffers.OfferAfterSurge(0, false));
        }

        [Test]
        public void WeaponXpCurveIsMonotonic()
        {
            Assert.AreEqual(1, WeaponXp.LevelForXp(0));
            int last = 1;
            for (long xp = 0; xp < 40000; xp += 37)
            {
                int l = WeaponXp.LevelForXp(xp);
                Assert.GreaterOrEqual(l, last);
                last = l;
            }
            Assert.AreEqual(WeaponXp.MaxLevel, WeaponXp.LevelForXp(long.MaxValue / 2));
            Assert.AreEqual(2, WeaponXp.LevelForXp(WeaponXp.XpForLevel(2)));
        }
    }
}
