using Deadfall.Rules.Common;
using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Rules.Combat;
using Deadfall.Rules.Engineering;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// The Engineer's Drone: hovers beside its owner. Attack mode shoots the nearest infected; Repair mode mends the
    /// most damaged structure nearby (a beam shows which). Not a target for enemies. Every machine flies its own copy
    /// beside the owner, so only mode, tier and health travel over the network.
    /// </summary>
    public sealed class DroneStructure : Structure
    {
        public const string WeaponId = "structure.drone";
        private const float ShotsPerSecond = 4f;
        private Transform _body;
        private readonly List<Transform> _rotors = new List<Transform>();
        private LineRenderer _beam;
        private IHostile _target;
        private Structure _repairTarget;
        private double _nextShot, _nextRetarget;
        private float _bob;
        private Material _beamMat;

        protected override void Build()
        {
            _body = new GameObject("Body").transform;
            _body.SetParent(transform, false);
            Part(PrimitiveType.Sphere, _body, Vector3.zero, new Vector3(0.45f, 0.22f, 0.45f), new Color(0.35f, 0.75f, 0.5f), false, tintWithHealth: true);
            Part(PrimitiveType.Cube, _body, new Vector3(0f, 0.05f, 0f), new Vector3(0.9f, 0.03f, 0.08f), new Color(0.2f, 0.2f, 0.2f), false);
            Part(PrimitiveType.Cube, _body, new Vector3(0f, 0.05f, 0f), new Vector3(0.08f, 0.03f, 0.9f), new Color(0.2f, 0.2f, 0.2f), false);
            Transform model = Dress("eng_drone", _body, Vector3.zero);
            if (model != null)
            {
                foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("Rotor")) _rotors.Add(t);
            }
            HidePlaceholders();
            var beamGo = new GameObject("Beam");
            beamGo.transform.SetParent(transform, false);
            _beam = beamGo.AddComponent<LineRenderer>();
            _beam.positionCount = 2;
            _beam.startWidth = _beam.endWidth = 0.05f;
            _beamMat = RuntimeMaterials.CreateLit(new Color(0.3f, 1f, 0.5f));
            _beam.sharedMaterial = _beamMat;
            _beam.enabled = false;
            _bob = Random.value * 6f;
        }

        protected override void TickVisual(float dt)
        {
            GameObject owner = OwnerObject;
            bool idle = OwnerDown();
            if (owner != null)
            {
                Transform o = owner.transform;
                // With its owner down it powers down: sinks to hover low beside them and holds fire until they're back up.
                float height = idle ? 1.1f : 2.1f;
                Vector3 want = o.position + o.right * 0.9f + Vector3.up * (height + Mathf.Sin(Time.time * 2f + _bob) * 0.08f) - o.forward * 0.3f;
                transform.position = Vector3.Lerp(transform.position, want, 1f - Mathf.Exp(-6f * dt));
            }
            if (_rotors.Count > 0)
            {
                // The model's rotors spin; the body just faces where the owner looks, tilting a little as it flies.
                for (int i = 0; i < _rotors.Count; i++) _rotors[i].Rotate(0f, (i % 2 == 0 ? 1440f : -1440f) * dt, 0f, Space.Self);
                Vector3 face = owner != null ? owner.transform.forward : _body.forward;
                face.y = 0f;
                if (face.sqrMagnitude > 0.01f)
                    _body.rotation = Quaternion.Slerp(_body.rotation, Quaternion.LookRotation(face) * Quaternion.Euler(6f, 0f, 0f), 1f - Mathf.Exp(-4f * dt));
            }
            else _body.Rotate(0f, 720f * dt, 0f, Space.Self);

            double now = Time.timeAsDouble;
            if (idle)
            {
                _target = null;
                _repairTarget = null;
                _beam.enabled = false;
                return;
            }
            if (now >= _nextRetarget)
            {
                _nextRetarget = now + 0.25;
                _target = Mode == DroneMode.Attack ? EngineeringTargeting.Nearest(transform.position, Stats.Range, transform) : null;
                _repairTarget = Mode == DroneMode.Repair ? MostDamaged() : null;
            }

            if (Mode == DroneMode.Repair)
            {
                bool beam = _repairTarget != null && _repairTarget.IsAlive && _repairTarget.Health01 < 1f;
                _beam.enabled = beam;
                if (!beam) return;
                _beam.SetPosition(0, transform.position);
                _beam.SetPosition(1, _repairTarget.transform.position + Vector3.up * 0.8f);
                if (!Mirror)
                {
                    _repairTarget.Heal(Stats.Rate * dt);
                    EngineeringDirector.Current?.MarkDirty();
                }
                return;
            }
            _beam.enabled = false;
            if (_target == null || !_target.IsAlive || now < _nextShot) return;
            _nextShot = now + 1f / ShotsPerSecond;
            Vector3 end = _target.AimPoint;
            TracerPool.Instance.Spawn(transform.position, end);
            GameAudio.Play(SfxId.ShotTurret, transform.position, 0.7f, 1.2f);
            if (Mirror) return;
            GameObject attacker = owner != null ? owner : gameObject;
            LocalHitResolver.Apply(new HitClaim
            {
                Attacker = attacker,
                Target = _target.Damageable,
                Zone = HitZone.Body,
                WeakPointMultiplier = 1f,
                Origin = transform.position,
                Point = end,
                WeaponId = WeaponId,
                Request = new DamageRequest(Stats.Damage / ShotsPerSecond, HitZone.Body, 0f, 1f, 1f, 1f, Stats.Range, Stats.Range + 2f, 1f),
            });
        }

        /// <summary>The owner is downed, eliminated or dropped (every peer sees the squad's life states).</summary>
        private bool OwnerDown()
        {
            IReadOnlyList<ISquadMember> squad = SquadMembers.All;
            for (int i = 0; i < squad.Count; i++)
            {
                ISquadMember m = squad[i];
                if (m == null || m.ClientId != OwnerClientId) continue;
                return m.Disconnected || m.Life != LifeState.Alive;
            }
            return false;
        }

        private Structure MostDamaged()
        {
            EngineeringDirector d = EngineeringDirector.Current;
            if (d == null) return null;
            Structure best = null;
            float lowest = 0.999f;
            float rangeSq = Stats.Range * Stats.Range;
            IReadOnlyCollection<Structure> all = d.Structures;
            foreach (Structure s in all)
            {
                if (s == null || s == this || !s.IsAlive || s.Health01 >= lowest) continue;
                if ((s.transform.position - transform.position).sqrMagnitude > rangeSq) continue;
                lowest = s.Health01;
                best = s;
            }
            return best;
        }
    }
}
