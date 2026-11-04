using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Enemies;
using Deadfall.Net;
using Deadfall.Rules.Common;
using Deadfall.Rules.World;
using Deadfall.World;
using UnityEngine;
using UnityEngine.AI;

namespace Deadfall.Game
{
    /// <summary>
    /// Host: sends Creepers through the map's crawl vents (<see cref="BuiltMap.CrawlVents"/>, <see cref="VentRules"/>).
    /// A Creeper far from the squad and next to a grate drops in and climbs out of the open-room grate nearest its
    /// target. Clients see it through the normal enemy snapshots (it sinks below the floor and rises again).
    /// </summary>
    public sealed class VentCrawlers : MonoBehaviour
    {
        private readonly Dictionary<Enemy, double> _cooldown = new Dictionary<Enemy, double>();
        private readonly List<Enemy> _stale = new List<Enemy>(8);
        private DeterministicRandom _rng = new DeterministicRandom(0x7E47UL);
        private SessionService _session;
        private float _timer;

        private bool IsAuthority => _session == null || _session.Role != SessionRole.Client;

        private void Awake() => _session = SessionService.Instance;

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = VentRules.CheckSeconds;
            MapSession map = MapSession.Current;
            EnemyManager em = EnemyManager.Current;
            if (!IsAuthority || map == null || !map.HasMap || em == null || map.Map.CrawlVents.Count < 2)
            {
                _cooldown.Clear();
                return;
            }
            double now = Time.timeAsDouble;
            IReadOnlyList<Enemy> all = em.Active;
            for (int i = 0; i < all.Count; i++)
            {
                Enemy e = all[i];
                if (e == null || !e.IsAlive || e.InVent || e.Definition != PrototypeEnemies.Creeper) continue;
                if (_cooldown.TryGetValue(e, out double ready) && now < ready) continue;
                if (_rng.NextFloat() >= VentRules.DiveChance) continue;
                TryDive(map, e, now);
            }
            _stale.Clear();
            foreach (KeyValuePair<Enemy, double> kv in _cooldown)
                if (kv.Key == null || !kv.Key.IsAlive) _stale.Add(kv.Key);
            foreach (Enemy e in _stale) _cooldown.Remove(e);
        }

        private void TryDive(MapSession map, Enemy e, double now)
        {
            Vector3 pos = e.transform.position;
            ICombatTarget target = CombatTargets.Nearest(pos, out float toTarget);
            if (target?.TargetTransform == null || toTarget < VentRules.MinTargetDistance) return;
            List<CrawlVent> vents = map.Map.CrawlVents;
            int entry = -1;
            float toGrate = float.MaxValue;
            for (int v = 0; v < vents.Count; v++)
            {
                float d = Vector3.Distance(pos, vents[v].Position);
                if (d < toGrate)
                {
                    toGrate = d;
                    entry = v;
                }
            }
            if (entry < 0 || toGrate > VentRules.GrateReach) return;
            Vector3 targetPos = target.TargetTransform.position;
            int exit = -1;
            float exitToTarget = float.MaxValue;
            for (int v = 0; v < vents.Count; v++)
            {
                if (v == entry || !map.IsRoomOpen(vents[v].Room)) continue;
                float d = Vector3.Distance(targetPos, vents[v].Position);
                if (d < VentRules.MinExitDistance || d >= exitToTarget) continue;
                exitToTarget = d;
                exit = v;
            }
            if (exit < 0 || !VentRules.ShouldDive(toTarget, toGrate, exitToTarget)) return;
            Vector3 from = Floor(vents[entry].Position), to = Floor(vents[exit].Position);
            float travel = VentRules.TravelSeconds(Vector3.Distance(from, to));
            if (!e.EnterVent(from, to, travel)) return;
            _cooldown[e] = now + travel + VentRules.CooldownSeconds;
            GameAudio.Play(SfxId.EnemyGrowl, from + Vector3.up * 0.3f, 0.7f, 1.35f);
            StartCoroutine(Rattle(vents[exit].Grate, travel));
        }

        /// <summary>The exit grate rattles just before the Creeper climbs out (a tell for alert players).</summary>
        private System.Collections.IEnumerator Rattle(Transform grate, float delay)
        {
            yield return new WaitForSeconds(Mathf.Max(0f, delay - 0.8f));
            if (grate == null) yield break;
            GameAudio.Play(SfxId.BarricadeBreak, grate.position + Vector3.up * 0.2f, 0.45f, 1.6f);
            Vector3 rest = grate.localPosition;
            for (float t = 0f; t < 0.8f && grate != null; t += Time.deltaTime)
            {
                grate.localPosition = rest + new Vector3(Mathf.Sin(t * 70f) * 0.015f, Mathf.Abs(Mathf.Sin(t * 45f)) * 0.03f, 0f);
                yield return null;
            }
            if (grate != null) grate.localPosition = rest;
        }

        private static Vector3 Floor(Vector3 p) => NavMesh.SamplePosition(p, out NavMeshHit hit, 1.5f, NavMesh.AllAreas) ? hit.position : p;
    }
}
