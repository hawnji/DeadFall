using Deadfall.Core;
using UnityEngine;

namespace Deadfall.Combat
{
    /// <summary>
    /// Launches explosive projectiles for weapons (Mule Launcher, later others). Offline the local service spawns a
    /// real grenade; in co-op a networked service replaces it so the host owns the damage (T-38).
    /// </summary>
    public interface IProjectileService
    {
        void Launch(GameObject owner, string weaponId, Vector3 origin, Vector3 velocity, float damage, float radius);
        /// <summary>A thrown frag grenade (<see cref="GrenadeRules"/>): fused, damage and radius fixed by the rules.</summary>
        void Throw(GameObject owner, Vector3 origin, Vector3 velocity);
    }

    /// <summary>Thrown frag grenades: everyone's numbers, so the host can check what clients claim.</summary>
    public static class GrenadeRules
    {
        public const string Id = "frag_grenade";
        public const float Damage = 180f;
        public const float Radius = 5.5f;
        public const float FuseSeconds = 2.2f;
        public const float ThrowSpeed = 15f;
        public const float UpwardSpeed = 3.5f;
        /// <summary>Grenades carried by default; Bandolier adds to it.</summary>
        public const int BaseCapacity = 2;
        public const float Cooldown = 0.9f;
        public const float SelfDamageMultiplier = 0.5f;
    }

    public sealed class LocalProjectileService : IProjectileService
    {
        public void Launch(GameObject owner, string weaponId, Vector3 origin, Vector3 velocity, float damage, float radius) =>
            Grenade.Spawn(origin, velocity, damage, radius, 1f, owner, weaponId, authoritative: true);

        public void Throw(GameObject owner, Vector3 origin, Vector3 velocity) =>
            Grenade.Spawn(origin, velocity, GrenadeRules.Damage, GrenadeRules.Radius, GrenadeRules.SelfDamageMultiplier, owner,
                GrenadeRules.Id, authoritative: true, GrenadeRules.FuseSeconds);
    }

    public static class ProjectileServices
    {
        public static IProjectileService Current
        {
            get
            {
                if (!ServiceRegistry.TryGet(out IProjectileService s))
                {
                    s = new LocalProjectileService();
                    ServiceRegistry.Register(s);
                }
                return s;
            }
        }
    }
}
