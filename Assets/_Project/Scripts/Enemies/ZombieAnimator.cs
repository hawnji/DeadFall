using Deadfall.Rules.Combat;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Deadfall.Enemies
{
    /// <summary>
    /// Plays the animation clips baked into the rigged zm_* zombie models (build_models.py, "rigged zombies"): an
    /// Idle / Walk / Run blend driven by how fast the enemy actually moves, and Attack, Hit, Spit, Convulse and Death
    /// sampled from the brain state's progress, so wind-ups line up with the hit frames exactly. It reads only state and
    /// position (like <see cref="EnemyRig"/>), so client mirrors animate the same as the host's enemies.
    /// <para>
    /// Clips are sampled with a manually evaluated PlayableGraph (no Animator Controller asset): each clip's time is set
    /// directly every frame and weights cross-fade over <see cref="Fade"/> seconds.
    /// </para>
    /// </summary>
    public sealed class ZombieAnimator : MonoBehaviour, IEnemyRig
    {
        private enum Clip { Idle, Walk, Run, Attack, Hit, Spit, Convulse, Death }

        private static readonly string[] Names = { "Idle", "Walk", "Run", "Attack", "Hit", "Spit", "Convulse", "Death" };
        private const int Count = 8;
        private const float FarSqr = 55f * 55f;
        private const float FarInterval = 0.25f;
        private const float Fade = 0.14f;
        // Ground speed each locomotion clip was authored for at the 1.8 m reference height (two steps per cycle).
        private const float WalkSpeedRef = 1.2f;
        private const float RunSpeedRef = 3.4f;
        // Where the brain's wind-ups end inside the one-shot clips (the strike / spit frame).
        private const float AttackStrike = 0.42f;
        private const float SpitRelease = 0.6f;

        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private readonly AnimationClipPlayable[] _play = new AnimationClipPlayable[Count];
        private readonly float[] _length = new float[Count];
        private readonly bool[] _has = new bool[Count];
        private readonly float[] _weight = new float[Count];
        private readonly float[] _target = new float[Count];
        private readonly float[] _time = new float[Count];

        private float _scale = 1f;
        private float _speed;
        private float _phase;
        private float _deadTime;
        private float _farTimer;
        private bool _wasAlive = true;
        private Vector3 _lastPos;
        private bool _hasLast;
        private float _seed;

        public bool HandlesPose => true;

        /// <summary>Clip name from an imported take ("zm_standard|Walk" or "Walk"), without the armature prefix.</summary>
        public static string ClipKey(string takeName)
        {
            if (string.IsNullOrEmpty(takeName)) return string.Empty;
            int bar = takeName.LastIndexOf('|');
            return bar >= 0 ? takeName.Substring(bar + 1) : takeName;
        }

        /// <summary>
        /// Adds the animator to a spawned zm_* model when its clips are there (at least Idle and Walk); returns null
        /// otherwise so the caller can fall back. <paramref name="height"/> is the definition's height: locomotion
        /// playback speed scales with it.
        /// </summary>
        public static ZombieAnimator Bind(GameObject model, string modelId, float height)
        {
            if (model == null || string.IsNullOrEmpty(modelId)) return null;
            AnimationClip[] clips = Resources.LoadAll<AnimationClip>("Models/" + modelId);
            var found = new AnimationClip[Count];
            foreach (AnimationClip c in clips)
            {
                if (c == null || c.name.StartsWith("__preview__", System.StringComparison.Ordinal)) continue;
                string key = ClipKey(c.name);
                for (int i = 0; i < Count; i++) if (key == Names[i]) found[i] = c;
            }
            if (found[(int)Clip.Idle] == null || found[(int)Clip.Walk] == null) return null;

            Animator animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // The skinned bounds come from the rest pose; the death fall leaves them, so pad them out.
            foreach (SkinnedMeshRenderer smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Bounds b = smr.localBounds;
                b.Expand(b.size.magnitude * 0.8f);
                smr.localBounds = b;
            }

            var z = model.AddComponent<ZombieAnimator>();
            z.Build(animator, found, height);
            return z;
        }

        private void Build(Animator animator, AnimationClip[] clips, float height)
        {
            _scale = Mathf.Max(0.3f, height / 1.8f);
            _seed = Random.value * 10f;
            _graph = PlayableGraph.Create("Zombie " + name);
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(_graph, "Zombie", animator);
            _mixer = AnimationMixerPlayable.Create(_graph, Count);
            output.SetSourcePlayable(_mixer);
            for (int i = 0; i < Count; i++)
            {
                if (clips[i] == null) continue;
                _play[i] = AnimationClipPlayable.Create(_graph, clips[i]);
                _play[i].SetApplyFootIK(false);
                _graph.Connect(_play[i], 0, _mixer, i);
                _length[i] = Mathf.Max(0.05f, clips[i].length);
                _has[i] = true;
            }
            _graph.Play();
            Reset();
        }

        private void OnDestroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }

        public void Reset()
        {
            _speed = 0f;
            _phase = Random.value;
            _deadTime = 0f;
            _wasAlive = true;
            _hasLast = false;
            _farTimer = 0f;
            for (int i = 0; i < Count; i++) _weight[i] = _target[i] = _time[i] = 0f;
            _weight[(int)Clip.Idle] = 1f;
            Apply();
        }

        public void Tick(Vector3 position, EnemyState state, float progress, bool alive, float now, float dt, Vector3 viewer)
        {
            if (dt <= 0f || !_graph.IsValid()) return;
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
            if (!alive)
            {
                if (_wasAlive) _deadTime = 0f;
                _deadTime += dt;
            }
            _wasAlive = alive;

            float p = Mathf.Clamp01(progress);
            for (int i = 0; i < Count; i++) _target[i] = 0f;
            if (!alive) OneShot(Clip.Death, Mathf.Min(_deadTime, Len(Clip.Death)) / Len(Clip.Death));
            else
            {
                switch (state)
                {
                    case EnemyState.AttackWindup: OneShot(Clip.Attack, AttackStrike * p); break;
                    case EnemyState.AttackRecover: OneShot(Clip.Attack, AttackStrike + (1f - AttackStrike) * p); break;
                    case EnemyState.ChargeWindup: OneShot(Clip.Spit, SpitRelease * p); break; // rear back before the charge
                    case EnemyState.RangedWindup: OneShot(Clip.Spit, SpitRelease * p); break;
                    case EnemyState.RangedRecover: OneShot(Clip.Spit, SpitRelease + (1f - SpitRelease) * p); break;
                    case EnemyState.Stagger: OneShot(Clip.Hit, p); break;
                    case EnemyState.ExplodeWindup:
                        Clip c = Pick(Clip.Convulse);
                        _target[(int)c] = 1f;
                        _time[(int)c] = Mathf.Repeat(now * (1f + p), Len(c));
                        break;
                    default: Locomotion(state == EnemyState.Charge, dt, now); break;
                }
            }

            // Out of sight range: keep the bookkeeping, but pose only a few times a second.
            if (alive && (position - viewer).sqrMagnitude > FarSqr)
            {
                _farTimer -= dt;
                if (_farTimer > 0f) return;
                _farTimer = FarInterval;
                dt = Mathf.Max(dt, FarInterval);
            }

            float step = dt / Fade;
            for (int i = 0; i < Count; i++) _weight[i] = Mathf.MoveTowards(_weight[i], _target[i], step);
            Apply();
        }

        private void Locomotion(bool charging, float dt, float now)
        {
            float walkV = WalkSpeedRef * _scale, runV = RunSpeedRef * _scale;
            float moving = Mathf.Clamp01(_speed / (0.3f * walkV));
            float run = charging ? 1f : Mathf.Clamp01((_speed - walkV * 1.25f) / Mathf.Max(0.1f, runV * 0.75f - walkV * 1.25f));
            if (!_has[(int)Clip.Run]) run = 0f;
            float v = Mathf.Max(_speed, 0.4f * walkV);
            float walkRate = v / walkV / Len(Clip.Walk);
            float runRate = v / runV / Len(Clip.Run);
            _phase = Mathf.Repeat(_phase + dt * Mathf.Lerp(walkRate, runRate, run), 1f);

            _target[(int)Clip.Idle] += 1f - moving;
            _target[(int)Clip.Walk] += moving * (1f - run);
            if (_has[(int)Clip.Run]) _target[(int)Clip.Run] += moving * run;
            _time[(int)Clip.Idle] = Mathf.Repeat(now + _seed, Len(Clip.Idle));
            _time[(int)Clip.Walk] = _phase * Len(Clip.Walk);
            _time[(int)Clip.Run] = _phase * Len(Clip.Run);
        }

        private void OneShot(Clip clip, float normalized)
        {
            Clip c = Pick(clip);
            _target[(int)c] = 1f;
            _time[(int)c] = c == clip ? Mathf.Clamp01(normalized) * Len(c) : Mathf.Repeat(_time[(int)c], Len(c));
        }

        private Clip Pick(Clip clip) => _has[(int)clip] ? clip : Clip.Idle;

        private float Len(Clip c) => _has[(int)c] ? _length[(int)c] : 1f;

        private void Apply()
        {
            float total = 0f;
            for (int i = 0; i < Count; i++) if (_has[i]) total += _weight[i];
            if (total <= 0.0001f)
            {
                _weight[(int)Clip.Idle] = 1f;
                total = 1f;
            }
            for (int i = 0; i < Count; i++)
            {
                if (!_has[i]) continue;
                _mixer.SetInputWeight(i, _weight[i] / total);
                _play[i].SetTime(_time[i]);
            }
            _graph.Evaluate(0f);
        }
    }
}
