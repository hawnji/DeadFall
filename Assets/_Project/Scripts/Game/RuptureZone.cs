using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Encounters;
using Deadfall.Enemies;
using Deadfall.Player;
using Deadfall.Rules.Combat;
using Deadfall.Rules.Common;
using Deadfall.Rules.Encounters;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>One rupture: a ring that brightens for the telegraph, a burst, then a burning pool that fades.</summary>
    public sealed class RuptureZone : MonoBehaviour
    {
        private bool _brood;
        private bool _authority;
        private float _t;
        private bool _burst;
        private float _poolTick;
        private Transform _ring, _pool;
        private Material _ringMat, _poolMat;
        private Light _light;

        public void Init(bool brood, bool authority)
        {
            _brood = brood;
            _authority = authority;
            Color c = brood ? new Color(0.75f, 0.95f, 0.25f) : new Color(1f, 0.45f, 0.1f);
            _ringMat = RuntimeMaterials.CreateEmissive(c, 0.6f);
            _poolMat = RuntimeMaterials.CreateEmissive(c * 0.8f, 1.6f);
            _ring = Disc("Ring", ArenaHazardRules.BurstRadius * 2f, 0.02f, _ringMat);
            _pool = Disc("Pool", ArenaHazardRules.PoolRadius * 2f, 0.04f, _poolMat);
            _pool.gameObject.SetActive(false);
            var lg = new GameObject("Glow");
            lg.transform.SetParent(transform, false);
            lg.transform.localPosition = Vector3.up * 0.6f;
            _light = lg.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = c;
            _light.range = ArenaHazardRules.BurstRadius * 2.2f;
            _light.intensity = 0.3f;
        }

        private Transform Disc(string name, float diameter, float height, Material m)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * (0.03f + height * 0.5f);
            go.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go.transform;
        }

        private void Update()
        {
            _t += Time.deltaTime;
            if (!_burst)
            {
                float k = Mathf.Clamp01(_t / ArenaHazardRules.TelegraphSeconds);
                // The ring tightens and pulses faster as the burst nears.
                float pulse = 0.5f + 0.5f * Mathf.Sin(_t * Mathf.Lerp(6f, 22f, k));
                float s = Mathf.Lerp(1.15f, 1f, k);
                _ring.localScale = new Vector3(ArenaHazardRules.BurstRadius * 2f * s, 0.01f, ArenaHazardRules.BurstRadius * 2f * s);
                _light.intensity = 0.3f + k * 2f * pulse;
                if (_t >= ArenaHazardRules.TelegraphSeconds) Burst();
                return;
            }
            float poolT = _t - ArenaHazardRules.TelegraphSeconds;
            float fade = 1f - Mathf.Clamp01(poolT / ArenaHazardRules.PoolSeconds);
            _light.intensity = 2.5f * fade;
            _pool.localScale = new Vector3(ArenaHazardRules.PoolRadius * 2f, 0.02f * Mathf.Max(0.2f, fade), ArenaHazardRules.PoolRadius * 2f);
            _poolTick += Time.deltaTime;
            if (_poolTick >= 0.5f)
            {
                float dt = _poolTick;
                _poolTick = 0f;
                PlayerHealth hp = LocalPlayer();
                if (hp != null && Flat(hp.transform.position) <= ArenaHazardRules.PoolRadius)
                    hp.ApplyRawDamage(ArenaHazardRules.PoolDamagePerSecond * dt, Vector3.zero, gameObject);
            }
            if (poolT >= ArenaHazardRules.PoolSeconds) Destroy(gameObject);
        }

        private void Burst()
        {
            _burst = true;
            _ring.gameObject.SetActive(false);
            _pool.gameObject.SetActive(true);
            ExplosionFx.Spawn(transform.position + Vector3.up * 0.4f, ArenaHazardRules.BurstRadius * 0.8f);
            GameAudio.Play(_brood ? SfxId.EnemySpit : SfxId.Explosion, transform.position + Vector3.up, 0.8f, _brood ? 0.7f : 1.2f);
            PlayerHealth hp = LocalPlayer();
            if (hp != null)
            {
                float dmg = ArenaHazardRules.BurstDamageAt(Flat(hp.transform.position));
                if (dmg > 0f) hp.ApplyRawDamage(dmg, (hp.transform.position - transform.position).normalized, gameObject);
            }
            if (_authority && _brood && WaveManager.Current != null && WaveManager.Current.Director != null && WaveManager.Current.Phase == RunPhase.Surge)
                WaveManager.Current.Director.SpawnNear(PrototypeEnemies.Creeper, transform.position, 1, 0.2f, 0.8f);
        }

        private float Flat(Vector3 p)
        {
            Vector3 d = p - transform.position;
            if (Mathf.Abs(d.y) > 1.6f) return float.MaxValue; // a floor above or below
            d.y = 0f;
            return d.magnitude;
        }

        private static PlayerHealth LocalPlayer()
        {
            PlayerHealth hp = SquadLife.Current != null ? SquadLife.Current.LocalHealth : null;
            return hp != null && hp.IsAlive ? hp : null;
        }
    }
}
