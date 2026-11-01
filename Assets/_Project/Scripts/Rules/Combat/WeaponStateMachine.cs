using System;

namespace Deadfall.Rules.Combat
{
    public enum WeaponState
    {
        Holstered,
        Equipping,
        Ready,
        Reloading,
    }

    public readonly struct FireResult
    {
        public readonly int ShotsFired;
        /// <summary>True when the trigger was pulled on an empty magazine.</summary>
        public readonly bool DryFire;

        public FireResult(int shotsFired, bool dryFire)
        {
            ShotsFired = shotsFired;
            DryFire = dryFire;
        }

        public static readonly FireResult None = new FireResult(0, false);
    }

    /// <summary>
    /// Ammunition, fire rate, equip, and reload rules for one firearm. Engine-free and time-driven:
    /// callers pass the current time in seconds, so it behaves the same at any frame rate and is
    /// fully unit-testable. Several shots can be released in one call when a frame is longer than
    /// the fire interval, so high fire-rate weapons don't slow down at low FPS.
    /// </summary>
    public sealed class WeaponStateMachine
    {
        private readonly WeaponStats _stats;
        private double _nextShotTime;
        private double _stateStart;
        private double _stateEnd;
        private bool _reloadCommitted;
        private bool _reloadFromEmpty;
        private bool _triggerWasDown;

        /// <summary>Upper bound on rounds released in one call, so a long hitch can't empty a magazine instantly.</summary>
        public const int MaxShotsPerCall = 8;

        public WeaponStateMachine(WeaponStats stats, int magazine, int reserve)
        {
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
            Magazine = Clamp(magazine, 0, stats.MagazineSize);
            Reserve = Clamp(reserve, 0, stats.MaxReserve);
            State = WeaponState.Holstered;
        }

        public static WeaponStateMachine CreateFull(WeaponStats stats) => new WeaponStateMachine(stats, stats.MagazineSize, stats.MaxReserve);

        public WeaponStats Stats => _stats;
        public WeaponState State { get; private set; }
        public int Magazine { get; private set; }
        public int Reserve { get; private set; }
        public bool IsReloading => State == WeaponState.Reloading;
        public bool IsEmpty => Magazine == 0;
        public bool CanReload => Magazine < _stats.MagazineSize && Reserve > 0;

        /// <summary>0..1 progress of the current equip or reload, 1 otherwise.</summary>
        public float StateProgress(double now)
        {
            if (State != WeaponState.Reloading && State != WeaponState.Equipping) return 1f;
            double length = _stateEnd - _stateStart;
            if (length <= 0) return 1f;
            return (float)Math.Min(1.0, Math.Max(0.0, (now - _stateStart) / length));
        }

        /// <summary>Raised when ammo enters the magazine (at the commit point of a reload).</summary>
        public event Action ReloadCommitted;

        public void Equip(double now)
        {
            State = WeaponState.Equipping;
            _stateStart = now;
            _stateEnd = now + Math.Max(0f, _stats.EquipSeconds);
            _nextShotTime = _stateEnd;
            if (_stateEnd <= now) State = WeaponState.Ready;
        }

        /// <summary>Holstering cancels any reload. A committed reload keeps its ammo.</summary>
        public void Holster()
        {
            if (State == WeaponState.Reloading) CancelReload();
            State = WeaponState.Holstered;
        }

        /// <summary>Advances timed states. Call once per frame before firing.</summary>
        public void Tick(double now)
        {
            if (State == WeaponState.Equipping && now >= _stateEnd)
            {
                State = WeaponState.Ready;
            }
            else if (State == WeaponState.Reloading)
            {
                double commitTime = _stateStart + (_stateEnd - _stateStart) * Clamp01(_stats.ReloadCommitFraction);
                if (!_reloadCommitted && now >= commitTime) CommitReload();
                if (now >= _stateEnd)
                {
                    State = WeaponState.Ready;
                    // The weapon can fire as soon as the reload ends, not one fire interval later.
                    if (_nextShotTime < _stateEnd) _nextShotTime = _stateEnd;
                }
            }
        }

        /// <summary>
        /// Releases the shots due by <paramref name="now"/>. Semi-auto fires once per press;
        /// full-auto fires while held. Returns how many rounds left the barrel.
        /// </summary>
        public FireResult TryFire(double now, bool triggerPressedThisFrame, bool triggerHeld)
        {
            Tick(now);
            if (State != WeaponState.Ready)
            {
                _triggerWasDown = false;
                return FireResult.None;
            }

            bool wantsFire = _stats.FireMode == FireMode.FullAuto ? triggerHeld || triggerPressedThisFrame : triggerPressedThisFrame;
            bool continuing = _triggerWasDown;
            _triggerWasDown = triggerHeld || triggerPressedThisFrame;
            if (!wantsFire) return FireResult.None;

            if (Magazine == 0)
                return new FireResult(0, triggerPressedThisFrame);

            double interval = _stats.SecondsBetweenShots;
            if (now < _nextShotTime) return FireResult.None;

            // A fresh trigger pull restarts the cadence from now instead of releasing a backlog of shots.
            // While the trigger stays down, shots owed from a long frame are released to keep the fire rate exact.
            if (!continuing)
            {
                if (_nextShotTime < now) _nextShotTime = now;
            }
            else if (now - _nextShotTime > interval * MaxShotsPerCall)
            {
                _nextShotTime = now - interval * (MaxShotsPerCall - 1);
            }

            int shots = 0;
            if (_stats.FireMode == FireMode.SemiAuto)
            {
                shots = 1;
                _nextShotTime = now + interval;
            }
            else
            {
                while (now >= _nextShotTime && Magazine - shots > 0 && shots < MaxShotsPerCall)
                {
                    shots++;
                    _nextShotTime += interval;
                }
            }

            Magazine -= shots;
            return new FireResult(shots, false);
        }

        /// <summary>Starts a reload if the magazine isn't full and reserve ammo exists.</summary>
        public bool StartReload(double now)
        {
            if (State != WeaponState.Ready || !CanReload) return false;
            _reloadFromEmpty = Magazine == 0;
            float duration = _reloadFromEmpty ? _stats.EmptyReloadSeconds : _stats.TacticalReloadSeconds;
            State = WeaponState.Reloading;
            _reloadCommitted = false;
            _stateStart = now;
            _stateEnd = now + Math.Max(0f, duration);
            Tick(now);
            return true;
        }

        /// <summary>
        /// Interrupts a reload (swap, melee, sprint rules decided by the caller). Ammo is only gained
        /// if the commit point had already passed.
        /// </summary>
        public void CancelReload()
        {
            if (State != WeaponState.Reloading) return;
            State = WeaponState.Ready;
            _reloadCommitted = false;
        }

        /// <summary>Adds reserve ammo up to the cap. Returns the amount actually added.</summary>
        public int AddReserve(int amount)
        {
            if (amount <= 0) return 0;
            int before = Reserve;
            Reserve = Clamp(Reserve + amount, 0, _stats.MaxReserve);
            return Reserve - before;
        }

        private void CommitReload()
        {
            _reloadCommitted = true;
            int needed = _stats.MagazineSize - Magazine;
            int taken = Math.Min(needed, Reserve);
            Magazine += taken;
            Reserve -= taken;
            ReloadCommitted?.Invoke();
        }

        private static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
