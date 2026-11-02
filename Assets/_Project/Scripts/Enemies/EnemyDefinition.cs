using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Enemies
{
    /// <summary>Body shape used by the placeholder visuals and hurtboxes.</summary>
    public enum EnemyBodyType
    {
        /// <summary>Upright shambler: head and body.</summary>
        Standard,
        /// <summary>Lean runner, hunched forward.</summary>
        Lean,
        /// <summary>Wide brute with a front armor plate and a weak-point pack on its back.</summary>
        Plated,
    }

    /// <summary>
    /// Data asset for one enemy type. Gameplay reads stats from here; nothing is hardcoded per enemy.
    /// Create via Assets → Create → Deadfall → Enemy.
    /// </summary>
    [CreateAssetMenu(menuName = "Deadfall/Enemy", fileName = "Enemy_New")]
    public sealed class EnemyDefinition : ScriptableObject
    {
        [Tooltip("Stable id used in saves, wave data and network messages. Never change after release.")]
        [SerializeField] private string _contentId = "enemy.new";
        [SerializeField] private string _displayName = "New Enemy";
        [SerializeField] private EnemyStats _stats = new EnemyStats();
        [Tooltip("Cost against a wave's threat budget (M3).")]
        [SerializeField] private float _threatCost = 1f;

        [Header("Body")]
        [SerializeField] private EnemyBodyType _bodyType = EnemyBodyType.Standard;
        [SerializeField] private float _height = 1.8f;
        [SerializeField] private float _radius = 0.35f;
        [SerializeField] private Color _color = new Color(0.42f, 0.48f, 0.38f);
        [Tooltip("Multiplier for the front plate (Plated bodies). Below 1 reduces damage.")]
        [SerializeField] private float _armorMultiplier = 0.25f;
        [Tooltip("Multiplier for the back weak point (Plated bodies).")]
        [SerializeField] private float _weakPointMultiplier = 2.5f;

        public string ContentId => _contentId;
        public string DisplayName => _displayName;
        public EnemyStats Stats => _stats;
        public float ThreatCost => _threatCost;
        public EnemyBodyType BodyType => _bodyType;
        public float Height => _height;
        public float Radius => _radius;
        public Color Color => _color;
        public float ArmorMultiplier => _armorMultiplier;
        public float WeakPointMultiplier => _weakPointMultiplier;

        /// <summary>Builds an in-memory definition (prototype content and tests) without an asset file.</summary>
        public static EnemyDefinition CreateRuntime(string contentId, string displayName, EnemyStats stats, EnemyBodyType body,
            float height, float radius, Color color, float threatCost = 1f)
        {
            var def = CreateInstance<EnemyDefinition>();
            def.name = displayName;
            def._contentId = contentId;
            def._displayName = displayName;
            def._stats = stats ?? new EnemyStats();
            def._bodyType = body;
            def._height = height;
            def._radius = radius;
            def._color = color;
            def._threatCost = threatCost;
            return def;
        }
    }
}
