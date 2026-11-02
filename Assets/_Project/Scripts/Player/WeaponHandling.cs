using Deadfall.Combat;

namespace Deadfall.Player
{
    /// <summary>
    /// Multipliers a class (or later an attachment or Field Mod) applies to how a weapon handles. 1 means
    /// unchanged. Built fresh each frame by <see cref="IWeaponHandlingSource"/> for the weapon in hand.
    /// </summary>
    public struct WeaponHandling
    {
        public float FireRate;
        public float ReloadSpeed;
        public float EquipSpeed;
        public float Recoil;
        /// <summary>Outgoing damage for bullets.</summary>
        public float Damage;
        /// <summary>Outgoing damage for melee swings (quick melee and melee weapons).</summary>
        public float MeleeDamage;
        /// <summary>Reloads don't use reserve ammo.</summary>
        public bool FreeReloads;

        public static WeaponHandling Default => new WeaponHandling
        {
            FireRate = 1f, ReloadSpeed = 1f, EquipSpeed = 1f, Recoil = 1f, Damage = 1f, MeleeDamage = 1f,
        };
    }

    /// <summary>Supplies handling modifiers to <see cref="PlayerWeapons"/> (the player's class does this).</summary>
    public interface IWeaponHandlingSource
    {
        void ModifyHandling(WeaponDefinition weapon, ref WeaponHandling handling);
    }

    /// <summary>A one-off bonus for the next melee swing (Warrior Lunge strike).</summary>
    public struct MeleeBonus
    {
        public float DamageMultiplier;
        public bool Stagger;
        /// <summary>Arc width multiplier (cleave).</summary>
        public float ArcMultiplier;
        /// <summary>Extra targets the swing may hit.</summary>
        public int ExtraTargets;
        /// <summary>Who set it, so one effect never clears another's bonus.</summary>
        public string Tag;
    }
}
