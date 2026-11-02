using System.Collections.Generic;
using UnityEngine;

namespace Deadfall.Combat
{
    /// <summary>
    /// A damageable world object that has the same id on every peer (map pieces built from the seed, host-spawned
    /// structures). Client hits on it are forwarded to the host by id, the host applies them, and the object's
    /// own system syncs the result (broken, destroyed, health).
    /// </summary>
    public interface INetTarget : IDamageable
    {
        ushort NetTargetId { get; }
        /// <summary>Where the host checks claimed hit points against (with <see cref="NetTargetRadius"/> slack).</summary>
        Vector3 NetTargetCenter { get; }
        float NetTargetRadius { get; }
    }

    /// <summary>
    /// Id registry for <see cref="INetTarget"/>s. Id ranges: 0x1000.. map pieces (secret panels), 0x2000.. objectives
    /// (M8), 0x4000.. structures (M9). Ids only need to be unique among live targets.
    /// </summary>
    public static class NetTargets
    {
        public const ushort MapBase = 0x1000;
        public const ushort ObjectiveBase = 0x2000;
        public const ushort StructureBase = 0x4000;

        private static readonly Dictionary<ushort, INetTarget> ById = new Dictionary<ushort, INetTarget>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ById.Clear();

        public static void Register(INetTarget target)
        {
            if (target != null) ById[target.NetTargetId] = target;
        }

        public static void Unregister(INetTarget target)
        {
            if (target != null && ById.TryGetValue(target.NetTargetId, out INetTarget t) && ReferenceEquals(t, target))
                ById.Remove(target.NetTargetId);
        }

        public static INetTarget Find(ushort id) => ById.TryGetValue(id, out INetTarget t) && !(t is Object o && o == null) ? t : null;
    }
}
