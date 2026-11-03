using System;
using System.Collections.Generic;
using Deadfall.Rules.Common;

namespace Deadfall.Rules.Encounters
{
    public enum ObjectiveKind : byte
    {
        /// <summary>Restart every generator in the area (hold position at each for a few seconds).</summary>
        RestorePower,
        /// <summary>Collect sample cases scattered around the open sectors.</summary>
        RecoverSamples,
        /// <summary>Destroy an infestation nest that keeps birthing Creepers.</summary>
        DestroyNest,
        /// <summary>Download a data core at a terminal (hold position).</summary>
        RetrieveDataCore,
        /// <summary>Find a keycard, then open a security locker with it.</summary>
        Keycard,
        /// <summary>Hack a terminal to lift a lockdown (Emergency Lockdown event).</summary>
        LiftLockdown,
        /// <summary>Hold the extraction zone until the timer runs out (Operations finale).</summary>
        Extraction,
        /// <summary>Hold the uplink terminal for a long upload while Husks rush it (M12). Appended: wire order.</summary>
        DefendUplink,
        /// <summary>Destroy two smaller hive nests in different rooms (M12).</summary>
        PurgeHives,
    }

    public enum ObjectiveStatus : byte { Active, Complete, Failed }

    /// <summary>One objective's state. Plain data so the host can send it and tests can drive it.</summary>
    public sealed class Objective
    {
        public byte Id;
        public ObjectiveKind Kind;
        public bool Required;
        public int Progress;
        public int Target = 1;
        public ObjectiveStatus Status;
        public int RewardCredits;
        /// <summary>Survival side objectives expire after this surge (0 = never).</summary>
        public int ExpiresAfterSurge;

        public bool IsDone => Status != ObjectiveStatus.Active;

        public string Title => ObjectiveRules.Title(Kind);

        public string Describe()
        {
            string count = Target > 1 ? $" {Progress}/{Target}" : string.Empty;
            string tag = Required ? string.Empty : " (optional)";
            return Title + count + tag;
        }
    }

    /// <summary>
    /// Holds the run's objectives and moves them forward. Engine-free; the host's ObjectiveDirector owns one and
    /// sends its state to clients.
    /// </summary>
    public sealed class ObjectiveTracker
    {
        private readonly List<Objective> _list = new List<Objective>();
        private byte _nextId = 1;

        public IReadOnlyList<Objective> All => _list;

        /// <summary>Raised when an objective completes or fails.</summary>
        public event Action<Objective> Finished;

        public Objective Add(ObjectiveKind kind, int target, bool required, int reward, int expiresAfterSurge = 0)
        {
            var o = new Objective
            {
                Id = _nextId++,
                Kind = kind,
                Target = Math.Max(1, target),
                Required = required,
                RewardCredits = Math.Max(0, reward),
                ExpiresAfterSurge = expiresAfterSurge,
            };
            if (_nextId == 0) _nextId = 1;
            _list.Add(o);
            return o;
        }

        public Objective Find(byte id)
        {
            for (int i = 0; i < _list.Count; i++) if (_list[i].Id == id) return _list[i];
            return null;
        }

        public Objective FindActive(ObjectiveKind kind)
        {
            for (int i = 0; i < _list.Count; i++)
                if (_list[i].Kind == kind && _list[i].Status == ObjectiveStatus.Active) return _list[i];
            return null;
        }

        /// <summary>Adds progress; completes it at the target. Returns true if this call completed it.</summary>
        public bool Advance(byte id, int amount = 1)
        {
            Objective o = Find(id);
            if (o == null || o.IsDone || amount <= 0) return false;
            o.Progress = Math.Min(o.Target, o.Progress + amount);
            if (o.Progress < o.Target) return false;
            o.Status = ObjectiveStatus.Complete;
            Finished?.Invoke(o);
            return true;
        }

        public void Fail(byte id)
        {
            Objective o = Find(id);
            if (o == null || o.IsDone) return;
            o.Status = ObjectiveStatus.Failed;
            Finished?.Invoke(o);
        }

        /// <summary>Fails side objectives whose deadline surge has been cleared. Returns how many expired.</summary>
        public int ExpireAfter(int clearedSurge)
        {
            int n = 0;
            for (int i = 0; i < _list.Count; i++)
            {
                Objective o = _list[i];
                if (o.IsDone || o.ExpiresAfterSurge <= 0 || clearedSurge < o.ExpiresAfterSurge) continue;
                Fail(o.Id);
                n++;
            }
            return n;
        }

        /// <summary>True when every required objective (other than <paramref name="except"/>) is complete.</summary>
        public bool RequiredComplete(ObjectiveKind except)
        {
            bool any = false;
            for (int i = 0; i < _list.Count; i++)
            {
                Objective o = _list[i];
                if (!o.Required || o.Kind == except) continue;
                any = true;
                if (o.Status != ObjectiveStatus.Complete) return false;
            }
            return any;
        }

        /// <summary>Drops finished objectives (keeps the list short on the HUD and the wire).</summary>
        public void PruneFinished()
        {
            _list.RemoveAll(o => o.IsDone);
        }

        public void Clear()
        {
            _list.Clear();
            _nextId = 1;
        }

        /// <summary>Replaces the list with the host's copy (clients).</summary>
        public void Replace(IEnumerable<Objective> objectives)
        {
            _list.Clear();
            _list.AddRange(objectives);
        }
    }

    /// <summary>
    /// "Stand here for N seconds" progress (generators, terminals). Moves while someone is in range, drains at half
    /// speed when nobody is, so leaving to fight doesn't wipe it.
    /// </summary>
    public sealed class ChannelProgress
    {
        public const float DrainFraction = 0.5f;
        private readonly float _seconds;

        public ChannelProgress(float seconds) => _seconds = Math.Max(0.1f, seconds);

        public float Value01 { get; private set; }
        public bool Started { get; private set; }
        public bool Complete => Value01 >= 1f;

        public void Start() => Started = true;

        /// <summary>Advances; returns true on the tick it completes.</summary>
        public bool Tick(float dt, bool someoneInRange)
        {
            if (!Started || Complete || dt <= 0f) return false;
            float rate = dt / _seconds;
            Value01 = someoneInRange ? Math.Min(1f, Value01 + rate) : Math.Max(0f, Value01 - rate * DrainFraction);
            return Complete;
        }

        public void Set(float value01, bool started)
        {
            Value01 = Math.Max(0f, Math.Min(1f, value01));
            Started = started;
        }
    }

    /// <summary>
    /// The extraction countdown: runs only while at least one living player is in the zone, pauses (doesn't reset)
    /// when the zone is empty (GAME_DESIGN §3.2: win when the timer completes with someone alive inside).
    /// </summary>
    public sealed class HoldTimer
    {
        public HoldTimer(float seconds) => Duration = Math.Max(1f, seconds);

        public float Duration { get; }
        public float Elapsed { get; private set; }
        public float Remaining => Math.Max(0f, Duration - Elapsed);
        public bool Complete => Elapsed >= Duration;
        public float Value01 => Elapsed / Duration;

        public bool Tick(float dt, bool aliveInside)
        {
            if (Complete || !aliveInside || dt <= 0f) return false;
            Elapsed = Math.Min(Duration, Elapsed + dt);
            return Complete;
        }

        public void Set(float elapsed) => Elapsed = Math.Max(0f, Math.Min(Duration, elapsed));
    }

    /// <summary>One step of an Operations mission template.</summary>
    public readonly struct MissionStep
    {
        public readonly ObjectiveKind Kind;
        public readonly bool Required;

        public MissionStep(ObjectiveKind kind, bool required)
        {
            Kind = kind;
            Required = required;
        }
    }

    /// <summary>An Operations mission (GAME_DESIGN §3.2): objectives that run together, then a timed extraction.</summary>
    public sealed class MissionTemplate
    {
        public string Id;
        public string Name;
        public string Briefing;
        public MissionStep[] Steps;
        public float ExtractionSeconds = 150f;
        /// <summary>Lull length between surges during the mission (surges come on a timer, not on readiness).</summary>
        public float LullSeconds = 35f;
        /// <summary>Lull length once extraction starts: almost constant pressure.</summary>
        public float ExtractionLullSeconds = 4f;
        public int CompletionBonus = 1500;
    }

    /// <summary>Numbers and rules for objectives (T-46). Pure functions so they can be tested and tuned.</summary>
    public static class ObjectiveRules
    {
        public const int Generators = 3;
        public const int Samples = 3;
        public const float GeneratorSeconds = 5f;
        public const float TerminalSeconds = 8f;
        public const float LockdownHackSeconds = 6f;
        public const float ChannelRange = 3.5f;
        public const float NestHealth = 1500f;
        public const float NestSpawnSeconds = 6f;
        public const int NestMaxAlive = 5;
        public const float UplinkSeconds = 30f;
        /// <summary>While the uplink uploads, Husks rush the terminal this often.</summary>
        public const float UplinkRushSeconds = 7f;
        public const int UplinkRushCount = 2;
        public const int Hives = 2;
        /// <summary>Each Purge Hives nest has this share of a full nest's health.</summary>
        public const float HiveHealthFraction = 0.6f;
        /// <summary>The last objective kind, for validating synced state.</summary>
        public const ObjectiveKind LastKind = ObjectiveKind.PurgeHives;
        /// <summary>Side objectives stay up for this many surges.</summary>
        public const int SideObjectiveSurges = 2;

        public static readonly MissionTemplate ColdStart = new MissionTemplate
        {
            Id = "mission.cold_start",
            Name = "Operation Cold Start",
            Briefing = "Restore power to the complex, pull the research data core, then hold the airlock until the lift arrives.",
            Steps = new[]
            {
                new MissionStep(ObjectiveKind.RestorePower, true),
                new MissionStep(ObjectiveKind.RetrieveDataCore, true),
                new MissionStep(ObjectiveKind.DestroyNest, false),
                new MissionStep(ObjectiveKind.RecoverSamples, false),
            },
        };

        /// <summary>Harrow Line (M12): get the depot's power back, find the line keycard, clear the nest in the pump rooms, then hold the platform for the last train.</summary>
        public static readonly MissionTemplate LastTrain = new MissionTemplate
        {
            Id = "mission.last_train",
            Name = "Operation Last Train",
            Briefing = "Bring the depot's power back, take the line keycard to the signal locker, then hold the lift until the last train arrives.",
            Steps = new[]
            {
                new MissionStep(ObjectiveKind.RestorePower, true),
                new MissionStep(ObjectiveKind.Keycard, true),
                new MissionStep(ObjectiveKind.RetrieveDataCore, false),
                new MissionStep(ObjectiveKind.DestroyNest, false),
            },
            ExtractionSeconds = 170f,
            LullSeconds = 32f,
            CompletionBonus = 1700,
        };

        public static readonly MissionTemplate[] Missions = { ColdStart, LastTrain };

        public static MissionTemplate MissionById(string id)
        {
            foreach (MissionTemplate m in Missions) if (m.Id == id) return m;
            return ColdStart;
        }

        public static string Title(ObjectiveKind k) => k switch
        {
            ObjectiveKind.RestorePower => "Restart the generators",
            ObjectiveKind.RecoverSamples => "Recover sample cases",
            ObjectiveKind.DestroyNest => "Destroy the nest",
            ObjectiveKind.RetrieveDataCore => "Download the data core",
            ObjectiveKind.Keycard => "Find the keycard and open the security locker",
            ObjectiveKind.LiftLockdown => "Hack the terminal to lift the lockdown",
            ObjectiveKind.Extraction => "Hold the extraction zone",
            ObjectiveKind.DefendUplink => "Defend the uplink while it uploads",
            ObjectiveKind.PurgeHives => "Purge the hive nests",
            _ => k.ToString(),
        };

        public static int Target(ObjectiveKind k) => k switch
        {
            ObjectiveKind.RestorePower => Generators,
            ObjectiveKind.RecoverSamples => Samples,
            ObjectiveKind.Keycard => 2, // pick up, then use
            ObjectiveKind.PurgeHives => Hives,
            _ => 1,
        };

        public static int Reward(ObjectiveKind k) => k switch
        {
            ObjectiveKind.RestorePower => 400,
            ObjectiveKind.RecoverSamples => 350,
            ObjectiveKind.DestroyNest => 500,
            ObjectiveKind.RetrieveDataCore => 450,
            ObjectiveKind.Keycard => 300,
            ObjectiveKind.LiftLockdown => 200,
            ObjectiveKind.DefendUplink => 600,
            ObjectiveKind.PurgeHives => 700,
            _ => 0,
        };

        /// <summary>Survival offers an optional side objective in the lull before surges 3, 7, 11... (GAME_DESIGN §3.1).</summary>
        public static bool OfferSideObjective(int upcomingSurge) => upcomingSurge >= 3 && upcomingSurge % 4 == 3;

        private static readonly ObjectiveKind[] SidePool =
            { ObjectiveKind.RestorePower, ObjectiveKind.RecoverSamples, ObjectiveKind.DestroyNest, ObjectiveKind.RetrieveDataCore, ObjectiveKind.Keycard,
              ObjectiveKind.DefendUplink, ObjectiveKind.PurgeHives };

        /// <summary>Picks a side objective, never the same kind twice in a row.</summary>
        public static ObjectiveKind PickSide(DeterministicRandom rng, ObjectiveKind? last)
        {
            for (int tries = 0; tries < 8; tries++)
            {
                ObjectiveKind k = SidePool[rng.Range(0, SidePool.Length)];
                if (last.HasValue && k == last.Value) continue;
                return k;
            }
            return last == ObjectiveKind.RestorePower ? ObjectiveKind.RecoverSamples : ObjectiveKind.RestorePower;
        }
    }
}
