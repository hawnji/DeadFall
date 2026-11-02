using System.Collections.Generic;
using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Classes
{
    /// <summary>
    /// A playable class: its ability, passives, and the weapon categories it handles best. Any class can use any
    /// weapon (C-05); preferred ones just reload, swap and settle faster.
    /// </summary>
    [CreateAssetMenu(menuName = "Deadfall/Class", fileName = "Class_New")]
    public sealed class ClassDefinition : ScriptableObject
    {
        [Tooltip("Stable id used in saves, doctrines and network messages.")]
        [SerializeField] private string _contentId = "class.new";
        [SerializeField] private string _displayName = "New Class";
        [SerializeField] private string _role = "";
        [SerializeField] private Color _color = Color.white;
        [SerializeField] private List<WeaponCategory> _preferredCategories = new List<WeaponCategory>();
        [Header("Preferred weapon handling")]
        [SerializeField] private float _preferredReloadSpeed = 1.15f;
        [SerializeField] private float _preferredSwapSpeed = 1.25f;
        [SerializeField] private float _preferredRecoil = 0.85f;
        [Header("Kit")]
        [SerializeField] private AbilityDefinition _ability;
        [SerializeField] private List<PassiveDefinition> _passives = new List<PassiveDefinition>();

        public string ContentId => _contentId;
        public string DisplayName => _displayName;
        public string Role => _role;
        public Color Color => _color;
        public IReadOnlyList<WeaponCategory> PreferredCategories => _preferredCategories;
        public float PreferredReloadSpeed => _preferredReloadSpeed;
        public float PreferredSwapSpeed => _preferredSwapSpeed;
        public float PreferredRecoil => _preferredRecoil;
        public AbilityDefinition Ability => _ability;
        public IReadOnlyList<PassiveDefinition> Passives => _passives;

        public bool Prefers(WeaponCategory category) => _preferredCategories.Contains(category);

        public static ClassDefinition CreateRuntime(string contentId, string displayName, string role, Color color,
            WeaponCategory[] preferred, AbilityDefinition ability, params PassiveDefinition[] passives)
        {
            var c = CreateInstance<ClassDefinition>();
            c.name = displayName;
            c._contentId = contentId;
            c._displayName = displayName;
            c._role = role;
            c._color = color;
            c._preferredCategories = new List<WeaponCategory>(preferred);
            c._ability = ability;
            c._passives = new List<PassiveDefinition>(passives);
            return c;
        }
    }
}
