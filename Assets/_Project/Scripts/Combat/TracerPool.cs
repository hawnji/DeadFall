using System.Collections.Generic;
using UnityEngine;

namespace Deadfall.Combat
{
    /// <summary>
    /// Pooled bullet tracers: a short bright streak that flies from the muzzle to the hit point, then an impact
    /// (sparks, dust and a bullet hole, or blood on anything with a hurtbox) where it lands.
    /// </summary>
    public sealed class TracerPool : MonoBehaviour
    {
        private struct Active
        {
            public LineRenderer Line;
            public float StartTime;
            public float EndTime;
            public Vector3 From, To;
            public bool Impact;
        }

        /// <summary>Visual bullet speed (m/s); real hits are instant.</summary>
        private const float StreakSpeed = 420f;
        private const float StreakLength = 4f;

        [SerializeField] private int _capacity = 48;
        [SerializeField] private float _width = 0.03f;
        [SerializeField] private Color _color = new Color(1f, 0.88f, 0.55f, 1f);

        private readonly Stack<LineRenderer> _free = new Stack<LineRenderer>();
        private readonly List<Active> _active = new List<Active>();
        private Material _material;

        private static TracerPool _instance;

        public static TracerPool Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("[TracerPool]");
                    _instance = go.AddComponent<TracerPool>();
                }
                return _instance;
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            // Sprites/Default is in Unity's "Always Included Shaders" list, so it survives builds. If a project
            // removes it, fall back to the pipeline's lit material rather than drawing pink.
            Shader shader = Shader.Find("Sprites/Default");
            _material = shader != null ? new Material(shader) : Deadfall.Core.RuntimeMaterials.CreateLit(_color);
            for (int i = 0; i < _capacity; i++) _free.Push(Create());
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private LineRenderer Create()
        {
            var go = new GameObject("Tracer");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.startWidth = _width;
            lr.endWidth = _width * 0.5f;
            if (_material != null) lr.sharedMaterial = _material;
            lr.startColor = _color;
            lr.endColor = new Color(_color.r, _color.g, _color.b, 0f);
            go.SetActive(false);
            return lr;
        }

        /// <summary>Shows a shot from <paramref name="from"/> to <paramref name="to"/>; <paramref name="impact"/> adds hit effects at the end.</summary>
        public void Spawn(Vector3 from, Vector3 to) => Spawn(from, to, true);

        public void Spawn(Vector3 from, Vector3 to, bool impact)
        {
            LineRenderer lr;
            if (_free.Count > 0)
            {
                lr = _free.Pop();
            }
            else if (_active.Count > 0)
            {
                // Pool exhausted: recycle the oldest tracer instead of allocating.
                lr = _active[0].Line;
                if (_active[0].Impact) FxParticles.ImpactAt(_active[0].From, _active[0].To);
                _active.RemoveAt(0);
            }
            else
            {
                lr = Create();
            }
            float travel = Mathf.Clamp(Vector3.Distance(from, to) / StreakSpeed, 0.02f, 0.25f);
            lr.SetPosition(0, from);
            lr.SetPosition(1, from);
            lr.gameObject.SetActive(true);
            float now = Time.time;
            _active.Add(new Active { Line = lr, StartTime = now, EndTime = now + travel, From = from, To = to, Impact = impact });
        }

        private void Update()
        {
            float now = Time.time;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Active a = _active[i];
                if (now < a.EndTime)
                {
                    float t = Mathf.InverseLerp(a.StartTime, a.EndTime, now);
                    float dist = Vector3.Distance(a.From, a.To);
                    Vector3 head = Vector3.Lerp(a.From, a.To, t);
                    float tailT = dist > 0.01f ? Mathf.Max(0f, t - StreakLength / dist) : 0f;
                    a.Line.SetPosition(0, head);
                    a.Line.SetPosition(1, Vector3.Lerp(a.From, a.To, tailT));
                    continue;
                }
                if (a.Impact) FxParticles.ImpactAt(a.From, a.To);
                a.Line.gameObject.SetActive(false);
                _free.Push(a.Line);
                _active.RemoveAt(i);
            }
        }
    }
}
