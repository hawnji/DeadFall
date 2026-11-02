using UnityEngine;

namespace Deadfall.Classes
{
    /// <summary>
    /// Numbers for one active ability. Behaviour comes from code, picked by <see cref="BehaviourId"/>; the numbers
    /// stay here so they can be tuned without touching code. What Magnitude, Secondary and Range mean depends on
    /// the ability and is listed in its tooltip text.
    /// </summary>
    [CreateAssetMenu(menuName = "Deadfall/Ability", fileName = "Ability_New")]
    public sealed class AbilityDefinition : ScriptableObject
    {
        [SerializeField] private string _contentId = "ability.new";
        [SerializeField] private string _displayName = "New Ability";
        [TextArea] [SerializeField] private string _description = "";
        [Tooltip("Which code runs this ability (see ClassBehaviours).")]
        [SerializeField] private string _behaviourId = "";
        [SerializeField] private float _cooldownSeconds = 30f;
        [Tooltip("0 for instant abilities.")]
        [SerializeField] private float _durationSeconds;
        [SerializeField] private float _magnitude = 1f;
        [SerializeField] private float _secondary;
        [SerializeField] private float _range;

        public string ContentId => _contentId;
        public string DisplayName => _displayName;
        public string Description => _description;
        public string BehaviourId => _behaviourId;
        public float CooldownSeconds => _cooldownSeconds;
        public float DurationSeconds => _durationSeconds;
        public float Magnitude => _magnitude;
        public float Secondary => _secondary;
        public float Range => _range;

        public static AbilityDefinition CreateRuntime(string contentId, string displayName, string behaviourId, string description,
            float cooldown, float duration, float magnitude, float secondary = 0f, float range = 0f)
        {
            var a = CreateInstance<AbilityDefinition>();
            a.name = displayName;
            a._contentId = contentId;
            a._displayName = displayName;
            a._behaviourId = behaviourId;
            a._description = description;
            a._cooldownSeconds = cooldown;
            a._durationSeconds = duration;
            a._magnitude = magnitude;
            a._secondary = secondary;
            a._range = range;
            return a;
        }
    }
}
