using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Enemies;
using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// Arc Fence: two posts with an electric arc between them. Infected crossing it are slowed and shocked; each zap
    /// drains charge and wears the fence a little. Out of charge, the arc goes dark until refilled with Scrap.
    /// </summary>
    public sealed class ArcFenceStructure : Structure
    {
        public const string WeaponId = "structure.arc_fence";
        private const float TickSeconds = 0.1f;
        private const float HitDistance = 0.9f;
        private const float WearPerZap = 2f;
        private Transform _arc;
        private Material _arcMat;
        private float _tick;
        private float _flash;

        protected override void Build()
        {
            float half = StructureWidth * 0.5f;
            Part(PrimitiveType.Cube, transform, new Vector3(-half, 0.75f, 0f), new Vector3(0.18f, 1.5f, 0.18f), new Color(0.3f, 0.32f, 0.35f), true, tintWithHealth: true);
            Part(PrimitiveType.Cube, transform, new Vector3(half, 0.75f, 0f), new Vector3(0.18f, 1.5f, 0.18f), new Color(0.3f, 0.32f, 0.35f), true);
            GameObject arc = Part(PrimitiveType.Cube, transform, new Vector3(0f, 0.9f, 0f), new Vector3(StructureWidth, 0.06f, 0.06f), new Color(0.5f, 0.85f, 1f), false);
            _arc = arc.transform;
            _arcMat = arc.GetComponent<Renderer>().sharedMaterial;
        }

        private float StructureWidth => Stats.Range > 0f ? Stats.Range : 3.2f;

        protected override void TickHost(float dt)
        {
            if (Supply <= 0f || !Powered) return;
            _tick += dt;
            if (_tick < TickSeconds) return;
            _tick -= TickSeconds;
            Vector3 a = transform.position - transform.right * (StructureWidth * 0.5f);
            Vector3 b = transform.position + transform.right * (StructureWidth * 0.5f);
            int zapped = 0;
            var all = Hostiles.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (!(all[i] is Enemy e) || !e.IsAlive) continue;
                Vector3 p = e.transform.position;
                if (Mathf.Abs(p.y - transform.position.y) > 2f) continue;
                if (DistanceToSegmentXZ(p, a, b) > HitDistance + e.Definition.Radius) continue;
                e.ApplySlow(Stats.Rate, 0.5f);
                GameObject owner = OwnerObject;
                e.ApplyDamage(new DamageInfo
                {
                    Amount = Stats.Damage * TickSeconds * (1f - Mathf.Clamp01(e.DamageReduction)),
                    Zone = HitZone.Body,
                    Point = e.AimPoint,
                    Direction = transform.forward,
                    Source = owner != null ? owner : gameObject,
                    WeaponId = WeaponId,
                });
                zapped++;
            }
            if (zapped == 0) return;
            Supply = Mathf.Max(0f, Supply - TickSeconds);
            _flash = 0.15f;
            EngineeringDirector.Current?.MarkDirty();
            ApplyDamage(new DamageInfo { Amount = WearPerZap * zapped * TickSeconds * 5f, Zone = HitZone.Body, Source = gameObject });
        }

        protected override void TickVisual(float dt)
        {
            bool live = Supply > 0f && Powered;
            _arc.gameObject.SetActive(live);
            if (!live || _arcMat == null) return;
            _flash = Mathf.Max(0f, _flash - dt);
            float flicker = 0.75f + 0.25f * Mathf.Sin(Time.time * 40f);
            RuntimeMaterials.SetColor(_arcMat, Color.Lerp(new Color(0.4f, 0.75f, 1f) * flicker, Color.white, _flash / 0.15f));
        }

        private static float DistanceToSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
        {
            var p2 = new Vector2(p.x, p.z);
            var a2 = new Vector2(a.x, a.z);
            var b2 = new Vector2(b.x, b.z);
            Vector2 ab = b2 - a2;
            float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector2.Dot(p2 - a2, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(p2, a2 + ab * t);
        }
    }
}
