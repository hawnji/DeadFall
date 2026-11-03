using System.Collections.Generic;
using Deadfall.Core;
using Deadfall.Player;
using Deadfall.Rules.World;
using Deadfall.World;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// Toxic Leak event (M12): two rooms fill with a green haze that hurts players standing in them. Every peer builds
    /// the same rooms (<see cref="HazardRules.PickToxicRooms"/>) and damages only its own player, so health stays
    /// owner-authoritative and nothing extra goes over the network. Enemies are unaffected.
    /// </summary>
    public sealed class ToxicLeak : MonoBehaviour
    {
        private static ToxicLeak _active;
        private readonly List<int> _rooms = new List<int>();
        private BuiltMap _map;
        private float _tick;

        public static bool IsActive => _active != null;

        public static void Begin(MapSession session, int surge)
        {
            End();
            if (session == null || !session.HasMap) return;
            var go = new GameObject("[ToxicLeak]");
            go.transform.SetParent(session.Map.Root, false);
            _active = go.AddComponent<ToxicLeak>();
            _active.Build(session.Map, HazardRules.PickToxicRooms(session.Map.Layout, session.OpenSectors, session.Seed, surge));
        }

        public static void End()
        {
            if (_active != null) Destroy(_active.gameObject);
            _active = null;
        }

        private void Build(BuiltMap map, List<int> rooms)
        {
            _map = map;
            _rooms.AddRange(rooms);
            Material gas = RuntimeMaterials.CreateEmissive(new Color(0.35f, 0.9f, 0.25f), 0.9f);
            foreach (int room in rooms)
            {
                Bounds b = map.RoomBounds[room];
                var rng = new System.Random(room * 7919 + 17);
                // Low drifting puffs over the floor of the room, and a sickly light.
                for (int i = 0; i < 18; i++)
                {
                    var puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Destroy(puff.GetComponent<Collider>());
                    puff.name = "Gas";
                    puff.transform.SetParent(transform, false);
                    float s = 1.2f + (float)rng.NextDouble() * 1.6f;
                    Vector3 p = new Vector3(Mathf.Lerp(b.min.x + 1f, b.max.x - 1f, (float)rng.NextDouble()), 0.3f + (float)rng.NextDouble() * 0.5f,
                        Mathf.Lerp(b.min.z + 1f, b.max.z - 1f, (float)rng.NextDouble()));
                    if (map.RoomAt(p) != room) continue; // shaped rooms: skip puffs outside the footprint
                    puff.transform.position = p;
                    puff.transform.localScale = new Vector3(s * 1.6f, s * 0.35f, s * 1.6f);
                    puff.GetComponent<Renderer>().sharedMaterial = gas;
                }
                var lightGo = new GameObject("GasLight");
                lightGo.transform.SetParent(transform, false);
                lightGo.transform.position = map.RoomCenter(room) + Vector3.up * 1.2f;
                Light l = lightGo.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(0.4f, 1f, 0.3f);
                l.range = Mathf.Max(b.size.x, b.size.z) * 0.6f;
                l.intensity = 1.6f;
            }
        }

        private void Update()
        {
            // Gentle drift so the haze reads as gas.
            float t = Time.time;
            int i = 0;
            foreach (Transform c in transform)
            {
                if (c.name != "Gas") continue;
                c.localPosition += new Vector3(Mathf.Sin(t * 0.4f + i) * 0.002f, 0f, Mathf.Cos(t * 0.3f + i * 1.7f) * 0.002f);
                i++;
            }

            _tick += Time.deltaTime;
            if (_tick < 0.5f) return;
            float dt = _tick;
            _tick = 0f;
            PlayerHealth hp = SquadLife.Current != null ? SquadLife.Current.LocalHealth : null;
            if (hp == null || !hp.IsAlive || _map == null) return;
            int room = _map.RoomAt(hp.transform.position);
            if (room >= 0 && _rooms.Contains(room)) hp.ApplyRawDamage(HazardRules.ToxicDamagePerSecond * dt, Vector3.zero, gameObject);
        }

        private void OnDestroy()
        {
            if (_active == this) _active = null;
        }
    }
}
