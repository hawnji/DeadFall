using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Rules.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Deadfall.Tests.PlayMode
{
    public class CombatIntegrationTests
    {
        [TearDown]
        public void TearDown() => PlayModeTestUtil.Cleanup();

        [Test]
        public void HitscanHeadshotDealsHeadMultiplier()
        {
            TestTarget target = PlayModeTestUtil.Target(new Vector3(0f, 0f, 10f));
            Physics.SyncTransforms();
            var stats = new WeaponStats { Damage = 20f, HeadMultiplier = 2f, EffectiveRange = 50f, MaxRange = 100f };

            Vector3 origin = new Vector3(0f, 2.3f, 0f);
            Assert.IsTrue(CombatQueries.Raycast(origin, Vector3.forward, 100f, null, out RaycastHit hit));
            Assert.IsTrue(Hurtbox.TryResolve(hit.collider, out IDamageable dmg, out HitZone zone, out float weak));
            Assert.AreEqual(HitZone.Head, zone);

            new LocalHitResolver().Submit(new HitClaim
            {
                Target = dmg, Zone = zone, WeakPointMultiplier = weak, Origin = origin, Point = hit.point,
                Request = DamageRequest.FromWeapon(stats, zone, hit.distance, weak),
            });
            Assert.AreEqual(40f, target.LastDamage, 0.01f);
            Assert.IsTrue(target.LastZone.Critical);
        }

        [Test]
        public void RaycastIgnoresShootersOwnColliders()
        {
            var shooter = PlayModeTestUtil.Track(GameObject.CreatePrimitive(PrimitiveType.Capsule));
            shooter.transform.position = new Vector3(0f, 1f, 0.6f);
            PlayModeTestUtil.Target(new Vector3(0f, 0f, 10f));
            Physics.SyncTransforms();
            Assert.IsTrue(CombatQueries.Raycast(new Vector3(0f, 1f, -1f), Vector3.forward, 100f, shooter.transform, out RaycastHit hit));
            Assert.Greater(hit.distance, 5f, "Should pass through the shooter and hit the target");
        }

        [Test]
        public void MeleeHitsTargetsInFrontButNotBehind()
        {
            TestTarget front = PlayModeTestUtil.Target(new Vector3(0f, 0f, 1.5f));
            TestTarget behind = PlayModeTestUtil.Target(new Vector3(0f, 0f, -1.5f));
            Physics.SyncTransforms();
            var stats = new MeleeStats { Range = 2.5f, ArcDegrees = 90f, MaxTargets = 3 };
            var results = new List<MeleeTarget>();
            CombatQueries.MeleeArc(new Vector3(0f, 1.2f, 0f), Vector3.forward, stats, null, results);
            Assert.AreEqual(1, results.Count);
            Assert.AreSame(front, results[0].Target);
            Assert.AreNotSame(behind, results[0].Target);
        }

        [Test]
        public void MeleeHitsEachTargetOnce()
        {
            PlayModeTestUtil.Target(new Vector3(0f, 0f, 1.2f)); // head and body both overlap
            Physics.SyncTransforms();
            var results = new List<MeleeTarget>();
            CombatQueries.MeleeArc(new Vector3(0f, 1.5f, 0f), Vector3.forward, new MeleeStats { Range = 3f, ArcDegrees = 120f }, null, results);
            Assert.AreEqual(1, results.Count);
        }

        [Test]
        public void DeadTargetsTakeNoDamage()
        {
            TestTarget target = PlayModeTestUtil.Target(new Vector3(0f, 0f, 5f));
            target.Health = 0f;
            new LocalHitResolver().Submit(new HitClaim
            {
                Target = target, Zone = HitZone.Body, Origin = Vector3.zero, Point = new Vector3(0f, 1f, 5f),
                Request = DamageRequest.FromWeapon(new WeaponStats(), HitZone.Body, 5f),
            });
            Assert.AreEqual(0f, target.LastDamage);
        }
    }
}
