using Deadfall.Rules.Combat;
using NUnit.Framework;

namespace Deadfall.Tests.EditMode.Rules
{
    public class DodgeRulesTests
    {
        [Test]
        public void ChanceStartsAtTenPercentAndRampsToTwentyFive()
        {
            Assert.AreEqual(0.10f, DodgeRules.Chance(0), 1e-5f);
            Assert.AreEqual(0.10f, DodgeRules.Chance(5), 1e-5f);
            Assert.AreEqual(0.15f, DodgeRules.Chance(10), 1e-5f);
            Assert.AreEqual(0.25f, DodgeRules.Chance(20), 1e-5f);
            Assert.AreEqual(0.25f, DodgeRules.Chance(60), 1e-5f);
        }

        [Test]
        public void OnlyStandardHusksDodge()
        {
            Assert.IsTrue(DodgeRules.CanDodge("enemy.husk"));
            foreach (string id in new[] { "enemy.bolter", "enemy.bulwark", "enemy.creeper", "enemy.pyre", "enemy.retcher", "enemy.alpha", "enemy.goliath", "enemy.stalker", "enemy.specimen_zero", "enemy.broodmother" })
                Assert.IsFalse(DodgeRules.CanDodge(id), id);
        }

        [Test]
        public void OnlyGunfireTriggersADodge()
        {
            Assert.IsTrue(DodgeRules.IsGunfire("weapon.halberd_ar", false));
            Assert.IsFalse(DodgeRules.IsGunfire("weapon.saber_7", true));
            Assert.IsFalse(DodgeRules.IsGunfire("structure.sentry", false));
            Assert.IsFalse(DodgeRules.IsGunfire("ability.deploy_sentry", false));
            Assert.IsFalse(DodgeRules.IsGunfire(null, false));
        }
    }
}
