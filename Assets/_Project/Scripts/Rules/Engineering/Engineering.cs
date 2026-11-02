using System;
using System.Collections.Generic;

namespace Deadfall.Rules.Engineering
{
    /// <summary>Engineering structures (GAME_DESIGN §11). The byte value goes over the network: append only.</summary>
    public enum StructureKind : byte
    {
        Sentry,
        Drone,
        ArcFence,
        Barricade,
    }

    public enum DroneMode : byte { Attack, Repair }

    /// <summary>Numbers for one tier of a structure.</summary>
    public readonly struct StructureTier
    {
        public readonly float Health;
        /// <summary>Sentry: damage per shot. Drone: damage per second. Arc Fence: damage per second while zapping.</summary>
        public readonly float Damage;
        /// <summary>Sentry: shots per second. Drone: repair HP per second. Arc Fence: slow fraction (0..1).</summary>
        public readonly float Rate;
        public readonly float Range;
        /// <summary>Sentry: rounds. Arc Fence: seconds of zapping. 0 = no supply.</summary>
        public readonly float Supply;
        /// <summary>Scrap to reach this tier from the one below (tier 1: build cost).</summary>
        public readonly int Cost;

        public StructureTier(float health, float damage, float rate, float range, float supply, int cost)
        {
            Health = health;
            Damage = damage;
            Rate = rate;
            Range = range;
            Supply = supply;
            Cost = cost;
        }
    }

    /// <summary>Data for one structure type: tiers, who can build it, caps.</summary>
    public sealed class StructureDef
    {
        public StructureKind Kind;
        public string Name;
        public string Description;
        public StructureTier[] Tiers;
        public bool EngineerOnly = true;
        /// <summary>Most of this kind one Engineer can have up.</summary>
        public int EngineerCap = 2;
        /// <summary>Most of this kind other classes can have up (only for kinds they can build).</summary>
        public int OtherCap;
        /// <summary>Scrap to refill supply (ammo, charge) to full.</summary>
        public int RefillCost;

        public int MaxTier => Tiers.Length;
        public StructureTier Tier(int tier) => Tiers[Math.Max(1, Math.Min(MaxTier, tier)) - 1];
    }

    /// <summary>The four prototype structures (T-49). Numbers are placeholders for playtests.</summary>
    public static class StructureCatalog
    {
        public static readonly StructureDef Sentry = new StructureDef
        {
            Kind = StructureKind.Sentry, Name = "Sentry", Description = "Auto-targets infected in range; ammo-limited, refill with Scrap",
            Tiers = new[]
            {
                new StructureTier(300f, 9f, 6f, 22f, 300f, 120),
                new StructureTier(420f, 12f, 7f, 25f, 400f, 100),
                new StructureTier(560f, 15f, 8f, 28f, 500f, 160),
            },
            EngineerCap = 2, RefillCost = 30,
        };

        public static readonly StructureDef Drone = new StructureDef
        {
            Kind = StructureKind.Drone, Name = "Drone", Description = "Follows you; attack mode shoots infected, repair mode mends structures",
            Tiers = new[]
            {
                new StructureTier(150f, 12f, 15f, 12f, 0f, 150),
                new StructureTier(200f, 18f, 22f, 13f, 0f, 120),
                new StructureTier(260f, 24f, 30f, 14f, 0f, 180),
            },
            EngineerCap = 1,
        };

        public static readonly StructureDef ArcFence = new StructureDef
        {
            Kind = StructureKind.ArcFence, Name = "Arc Fence", Description = "Slows and shocks infected crossing it; drains charge while zapping",
            Tiers = new[]
            {
                new StructureTier(250f, 20f, 0.5f, 3.2f, 30f, 90),
                new StructureTier(350f, 28f, 0.55f, 3.2f, 40f, 80),
                new StructureTier(450f, 36f, 0.6f, 3.2f, 50f, 130),
            },
            EngineerCap = 2, RefillCost = 25,
        };

        public static readonly StructureDef Barricade = new StructureDef
        {
            Kind = StructureKind.Barricade, Name = "Barricade", Description = "Blocks a path until the infected tear it down",
            Tiers = new[]
            {
                new StructureTier(500f, 0f, 0f, 3f, 0f, 40),
                new StructureTier(800f, 0f, 0f, 3f, 0f, 50),
                new StructureTier(1100f, 0f, 0f, 3f, 0f, 80),
            },
            EngineerOnly = false, EngineerCap = 4, OtherCap = 2,
        };

        /// <summary>Wire order: index = (byte)StructureKind.</summary>
        public static readonly StructureDef[] All = { Sentry, Drone, ArcFence, Barricade };

        public static StructureDef Of(StructureKind kind) => (int)kind < All.Length ? All[(int)kind] : null;
    }

    /// <summary>Scrap: the squad's shared construction resource (D-13).</summary>
    public static class ScrapRules
    {
        public const int StartingScrap = 60;
        public const float PerThreat = 3f;
        /// <summary>Engineer kills earn +50% Scrap (GAME_DESIGN §5).</summary>
        public const float EngineerBonus = 1.5f;
        public const int PerSurgeCleared = 20;
        public const int PerObjective = 40;
        /// <summary>Non-Engineers pay this much more for what they can build.</summary>
        public const float OtherClassCostMultiplier = 1.5f;
        public const float DismantleRefund = 0.5f;
        /// <summary>The squad can't hold more than this (keeps late runs from turning into fortresses).</summary>
        public const int MaxScrap = 2000;

        public static int KillScrap(float threatCost, bool engineer) =>
            Math.Max(1, (int)Math.Round(Math.Max(0f, threatCost) * PerThreat * (engineer ? EngineerBonus : 1f), MidpointRounding.AwayFromZero));

        public static int BuildCost(StructureDef def, bool engineer) =>
            def == null ? 0 : (int)Math.Ceiling(def.Tier(1).Cost * (engineer ? 1f : OtherClassCostMultiplier));

        /// <summary>Scrap to go from <paramref name="tier"/> to the next, or -1 at max.</summary>
        public static int UpgradeCost(StructureDef def, int tier) => def == null || tier >= def.MaxTier ? -1 : def.Tier(tier + 1).Cost;

        /// <summary>Everything paid into a structure up to <paramref name="tier"/> (for the dismantle refund).</summary>
        public static int Invested(StructureDef def, int tier, bool engineer)
        {
            int total = BuildCost(def, engineer);
            for (int t = 2; t <= Math.Min(tier, def.MaxTier); t++) total += def.Tier(t).Cost;
            return total;
        }

        public static int Refund(StructureDef def, int tier, bool engineer) => (int)Math.Floor(Invested(def, tier, engineer) * DismantleRefund);
    }

    /// <summary>Repairs: each use restores a chunk of health for Scrap; Engineers repair more for less.</summary>
    public static class RepairRules
    {
        public const float EngineerChunk = 0.4f;
        public const float OtherChunk = 0.25f;
        public const float EngineerHpPerScrap = 20f;
        public const float OtherHpPerScrap = 12f;

        /// <summary>Health restored by one repair use (never more than missing).</summary>
        public static float Amount(float health, float maxHealth, bool engineer)
        {
            float missing = Math.Max(0f, maxHealth - health);
            return Math.Min(missing, maxHealth * (engineer ? EngineerChunk : OtherChunk));
        }

        public static int Cost(float amount, bool engineer) =>
            amount <= 0f ? 0 : (int)Math.Ceiling(amount / (engineer ? EngineerHpPerScrap : OtherHpPerScrap));
    }

    public enum PlacementError : byte
    {
        Ok,
        NotEnoughScrap,
        ClassCannotBuild,
        PlayerCap,
        SquadCap,
        TooSteep,
        Blocked,
        TooFar,
        NearEnemySpawn,
        NearObjective,
        NearDoor,
        NearPlayerSpawn,
        OffNavMesh,
    }

    /// <summary>Everything the validator needs, measured by the engine.</summary>
    public struct PlacementQuery
    {
        public StructureKind Kind;
        public bool Engineer;
        /// <summary>Building is free outside a run (sandbox testing).</summary>
        public bool Free;
        public int Scrap;
        public int OwnedOfKind;
        public int SquadTotal;
        public float GroundAngle;
        public bool Clear;
        public float DistanceFromBuilder;
        public float NearestEnemySpawn;
        public float NearestObjective;
        public float NearestDoor;
        public float NearestPlayerSpawn;
        public bool OnNavMesh;
    }

    /// <summary>The placement validator (GAME_DESIGN §11). Checks run cheapest and most explainable first.</summary>
    public static class PlacementRules
    {
        public const int SquadCap = 16;
        public const float MaxGroundAngle = 25f;
        public const float MaxBuildDistance = 6f;
        public const float MinEnemySpawnDistance = 4f;
        public const float MinObjectiveDistance = 2.5f;
        public const float MinDoorDistance = 2f;
        public const float MinPlayerSpawnDistance = 3f;

        public static PlacementError Validate(in PlacementQuery q)
        {
            StructureDef def = StructureCatalog.Of(q.Kind);
            if (def == null) return PlacementError.ClassCannotBuild;
            if (def.EngineerOnly && !q.Engineer) return PlacementError.ClassCannotBuild;
            int cap = q.Engineer ? def.EngineerCap : def.OtherCap;
            if (q.OwnedOfKind >= cap) return PlacementError.PlayerCap;
            if (q.SquadTotal >= SquadCap) return PlacementError.SquadCap;
            if (!q.Free && q.Scrap < ScrapRules.BuildCost(def, q.Engineer)) return PlacementError.NotEnoughScrap;
            if (q.Kind == StructureKind.Drone) return PlacementError.Ok; // flies beside its owner; no ground checks
            if (q.DistanceFromBuilder > MaxBuildDistance) return PlacementError.TooFar;
            if (q.GroundAngle > MaxGroundAngle) return PlacementError.TooSteep;
            if (!q.OnNavMesh) return PlacementError.OffNavMesh;
            if (!q.Clear) return PlacementError.Blocked;
            if (q.NearestEnemySpawn < MinEnemySpawnDistance) return PlacementError.NearEnemySpawn;
            if (q.NearestObjective < MinObjectiveDistance) return PlacementError.NearObjective;
            if (q.NearestDoor < MinDoorDistance) return PlacementError.NearDoor;
            if (q.NearestPlayerSpawn < MinPlayerSpawnDistance) return PlacementError.NearPlayerSpawn;
            return PlacementError.Ok;
        }

        public static string Explain(PlacementError e) => e switch
        {
            PlacementError.Ok => "Place",
            PlacementError.NotEnoughScrap => "Not enough Scrap",
            PlacementError.ClassCannotBuild => "Only Engineers can build this",
            PlacementError.PlayerCap => "You have the most of these you can",
            PlacementError.SquadCap => $"The squad has {SquadCap} structures up",
            PlacementError.TooSteep => "Ground too steep",
            PlacementError.Blocked => "Something is in the way",
            PlacementError.TooFar => "Too far away",
            PlacementError.NearEnemySpawn => "Too close to where infected arrive",
            PlacementError.NearObjective => "Too close to an objective",
            PlacementError.NearDoor => "Too close to a sector door",
            PlacementError.NearPlayerSpawn => "Too close to the deployment point",
            PlacementError.OffNavMesh => "Can't build here",
            _ => e.ToString(),
        };
    }

    /// <summary>Shared Scrap balance (host). Plain data so tests can drive it.</summary>
    public sealed class ScrapLedger
    {
        public int Amount { get; private set; }

        public void Set(int amount) => Amount = Math.Max(0, Math.Min(ScrapRules.MaxScrap, amount));
        public void Add(int amount) { if (amount > 0) Set(Amount + amount); }

        public bool TrySpend(int amount)
        {
            if (amount <= 0) return true;
            if (Amount < amount) return false;
            Amount -= amount;
            return true;
        }
    }
}
