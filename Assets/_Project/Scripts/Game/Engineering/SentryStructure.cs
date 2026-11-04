using Deadfall.Rules.Common;
using Deadfall.Core;
using Deadfall.Combat;
using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// Engineering Sentry: tracks the nearest hostile in range with a clear line of fire and shoots it until its ammo
    /// runs out (refill with Scrap). Enemies treat it as a target. Kills credit its owner.
    /// </summary>
    public sealed class SentryStructure : Structure, ICombatTarget
    {
        public const string WeaponId = "structure.sentry";
        private const float TurnSpeed = 480f;
        private const float RetargetInterval = 0.25f;
        private Transform _head;
        private IHostile _target;
        private double _nextShot, _nextRetarget;

        public Transform TargetTransform => transform;
        public bool IsTargetable => !Mirror && IsAlive && isActiveAndEnabled;
        public IDamageable Damageable => this;

        protected override void Build()
        {
            Part(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.35f, 0f), new Vector3(0.75f, 0.35f, 0.75f), new Color(0.2f, 0.22f, 0.22f), true);
            _head = new GameObject("Head").transform;
            _head.SetParent(transform, false);
            _head.localPosition = new Vector3(0f, 1.05f, 0f);
            Part(PrimitiveType.Cube, _head, Vector3.zero, new Vector3(0.55f, 0.35f, 0.6f), new Color(0.35f, 0.75f, 0.5f), true, tintWithHealth: true);
            Part(PrimitiveType.Cube, _head, new Vector3(0.12f, 0f, 0.5f), new Vector3(0.08f, 0.08f, 0.55f), new Color(0.15f, 0.15f, 0.15f), false);
            Part(PrimitiveType.Cube, _head, new Vector3(-0.12f, 0f, 0.5f), new Vector3(0.08f, 0.08f, 0.55f), new Color(0.15f, 0.15f, 0.15f), false);
            if (!Mirror) CombatTargets.Register(this);
        }

        protected override void TickVisual(float dt)
        {
            double now = Time.timeAsDouble;
            if (Supply <= 0f || !Powered) return;
            if (now >= _nextRetarget || _target == null || !_target.IsAlive)
            {
                _nextRetarget = now + RetargetInterval;
                _target = FindTarget();
            }
            if (_target == null) return;
            Vector3 to = _target.AimPoint - _head.position;
            if (to.sqrMagnitude > 0.01f)
                _head.rotation = Quaternion.RotateTowards(_head.rotation, Quaternion.LookRotation(to), TurnSpeed * dt);
            if (now < _nextShot || Vector3.Angle(_head.forward, to) > 8f) return;
            _nextShot = now + 1f / Mathf.Max(0.5f, Stats.Rate);
            Vector3 end = _target.AimPoint;
            TracerPool.Instance.Spawn(_head.position + _head.forward * 0.8f, end);
            GameAudio.Play(SfxId.ShotTurret, _head.position);
            if (Mirror) return;
            Supply = Mathf.Max(0f, Supply - 1f);
            GameObject owner = OwnerObject;
            LocalHitResolver.Apply(new HitClaim
            {
                Attacker = owner != null ? owner : gameObject,
                Target = _target.Damageable,
                Zone = HitZone.Body,
                WeakPointMultiplier = 1f,
                Origin = _head.position,
                Point = end,
                WeaponId = WeaponId,
                Request = new DamageRequest(Stats.Damage, HitZone.Body, 0f, 1f, 1f, 1f, Stats.Range, Stats.Range + 2f, 1f),
            });
            EngineeringDirector.Current?.MarkDirty();
        }

        private IHostile FindTarget() => EngineeringTargeting.Nearest(_head.position, Stats.Range, transform);

        protected override void OnDestroy() => CombatTargets.Unregister(this);
        private void OnDisable() => CombatTargets.Unregister(this);
    }
}
