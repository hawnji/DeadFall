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
    }

    public sealed class LocalProjectileService : IProjectileService
    {
        public void Launch(GameObject owner, string weaponId, Vector3 origin, Vector3 velocity, float damage, float radius) =>
            Grenade.Spawn(origin, velocity, damage, radius, 1f, owner, weaponId, authoritative: true);
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
