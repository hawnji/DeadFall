using System.Collections.Generic;
using Deadfall.Core;
using UnityEngine;
using UnityEngine.AI;

namespace Deadfall.World
{
    /// <summary>
    /// Bakes a NavMesh at runtime from the physics colliders inside a box, using Unity's built-in
    /// NavMeshBuilder (no AI Navigation package needed, see DECISIONS T-21). Generated maps (M5) bake per
    /// sector the same way. Keeps the data so it can be removed or rebuilt.
    /// </summary>
    public sealed class RuntimeNavMesh
    {
        private NavMeshDataInstance _instance;
        private NavMeshData _data;

        public bool IsBuilt => _instance.valid;

        /// <summary>
        /// Builds and registers a NavMesh for <paramref name="bounds"/>. Colliders on layers outside
        /// <paramref name="layerMask"/> (players, enemies) are ignored. Uses agent type 0 (Humanoid) settings
        /// with a smaller radius so narrow sandbox corridors stay walkable.
        /// </summary>
        public bool Build(Bounds bounds, int layerMask, float agentRadius = 0.35f, float agentClimb = 0.4f, float maxSlope = 45f)
        {
            Clear();
            var sources = new List<NavMeshBuildSource>();
            var markups = new List<NavMeshBuildMarkup>();
            NavMeshBuilder.CollectSources(bounds, layerMask, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);
            if (sources.Count == 0)
            {
                GameLog.Warn("NavMesh", "No colliders found to bake.");
                return false;
            }

            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = agentRadius;
            settings.agentClimb = agentClimb;
            settings.agentSlope = maxSlope;

            float started = Time.realtimeSinceStartup;
            _data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (_data == null)
            {
                GameLog.Warn("NavMesh", "Bake failed.");
                return false;
            }
            _instance = NavMesh.AddNavMeshData(_data);
            GameLog.Info("NavMesh", $"Baked {sources.Count} sources in {(Time.realtimeSinceStartup - started) * 1000f:0} ms");
            return _instance.valid;
        }

        public void Clear()
        {
            if (_instance.valid) _instance.Remove();
            _instance = default;
            if (_data != null) Object.Destroy(_data);
            _data = null;
        }
    }
}
