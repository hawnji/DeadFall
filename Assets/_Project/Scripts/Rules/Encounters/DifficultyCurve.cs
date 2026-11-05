using System;

namespace Deadfall.Rules.Encounters
{
    /// <summary>Tunable numbers for how surges grow. Lives in a difficulty asset; one per difficulty level.</summary>
    [Serializable]
    public sealed class DifficultySettings
    {
        /// <summary>Threat budget of surge 1 for one player.</summary>
        public float BaseBudget = 6f;
        /// <summary>Budget added per surge (linear part).</summary>
        public float BudgetPerSurge = 4f;
        /// <summary>Budget added per surge squared (keeps late surges climbing).</summary>
        public float BudgetPerSurgeSquared = 0.25f;
        /// <summary>Budget multiplier by player count (index 0 = solo). More players, more enemies, not tougher ones.</summary>
        public float[] PlayerBudgetMultiplier = { 1f, 1.75f, 2.4f, 3f };
        /// <summary>Overall multiplier for the difficulty level.</summary>
        public float DifficultyMultiplier = 1f;

        /// <summary>Enemy health multiplier reaches this by <see cref="HealthScaleFullAtSurge"/> and stays there (GAME_DESIGN §8: ×2.5 by surge 30).</summary>
        public float MaxHealthScale = 2.5f;
        public int HealthScaleFullAtSurge = 30;

        /// <summary>Enemy move speed multiplier reaches this by <see cref="SpeedScaleFullAtSurge"/> and stays there.</summary>
        public float MaxSpeedScale = 1.35f;
        public int SpeedScaleFullAtSurge = 25;

        /// <summary>Enemies alive at once from the director's side (the enemy manager's cap still applies).</summary>
        public int BaseConcurrency = 8;
        public float ConcurrencyPerSurge = 1.5f;
        public int MaxConcurrency = 48;

        /// <summary>Directions enemies arrive from: 1 at first, +1 every N surges, up to the max.</summary>
        public int SurgesPerExtraDirection = 5;
        public int MaxDirections = 4;

        /// <summary>D-14: the adaptive factor may move the budget by at most this fraction either way.</summary>
        public float MaxAdaptiveAdjustment = 0.1f;
    }

    /// <summary>
    /// Everything that scales with surge number, player count and difficulty, in one place so it can be
    /// graphed and tested. Pure functions of their inputs.
    /// </summary>
    public sealed class DifficultyCurve
    {
        private readonly DifficultySettings _s;

        public DifficultyCurve(DifficultySettings settings) => _s = settings ?? new DifficultySettings();

        public DifficultySettings Settings => _s;

        /// <summary>
        /// Threat budget for a surge. <paramref name="adaptive"/> is the squad-performance factor in -1..1
        /// (negative when the squad is struggling); it moves the budget by at most ±MaxAdaptiveAdjustment.
        /// </summary>
        public float Budget(int surge, int players, float adaptive = 0f)
        {
            int n = Math.Max(1, surge) - 1;
            float raw = _s.BaseBudget + _s.BudgetPerSurge * n + _s.BudgetPerSurgeSquared * n * n;
            float adjust = 1f + Clamp(adaptive, -1f, 1f) * _s.MaxAdaptiveAdjustment;
            return raw * PlayerMultiplier(players) * Math.Max(0.05f, _s.DifficultyMultiplier) * adjust;
        }

        public float PlayerMultiplier(int players)
        {
            float[] m = _s.PlayerBudgetMultiplier;
            if (m == null || m.Length == 0) return 1f;
            int i = Math.Min(Math.Max(1, players), m.Length) - 1;
            return m[i];
        }

        public float HealthScale(int surge)
        {
            if (_s.HealthScaleFullAtSurge <= 1) return _s.MaxHealthScale;
            float t = Clamp((surge - 1) / (float)(_s.HealthScaleFullAtSurge - 1), 0f, 1f);
            return 1f + (_s.MaxHealthScale - 1f) * t;
        }

        /// <summary>How much faster enemies move this surge (1 at surge 1, rising to MaxSpeedScale).</summary>
        public float SpeedScale(int surge)
        {
            if (_s.SpeedScaleFullAtSurge <= 1) return _s.MaxSpeedScale;
            float t = Clamp((surge - 1) / (float)(_s.SpeedScaleFullAtSurge - 1), 0f, 1f);
            return 1f + (_s.MaxSpeedScale - 1f) * t;
        }

        public int Concurrency(int surge, int players)
        {
            float c = (_s.BaseConcurrency + _s.ConcurrencyPerSurge * (Math.Max(1, surge) - 1)) * (0.75f + 0.25f * Math.Max(1, players));
            return (int)Math.Min(_s.MaxConcurrency, Math.Max(1f, Math.Round(c)));
        }

        public int Directions(int surge)
        {
            int extra = _s.SurgesPerExtraDirection > 0 ? (Math.Max(1, surge) - 1) / _s.SurgesPerExtraDirection : 0;
            return Math.Min(Math.Max(1, _s.MaxDirections), 1 + extra);
        }

        private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    }
}
