using Deadfall.Classes;
using Deadfall.Encounters;
using Deadfall.Player;
using Deadfall.Rules.Encounters;
using Deadfall.Rules.Progression;
using Deadfall.World;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// Run state for the local player (M6): starting loadout per class, reset of every temporary thing (weapons,
    /// Field Mods, drops) when a deployment or run starts, Field Mod offers after surges, and Supply Caches on
    /// generated maps.
    /// </summary>
    public sealed class RunDirector : MonoBehaviour
    {
        private GameObject _player;
        private PlayerWeapons _weapons;
        private PlayerClass _class;
        private FieldModRunner _mods;
        private WaveManager _waves;
        private MapSession _map;

        public void Bind(GameObject player)
        {
            _player = player;
            _weapons = player.GetComponent<PlayerWeapons>();
            _class = player.GetComponent<PlayerClass>();
            _mods = player.GetComponent<FieldModRunner>();
            if (_mods == null) _mods = player.AddComponent<FieldModRunner>();
            if (_class != null) _class.ClassChanged += OnClassChanged;
        }

        private void OnDestroy()
        {
            if (_class != null) _class.ClassChanged -= OnClassChanged;
            if (_waves != null) _waves.PhaseChanged -= OnPhaseChanged;
            if (_map != null) { _map.MapBuilt -= OnMapBuilt; _map.MapCleared -= OnMapCleared; }
        }

        private void Update()
        {
            if (_waves == null && WaveManager.Current != null)
            {
                _waves = WaveManager.Current;
                _waves.PhaseChanged += OnPhaseChanged;
            }
            if (_map == null && MapSession.Current != null)
            {
                _map = MapSession.Current;
                _map.MapBuilt += OnMapBuilt;
                _map.MapCleared += OnMapCleared;
                _map.SupplyCacheFactory = (p, parent) => SupplyCache.Create(p.Id, p.Sector, _map.Seed, p.Position, p.Yaw, parent).gameObject;
            }
        }

        private string ClassId => _class != null && _class.Class != null ? _class.Class.ContentId : null;

        /// <summary>Back to the class's starting loadout with no Field Mods (deployment or run start).</summary>
        public void ResetRunState()
        {
            if (_weapons != null)
            {
                _weapons.ResetLoadout(RunLoadouts.For(ClassId));
                _weapons.RestockAll(); // ammo and grenades spent in the hub come back for the next game mode
            }
            if (_mods != null) _mods.ResetRun();
            if (RunEconomy.Current != null) RunEconomy.Current.ClearDrops();
        }

        private void OnClassChanged(ClassDefinition c)
        {
            // Outside a run, picking a class hands out its starting loadout. During a run you keep what you carry.
            if (_waves == null || !_waves.IsRunning) ResetRunState();
        }

        private void OnMapBuilt(BuiltMap map) => ResetRunState();
        private void OnMapCleared() => ResetRunState();

        private void OnPhaseChanged(RunPhase from, RunPhase to)
        {
            if (from == RunPhase.NotStarted && to == RunPhase.Lull)
            {
                ResetRunState();
                return;
            }
            if (to == RunPhase.Reward && _waves != null)
            {
                if (_mods != null)
                {
                    _mods.OnSurgeCleared();
                    if (FieldModOffers.OfferAfterSurge(_waves.Surge, specimen: _waves.SurgeType == SurgeType.Specimen))
                    {
                        _mods.QueueOffer(_waves.Surge);
                        RunToasts.Show("Field Mod available: press T", new Color(1f, 0.85f, 0.4f));
                    }
                }
            }
        }
    }
}
