using System;
using System.Collections.Generic;

namespace Deadfall.Rules.Encounters
{
    public enum RunPhase : byte
    {
        NotStarted,
        /// <summary>Preparation between surges. Ends on a timer or when every player is ready.</summary>
        Lull,
        Surge,
        /// <summary>A short beat after a surge is cleared: bonus paid, announcement shown.</summary>
        Reward,
        Complete,
        Failed,
    }

    /// <summary>What happened during one <see cref="SurgeRun.Tick"/>. Several can happen in one tick.</summary>
    [Flags]
    public enum SurgeRunEvents
    {
        None = 0,
        LullStarted = 1 << 0,
        SurgeStarted = 1 << 1,
        SurgeCleared = 1 << 2,
        RunCompleted = 1 << 3,
        RunFailed = 1 << 4,
        /// <summary>Stragglers have gone too long without a kill: the engine should remove the rest of this surge.</summary>
        CullStragglers = 1 << 5,
    }

    [Serializable]
    public sealed class SurgeRunSettings
    {
        public float FirstLullSeconds = 10f;
        public float LullSeconds = 30f;
        public float RewardSeconds = 3f;
        /// <summary>Clearing this surge wins the run. 0 or less means endless.</summary>
        public int FinalSurge = 20;
        /// <summary>
        /// Deadlock guard: once everything has spawned, if enemies are still alive but nobody has died for this
        /// long (stuck behind geometry, lost off the NavMesh), the leftovers are removed so the run can go on.
        /// </summary>
        public float StragglerTimeoutSeconds = 40f;
        /// <summary>Same guard for the spawning side: a surge that cannot finish spawning ends after this long.</summary>
        public float MaxSurgeSeconds = 600f;

        /// <summary>Field Credits per threat point of a kill (Husk 10, Bolter 15, Bulwark 60).</summary>
        public float CreditsPerThreat = 10f;
        /// <summary>Each player gets ClearBonusBase + ClearBonusPerSurge × surge when a surge is cleared.</summary>
        public int ClearBonusBase = 50;
        public int ClearBonusPerSurge = 10;
    }

    /// <summary>What the engine tells the run each tick.</summary>
    public struct SurgeRunInput
    {
        public double Now;
        /// <summary>The spawn director has spawned (or given up on) every enemy in the current plan.</summary>
        public bool AllSpawned;
        /// <summary>Enemies from the current surge still alive.</summary>
        public int Alive;
        /// <summary>At least one player is alive. When this is false during a run, the run fails.</summary>
        public bool AnyPlayerAlive;
        /// <summary>Every connected player has pressed ready during the lull.</summary>
        public bool AllReady;
    }

    /// <summary>
    /// The survival loop as a pure state machine: Lull → Surge → Reward → Lull … until the final surge is
    /// cleared (Complete) or the whole squad is down (Failed). Driven by timestamps so it can be tested
    /// without an engine, and it holds no enemies itself; the engine reports counts each tick.
    /// </summary>
    public sealed class SurgeRun
    {
        private readonly SurgeRunSettings _s;
        private double _lastKill;
        private double _surgeStarted;
        private bool _culled;

        public SurgeRun(SurgeRunSettings settings) => _s = settings ?? new SurgeRunSettings();

        public SurgeRunSettings Settings => _s;
        public RunPhase Phase { get; private set; } = RunPhase.NotStarted;
        /// <summary>The surge being fought, or the next one during a lull. 0 before the run starts.</summary>
        public int Surge { get; private set; }
        /// <summary>When the current timed phase (Lull, Reward) ends.</summary>
        public double PhaseEndsAt { get; private set; }
        public bool IsRunning => Phase == RunPhase.Lull || Phase == RunPhase.Surge || Phase == RunPhase.Reward;
        public bool IsEndless => _s.FinalSurge <= 0;

        public double TimeLeft(double now) =>
            Phase == RunPhase.Lull || Phase == RunPhase.Reward ? Math.Max(0.0, PhaseEndsAt - now) : 0.0;

        /// <summary>Starts a new run at the first lull. Returns <see cref="SurgeRunEvents.LullStarted"/>.</summary>
        public SurgeRunEvents Start(double now)
        {
            Surge = 1;
            EnterLull(now, _s.FirstLullSeconds);
            return SurgeRunEvents.LullStarted;
        }

        /// <summary>Ends the run without a result (debug stop, leaving the map).</summary>
        public void Abort()
        {
            Phase = RunPhase.NotStarted;
            Surge = 0;
        }

        /// <summary>Ends the run as a win now (Operations: extraction held). No effect unless running.</summary>
        public SurgeRunEvents Complete()
        {
            if (!IsRunning) return SurgeRunEvents.None;
            Phase = RunPhase.Complete;
            return SurgeRunEvents.RunCompleted;
        }

        /// <summary>Shortens the current lull so it ends within <paramref name="seconds"/>.</summary>
        public void ShortenLull(double now, float seconds)
        {
            if (Phase == RunPhase.Lull) PhaseEndsAt = Math.Min(PhaseEndsAt, now + Math.Max(0f, seconds));
        }

        /// <summary>Ends the lull now (all players ready, or debug). No effect outside a lull.</summary>
        public void SkipLull(double now)
        {
            if (Phase == RunPhase.Lull) PhaseEndsAt = now;
        }

        /// <summary>An enemy from the current surge died; resets the straggler timer.</summary>
        public void NotifyKill(double now) => _lastKill = now;

        public SurgeRunEvents Tick(in SurgeRunInput input)
        {
            if (!IsRunning) return SurgeRunEvents.None;
            double now = input.Now;
            if (!input.AnyPlayerAlive)
            {
                Phase = RunPhase.Failed;
                return SurgeRunEvents.RunFailed;
            }

            switch (Phase)
            {
                case RunPhase.Lull:
                    if (input.AllReady) PhaseEndsAt = Math.Min(PhaseEndsAt, now);
                    if (now < PhaseEndsAt) return SurgeRunEvents.None;
                    Phase = RunPhase.Surge;
                    _surgeStarted = now;
                    _lastKill = now;
                    _culled = false;
                    return SurgeRunEvents.SurgeStarted;

                case RunPhase.Surge:
                {
                    if (input.AllSpawned && input.Alive <= 0) return ClearSurge(now);
                    var events = SurgeRunEvents.None;
                    if (!_culled && input.Alive > 0)
                    {
                        bool stalled = input.AllSpawned && now - _lastKill >= _s.StragglerTimeoutSeconds;
                        bool tooLong = now - _surgeStarted >= _s.MaxSurgeSeconds;
                        if (stalled || tooLong)
                        {
                            _culled = true;
                            events |= SurgeRunEvents.CullStragglers;
                        }
                    }
                    // A surge that could not spawn anything at all (no spawn points) must still end.
                    if (!input.AllSpawned && input.Alive <= 0 && now - _surgeStarted >= _s.MaxSurgeSeconds)
                        events |= ClearSurge(now);
                    return events;
                }

                case RunPhase.Reward:
                    if (now < PhaseEndsAt) return SurgeRunEvents.None;
                    Surge++;
                    EnterLull(now, _s.LullSeconds);
                    return SurgeRunEvents.LullStarted;
            }
            return SurgeRunEvents.None;
        }

        private SurgeRunEvents ClearSurge(double now)
        {
            if (!IsEndless && Surge >= _s.FinalSurge)
            {
                Phase = RunPhase.Complete;
                return SurgeRunEvents.SurgeCleared | SurgeRunEvents.RunCompleted;
            }
            Phase = RunPhase.Reward;
            PhaseEndsAt = now + _s.RewardSeconds;
            return SurgeRunEvents.SurgeCleared;
        }

        private void EnterLull(double now, float seconds)
        {
            Phase = RunPhase.Lull;
            PhaseEndsAt = now + Math.Max(0f, seconds);
        }

        // ------------------------------------------------------------ rewards

        public int KillCredits(float threatCost) => (int)Math.Round(Math.Max(0f, threatCost) * _s.CreditsPerThreat, MidpointRounding.AwayFromZero);

        public int ClearBonus(int surge) => _s.ClearBonusBase + _s.ClearBonusPerSurge * Math.Max(0, surge);
    }

    /// <summary>Individual Field Credits per player (D-13). Cleared when a deployment ends.</summary>
    public sealed class CreditLedger
    {
        private readonly Dictionary<ulong, int> _credits = new Dictionary<ulong, int>();

        public IReadOnlyDictionary<ulong, int> All => _credits;

        public int Get(ulong player) => _credits.TryGetValue(player, out int c) ? c : 0;

        public void Add(ulong player, int amount)
        {
            if (amount == 0) return;
            _credits[player] = Math.Max(0, Get(player) + amount);
        }

        /// <summary>Spends if the player can afford it. Returns false (and spends nothing) otherwise.</summary>
        public bool TrySpend(ulong player, int amount)
        {
            if (amount < 0 || Get(player) < amount) return false;
            _credits[player] = Get(player) - amount;
            return true;
        }

        /// <summary>Overwrites a player's balance (clients mirroring the host).</summary>
        public void Set(ulong player, int amount) => _credits[player] = Math.Max(0, amount);

        public void Clear() => _credits.Clear();
    }
}
