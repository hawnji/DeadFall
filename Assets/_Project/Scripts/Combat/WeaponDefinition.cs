using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Combat
{
    public enum WeaponKind
    {
        Firearm,
        Melee,
    }

    /// <summary>
    /// Data asset for one weapon. Gameplay code reads stats from here; nothing is hardcoded per weapon.
    /// Create via Assets → Create → Deadfall → Weapon.
    /// </summary>
    [CreateAssetMenu(menuName = "Deadfall/Weapon", fileName = "Weapon_New")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        [Tooltip("Stable id used in saves and network messages. Never change after release.")]
        [SerializeField] private string _contentId = "weapon.new";
        [SerializeField] private string _displayName = "New Weapon";
        [SerializeField] private WeaponKind _kind = WeaponKind.Firearm;
        [SerializeField] private WeaponStats _firearm = new WeaponStats();
        [SerializeField] private MeleeStats _melee = new MeleeStats();
        [Tooltip("Placeholder viewmodel tint until real models exist.")]
        [SerializeField] private Color _placeholderColor = new Color(0.25f, 0.27f, 0.3f);

        public string ContentId => _contentId;
        public string DisplayName => _displayName;
        public WeaponKind Kind => _kind;
        public WeaponStats Firearm => _firearm;
        public MeleeStats Melee => _melee;
        public Color PlaceholderColor => _placeholderColor;
        public WeaponCategory Category => _kind == WeaponKind.Melee ? WeaponCategory.Melee : _firearm.Category;

        /// <summary>Builds an in-memory definition (sandbox and tests) without an asset file.</summary>
        public static WeaponDefinition CreateRuntime(string contentId, string displayName, WeaponKind kind,
            WeaponStats firearm, MeleeStats melee, Color color)
        {
            var def = CreateInstance<WeaponDefinition>();
            def.name = displayName;
            def._contentId = contentId;
            def._displayName = displayName;
            def._kind = kind;
            def._firearm = firearm ?? new WeaponStats();
            def._melee = melee ?? new MeleeStats();
            def._placeholderColor = color;
            return def;
        }
    }
}
