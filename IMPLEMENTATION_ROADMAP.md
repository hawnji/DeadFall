# Implementation Roadmap

Each milestone ends with: compiles cleanly, tests green, acceptance criteria checked, docs updated, known issues logged in `TESTING.md`. Networking is woven in from M1 rather than left for M11.

Legend: ✅ done · 🔄 in progress · ⬜ not started

## M0 – Project inspection and technical plan ✅
- ✅ Inspect environment: repo `hawnji/Zamby` was empty; no Unity project existed
- ✅ Engine and networking recommendation (Unity 6 LTS + URP, NGO 2.x host mode)
- ✅ Architecture, folder structure, dependency diagram
- ✅ Game design baseline with original names
- ✅ Roadmap, testing strategy, risk list, conventions
- ✅ Open decisions listed with recommended defaults
- ✅ `Assets/_Project` folder tree and assembly definitions
- ⬜ User creates the Unity project per `README.md` (requires the Unity editor)
- ✅ Approval of open decisions (`DECISIONS.md`, 2026-11-01)

## M1 – Playable first-person foundation ✅
1. ✅ Rules: `WeaponStateMachine` (semi/auto, exact fire rate at any FPS, mag/reserve, tactical/empty reload, commit point for interruption), `DamageCalculator` (range falloff, zones, crits, reduction), `SpreadModel`, `HealthModel` (segmented regen), `MotionMath`, `ModifierStack`, `DeterministicRandom`. 38 EditMode tests.
2. ✅ Core: `GameBootstrap`, `ServiceRegistry`, `EventBus<T>`, `GameLog`, `GameSettings` + PlayerPrefs settings service. Fast enter-play-mode safe.
3. ✅ Input: actions defined in code in `PlayerInputReader` (keyboard/mouse + gamepad bindings). An `.inputactions` asset with rebinding comes with the settings menu.
4. ✅ `PlayerMotor` (exact frame-rate independent acceleration, sprint cone, jump with coyote time and buffering, crouch with headroom check) + `PlayerLook` (raw delta, recoil with recovery, horizontal FOV).
5. ✅ `PlayerHealth` (IDamageable), hazard/heal/ammo pads in the sandbox.
6. ✅ `PlayerWeapons`: hitscan with pellets and spread, recoil, reload, swap, ADS, sprint-to-fire delay, quick melee and melee weapons, all through `IHitResolver` (local now, host later). Four placeholder weapons (AR, shotgun, pistol, sword).
7. ✅ `PrototypeHud` (IMGUI placeholder): health, ammo, weapon, reload bar, spread-accurate crosshair, hit/crit/kill markers, damage direction.
8. ✅ Runtime-built sandbox (`SandboxBootstrap`): range with distance markers and strafing target, melee pit with a target behind the player, ramps 15–50°, stairs 0.2/0.3/0.45 m, jump boxes, crouch tunnel, corridor, pads; debug overlay with FPS, frame cap hotkeys, damage numbers.
9. ⬜ Net spike: host + one client see each other move and shoot. **Moved to M1.5** so the feel pass happens first and NGO code can be compiled in the editor (packages aren't reachable from the cloud environment).
10. ✅ PlayMode tests: walk distance equal at 30/60/144 FPS, sprint vs strafe, stopping distance, headshot multiplier, self-ignore raycast, melee arc front/back, one hit per target, dead targets.
11. ✅ In-editor feel check against `TESTING.md` §3 (2026-11-02, "looks good for now"). Number tuning continues as later milestones land.

**Acceptance**: brief's M1 criteria, verified in the editor.

## M1.5 – Networking spike ✅
1. ✅ Netcode for GameObjects 2.13.3 added to `Packages/manifest.json`.
2. ✅ `Deadfall.Net/SessionService`: creates the NetworkManager and Unity Transport from code; host, join by IP, leave; peer joined/left events; named-message helpers (send to host, to one client, to all remote clients). Connection failures and host loss end the session with a readable reason.
3. ✅ Sandbox spike (`NetSpikeSync`, `RemotePlayerProxy`, `NetSpikePanel`): each player sends its own position, view, crouch, death and health at 20 Hz (owner-authoritative); others see a smoothed capsule with a name and health label; tracers are relayed.
4. ✅ Hits: clients swap the hit resolver for one that sends claims to the host. The host checks rate (60 claims/s), shooter origin, hit point near the target, a known weapon id, and range, then applies damage with **its own** weapon stats, confirms the hit to the shooter (hit marker) and broadcasts dummy health to everyone, including a full sync on join.
5. ✅ Host/Join/Leave panel on the pause screen, status line in the corner, command-line flags for builds.
6. ✅ Two-player check in the editor + a Development Build (`TESTING.md` §3.1), passed 2026-11-02.

**Acceptance**: host and client see each other move and shoot; both see the same dummy health after either one shoots it; a client leaving or the host closing is handled without errors.

Not in scope (comes later): players as NetworkObjects, NetworkTransform, interpolation buffers, Relay/Lobby, player-vs-player damage, reconnect (M11).

## M2 – Zombie AI and combat 🔄 in progress
**Part 1 (offline), in review**
1. ✅ Rules: `EnemyBrain` (idle → chase → wind-up → strike → recover, charge wind-up → charge, stagger, death; time-driven so identical at any tick rate), `StaggerMeter` (decaying build-up, immunity window), `EnemyStats`. New `HitZone.Armor` for plating. 16 EditMode tests.
2. ✅ `EnemyDefinition` asset type and three prototype enemies in code (`PrototypeEnemies`): **Husk** (baseline), **Bolter** (fast, weaving approach, lunge), **Bulwark** (650 HP, front armor plate ×0.25, weak-point growth on the back ×2.5, unstaggerable charge).
3. ✅ `Enemy` body: health, head/body/armor/weak-point hurtboxes, NavMeshAgent movement, strike arc check, charge contact, placeholder poses for wind-ups and staggers, death and corpse timer.
4. ✅ `EnemyManager`: per-type pools, alive cap from D-19 (24 + 8 per extra player), one update loop, perception (target + re-path) at 8 Hz spread across frames, smoothed AI cost in ms.
5. ✅ `CombatTargets` registry (players register themselves) so AI never searches the scene.
6. ✅ `RuntimeNavMesh` baking with Unity's built-in `NavMeshBuilder` (T-21); the sandbox bakes at startup.
7. ✅ Sandbox hotkeys F5–F8, F11, `[` `]`, enemy count and AI cost on the overlay.
8. ⬜ Feel check and first tuning pass in the editor (`TESTING.md` §3.2).

**Part 2 (next)**: enemy snapshot channel so co-op clients see the host's enemies (slot id, quantised position, yaw, state, health %, 10–15 Hz), host enemies target remote players, client hits on enemies through the host resolver. Then profile 50 / 100 enemies in a build.

## M3 – Wave management ⬜
`DifficultyCurve`, `WavePlanner` threat budget, `WaveManager` states (Lull → Surge → Reward), `SpawnDirector` with concurrency cap, special surge types (Swarm, Siege), announcements, Field Credit rewards. Deadlock tests.

## M4 – Class framework ⬜
`ClassDefinition`, `AbilityDefinition`, `AbilityRunner`, passive modifiers, preferred-weapon bonuses, class select, five prototype abilities, Squad Doctrine resolver (data only, placeholder magnitudes).

## M5 – Procedural map generation ⬜
Room kit for Cinder Ridge (greybox), sockets, `LayoutGenerator` + `LayoutValidator` (Rules, tested on thousands of seeds), runtime builder, NavMesh baking per sector, locked doors, sector unlocks, loot and secret placement, debug overlay, seed reproduction.

## M6 – Weapons and run progression ⬜
Full prototype roster (8), Supply Caches, starting loadouts, drops, attachments, weapon XP foundation, Field Mod offers (3 choices, rarities, class pools, stacking/evolution), run state reset.

## M7 – Advanced enemies and encounters ⬜
Creeper, Pyre, Retcher, Alpha, Variant mutations, mini-bosses, Specimen framework + Specimen Zero, boss rewards and modifiers, Elite/Hunt/Supply surges.

## M8 – Objectives, events, environment ⬜
Objective framework, generators/power grid, samples, keycards, nests, data cores, extraction defense, random events (Blackout and The Stalker first), lockdowns.

## M9 – Engineering ⬜
Scrap, placement validator, Sentry, Drone, Arc Fence, Barricade, upgrades, repair, destruction, perf caps.

## M10 – FOB and permanent progression ⬜
Greybox FOB, seven facilities, loadouts, skill trees, research, currencies, challenges, achievements, prestige foundation, versioned saves.

## M11 – Multiplayer completion ⬜
Lobby/Relay flow, 4-player validation of every system, revives, spectate, reconnect window, host-drop handling, reward duplication tests, bandwidth profiling.

## M12 – Modes and content ⬜
Survival, Operations, Endless complete; second environment; more weapons, bosses, mods, events, objectives.

## M13 – Polish and release prep ⬜
UI/audio/animation/VFX/lighting pass, optimisation, balance, save and MP stability, build automation, release config, final checklist on standalone builds.

## Prototype gate (after M8–M10 subset)
Matches brief Part 23: FOB, one facility, procedural rooms with unlocks, five classes with one ability each, ~8 weapons, 4–5 enemy types + 1 boss, waves, 1–2 events, objectives, Field Mods, rewards, host/join. **Content production waits until this is fun.**
