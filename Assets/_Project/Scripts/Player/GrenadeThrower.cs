using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Rules.Common;
using UnityEngine;

namespace Deadfall.Player
{
    /// <summary>
    /// Thrown frag grenades on their own key (E, or the left shoulder button): a fused grenade lobbed where you look.
    /// Carries <see cref="GrenadeRules.BaseCapacity"/> plus <see cref="BonusCapacity"/> (Bandolier); any full ammo
    /// refill (ammo station, Supply Cache, Max Ammo) tops them up. The count shows next to the ammo readout.
    /// </summary>
    public sealed class GrenadeThrower : MonoBehaviour
    {
        private PlayerInputReader _input;
        private PlayerLook _look;
        private PlayerHealth _health;
        private PlayerWeapons _weapons;
        private int _bonus;
        private float _readyAt;

        public int Count { get; private set; } = GrenadeRules.BaseCapacity;
        public int Capacity => GrenadeRules.BaseCapacity + _bonus;

        /// <summary>Extra grenades carried (Engineer Bandolier). Raising it also hands out the new ones.</summary>
        public int BonusCapacity
        {
            get => _bonus;
            set
            {
                int v = Mathf.Max(0, value);
                if (v > _bonus) Count += v - _bonus;
                _bonus = v;
                Count = Mathf.Min(Count, Capacity);
            }
        }

        private void Awake()
        {
            _input = GetComponent<PlayerInputReader>();
            _look = GetComponent<PlayerLook>();
            _health = GetComponent<PlayerHealth>();
            _weapons = GetComponent<PlayerWeapons>();
        }

        private void OnEnable() { if (_weapons != null) _weapons.AmmoRefilled += Refill; }
        private void OnDisable() { if (_weapons != null) _weapons.AmmoRefilled -= Refill; }

        public void Refill() => Count = Capacity;

        private void Update()
        {
            if (_input == null || !_input.Current.GrenadePressed) return;
            if (Count <= 0 || Time.time < _readyAt || (_health != null && !_health.IsAlive)) return;
            Camera cam = _look != null ? _look.Camera : null;
            if (cam == null) return;
            Transform t = cam.transform;
            Vector3 origin = t.position + t.forward * 0.6f + t.right * 0.15f - t.up * 0.1f;
            // Don't spawn it inside a wall right in front of the face.
            if (Physics.Raycast(t.position, t.forward, out RaycastHit hit, 0.7f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                origin = t.position + t.forward * Mathf.Max(0.1f, hit.distance - 0.15f);
            Vector3 velocity = t.forward * GrenadeRules.ThrowSpeed + Vector3.up * GrenadeRules.UpwardSpeed;
            ProjectileServices.Current.Throw(gameObject, origin, velocity);
            Count--;
            _readyAt = Time.time + GrenadeRules.Cooldown;
            GameAudio.Play(SfxId.MeleeSwing, t.position, 0.6f, 0.8f);
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || UiModal.Active) return;
            if (_health != null && !_health.IsAlive) return;
            HudKit.Begin();
            var r = new Rect(HudKit.W - 28f - 70f, HudKit.H - 28f - 84f - 42f, 70f, 34f); // just above the weapon card
            HudKit.Panel(r);
            Color c = Count > 0 ? HudKit.Text : HudKit.TextDim;
            HudKit.Label(new Rect(r.x + 8f, r.y, 30f, r.height), "●", 18, Count > 0 ? new Color(0.55f, 0.7f, 0.4f) : HudKit.TextDim, TextAnchor.MiddleLeft);
            HudKit.Label(new Rect(r.x + 26f, r.y, 40f, r.height), $"{Count}  E", 13, c, TextAnchor.MiddleLeft, head: true);
            HudKit.End();
        }
    }
}
