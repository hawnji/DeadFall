using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Enemies
{
    /// <summary>
    /// Procedural limb animation for the Blender enemy models (M13). Each model exports its legs, arms and head as
    /// child objects whose origins sit at the hip, shoulder and neck (build_models.py <c>m.rig</c>), and this swings
    /// them from the enemy's state and how fast it is actually moving: a walk or run cycle, a forward lean with arms
    /// thrown back while charging, arms raised in the attack wind-up and slammed down on the strike, head thrown back
    /// before a spit, flailing in a stagger, limp on death. It reads only state and position, so client mirrors
    /// animate exactly like the host's enemies with no extra network data.
    /// </summary>
    public sealed class EnemyRig : IEnemyRig
    {
        private enum Kind { Biped, Quadruped, Spider }

        private struct Part
        {
            public Transform T;
            public Quaternion Rest;
            public bool Valid => T != null;
        }

        private const float FarSqr = 55f * 55f;

        private readonly Kind _kind;
        private readonly Transform _model;
        private readonly Vector3 _modelRest;
        private readonly Quaternion _toParent;   // visual space -> the rig parts' parent space
        private readonly Quaternion _fromParent;
        private readonly float _height;
        private readonly float _seed;
        private Part _legL, _legR, _armL, _armR, _head;
        private readonly Part[] _spiderLegs = new Part[6]; // 0L, 0R, 1L, 1R, 2L, 2R

        private float _phase;
        private float _speed;
        private Vector3 _lastPos;
        private bool _hasLast;
        private float _deadBlend;

        private EnemyRig(Transform model, Transform visual, float height, Kind kind)
        {
            _model = model;
            _modelRest = model.localPosition;
            _height = Mathf.Max(0.5f, height);
            _kind = kind;
            _seed = Random.value * 10f;
            Transform parent = model; // rig parts are direct children of the model root
            _fromParent = Quaternion.Inverse(visual.rotation) * parent.rotation;
            _toParent = Quaternion.Inverse(_fromParent);
        }

        /// <summary>Returns a rig when the model has rig parts, else null (older single-mesh models stay static).</summary>
        public static EnemyRig Bind(Transform model, Transform visual, float height, bool crawler)
        {
            if (model == null || visual == null) return null;
            bool spider = Find(model, "Leg0L") != null;
            if (!spider && Find(model, "LegL") == null && Find(model, "ArmL") == null) return null;
            var rig = new EnemyRig(model, visual, height, spider ? Kind.Spider : crawler ? Kind.Quadruped : Kind.Biped);
            rig._head = rig.Grab("Head");
            if (spider)
            {
                for (int l = 0; l < 3; l++)
                {
                    rig._spiderLegs[l * 2] = rig.Grab($"Leg{l}L");
                    rig._spiderLegs[l * 2 + 1] = rig.Grab($"Leg{l}R");
                }
            }
            else
            {
                rig._legL = rig.Grab("LegL");
                rig._legR = rig.Grab("LegR");
                rig._armL = rig.Grab("ArmL");
                rig._armR = rig.Grab("ArmR");
            }
            return rig;
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t != root && (t.name == name || t.name.EndsWith("_" + name))) return t;
            return null;
        }

        private Part Grab(string name)
        {
            Transform t = Find(_model, name);
            return t == null ? default : new Part { T = t, Rest = t.localRotation };
        }

        public bool HandlesPose => false;

        /// <summary>Back to the rest pose (pooled enemies being reused).</summary>
        public void Reset()
        {
            _phase = 0f;
            _speed = 0f;
            _hasLast = false;
            _deadBlend = 0f;
            _model.localPosition = _modelRest;
            foreach (Part p in new[] { _legL, _legR, _armL, _armR, _head }) if (p.Valid) p.T.localRotation = p.Rest;
            foreach (Part p in _spiderLegs) if (p.Valid) p.T.localRotation = p.Rest;
        }

        /// <param name="position">The enemy's world position (speed is measured from it, so mirrors work too).</param>
        /// <param name="progress">0..1 through the current brain state.</param>
        public void Tick(Vector3 position, EnemyState state, float progress, bool alive, float now, float dt, Vector3 viewer)
        {
            if (dt <= 0f) return;
            if (_hasLast)
            {
                Vector3 d = position - _lastPos;
                d.y = 0f;
                float raw = d.magnitude / dt;
                if (raw > 30f) raw = 0f; // teleport or snap
                _speed = Mathf.Lerp(_speed, raw, 1f - Mathf.Exp(-10f * dt));
            }
            _lastPos = position;
            _hasLast = true;
            if ((position - viewer).sqrMagnitude > FarSqr && alive) return; // too far to notice; save the work

            float p = Mathf.Clamp01(progress);
            bool charging = state == EnemyState.Charge;
            float stride = Mathf.Max(0.5f, _height * (_kind == Kind.Biped ? 0.55f : 0.4f));
            _phase += dt * _speed / stride * Mathf.PI;
            if (_phase > 1000f) _phase -= 2f * Mathf.PI * 150f;
            float move = Mathf.Clamp01(_speed / Mathf.Max(0.8f, _height * 0.9f));

            _deadBlend = alive ? 0f : Mathf.MoveTowards(_deadBlend, 1f, dt / 0.4f);

            switch (_kind)
            {
                case Kind.Spider: TickSpider(state, p, move, charging, now); break;
                case Kind.Quadruped: TickQuad(state, p, move, charging, now); break;
                default: TickBiped(state, p, move, charging, now); break;
            }

            float bob = alive ? Mathf.Abs(Mathf.Cos(_phase)) * move * _height * (charging ? 0.035f : 0.02f) : 0f;
            _model.localPosition = _modelRest + Vector3.up * bob;
        }

        // ------------------------------------------------------------------------------- bipeds

        private void TickBiped(EnemyState state, float p, float move, bool charging, float now)
        {
            float s = Mathf.Sin(_phase);
            float legAmp = (charging ? 48f : 32f) * move;
            float legL = s * legAmp, legR = -s * legAmp;
            // Arms reach forward in the model; positive pitch swings them down and back, negative raises them.
            float sway = -s * 10f * move;
            float idle = Mathf.Sin(now * 1.6f + _seed) * 4f;
            float armL = sway + idle, armR = -sway - idle;
            float armRollL = 0f, armRollR = 0f;
            float headPitch = Mathf.Sin(_phase * 2f) * 3f * move, headYaw = Mathf.Sin(now * 0.7f + _seed) * 10f * (1f - move), headRoll = s * 5f * move;

            switch (state)
            {
                case EnemyState.Charge:
                    armL = armR = 95f + sway * 0.5f; // thrown back, sprinting
                    headPitch = -10f;
                    break;
                case EnemyState.ChargeWindup:
                    armL = armR = 70f * p;
                    legL = -12f * p; legR = 12f * p; // braces
                    headPitch = 15f * p;
                    break;
                case EnemyState.AttackWindup:
                    armL = armR = -85f * Ease(p); // raised overhead
                    headPitch = -15f * p;
                    break;
                case EnemyState.AttackRecover:
                    armL = armR = 55f * (1f - p) * (1f - p); // slammed down, recovering
                    headPitch = 12f * (1f - p);
                    break;
                case EnemyState.RangedWindup:
                    headPitch = -40f * Ease(p); // throat back before the spit
                    armL = armR = 25f * p;
                    armRollL = -25f * p; armRollR = 25f * p;
                    break;
                case EnemyState.RangedRecover:
                    headPitch = 25f * (1f - p);
                    break;
                case EnemyState.Stagger:
                    float k = 1f - p;
                    armL = Mathf.Sin(now * 22f) * 45f * k - 30f * k;
                    armR = Mathf.Cos(now * 19f) * 45f * k - 30f * k;
                    armRollL = -35f * k; armRollR = 35f * k;
                    headPitch = -25f * k; headRoll = 15f * k;
                    break;
                case EnemyState.ExplodeWindup:
                    armRollL = -60f * p; armRollR = 60f * p; // arms spread as it swells
                    armL = armR = Mathf.Sin(now * 40f) * 8f * p;
                    headPitch = -20f * p;
                    break;
            }

            if (_deadBlend > 0f)
            {
                legL = Mathf.Lerp(legL, 0f, _deadBlend);
                legR = Mathf.Lerp(legR, 0f, _deadBlend);
                armL = Mathf.Lerp(armL, 70f, _deadBlend);
                armR = Mathf.Lerp(armR, 60f, _deadBlend);
                headPitch = Mathf.Lerp(headPitch, 35f, _deadBlend);
            }

            Apply(_legL, legL, 0f, 0f);
            Apply(_legR, legR, 0f, 0f);
            Apply(_armL, armL, 0f, armRollL);
            Apply(_armR, armR, 0f, armRollR);
            Apply(_head, headPitch, headYaw, headRoll);
        }

        // ------------------------------------------------------------------------------- crawlers (four limbs)

        private void TickQuad(EnemyState state, float p, float move, bool charging, float now)
        {
            float s = Mathf.Sin(_phase);
            float amp = (charging ? 40f : 30f) * move;
            // Diagonal pairs move together: front-left with back-right.
            float a = s * amp, b = -s * amp;
            float frontL = a, backR = a, frontR = b, backL = b;
            float headPitch = Mathf.Sin(_phase * 2f) * 4f * move, headYaw = Mathf.Sin(now * 1.1f + _seed) * 12f * (1f - move);
            switch (state)
            {
                case EnemyState.AttackWindup:
                case EnemyState.ChargeWindup:
                    frontL = frontR = -60f * Ease(p); // rears up, claws raised
                    backL = backR = 15f * p;
                    headPitch = -20f * p;
                    break;
                case EnemyState.AttackRecover:
                    frontL = frontR = 40f * (1f - p);
                    break;
                case EnemyState.Stagger:
                    frontL = Mathf.Sin(now * 25f) * 35f * (1f - p);
                    frontR = Mathf.Cos(now * 23f) * 35f * (1f - p);
                    break;
                case EnemyState.ExplodeWindup:
                    frontL = frontR = backL = backR = Mathf.Sin(now * 45f) * 12f * p;
                    break;
            }
            if (_deadBlend > 0f)
            {
                frontL = Mathf.Lerp(frontL, -30f, _deadBlend);
                frontR = Mathf.Lerp(frontR, -20f, _deadBlend);
                backL = Mathf.Lerp(backL, 25f, _deadBlend);
                backR = Mathf.Lerp(backR, 30f, _deadBlend);
            }
            Apply(_armL, frontL, 0f, 0f);
            Apply(_armR, frontR, 0f, 0f);
            Apply(_legL, backL, 0f, 0f);
            Apply(_legR, backR, 0f, 0f);
            Apply(_head, headPitch, headYaw, 0f);
        }

        // ------------------------------------------------------------------------------- Broodmother (six legs)

        private void TickSpider(EnemyState state, float p, float move, bool charging, float now)
        {
            float swingAmp = (charging ? 28f : 20f) * move + 2f;
            for (int i = 0; i < 6; i++)
            {
                int l = i / 2;
                bool left = i % 2 == 0;
                // Alternating tripods: 0L, 1R, 2L step together, then 0R, 1L, 2R.
                bool groupA = left ? l % 2 == 0 : l % 2 == 1;
                float ph = _phase * 1.4f + (groupA ? 0f : Mathf.PI) + _seed * 0.1f;
                float swing = Mathf.Sin(ph) * swingAmp;          // forward/back around the vertical
                float lift = Mathf.Max(0f, Mathf.Cos(ph)) * 18f * move; // raised while it swings forward
                if (l == 0)
                {
                    if (state == EnemyState.AttackWindup || state == EnemyState.RangedWindup) { lift = 45f * Ease(p); swing = -20f * p; }
                    else if (state == EnemyState.AttackRecover) lift = 30f * (1f - p);
                }
                if (state == EnemyState.Stagger) lift += Mathf.Sin(now * 20f + i) * 15f * (1f - p);
                if (_deadBlend > 0f) { lift = Mathf.Lerp(lift, 35f, _deadBlend); swing *= 1f - _deadBlend; }
                // Left legs point -X: lifting them is a negative roll; right legs the opposite.
                Apply(_spiderLegs[i], 0f, left ? -swing : swing, left ? -lift : lift);
            }
            float headPitch = state == EnemyState.RangedWindup ? -30f * Ease(p) : state == EnemyState.AttackWindup ? -20f * p : Mathf.Sin(now * 2f + _seed) * 4f;
            if (_deadBlend > 0f) headPitch = Mathf.Lerp(headPitch, 30f, _deadBlend);
            Apply(_head, headPitch, Mathf.Sin(now * 0.8f + _seed) * 8f, 0f);
        }

        // ------------------------------------------------------------------------------- helpers

        private static float Ease(float t) => t * t * (3f - 2f * t);

        /// <summary>Pitch (around the enemy's right axis), yaw (up) and roll (forward), in the enemy's own frame.</summary>
        private void Apply(Part part, float pitch, float yaw, float roll)
        {
            if (!part.Valid) return;
            Quaternion inVisual = Quaternion.Euler(pitch, yaw, roll);
            part.T.localRotation = _toParent * inVisual * _fromParent * part.Rest;
        }
    }
}
