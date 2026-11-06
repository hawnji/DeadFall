using Deadfall.Core;
using Deadfall.Encounters;
using Deadfall.Enemies;
using Deadfall.Game;
using Deadfall.Net;
using Deadfall.Player;
using Deadfall.UI;
using Deadfall.World;
using UnityEngine;

namespace Deadfall.DebugTools
{
    /// <summary>
    /// Builds the Milestone 1 movement and combat sandbox from primitives at runtime, so it needs no
    /// scene authoring: add this component to an empty GameObject in any scene and press Play.
    /// Areas: shooting range with distance markers, melee pit, movement course (ramps, stairs, jump
    /// boxes, crouch tunnel), and hazard/heal/ammo pads. Esc opens the co-op test panel (host / join).
    /// A NavMesh is baked at startup so enemies (F5-F8) can path anywhere on the ground.
    /// M5: the pause screen can swap the sandbox for a generated Cinder Ridge map (seed field) and back; in co-op
    /// the host's choice is mirrored on every client.
    /// M10: FOB Halcyon is built next to the range and is where the player starts and returns after a run. The
    /// Command Center console deploys (map + Operation or Survival); the Training Range console leads to the range.
    /// </summary>
    public sealed class SandboxBootstrap : MonoBehaviour
    {
        [SerializeField] private Vector3 _spawnPosition = new Vector3(0f, 0.05f, 0f);
        [SerializeField] private float _spawnYaw;

        private Transform _world;
        private readonly RuntimeNavMesh _navMesh = new RuntimeNavMesh();

        public static readonly Vector3 FobOrigin = new Vector3(0f, 0f, -120f);
        private Vector3 _fobSpawn;
        private float _fobYaw;

        private MapSession _map;
        private GameObject _player;
        private WaveManager _waves;

        public static SandboxBootstrap Current { get; private set; }
        public bool InMap => _map != null && _map.HasMap;
        public MapSession MapSession => _map;

        private void Awake()
        {
            Current = this;
            DisableSceneCameras();
            // M10: the permanent profile loads before anything reads weapon XP or loadouts.
            new GameObject("[Profile]").AddComponent<ProfileService>();
            BuildSandbox();

            _player = PlayerFactory.CreateLocalPlayer(_fobSpawn, _fobYaw, SandboxContent.DefaultLoadout());
            GameObject player = _player;

            var ui = new GameObject("[SandboxUI]");
            ui.AddComponent<PrototypeHud>().Bind(player);
            ui.AddComponent<SandboxDebugOverlay>().Bind(player, _spawnPosition, _spawnYaw);
            ui.AddComponent<SandboxEnemySpawner>().Bind(player);
            ui.AddComponent<WaveHud>();
            // M4: classes. Pick one on the pause screen; Q uses the ability.
            ui.AddComponent<SandboxClassPanel>().Bind(player);
            ui.AddComponent<ClassHud>().Bind(player);
            // M5: generated maps. Seed field on the pause screen, F9 map overlay.
            ui.AddComponent<SandboxMapPanel>();
            ui.AddComponent<MapHud>();
            ui.AddComponent<Minimap>();
            // Level music (user's Endless Infestation tracks) on its own object so hiding the HUD never stops it.
            new GameObject("[LevelMusic]").AddComponent<LevelMusic>();
            LevelMusic.Suppressed = () => TitleScreen.Showing;
            ui.AddComponent<ClassPickPanel>();
            ClassPickPanel.Blocked = () => TitleScreen.Showing;
            ui.AddComponent<MapDebugOverlay>();
            // M6: run economy UI (Supply Cache menu, Field Mod picks with T, toasts, weapon XP).
            ui.AddComponent<RunToasts>();
            ui.AddComponent<SupplyCacheMenu>();
            ui.AddComponent<FieldModPanel>();
            ui.AddComponent<WeaponXpTracker>().Bind(player);
            ui.AddComponent<BossHud>();
            ui.AddComponent<ObjectiveHud>();
            ui.AddComponent<BlackoutLighting>();
            var build = ui.AddComponent<BuildTool>();
            build.Bind(player);
            ui.AddComponent<EngineeringHud>().Bind(build);
            // M10: FOB screens, end-of-run results, and the profile fed from play.
            ui.AddComponent<FobPanel>();
            ui.AddComponent<FobLabels>();
            ui.AddComponent<RunResultsPanel>();
            ui.AddComponent<ProgressionTracker>().Bind(player);
            // M11: downs, revives, kits, spectating, the squad roster, and drop/rejoin handling.
            ui.AddComponent<SquadHud>();
            ui.AddComponent<SpectatorCamera>().Bind(player);

            // M1.5 networking spike: host/join from the pause screen.
            var net = new GameObject("[NetSpike]");
            var sync = net.AddComponent<NetSpikeSync>();
            sync.Bind(player);
            net.AddComponent<NetSpikePanel>().Bind(sync, player);
            // M2: host enemies are sent to clients as compact snapshots.
            net.AddComponent<Deadfall.Enemies.EnemyReplicator>();
            // M4: classes in co-op (roster, host-run shields, sentries and charges).
            net.AddComponent<Deadfall.Classes.ClassNetwork>().Bind(player.GetComponent<Deadfall.Classes.PlayerClass>());
            // M5: host-authoritative pickups.
            net.AddComponent<Deadfall.RunSystems.LootNetwork>();
            // M6: host-run purchases and weapon drops; launcher grenades through the host.
            net.AddComponent<RunEconomy>();
            net.AddComponent<ProjectileNetwork>();
            // M7: enemy attack visuals for clients; supply surge crates.
            net.AddComponent<Deadfall.Enemies.EnemyFxRelay>();
            net.AddComponent<SupplyDrops>();
            // M8: objectives, Operations and random events (host-run, synced as one state message).
            net.AddComponent<ObjectiveDirector>();
            // Boss arena hazards (ruptures under the squad while a Specimen is enraged).
            net.AddComponent<ArenaHazards>();
            // Creepers flanking through the crawl-vent network (host).
            net.AddComponent<VentCrawlers>();
            // The power grid: generators that feed doors, lights, fences and sentries.
            net.AddComponent<PowerGrid>();
            // Gun Dispenser: random weapons for Field Credits in Survival and Endless.
            net.AddComponent<GunDispenserDirector>();
            // Wall-mounted ammo refill stations, one per sector.
            net.AddComponent<AmmoStationDirector>();
            // Perk machines (one of each perk) and the Weapon Forge on the walls.
            net.AddComponent<PerkMachineDirector>();
            // Squad pings (middle mouse) and per-player run stats (Tab, end-of-run screen).
            net.AddComponent<PingDirector>().Bind(player);
            net.AddComponent<RunStatsDirector>().Bind(player);
            // Power-ups from kills: Max Ammo, Double Credits, Insta-Kill, Nuke.
            net.AddComponent<PowerUpDirector>();
            // M9: Engineering structures and the squad's Scrap (host-run).
            net.AddComponent<EngineeringDirector>();
            // M11: squad life (revives, self-revive kits, returns) and D-16 rejoins.
            net.AddComponent<SquadLife>().Bind(player);
            net.AddComponent<ReconnectDirector>().Bind(player);
            net.AddComponent<AudioCues>().Bind(player);

            // M3: survival waves. Press N to start a run.
            _waves = new GameObject("[Waves]").AddComponent<WaveManager>();
            AddPerimeterSpawnPoints(_waves);

            // M5: the generated map lives here when active.
            _map = new GameObject("[Map]").AddComponent<MapSession>();
            _map.RemoteWorldRequested += OnRemoteWorldRequested;

            // M6: run state (class starting loadouts, Field Mods, resets) for the local player.
            var run = new GameObject("[Run]").AddComponent<RunDirector>();
            run.Bind(player);
            run.ResetRunState();

            WorldControl.EnterMap = EnterMap;
            WorldControl.ReturnToFob = EnterSandbox;
            WorldControl.EnterTrainingRange = GoToRange;
            WorldControl.AtBase = () => !InMap;
            PrototypeHud.QuitToHub = () => WorldControl.ReturnToFob?.Invoke();
            PrototypeHud.QuitToHubBlocked = () =>
            {
                if (!InMap) return "You're already at the hub.";
                SessionService s = SessionService.Current;
                return s != null && s.IsActive && !s.IsHost ? "Only the host can take the squad back to the hub." : null;
            };

            // Title screen over the FOB until the player picks Play, Host or Join.
            var title = new GameObject("[Title]").AddComponent<TitleScreen>();
            var attract = title.gameObject.AddComponent<AttractMode>();
            attract.Bind(_map, EnterMap, EnterSandbox, player);
            title.Backdrop = attract;
            title.Bind(player, ui, FobOrigin, () =>
            {
                EnterSandbox();
                run.ResetRunState();
                var health = player.GetComponent<PlayerHealth>();
                if (health != null) health.ResetLife();
            });
        }

        private void GoToRange()
        {
            var motor = _player != null ? _player.GetComponent<PlayerMotor>() : null;
            if (motor != null) motor.Teleport(_spawnPosition, _spawnYaw);
        }

        private void GoToFob()
        {
            var motor = _player != null ? _player.GetComponent<PlayerMotor>() : null;
            if (motor != null) motor.Teleport(_fobSpawn, _fobYaw);
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            if (_map != null) _map.RemoteWorldRequested -= OnRemoteWorldRequested;
        }

        private void OnRemoteWorldRequested(ulong seed)
        {
            if (seed == 0) EnterSandbox();
            else EnterMap(seed);
        }

        /// <summary>Swaps the sandbox (or the current map) for the generated map of <paramref name="seed"/>.</summary>
        public bool EnterMap(ulong seed)
        {
            if (seed == 0) seed = 1;
            PrepareSwitch();
            ClearSandbox();
            if (_map.Build(seed) == null)
            {
                EnterSandbox();
                return false;
            }
            if (RenderSettings.sun != null) RenderSettings.sun.enabled = false;
            SetMapAtmosphere(true, Deadfall.Rules.World.EnvironmentCatalog.IndexOf(seed));
            _map.MoveLocalPlayerToSpawn();
            return true;
        }

        /// <summary>Back to base: the M1 sandbox world with FOB Halcyon, player at the FOB.</summary>
        public void EnterSandbox()
        {
            PrepareSwitch();
            _map.Clear();
            SetMapAtmosphere(false);
            if (_world == null) BuildSandbox();
            if (_waves != null)
            {
                _waves.ClearSpawnPoints();
                AddPerimeterSpawnPoints(_waves);
            }
            GoToFob();
        }

        private void PrepareSwitch()
        {
            if (_waves != null) _waves.StopRun();
            if (EnemyManager.Current != null) EnemyManager.Current.DespawnAll();
        }

        private void BuildSandbox()
        {
            _world = new GameObject("[SandboxWorld]").transform;
            EnsureLight();
            BuildGround();
            BuildShootingRange();
            BuildMeleePit();
            BuildMovementCourse();
            BuildZones();
            BuildFob();
            // Bake after all geometry exists. Layer 2 (Ignore Raycast) holds players, so they don't carve holes.
            Physics.SyncTransforms();
            _navMesh.Build(new Bounds(new Vector3(0f, 3f, 20f), new Vector3(92f, 12f, 132f)), ~(1 << PlayerFactory.PlayerLayer));
        }

        private void BuildFob()
        {
            FobBuilder.Built fob = FobBuilder.Build(_world, FobOrigin);
            _fobSpawn = fob.Spawn;
            _fobYaw = fob.SpawnYaw;
            FobLabels.Register(fob.Terminals);
            // A console at the range leads back.
            GameObject back = Box("FobReturnConsole", new Vector3(8f, 0.55f, -3f), new Vector3(1.6f, 1.1f, 0.8f), new Color(0.85f, 0.65f, 0.2f));
            var tp = back.AddComponent<FobTeleporter>();
            tp.Prompt = "Return to FOB Halcyon";
            tp.Action = GoToFob;
            InteractGlow.Add(back, InteractGlow.Objectives);
        }

        private void ClearSandbox()
        {
            if (_world == null) return;
            TrainingDummy.ClearRegistry();
            _navMesh.Clear();
            // Out of the way at once (the bake for the map happens this frame), destroyed at the end of it.
            _world.gameObject.SetActive(false);
            Destroy(_world.gameObject);
            _world = null;
        }

        /// <summary>
        /// Spawn points along the inside of each perimeter wall, one group (arrival direction) per wall. The
        /// director picks points out of sight and at least 15 m from every player.
        /// </summary>
        private static void AddPerimeterSpawnPoints(WaveManager waves)
        {
            const float inset = 3f, spacing = 9f;
            const float minX = -45f + inset, maxX = 45f - inset, minZ = -45f + inset, maxZ = 85f - inset;
            for (float x = minX; x <= maxX + 0.01f; x += spacing)
            {
                waves.AddSpawnPoint(new Vector3(x, 0f, maxZ), 0); // north
                waves.AddSpawnPoint(new Vector3(x, 0f, minZ), 2); // south
            }
            for (float z = minZ + spacing; z <= maxZ - spacing + 0.01f; z += spacing)
            {
                waves.AddSpawnPoint(new Vector3(maxX, 0f, z), 1); // east
                waves.AddSpawnPoint(new Vector3(minX, 0f, z), 3); // west
            }
        }

        private static void DisableSceneCameras()
        {
            foreach (Camera cam in Camera.allCameras)
                cam.gameObject.SetActive(false);
        }

        /// <summary>
        /// Generated maps: darker ambient and a thin haze so far rooms fade and the room lights do the work (T-59).
        /// Fog keywords are kept in builds by Graphics settings (fog stripping set to custom, all modes kept).
        /// </summary>
        private static void SetMapAtmosphere(bool map, int environment = 0)
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = map ? new Color(0.2f, 0.21f, 0.24f) : new Color(0.32f, 0.34f, 0.38f);
            RenderSettings.fog = map;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.022f;
            RenderSettings.fogColor = new Color(0.05f, 0.055f, 0.065f);
            if (map && environment == 1)
            {
                // Harrow Line: damp, greener and a little thicker underground.
                RenderSettings.ambientLight = new Color(0.17f, 0.2f, 0.19f);
                RenderSettings.fogDensity = 0.03f;
                RenderSettings.fogColor = new Color(0.04f, 0.06f, 0.055f);
            }
        }

        private void EnsureLight()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.32f, 0.34f, 0.38f);
            if (RenderSettings.sun != null) { RenderSettings.sun.enabled = true; return; }
            if (Object.FindAnyObjectByType<Light>() != null && Object.FindAnyObjectByType<Light>().type == LightType.Directional) return;
            var go = new GameObject("Sun");
            go.transform.SetParent(_world, false);
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
        }

        private void BuildGround()
        {
            Box("Ground", new Vector3(0f, -0.25f, 20f), new Vector3(90f, 0.5f, 130f), new Color(0.3f, 0.31f, 0.3f));
            // Perimeter walls
            Box("WallN", new Vector3(0f, 2f, 85f), new Vector3(90f, 4f, 1f), new Color(0.25f, 0.26f, 0.28f));
            Box("WallS", new Vector3(0f, 2f, -45f), new Vector3(90f, 4f, 1f), new Color(0.25f, 0.26f, 0.28f));
            Box("WallE", new Vector3(45f, 2f, 20f), new Vector3(1f, 4f, 130f), new Color(0.25f, 0.26f, 0.28f));
            Box("WallW", new Vector3(-45f, 2f, 20f), new Vector3(1f, 4f, 130f), new Color(0.25f, 0.26f, 0.28f));
        }

        private void BuildShootingRange()
        {
            // Lane straight ahead of spawn (+Z). Markers every 10 m.
            for (int d = 10; d <= 80; d += 10)
                Box($"Marker{d}m", new Vector3(-3f, 0.02f, d), new Vector3(1.2f, 0.04f, 0.2f), d % 50 == 0 ? new Color(0.9f, 0.7f, 0.2f) : new Color(0.7f, 0.7f, 0.7f));

            float[] distances = { 5f, 15f, 30f, 50f, 80f };
            foreach (float d in distances) TrainingDummy.Create(new Vector3(0f, 0f, d), _world);
            TrainingDummy.Create(new Vector3(5f, 0f, 20f), _world, 150f, strafeDistance: 3f);
            TrainingDummy.Create(new Vector3(-6f, 0f, 25f), _world, 1000f); // high-HP target for full-magazine DPS checks

            // Cover pieces at the range.
            Box("Cover1", new Vector3(3f, 0.5f, 10f), new Vector3(2f, 1f, 0.4f), new Color(0.4f, 0.38f, 0.33f));
            Box("Cover2", new Vector3(-2f, 0.6f, 35f), new Vector3(2.5f, 1.2f, 0.4f), new Color(0.4f, 0.38f, 0.33f));
        }

        private void BuildMeleePit()
        {
            Vector3 c = new Vector3(-14f, 0f, 4f);
            Box("MeleePitFloor", c + new Vector3(0f, 0.01f, 0f), new Vector3(8f, 0.02f, 8f), new Color(0.35f, 0.28f, 0.25f));
            TrainingDummy.Create(c + new Vector3(-1.2f, 0f, 1.5f), _world);
            TrainingDummy.Create(c + new Vector3(0f, 0f, 2f), _world);
            TrainingDummy.Create(c + new Vector3(1.2f, 0f, 1.5f), _world);
            TrainingDummy.Create(c + new Vector3(0f, 0f, -2.5f), _world); // behind: must NOT be hit by a forward swing
        }

        private void BuildMovementCourse()
        {
            float x = 18f;
            Color ramp = new Color(0.42f, 0.45f, 0.5f);

            // Ramps at increasing angles (slope limit is 46°, so the 50° ramp should block).
            float[] angles = { 15f, 30f, 45f, 50f };
            for (int i = 0; i < angles.Length; i++)
            {
                float a = angles[i];
                float len = 6f;
                var go = Box($"Ramp{a}", Vector3.zero, new Vector3(2.5f, 0.3f, len), ramp);
                go.transform.position = new Vector3(x + i * 3.5f, Mathf.Sin(a * Mathf.Deg2Rad) * len * 0.5f, 8f + Mathf.Cos(a * Mathf.Deg2Rad) * len * 0.5f);
                go.transform.rotation = Quaternion.Euler(-a, 0f, 0f);
            }

            // Stairs: 0.2 m and 0.3 m steps should be walkable, 0.45 m steps should need a jump.
            BuildStairs(new Vector3(x, 0f, 20f), 0.2f, 10);
            BuildStairs(new Vector3(x + 4f, 0f, 20f), 0.3f, 7);
            BuildStairs(new Vector3(x + 8f, 0f, 20f), 0.45f, 4);

            // Jump boxes: 0.5 m (step/jump), 0.9 m (max jump), 1.3 m (too high).
            float[] heights = { 0.5f, 0.9f, 1.3f };
            for (int i = 0; i < heights.Length; i++)
                Box($"JumpBox{heights[i]}", new Vector3(x + i * 3.5f, heights[i] * 0.5f, 34f), new Vector3(2f, heights[i], 2f), new Color(0.5f, 0.42f, 0.3f));

            // Crouch tunnel: 1.3 m clearance (standing height is 1.8 m).
            Vector3 t = new Vector3(x + 12f, 0f, 30f);
            Box("TunnelRoof", t + new Vector3(0f, 1.3f + 0.15f, 0f), new Vector3(2.4f, 0.3f, 6f), new Color(0.3f, 0.3f, 0.35f));
            Box("TunnelWallL", t + new Vector3(-1.35f, 0.9f, 0f), new Vector3(0.3f, 1.8f, 6f), new Color(0.3f, 0.3f, 0.35f));
            Box("TunnelWallR", t + new Vector3(1.35f, 0.9f, 0f), new Vector3(0.3f, 1.8f, 6f), new Color(0.3f, 0.3f, 0.35f));

            // Narrow corridor with a corner, for strafing and collision checks.
            Box("CorridorA", new Vector3(x + 2f, 1.5f, 45f), new Vector3(0.3f, 3f, 10f), new Color(0.32f, 0.33f, 0.36f));
            Box("CorridorB", new Vector3(x + 4f, 1.5f, 43f), new Vector3(0.3f, 3f, 6f), new Color(0.32f, 0.33f, 0.36f));
            Box("CorridorC", new Vector3(x + 7f, 1.5f, 50f), new Vector3(10f, 3f, 0.3f), new Color(0.32f, 0.33f, 0.36f));
        }

        private void BuildStairs(Vector3 start, float stepHeight, int steps)
        {
            float depth = 0.35f;
            for (int i = 0; i < steps; i++)
            {
                float h = stepHeight * (i + 1);
                Box($"Step{stepHeight}_{i}", start + new Vector3(0f, h * 0.5f, i * depth), new Vector3(2.5f, h, depth), new Color(0.45f, 0.45f, 0.48f));
            }
        }

        private void BuildZones()
        {
            Zone("HazardPad", new Vector3(-8f, 0f, -8f), SandboxZone.ZoneKind.Damage, 10f, new Color(0.7f, 0.15f, 0.12f));
            Zone("HealPad", new Vector3(-3f, 0f, -8f), SandboxZone.ZoneKind.Heal, 15f, new Color(0.2f, 0.65f, 0.3f));
            Zone("AmmoPad", new Vector3(2f, 0f, -8f), SandboxZone.ZoneKind.Ammo, 0f, new Color(0.25f, 0.45f, 0.75f));
        }

        private void Zone(string zoneName, Vector3 pos, SandboxZone.ZoneKind kind, float amount, Color color)
        {
            var marker = Box(zoneName, pos + new Vector3(0f, 0.015f, 0f), new Vector3(3f, 0.03f, 3f), color);
            Object.Destroy(marker.GetComponent<Collider>());
            var zone = new GameObject(zoneName + "Zone").AddComponent<SandboxZone>();
            zone.transform.SetParent(_world, false);
            zone.transform.position = pos;
            zone.Configure(kind, new Vector3(3f, 2f, 3f), amount);
        }

        private GameObject Box(string boxName, Vector3 position, Vector3 size, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = boxName;
            go.transform.SetParent(_world, false);
            go.transform.position = position;
            go.transform.localScale = size;
            go.isStatic = true;
            if (!SurfaceArt.ApplyAuto(go, color)) Tint(go, color);
            return go;
        }

        /// <summary>Applies a flat lit color that also works in builds (see RuntimeMaterials).</summary>
        public static void Tint(GameObject go, Color color)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = RuntimeMaterials.CreateLit(color);
        }
    }
}
