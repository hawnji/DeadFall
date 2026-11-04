using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Enemies;
using Deadfall.Player;
using Deadfall.Rules.Common;
using Deadfall.Rules.World;
using Deadfall.World;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// Containment Breach event: one room's floor (<see cref="HazardRules.PickBreachRoom"/>) runs a cycle of calm, a
    /// warning flicker, then an electric arc. Every peer builds the same room and runs the same clock from the event's
    /// start, damages only its own player (owner-authoritative health, nothing extra on the wire), and the host also
    /// shocks infected standing in it. It lasts until the console is used or the event vents by itself.
    /// </summary>
    public sealed class ContainmentBreach : MonoBehaviour
    {
        private static ContainmentBreach _active;
        private readonly List<Renderer> _strips = new List<Renderer>();
        private readonly List<Light> _lights = new List<Light>();
        private BuiltMap _map;
        private int _room = -1;
        private float _floorY;
        private double _startedAt;
        private int _lastPlayerArc = -1;
        private int _lastEnemyArc = -1;
        private bool _authority;
        private Material _calm, _warn, _arc;

        public static bool IsActive => _active != null;
        /// <summary>The arcing room, or -1.</summary>
        public static int Room => _active != null ? _active._room : -1;

        public static void Begin(MapSession session, int surge, bool authority)
        {
            End();
            if (session == null || !session.HasMap) return;
            int room = HazardRules.PickBreachRoom(session.Map.Layout, session.OpenSectors, session.Seed, surge);
            if (room < 0) return;
            var go = new GameObject("[ContainmentBreach]");
            go.transform.SetParent(session.Map.Root, false);
            _active = go.AddComponent<ContainmentBreach>();
            _active.Build(session.Map, room, authority);
        }

        public static void End()
        {
            if (_active != null) Destroy(_active.gameObject);
            _active = null;
        }

        private void Build(BuiltMap map, int room, bool authority)
        {
            _map = map;
            _room = room;
            _authority = authority;
            _startedAt = Time.timeAsDouble;
            _floorY = map.RoomBounds[room].min.y;
            _calm = RuntimeMaterials.CreateEmissive(new Color(0.1f, 0.3f, 0.45f), 0.4f);
            _warn = RuntimeMaterials.CreateEmissive(new Color(0.9f, 0.75f, 0.2f), 1.2f);
            _arc = RuntimeMaterials.CreateEmissive(new Color(0.6f, 0.9f, 1f), 3f);
            float cs = map.CellSize;
            // Conductive floor plates: a grid of glowing strips over every cell of the room.
            foreach (Cell c in map.Layout.Rooms[room].Cells())
            {
                for (int k = 0; k < 2; k++)
                {
                    var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(strip.GetComponent<Collider>());
                    strip.name = "Arc strip";
                    strip.transform.SetParent(transform, false);
                    Vector3 centre = new Vector3((c.X + 0.5f) * cs, _floorY + 0.02f, (c.Z + 0.5f) * cs);
                    strip.transform.position = centre;
                    strip.transform.localScale = k == 0 ? new Vector3(cs * 0.92f, 0.03f, 0.12f) : new Vector3(0.12f, 0.03f, cs * 0.92f);
                    Renderer r = strip.GetComponent<Renderer>();
                    r.sharedMaterial = _calm;
                    _strips.Add(r);
                }
            }
            var lightGo = new GameObject("Arc light");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.position = map.RoomCenter(room) + Vector3.up * 1f;
            Light l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(0.55f, 0.85f, 1f);
            Bounds b = map.RoomBounds[room];
            l.range = Mathf.Max(b.size.x, b.size.z) * 0.7f;
            l.intensity = 0f;
            _lights.Add(l);
        }

        private void Update()
        {
            if (_map == null) return;
            float t = (float)(Time.timeAsDouble - _startedAt);
            HazardRules.BreachPhase phase = HazardRules.BreachPhaseAt(t);
            Material m = phase == HazardRules.BreachPhase.Arc ? _arc
                : phase == HazardRules.BreachPhase.Warning ? (Mathf.Repeat(t * 10f, 1f) < 0.5f ? _warn : _calm) : _calm;
            foreach (Renderer r in _strips) if (r != null && r.sharedMaterial != m) r.sharedMaterial = m;
            float flicker = phase == HazardRules.BreachPhase.Arc ? 3f + Mathf.PerlinNoise(t * 30f, 0.3f) * 3f : phase == HazardRules.BreachPhase.Warning ? 0.6f : 0f;
            foreach (Light l in _lights) if (l != null) l.intensity = flicker;
            if (phase != HazardRules.BreachPhase.Arc) return;

            int arc = HazardRules.BreachArcIndex(t);
            if (arc != _lastPlayerArc)
            {
                _lastPlayerArc = arc;
                PlayerHealth hp = SquadLife.Current != null ? SquadLife.Current.LocalHealth : null;
                if (hp != null && hp.IsAlive && OnFloor(hp.transform.position))
                    hp.ApplyRawDamage(HazardRules.BreachPlayerDamage, Vector3.zero, gameObject);
                GameAudio.Play(SfxId.ShotTurret, _map.RoomCenter(_room) + Vector3.up, 0.5f, 0.55f);
            }
            if (_authority && arc != _lastEnemyArc)
            {
                _lastEnemyArc = arc;
                EnemyManager em = EnemyManager.Current;
                if (em == null) return;
                IReadOnlyList<Enemy> all = em.Active;
                for (int i = all.Count - 1; i >= 0; i--)
                {
                    Enemy e = all[i];
                    if (e == null || !e.IsAlive || !OnFloor(e.transform.position)) continue;
                    e.ApplyDamage(new DamageInfo { Amount = HazardRules.BreachEnemyDamage, Point = e.transform.position, Source = gameObject, ForceStagger = true });
                }
            }
        }

        /// <summary>In the room and standing on (not jumping over) the floor.</summary>
        private bool OnFloor(Vector3 p) => _map.RoomAt(p) == _room && p.y - _floorY < 0.6f;

        private void OnDestroy()
        {
            if (_active == this) _active = null;
        }
    }
}
