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

## M2 – Zombie AI and combat ✅
**Part 1 (offline)** ✅ merged as #14, checked in the editor 2026-11-02
1. ✅ Rules: `EnemyBrain` (idle → chase → wind-up → strike → recover, charge wind-up → charge, stagger, death; time-driven so identical at any tick rate), `StaggerMeter` (decaying build-up, immunity window), `EnemyStats`. New `HitZone.Armor` for plating. 16 EditMode tests.
2. ✅ `EnemyDefinition` asset type and three prototype enemies in code (`PrototypeEnemies`): **Husk** (baseline), **Bolter** (fast, weaving approach, lunge), **Bulwark** (650 HP, front armor plate ×0.25, weak-point growth on the back ×2.5, unstaggerable charge).
3. ✅ `Enemy` body: health, head/body/armor/weak-point hurtboxes, NavMeshAgent movement, strike arc check, charge contact, placeholder poses for wind-ups and staggers, death and corpse timer.
4. ✅ `EnemyManager`: per-type pools, alive cap from D-19 (24 + 8 per extra player), one update loop, perception (target + re-path) at 8 Hz spread across frames, smoothed AI cost in ms.
5. ✅ `CombatTargets` registry (players register themselves) so AI never searches the scene.
6. ✅ `RuntimeNavMesh` baking with Unity's built-in `NavMeshBuilder` (T-21); the sandbox bakes at startup.
7. ✅ Sandbox hotkeys F5–F8, F11, `[` `]`, enemy count and AI cost on the overlay.
8. ✅ Feel check in the editor (`TESTING.md` §3.2). Number tuning continues with waves in M3.

**Part 2 (co-op)** ✅ merged as #15, two-player check passed 2026-11-02
1. ✅ `EnemyReplicator`: the host sends every enemy at 12 Hz in 12 bytes (slot id, type, position in 2 cm steps, yaw in 256 steps, state, health %), split into messages of at most 90 enemies so each fits one packet; despawns are sent reliably. About 7 KB/s per client at 48 enemies.
2. ✅ `EnemyManager` mirror mode on clients: pooled copies created from snapshots, smoothed toward each update, wind-up and stagger poses replayed from the host's state, removed on despawn or after 3 s without news.
3. ✅ Host enemies hunt remote players too (proxies register as combat targets); their hits are sent to that player's machine, which applies them with its own damage reduction.
4. ✅ Client hits on enemies go through the host's hit validation (same path as dummies, with extra tolerance for enemy movement).
5. ✅ The alive cap grows with connected players (D-19).
6. ✅ Two-player check (`TESTING.md` §3.3). The 50 / 100 enemy profile in a Development Build is still open and carries into M3 testing.

## M3 – Wave management ✅
1. ✅ Rules: `DifficultyCurve` (threat budget by surge, player count, difficulty and the ±10% adaptive factor from D-14; health scale capped at ×2.5 by surge 30; concurrency; arrival directions).
2. ✅ Rules: `WavePlanner` spends the budget on unlocked enemy types by weight, with a per-type budget share cap, a hard cap of 400 spawns, at least one enemy, and the same plan for the same seed. Surge types: **Standard**, **Swarm** (×1.3 budget, cheap types only, ×1.5 at once), **Siege** (3+ directions, heavies 2 surges early). Rotation in T-23.
3. ✅ Rules: `SurgeRun` state machine: Lull → Surge → Reward → Lull, Complete after the final surge (20), Failed when the whole squad is down. Lull ends on its timer or when every player is ready. Deadlock guards: stragglers are removed after 40 s without a kill, and a surge that cannot spawn ends after 10 min.
4. ✅ Rules: Field Credits (T-24): kills pay 10 per threat point to the killer, clearing a surge pays every player 50 + 10 × surge. `CreditLedger` keeps them per player (D-13).
5. ✅ `SpawnDirector`: spawns the plan over time under the surge's concurrency and the enemy cap, only from the surge's chosen directions, at least 15 m from every player and out of sight when possible; skips an enemy after 25 failed tries so a surge can't hang.
6. ✅ `WaveManager`: runs the loop on the host, pays credits using the new `PlayerIdentity` component (local player and remote stand-ins), sends a small state message to clients (phase, surge, type, time left, enemies left, ready count, credits) twice a second and on every change, and takes ready votes from clients.
7. ✅ Enemy data: `EnemyDefinition` gains unlock surge, spawn weight and budget share. Husk from surge 1, Bolter from 3, Bulwark from 6 (at most 40% of a budget). Enemies take a health scale when spawned.
8. ✅ HUD: surge panel (surge, type, enemies left, lull timer, ready count), Field Credits, big announcements, credit pop-ups. Sandbox: N starts or stops a run, G toggles ready, spawn points along the four walls.
9. ✅ 23 new EditMode tests (`WaveTests.cs`).
10. ✅ Play check in the editor (`TESTING.md` §3.4), passed 2026-11-02 after #18 (surge enemies now hunt from any distance).

Not in scope yet: ~~Elite, Hunt, Supply and Specimen surges~~ (done in M7), ambushes during lulls, spending credits (Supply Caches, M6), the adaptive factor's inputs (squad downs and health trend; the curve already accepts it).

## M4 – Class framework ✅
**Part 1 (framework, Gunner, Tank, Warrior)** ✅ merged as #19
1. ✅ Rules (`Deadfall.Rules.Classes`): `AbilityTimer` (ready → active → cooldown, cooldown starts when the effect ends per T-26, early end, cooldown rate and refunds), `ClassRules` (Guard reduction, sustained-fire recoil, combining reductions), `MeleeCombo`, `DoctrineResolver` (pairs on different players, 4 distinct classes, at most 2 per player in list order).
2. ✅ `WeaponStateMachine` handling modifiers: fire rate, reload speed, equip speed, free reloads, reserve capacity.
3. ✅ Player hooks: `WeaponHandling` / `IWeaponHandlingSource` (fire rate, reload, swap, recoil, bullet and melee damage), one-shot `MeleeBonus` (damage, stagger, wider arc, extra targets), sustained-fire time, `PlayerMotor.Dash`, `PlayerHealth.BonusDamageReduction`, forced stagger on enemies. Client hit claims now carry the melee flag, stagger flag and a class damage bonus capped at ×3 (T-28).
4. ✅ `ClassDefinition`, `AbilityDefinition`, `PassiveDefinition` assets (numbers in data, behaviour picked by id), `PlayerClass` runner (Q, cooldowns, passives, preferred-weapon handling: reload ×1.15, swap ×1.25, recoil ×0.85 per T-27), `PrototypeClasses` with all five classes.
5. ✅ Abilities: **Overdrive** (Gunner), **Bulwark Shield** (Tank: 600 HP wall that enemies within 12 m attack instead, via the new `IAggroTarget`), **Lunge** (Warrior: 8 m dash ending in a ×3 staggering strike).
6. ✅ Passives: Deep Pockets, Steady Hands, Guard, Bloodlust, Combo. The rest are listed and marked "soon" until their systems exist.
7. ✅ Class select on the pause screen (remembered), class and ability HUD, Squad Doctrine table in data.
8. ✅ 17 new EditMode tests (`ClassRulesTests.cs`).
9. ✅ Approved by the user after their check, 2026-11-02 (`TESTING.md` §3.5).

**Part 2 (Engineer, Demolitionist, co-op)** ✅ merged as #20
1. ✅ `Explosions.Detonate` in Combat: shared area damage (full at the centre to 30% at the edge), knockback, never hurts other players, the thrower takes a share (T-30).
2. ✅ **Cluster Charge** (Demolitionist): thrown, bursts on impact (90 damage, 4.5 m), scatters 5 bomblets (45 damage, 3 m each). **Blast Padding** halves self damage.
3. ✅ **Deploy Sentry** (Engineer): turret for 60 s, 22 m range, 6 shots/s at 9 damage, line of fire required, 300 HP, enemies can attack it. New `Hostiles` registry so turrets never search the scene.
4. ✅ **Shove** (Tank): melee pushes enemies back about 2.5 m (heavier enemies less), also for clients (claims carry knockback, capped at 4 m).
5. ✅ `ClassNetwork` (T-29): class roster shared through the host (remote players' labels show their class); shields, sentries and charges are created by the host, which deals the damage and holds the aggro, and clients get look-alike copies that vanish with the host's. Losing a shield or sentry starts its cooldown.
6. ✅ Squad Doctrines resolved from the roster and shown on the HUD. Effects with placeholder numbers (D-17): **Vanguard** +25% Lunge distance, **Combined Arms** cooldowns 15% faster. Covering Fire, Overwatch and Demolition Crew are listed until the systems they modify exist.
7. ⏸ Feel check in the editor and in co-op (`TESTING.md` §3.6): the user is holding all testing until M10.

## M5 – Procedural map generation ✅ (editor check held until M10)
1. ✅ Rules (`Deadfall.Rules.World`): grid, typed sockets, room templates with roles and tags, `GraphTemplate`, `LayoutGenerator` (spawn → main route with corridor pieces → objective → boss arena → side branches → secret vent rooms; retries with sub-seeds, then a known-good fallback seed), `LayoutValidator` (overlap, socket compatibility, reachability, required rooms, sectors open in order, spawn and loot counts, Supply Cache in the spawn sector, bounded size), `SectorSchedule`.
2. ✅ Cinder Ridge greybox kit (`CinderRidgeKit`): decon airlock, lab bay, server hall, cold storage, cafeteria, security checkpoint, reactor control (objective), specimen containment (boss), vent crawl (secret), four corridor pieces and maintenance tunnels. One floor for now.
3. ✅ `MapBuilder`: floors, ceilings, walls with doorways only where sockets connect, a light per room, cover props, player, enemy and loot points. One NavMesh bake for the whole map with doorways open; locked doors carve it while closed (T-32).
4. ✅ Sectors: locked `SectorDoor`s open after surges 3, 7 and 12 (T-33); only open sectors feed spawn points to the wave director. Run start closes them again and returns everyone to the airlock.
5. ✅ Loot: ammo crates, medkits and secret stashes (full refill plus 250 credits) on randomised slots (D-05), taken through the host so only one player gets each (T-35). Supply Cache slots are placed and filled in M6.
6. ✅ Secret vents: breakable panels, resolved by the host through the new `INetTarget` id path (T-34).
7. ✅ Co-op: only the seed travels (T-09). `df.map` on join and regenerate, `df.sector`, `df.secret`, `df.loot*`.
8. ✅ Debug: seed field, Random, Back to sandbox and Open next sector on the pause screen; M minimap overlay (was F9, which clashed with respawn) with validation result; scene-view gizmos.
9. ✅ Also: Interact key (F) with `IInteractable` and HUD prompts, explosions blocked by walls (T-30 follow-up), wave spawns accept points 7 m away when nothing is 15 m away.
10. ✅ 10 new EditMode tests (`LayoutTests.cs`), including 3000 seeds validated.

## M6 – Weapons and run progression ✅ (editor check held until M10)
1. ✅ Full prototype roster in `WeaponCatalog` (8): Halberd AR, Breacher-12, Ranger-9, Saber-7, plus **Wasp SMG**, **Anvil LMG**, **Tether Cleaver** and the **Mule Launcher** (impact grenades through the new projectile service, host-authoritative in co-op, T-38).
2. ✅ Tiers (Standard / Refined / Prototype) and 12 attachments across the six slots (`WeaponMods`, `WeaponModder`, `AttachmentCatalog`). Each weapon in a loadout has its own effective stats; damage bonuses travel as the capped outgoing multiplier (T-39).
3. ✅ Supply Caches on the map's cache slots: three seeded offers (better tiers in later sectors) and an ammo refill, bought with Field Credits through the host (T-36).
4. ✅ Weapon drops from kills during runs: tier and attachments rolled, finder-only for 10 s then shared (D-06), taken through the host.
5. ✅ Starting loadouts per class (two primaries, sidearm, melee); everything temporary resets at deployment and run start.
6. ✅ Field Mods: offers after every third surge, 3 choices, Common/Rare/Legendary, class pools, Unique/Stacks/Evolves (Vampiric Rounds → Blood Harvest), T to pick (T-37). Effects: damage, fire rate, reload, recoil, reserve, melee, move speed, max health, damage reduction, cooldowns, heal on kill/clear, low-health damage, kill fire-rate burst.
7. ✅ Weapon XP foundation (`WeaponXp` curve, per-weapon XP from kills, level-up toasts; PlayerPrefs until M10's save).
8. ✅ 11 new EditMode tests (`RunEconomyTests.cs`).

## M7 – Advanced enemies and encounters ✅ (editor check held until M10)
1. ✅ Brain: ranged attacks (hold at range with line of sight, wind-up, cooldown), exploders (commit, can't be staggered out, burst on contact or death), regeneration, per-enemy stat copies (T-40).
2. ✅ New enemies in `PrototypeEnemies` (appended, network order kept): **Creeper** (low crawler), **Pyre** (goes for the biggest cluster, blast hurts players and infected; chain kills credit whoever shot it), **Retcher** (lobbed acid glob that leaves a pool), **Alpha** (elite, aura buffs nearby infected, ×3 glowing growth), **Goliath** (elite heavy), **The Stalker** (Hunt pursuer), **Specimen Zero**.
3. ✅ Variants (`VariantRules`): Regenerating, Splitting (bursts into two Creepers), Hardened, Swift, with coloured bands as the visual tell; +50% kill credits per trait.
4. ✅ Specimen framework (`SpecimenRules`): three phases by health (stalk; rampage with charges and summons, back growths open; exposed core ×3 with acid), phase roar, modifiers (Enraged, Armored, Brood) in late surges, extra health per player.
5. ✅ Surge types Elite, Hunt, Supply and Specimen in the planner and rotation (T-44); hunters and bosses are placed, never bought.
6. ✅ Boss rewards: 1000 credits each, a guaranteed Prototype weapon drop, a Field Mod offer.
7. ✅ Supply surges drop ammo crates and medkits near the squad (host-authoritative pickups).
8. ✅ Co-op: one extra snapshot byte for traits and boss phase (T-41), attack visuals relayed to clients (T-42).
9. ✅ HUD: boss bar with phase ticks, phase and modifiers; Hunt pursuer bar; phase banners. Sandbox: F12 spawns the next advanced enemy (Ctrl for a variant).
10. ✅ 18 new EditMode tests (`AdvancedEnemyTests.cs`).

Not in scope yet: Creepers using vents (vent sockets exist in the map kit; needs NavMesh links), arena hazards for Specimens, damage-type armor, ambushes during lulls.

## M8 – Objectives, events, environment ✅ (editor check held until M10)
1. ✅ Objective framework (`Deadfall.Rules.Encounters`): `ObjectiveTracker` (progress, complete once, fail, expiry by surge, required-complete check), `ChannelProgress` (stand-near channels that drain at half speed when left), `HoldTimer` (extraction pauses when the zone is empty), `ObjectiveRules` (counts, timings, rewards, side-objective schedule), `MissionTemplate`.
2. ✅ Objective types: restart 3 generators, recover 3 sample cases, destroy a nest (births Creepers during surges), download a data core, keycard plus security locker (reward weapon), hack the lockdown terminal, hold extraction.
3. ✅ Survival: an optional side objective in the lull before surges 3, 7, 11... lasting two surges, credits for everyone on completion (GAME_DESIGN §3.1).
4. ✅ Operations (first mission **Operation Cold Start**): pause-screen button on a generated map. All sectors open, required and optional objectives run together, surges come on a 35 s timer, and once the required ones are done a 150 s extraction defense starts at the airlock (4 s lulls). Holding it completes the run with a 1500-credit bonus (GAME_DESIGN §3.2).
5. ✅ Random events (`EventScheduler`): at most one, from surge 4, 30% chance, 3-surge cooldown, never on Specimen or Hunt surges, warned a lull ahead. **Blackout** (lights to a glimmer, auto flashlight, L toggles it; restarting the generators restores power early), **The Stalker** (joins the surge; Prototype drop when killed), **Emergency Lockdown** (sector doors seal; a terminal appears where the squad is).
6. ✅ Co-op: one host state message (`df.obj`) carries objectives, props, nests, events, extraction and announcements; props are used through the host (`df.objuse`); nests take client hits by id (T-34).
7. ✅ HUD: objective list, event line, extraction bar, objective banners. The boss bar moved below the wave panel.
8. ✅ 10 new EditMode tests (`ObjectiveTests.cs`).

Not in scope yet: the power grid feeding doors and turrets (generators only end Blackouts for now), Red Sky and Containment Breach events, objective-driven sector unlocks in Operations (all sectors open at once, T-47), more mission templates, failing objectives (destroyable sample containers).

## M9 – Engineering ✅ (editor check held until M10)
1. ✅ Rules (`Deadfall.Rules.Engineering`): `StructureCatalog` (three tiers each), `ScrapRules` (kills, surge clears, objectives, Engineer +50%, non-Engineer cost ×1.5, dismantle refund), `RepairRules` (chunked repairs, Engineers better), `PlacementRules` validator with a reason for every refusal, `ScrapLedger`.
2. ✅ Shared Scrap for the squad (D-13), starting at 60 per run; building is free outside runs so the sandbox can be used to try structures.
3. ✅ **Sentry** (ammo-limited, refill with Scrap, enemies attack it), **Drone** (follows its Engineer; attack mode shoots, repair mode beams health into structures; Z switches), **Arc Fence** (slows and shocks infected crossing it, drains charge, wears down), **Barricade** (carves the NavMesh; enemies cut off from the squad tear it down, so a path is never blocked for good; anyone can build it, Engineers cheaper).
4. ✅ Build mode on B with a ghost preview (green or red with the reason): ground angle, clearance, distance, NavMesh, and distance from enemy spawn points, objectives, sector doors and the deployment point.
5. ✅ Upgrades (U, tiers 2 and 3), repairs and refills (F), dismantle (X, half refund), destruction by enemies.
6. ✅ Perf caps: 16 structures per squad, per-player caps per type, 4 Hz targeting, 10 Hz fence checks.
7. ✅ Co-op: host-authoritative (`df.build`, `df.sact`, state in `df.eng`, refusals in `df.engmsg`); drones fly beside their owner on every machine.
8. ✅ Demolition Crew doctrine is live: a Demolitionist's blasts repair structures by 10% when the squad has an Engineer.
9. ✅ 8 new EditMode tests (`EngineeringTests.cs`).

Not in scope yet: the Arc Fence drawing on the map's power grid, the Overwatch and Covering Fire doctrine effects, structure upgrade branches from the Engineer specialization tree (M10+).

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
