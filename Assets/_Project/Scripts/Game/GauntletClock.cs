using Deadfall.Core;
using Deadfall.Encounters;
using Deadfall.Rules.Encounters;
using Deadfall.Rules.Progression;
using Deadfall.Rules.World;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// The Gauntlet clock: starts when the first surge starts, stops on the win, on every peer (each machine times
    /// it from the synced phase changes). Drawn under the compass with the par time and your best for this map; the
    /// progression tracker banks <see cref="LastClearMs"/> as the run's time.
    /// </summary>
    public sealed class GauntletClock : MonoBehaviour
    {
        public static GauntletClock Current { get; private set; }

        private WaveManager _waves;
        private double _startedAt = -1.0;
        private double _stoppedAt = -1.0;

        /// <summary>Clear time of the last finished Gauntlet in milliseconds, or 0 if the last run wasn't cleared.</summary>
        public long LastClearMs { get; private set; }
        public bool Running => _startedAt >= 0.0 && _stoppedAt < 0.0;
        public double Elapsed => _startedAt < 0.0 ? 0.0 : (_stoppedAt >= 0.0 ? _stoppedAt : Time.timeAsDouble) - _startedAt;

        private void Awake() => Current = this;
        private void OnDestroy()
        {
            if (Current == this) Current = null;
            if (_waves != null) _waves.PhaseChanged -= OnPhase;
        }

        private void Update()
        {
            if (_waves == WaveManager.Current) return;
            if (_waves != null) _waves.PhaseChanged -= OnPhase;
            _waves = WaveManager.Current;
            if (_waves != null) _waves.PhaseChanged += OnPhase;
        }

        private void OnPhase(RunPhase from, RunPhase to)
        {
            if (from == RunPhase.NotStarted || to == RunPhase.NotStarted)
            {
                _startedAt = _stoppedAt = -1.0;
                if (from == RunPhase.NotStarted) LastClearMs = 0;
            }
            if (!_waves.IsGauntlet) return;
            if (to == RunPhase.Surge && _waves.Surge == 1 && _startedAt < 0.0) _startedAt = Time.timeAsDouble;
            if ((to == RunPhase.Complete || to == RunPhase.Failed) && Running)
            {
                _stoppedAt = Time.timeAsDouble;
                LastClearMs = to == RunPhase.Complete ? (long)System.Math.Round(Elapsed * 1000.0) : 0;
            }
        }

        /// <summary>The run's clear time for banking: the stopped time, or the time so far if the win is being banked first.</summary>
        public long ClearMsFor(bool won)
        {
            if (!won || _startedAt < 0.0) return 0;
            return _stoppedAt >= 0.0 ? LastClearMs : (long)System.Math.Round(Elapsed * 1000.0);
        }

        /// <summary>Best time on the current map's environment (ms, 0 = none).</summary>
        public static long BestMs()
        {
            ProfileService svc = ProfileService.Current;
            MapSession map = MapSession.Current;
            if (svc == null || svc.Data == null || map == null) return 0;
            return svc.Data.Stat(GauntletRecords.BestKey(EnvironmentCatalog.IndexOf(map.Seed)));
        }

        private void OnGUI()
        {
            if (_waves == null || !_waves.IsGauntlet || _waves.Phase == RunPhase.NotStarted) return;
            HudKit.Begin();
            double t = Elapsed;
            bool done = _stoppedAt >= 0.0;
            long best = BestMs();
            var box = new Rect(HudKit.W * 0.5f - 160f, 64f, 320f, 64f);
            HudKit.Panel(box, 0.9f);
            string info = $"GAUNTLET  ·  SURGE {Mathf.Clamp(_waves.Surge, 1, GauntletRules.Surges)}/{GauntletRules.Surges}  ·  PAR {GauntletRules.Clock(GauntletRules.ParSeconds)}" +
                          (best > 0 ? $"  ·  BEST {GauntletRules.Clock(best / 1000.0)}" : string.Empty);
            HudKit.Label(new Rect(box.x, box.y + 4f, box.width, 18f), info, 12, HudKit.TextDim, TextAnchor.MiddleCenter, head: true);
            Color c = t > GauntletRules.ParSeconds ? HudKit.Warn : done && _waves.Phase == RunPhase.Complete ? HudKit.Good : Color.white;
            string clock = _startedAt < 0.0 ? GauntletRules.Clock(0.0) : GauntletRules.Clock(t);
            HudKit.Label(new Rect(box.x, box.y + 20f, box.width, 40f), clock, 34, c, TextAnchor.MiddleCenter, head: true);
            HudKit.End();
        }
    }
}
