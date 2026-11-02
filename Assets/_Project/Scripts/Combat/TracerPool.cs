using System.Collections.Generic;
using UnityEngine;

namespace Deadfall.Combat
{
    /// <summary>Pooled bullet tracers and impact flashes. Placeholder VFX until the art pass.</summary>
    public sealed class TracerPool : MonoBehaviour
    {
        private struct Active
        {
            public LineRenderer Line;
            public float EndTime;
            public float Duration;
        }

        [SerializeField] private int _capacity = 48;
        [SerializeField] private float _duration = 0.05f;
        [SerializeField] private float _width = 0.02f;
        [SerializeField] private Color _color = new Color(1f, 0.85f, 0.5f, 0.9f);

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

        public void Spawn(Vector3 from, Vector3 to)
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
                _active.RemoveAt(0);
            }
            else
            {
                lr = Create();
            }
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            lr.gameObject.SetActive(true);
            _active.Add(new Active { Line = lr, EndTime = Time.time + _duration, Duration = _duration });
        }

        private void Update()
        {
            float now = Time.time;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (now < _active[i].EndTime) continue;
                LineRenderer lr = _active[i].Line;
                lr.gameObject.SetActive(false);
                _free.Push(lr);
                _active.RemoveAt(i);
            }
        }
    }
}
