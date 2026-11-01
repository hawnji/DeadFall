namespace Deadfall.Rules.Combat
{
    public enum WeaponCategory
    {
        Pistol,
        SubmachineGun,
        AssaultRifle,
        Shotgun,
        LightMachineGun,
        Heavy,
        Melee,
        Experimental,
    }

    public enum FireMode
    {
        SemiAuto,
        FullAuto,
    }

    public enum HitZone
    {
        Body,
        Head,
        Limb,
        /// <summary>Enemy-specific weak point with its own multiplier (e.g. a growth or fuel tank).</summary>
        WeakPoint,
    }
}
