using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Combat
{
    /// <summary>A resolved hit, produced by the hit resolver after validation and the damage formula.</summary>
    public struct DamageInfo
    {
        public float Amount;
        public HitZone Zone;
        public bool IsCritical;
        public Vector3 Point;
        public Vector3 Direction;
        public GameObject Source;
        public string WeaponId;
    }

    /// <summary>Anything that can take damage: players, enemies, structures, training dummies.</summary>
    public interface IDamageable
    {
        bool IsAlive { get; }
        /// <summary>Target-side damage reduction 0..1 (armor), applied by the damage formula.</summary>
        float DamageReduction { get; }
        /// <summary>Applies already-calculated damage. Returns the amount actually removed.</summary>
        float ApplyDamage(in DamageInfo info);
    }

    /// <summary>Raised on the attacker's machine after a hit is applied, for hit markers and stats.</summary>
    public struct HitConfirmed
    {
        public GameObject Attacker;
        public float Amount;
        public bool IsCritical;
        public bool Killed;
        public Vector3 Point;
    }

    /// <summary>Raised when anything with an IDamageable dies.</summary>
    public struct DamageableKilled
    {
        public GameObject Victim;
        public GameObject Killer;
    }
}
