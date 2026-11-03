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
    /// <summary>
    /// Turns game events into sounds (M13): the local player's gunfire, hit markers, damage, downs and revives,
    /// enemy deaths and idle growls, surge horns, credit ticks, boss phase roars, and the ambience bed for where the
    /// player is (FOB hum, Cinder Ridge industrial, Harrow Line tunnels). Sounds tied to one object (explosions,
    /// spit, turrets, barricades, remote shots, UI buttons) are played where they happen instead.
    /// </summary>
    public sealed class AudioCues : MonoBehaviour
    {
        private GameObject _player;
        private WaveManager _waves;
        private ObjectiveDirector _objectives;
        private float _nextHurt, _nextCredits, _nextGrowl;

        public void Bind(GameObject player) => _player = player;

        private void OnEnable()
        {
            EventBus<WeaponFired>.Subscribe(OnFired);
            EventBus<HitConfirmed>.Subscribe(OnHit);
            EventBus<PlayerDamaged>.Subscribe(OnDamaged);
            EventBus<PlayerDowned>.Subscribe(OnDowned);
            EventBus<PlayerRevived>.Subscribe(OnRevived);
            EventBus<EnemyDied>.Subscribe(OnEnemyDied);
            Enemy.PhaseChanged += OnBossPhase;
        }

        private void OnDisable()
        {
            EventBus<WeaponFired>.Unsubscribe(OnFired);
            EventBus<HitConfirmed>.Unsubscribe(OnHit);
            EventBus<PlayerDamaged>.Unsubscribe(OnDamaged);
            EventBus<PlayerDowned>.Unsubscribe(OnDowned);
            EventBus<PlayerRevived>.Unsubscribe(OnRevived);
            EventBus<EnemyDied>.Unsubscribe(OnEnemyDied);
            Enemy.PhaseChanged -= OnBossPhase;
            BindWaves(null);
            BindObjectives(null);
        }

        private void BindObjectives(ObjectiveDirector d)
        {
            if (_objectives != null) _objectives.Announced -= OnAnnounced;
            _objectives = d;
            if (_objectives != null) _objectives.Announced += OnAnnounced;
        }

        private static void OnAnnounced(string title, string subtitle)
        {
            if (string.IsNullOrEmpty(title)) return;
            if (title == "OBJECTIVE COMPLETE" || title == "KEYCARD FOUND") GameAudio.Play2D(SfxId.ObjectiveDone);
            else if (title.StartsWith("WARNING") || IsEventName(title)) GameAudio.Play2D(SfxId.EventAlarm);
            else if (title == "SIDE OBJECTIVE" || title == "EXTRACTION") GameAudio.Play2D(SfxId.UiConfirm);
        }

        private static bool IsEventName(string title)
        {
            for (var k = RunEventKind.Blackout; k <= EventScheduler.Last; k++)
                if (EventScheduler.Name(k) == title) return true;
            return false;
        }

        private void BindWaves(WaveManager w)
        {
            if (_waves != null)
            {
                _waves.PhaseChanged -= OnPhase;
                _waves.LocalCreditsEarned -= OnCredits;
            }
            _waves = w;
            if (_waves != null)
            {
                _waves.PhaseChanged += OnPhase;
                _waves.LocalCreditsEarned += OnCredits;
            }
        }

        private bool IsLocal(GameObject go) => go != null && go == _player;

        public static SfxId ShotFor(WeaponDefinition def)
        {
            if (def == null) return SfxId.ShotRifle;
            if (def.Firearm != null && def.Firearm.IsProjectile) return SfxId.ShotLauncher;
            return def.Category switch
            {
                WeaponCategory.Pistol => SfxId.ShotPistol,
                WeaponCategory.SubmachineGun => SfxId.ShotSmg,
                WeaponCategory.Shotgun => SfxId.ShotShotgun,
                WeaponCategory.LightMachineGun => SfxId.ShotHeavy,
                WeaponCategory.Heavy => SfxId.ShotHeavy,
                _ => SfxId.ShotRifle,
            };
        }

        private void OnFired(WeaponFired e)
        {
            if (!IsLocal(e.Owner)) return;
            GameAudio.Play2D(ShotFor(WeaponCatalog.ById(e.WeaponId)));
        }

        private void OnHit(HitConfirmed e)
        {
            if (!IsLocal(e.Attacker)) return;
            GameAudio.Play2D(e.Killed ? SfxId.KillMarker : e.IsCritical ? SfxId.CritMarker : SfxId.HitMarker);
        }

        private void OnDamaged(PlayerDamaged e)
        {
            if (!IsLocal(e.Player) || Time.time < _nextHurt) return;
            _nextHurt = Time.time + 0.3f;
            GameAudio.Play2D(SfxId.PlayerHurt, Mathf.Clamp(0.5f + e.Amount / 40f, 0.5f, 1.2f));
        }

        private void OnDowned(PlayerDowned e)
        {
            if (IsLocal(e.Player)) GameAudio.Play2D(SfxId.PlayerDowned);
        }

        private void OnRevived(PlayerRevived e)
        {
            if (IsLocal(e.Player)) GameAudio.Play2D(SfxId.PlayerRevived);
        }

        private void OnEnemyDied(EnemyDied e)
        {
            bool big = e.Definition != null && e.Definition.IsBoss;
            GameAudio.Play(big ? SfxId.BossRoar : SfxId.EnemyDeath, e.Position + Vector3.up, 1f, big ? 0.7f : 1f);
        }

        private void OnBossPhase(Enemy boss, SpecimenPhase phase)
        {
            if (boss != null) GameAudio.Play(SfxId.BossRoar, boss.transform.position + Vector3.up * 2f, 1f, phase == SpecimenPhase.Rampage ? 1.1f : 0.9f);
        }

        private void OnPhase(RunPhase from, RunPhase to)
        {
            if (to == RunPhase.Surge) GameAudio.Play2D(SfxId.SurgeStart);
            else if (from == RunPhase.Surge && (to == RunPhase.Reward || to == RunPhase.Lull || to == RunPhase.Complete)) GameAudio.Play2D(SfxId.SurgeClear);
        }

        private void OnCredits(int amount, string reason)
        {
            if (amount <= 0 || Time.time < _nextCredits) return;
            _nextCredits = Time.time + 0.12f;
            GameAudio.Play2D(SfxId.Credits, amount >= 100 ? 1f : 0.6f);
        }

        private void Update()
        {
            if (WaveManager.Current != _waves) BindWaves(WaveManager.Current);
            if (ObjectiveDirector.Current != _objectives) BindObjectives(ObjectiveDirector.Current);

            MapSession map = MapSession.Current;
            SfxId bed = map != null && map.HasMap
                ? (map.Environment != null && map.Environment.Id == "env.harrow_line" ? SfxId.AmbienceTunnel : SfxId.AmbienceIndustrial)
                : SfxId.AmbienceFob;
            GameAudio.SetAmbience(bed);

            TickGrowls();
        }

        /// <summary>Every couple of seconds one nearby living enemy makes a noise, so the horde is heard before it's seen.</summary>
        private void TickGrowls()
        {
            if (Time.time < _nextGrowl || _player == null) return;
            _nextGrowl = Time.time + Random.Range(1.4f, 3.6f);
            EnemyManager em = EnemyManager.Current;
            if (em == null || em.Active.Count == 0) return;
            Vector3 me = _player.transform.position;
            int start = Random.Range(0, em.Active.Count);
            for (int k = 0; k < Mathf.Min(6, em.Active.Count); k++)
            {
                Enemy e = em.Active[(start + k) % em.Active.Count];
                if (e == null || !e.IsAlive) continue;
                if ((e.transform.position - me).sqrMagnitude > 26f * 26f) continue;
                bool boss = e.Definition != null && e.Definition.IsBoss;
                GameAudio.Play(boss ? SfxId.BossRoar : SfxId.EnemyGrowl, e.transform.position + Vector3.up * 1.4f, boss ? 0.6f : 1f, boss ? 0.8f : 1f);
                return;
            }
        }
    }
}
