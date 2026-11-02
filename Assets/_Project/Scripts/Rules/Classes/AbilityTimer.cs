using System;

namespace Deadfall.Rules.Classes
{
    public enum AbilityPhase : byte
    {
        Ready,
        /// <summary>The effect is running (Overdrive firing, shield standing, lunge dashing).</summary>
        Active,
        Cooldown,
    }

    [Flags]
    public enum AbilityEvents
    {
        None = 0,
        Activated = 1 << 0,
        /// <summary>The active effect ran out or was ended early; the cooldown has started.</summary>
        Ended = 1 << 1,
        /// <summary>The cooldown finished.</summary>
        Ready = 1 << 2,
    }

    /// <summary>
    /// Timing for one active ability: Ready → Active (duration) → Cooldown → Ready. The cooldown starts when the
    /// effect ends (T-26), so making an effect last longer never lets it overlap the next use. Timestamp driven
    /// and engine-free, so it behaves the same at any frame rate and can be tested directly.
    /// </summary>
    public sealed class AbilityTimer
    {
        private double _activeEndsAt;
        private double _cooldownEndsAt;
        private float _cooldownRate = 1f;

        public AbilityTimer(float cooldownSeconds, float durationSeconds)
        {
            CooldownSeconds = Math.Max(0f, cooldownSeconds);
            DurationSeconds = Math.Max(0f, durationSeconds);
        }

        public float CooldownSeconds { get; set; }
        /// <summary>0 for instant abilities: they go straight to cooldown when used.</summary>
        public float DurationSeconds { get; set; }
        public AbilityPhase Phase { get; private set; } = AbilityPhase.Ready;
        public bool IsReady => Phase == AbilityPhase.Ready;
        public bool IsActive => Phase == AbilityPhase.Active;

        /// <summary>
        /// Cooldown speed: 1 is normal, 1.25 recovers 25% faster (Combined Arms, Field Mods). Applies to cooldowns
        /// that start after it is set.
        /// </summary>
        public float CooldownRate
        {
            get => _cooldownRate;
            set => _cooldownRate = Math.Max(0.05f, value);
        }

        /// <summary>The length the next (or current) cooldown takes after the rate.</summary>
        public float EffectiveCooldown => CooldownSeconds / _cooldownRate;

        public bool TryActivate(double now)
        {
            Tick(now);
            if (Phase != AbilityPhase.Ready) return false;
            if (DurationSeconds > 0f)
            {
                Phase = AbilityPhase.Active;
                _activeEndsAt = now + DurationSeconds;
            }
            else
            {
                StartCooldown(now);
            }
            return true;
        }

        /// <summary>Ends an active effect early (shield destroyed, lunge connected). No effect otherwise.</summary>
        public AbilityEvents End(double now)
        {
            if (Phase != AbilityPhase.Active) return AbilityEvents.None;
            StartCooldown(now);
            return AbilityEvents.Ended | (Phase == AbilityPhase.Ready ? AbilityEvents.Ready : AbilityEvents.None);
        }

        public AbilityEvents Tick(double now)
        {
            var events = AbilityEvents.None;
            if (Phase == AbilityPhase.Active && now >= _activeEndsAt)
            {
                StartCooldown(_activeEndsAt);
                events |= AbilityEvents.Ended;
            }
            if (Phase == AbilityPhase.Cooldown && now >= _cooldownEndsAt)
            {
                Phase = AbilityPhase.Ready;
                events |= AbilityEvents.Ready;
            }
            return events;
        }

        /// <summary>Takes seconds off a running cooldown (kill refunds and similar).</summary>
        public void ReduceCooldown(float seconds)
        {
            if (Phase == AbilityPhase.Cooldown && seconds > 0f) _cooldownEndsAt -= seconds;
        }

        /// <summary>Ready immediately (class change, run start, debug).</summary>
        public void Reset() => Phase = AbilityPhase.Ready;

        public float ActiveRemaining(double now) => Phase == AbilityPhase.Active ? (float)Math.Max(0.0, _activeEndsAt - now) : 0f;

        public float CooldownRemaining(double now) => Phase == AbilityPhase.Cooldown ? (float)Math.Max(0.0, _cooldownEndsAt - now) : 0f;

        /// <summary>0 right after the cooldown starts, 1 when ready. 1 while ready or active.</summary>
        public float CooldownProgress(double now)
        {
            if (Phase != AbilityPhase.Cooldown) return 1f;
            float length = EffectiveCooldown;
            return length <= 0f ? 1f : 1f - Math.Min(1f, CooldownRemaining(now) / length);
        }

        private void StartCooldown(double from)
        {
            Phase = AbilityPhase.Cooldown;
            _cooldownEndsAt = from + EffectiveCooldown;
            if (EffectiveCooldown <= 0f) Phase = AbilityPhase.Ready;
        }
    }
}
