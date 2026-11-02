using System.Collections.Generic;
using Deadfall.Combat;
using UnityEngine;

namespace Deadfall.Tests.PlayMode
{
    /// <summary>Simple damageable used by integration tests.</summary>
    public sealed class TestTarget : MonoBehaviour, IDamageable
    {
        // Properties, not fields: test-only state that Unity shouldn't try to serialize.
        public float Health { get; set; } = 100f;
        public float LastDamage { get; private set; }
        public HitZoneRecord LastZone { get; private set; }
        public bool IsAlive => Health > 0f;
        public float DamageReduction { get; set; }

        public float ApplyDamage(in DamageInfo info)
        {
            float applied = Mathf.Min(Health, info.Amount);
            Health -= applied;
            LastDamage = applied;
            LastZone = new HitZoneRecord { Zone = info.Zone, Critical = info.IsCritical };
            return applied;
        }

        public struct HitZoneRecord
        {
            public Deadfall.Rules.Combat.HitZone Zone;
            public bool Critical;
        }
    }

    public static class PlayModeTestUtil
    {
        private static readonly List<GameObject> Spawned = new List<GameObject>();

        public static GameObject Track(GameObject go)
        {
            Spawned.Add(go);
            return go;
        }

        public static GameObject Ground()
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = "TestGround";
            g.transform.position = new Vector3(0f, -0.5f, 0f);
            g.transform.localScale = new Vector3(200f, 1f, 200f);
            return Track(g);
        }

        public static TestTarget Target(Vector3 position)
        {
            var root = Track(new GameObject("TestTarget"));
            root.transform.position = position;
            var target = root.AddComponent<TestTarget>();

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);
            body.AddComponent<Hurtbox>().Configure(Deadfall.Rules.Combat.HitZone.Body);

            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, 2.3f, 0f);
            head.transform.localScale = Vector3.one * 0.4f;
            head.AddComponent<Hurtbox>().Configure(Deadfall.Rules.Combat.HitZone.Head);
            return target;
        }

        public static void Cleanup()
        {
            foreach (GameObject go in Spawned)
                if (go != null) Object.DestroyImmediate(go);
            Spawned.Clear();
        }
    }
}
