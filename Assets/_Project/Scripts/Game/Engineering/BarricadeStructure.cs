using Deadfall.Combat;
using UnityEngine;
using UnityEngine.AI;

namespace Deadfall.Game
{
    /// <summary>
    /// Barricade: a wall section that carves the NavMesh. Enemies path around it when they can; when it cuts them off
    /// they attack it (it is an <see cref="IBlocker"/>), so a path is never blocked for good.
    /// </summary>
    public sealed class BarricadeStructure : Structure, IBlocker
    {
        public const float Width = 3f;
        public const float Height = 1.4f;
        private BoxCollider _box;

        public Transform TargetTransform => transform;
        public bool IsTargetable => !Mirror && IsAlive && isActiveAndEnabled;
        public IDamageable Damageable => this;

        protected override void Build()
        {
            GameObject wall = Part(PrimitiveType.Cube, transform, new Vector3(0f, Height * 0.5f, 0f), new Vector3(Width, Height, 0.45f),
                new Color(0.45f, 0.4f, 0.32f), true, tintWithHealth: true);
            _box = wall.GetComponent<BoxCollider>();
            Part(PrimitiveType.Cube, transform, new Vector3(-Width * 0.35f, Height * 0.5f, -0.3f), new Vector3(0.12f, Height * 1.05f, 0.4f), new Color(0.3f, 0.3f, 0.3f), false);
            Part(PrimitiveType.Cube, transform, new Vector3(Width * 0.35f, Height * 0.5f, -0.3f), new Vector3(0.12f, Height * 1.05f, 0.4f), new Color(0.3f, 0.3f, 0.3f), false);
            var obstacle = gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = new Vector3(0f, Height * 0.5f, 0f);
            obstacle.size = new Vector3(Width, Height, 0.6f);
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;
            if (!Mirror) Blockers.Register(this);
        }

        public float DistanceFrom(Vector3 point)
        {
            if (_box == null) return Vector3.Distance(point, transform.position);
            Vector3 closest = _box.ClosestPoint(point);
            closest.y = point.y;
            return Vector3.Distance(point, closest);
        }

        protected override void OnDestroy() => Blockers.Unregister(this);
        private void OnDisable() => Blockers.Unregister(this);
    }
}
