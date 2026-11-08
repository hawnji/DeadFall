using Deadfall.Core;
using Deadfall.Rules.Common;
using UnityEngine;

namespace Deadfall.Enemies
{
    /// <summary>
    /// Footsteps for an infected, from how far it has actually moved (works on the host and on client mirrors).
    /// Bigger bodies take longer strides and sound lower and louder. Only enemies near the listener play, so a
    /// horde doesn't flood the voice pool.
    /// </summary>
    [RequireComponent(typeof(Enemy))]
    public sealed class EnemyFootsteps : MonoBehaviour
    {
        private const float HearRange = 20f;

        private Enemy _enemy;
        private Vector3 _last;
        private float _travelled;
        private float _stride, _pitch, _volume;
        private SfxId _sound = SfxId.EnemyWalk;
        private static Transform _listener;

        private void Awake()
        {
            _enemy = GetComponent<Enemy>();
            float r = _enemy.Definition != null ? _enemy.Definition.Radius : 0.35f;
            _stride = Mathf.Clamp(0.9f + r * 2.2f, 1f, 3.2f);
            _pitch = Mathf.Clamp(1.25f - r * 0.6f, 0.65f, 1.15f);
            _volume = Mathf.Clamp(0.55f + r * 0.6f, 0.6f, 1.2f);
            if (_enemy.Definition != null && _enemy.Definition.IsBrood)
            {
                // Spiders skitter: the spider walk sound at its own pitch, every short step of her six legs.
                _sound = SfxId.SpiderWalk;
                _stride = 1.4f;
                _pitch = 1f;
                _volume = 1f;
            }
        }

        private void OnEnable()
        {
            _last = transform.position;
            _travelled = Random.Range(0f, _stride);
        }

        private void Update()
        {
            Vector3 p = transform.position;
            Vector3 d = p - _last;
            _last = p;
            if (!_enemy.IsAlive) return;
            d.y = 0f;
            float step = d.magnitude;
            if (step > 2f) return; // teleport or snapshot jump, not walking
            _travelled += step;
            if (_travelled < _stride) return;
            _travelled -= _stride;
            if (_listener == null || !_listener.gameObject.activeInHierarchy)
            {
                var l = FindListener();
                _listener = l != null ? l.transform : null;
            }
            if (_listener != null && (_listener.position - p).sqrMagnitude > HearRange * HearRange) return;
            GameAudio.Play(_sound, p + Vector3.up * 0.1f, _volume, _pitch);
        }

        private static AudioListener FindListener()
        {
            foreach (AudioListener l in Resources.FindObjectsOfTypeAll<AudioListener>())
                if (l != null && l.isActiveAndEnabled && l.gameObject.scene.IsValid()) return l;
            return null;
        }
    }
}
