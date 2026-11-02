using UnityEngine;

namespace Deadfall.Classes
{
    /// <summary>Numbers for one passive. Like abilities, the behaviour is code picked by id.</summary>
    [CreateAssetMenu(menuName = "Deadfall/Passive", fileName = "Passive_New")]
    public sealed class PassiveDefinition : ScriptableObject
    {
        [SerializeField] private string _contentId = "passive.new";
        [SerializeField] private string _displayName = "New Passive";
        [TextArea] [SerializeField] private string _description = "";
        [SerializeField] private string _behaviourId = "";
        [SerializeField] private float _magnitude;
        [SerializeField] private float _secondary;
        [SerializeField] private float _tertiary;

        public string ContentId => _contentId;
        public string DisplayName => _displayName;
        public string Description => _description;
        public string BehaviourId => _behaviourId;
        public float Magnitude => _magnitude;
        public float Secondary => _secondary;
        public float Tertiary => _tertiary;

        public static PassiveDefinition CreateRuntime(string contentId, string displayName, string behaviourId, string description,
            float magnitude, float secondary = 0f, float tertiary = 0f)
        {
            var p = CreateInstance<PassiveDefinition>();
            p.name = displayName;
            p._contentId = contentId;
            p._displayName = displayName;
            p._behaviourId = behaviourId;
            p._description = description;
            p._magnitude = magnitude;
            p._secondary = secondary;
            p._tertiary = tertiary;
            return p;
        }
    }
}
