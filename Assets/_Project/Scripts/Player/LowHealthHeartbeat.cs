using Deadfall.Core;
using Deadfall.Rules.Common;
using UnityEngine;

namespace Deadfall.Player
{
    /// <summary>
    /// The local player's heartbeat when badly hurt: beats below 30% health, faster below 15%, and while downed.
    /// </summary>
    [RequireComponent(typeof(PlayerHealth))]
    public sealed class LowHealthHeartbeat : MonoBehaviour
    {
        public const float Threshold = 0.3f;
        public const float Critical = 0.15f;

        private PlayerHealth _health;
        private float _next;

        private void Awake() => _health = GetComponent<PlayerHealth>();

        private void Update()
        {
            if (_health == null) return;
            bool downed = _health.IsDowned;
            float hp = _health.Normalized;
            if (!downed && (!_health.IsAlive || hp >= Threshold))
            {
                _next = 0f; // the first beat comes right away next time
                return;
            }
            float now = Time.time;
            if (now < _next) return;
            bool critical = downed || hp < Critical;
            _next = now + (critical ? 0.62f : 0.9f);
            GameAudio.Play2D(SfxId.LowHealthHeartbeat, critical ? 1f : 0.75f);
        }
    }
}
