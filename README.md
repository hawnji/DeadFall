# Project Deadfall

A class-based military zombie survival FPS for PC. Two to four players deploy from a walkable forward operating base into procedurally assembled infected sites, survive escalating waves of the Blight, complete objectives, build temporary run builds, and earn permanent progression.

> Working title. See `DECISIONS.md` (D-20).

## Status

| Milestone | State |
|---|---|
| M0 – Inspection and technical plan | **Done** |
| M1 – Playable first-person foundation | **Done** (feel pass checked in the editor 2026-11-02; tuning continues) |
| M1.5 – Networking spike (host and join) | **Done** (two-player test passed 2026-11-02) |
| M2 – Zombie AI and combat | **Done** (three enemy types, enemies in co-op; two-player check passed 2026-11-02) |
| M3 – Wave management | **Done** (checked in the editor 2026-11-02) |
| M4 – Class framework | Done: five classes, abilities, passives, Squad Doctrines, classes in co-op |
| M5 – Procedural maps | Done (editor check held until M10): Cinder Ridge greybox kit, seeded layouts, sectors, loot, secrets |
| M6 – Weapons and run progression | Done (editor check held until M10): 8 weapons, tiers and attachments, Supply Caches, drops, Field Mods, weapon XP |
| M7 – Advanced enemies and encounters | Done (editor check held until M10): Creeper, Pyre, Retcher, Alpha, Goliath, Stalker, Specimen Zero, variants, Elite/Hunt/Supply/Specimen surges |
| M8 – Objectives, events, environment | Done (editor check held until M10): side objectives, Operation Cold Start with extraction, Blackout / Stalker / Lockdown events |
| M9 – Engineering | Done (editor check held until M10): Scrap, build mode, Sentry, Drone, Arc Fence, Barricade, upgrades, repairs |
| M10 – FOB and permanent progression | Done (editor check: `TESTING.md` §3.12): FOB Halcyon with seven facilities, loadouts, skill trees, research, challenges, achievements, prestige, versioned saves |
| M11 – Multiplayer completion | Done except Lobby/Relay (editor check: `TESTING.md` §3.17): downs, revives, self-revive kits, spectating, returns after surges, 90 s rejoin window, single reward banking, bandwidth counters |
| M12 – Modes and content | Done (editor check: `TESTING.md` §3.18 and §3.19): second environment Harrow Line Transit Depot, Endless mode with escalation, Operation Last Train, five weapons, the Broodmother boss, eight Field Mods, Frenzy and Toxic Leak events, Defend Uplink and Purge Hives objectives |
| M13 – Polish and release prep | Done in code (editor and build checks: `TESTING.md` §3.20, §3.21): synthesised sound effects and ambience, Settings window, release mode with developer tools hidden, build script, models for the M12 weapons and the Broodmother |

M1 adds the first-person controller, hitscan and melee combat, reloads, weapon swapping, health, a prototype HUD, and a sandbox that builds itself at runtime.

## Engine and core stack

| Area | Choice |
|---|---|
| Engine | **Unity 6** (pinned to 6000.6.6f1) |
| Render pipeline | Universal Render Pipeline (URP), Forward+ |
| Language | C# |
| Networking | **Netcode for GameObjects (NGO) 2.x**, host mode |
| Session/relay | Unity Multiplayer Services (Lobby + Relay). Steam transport can be added later |
| Input | Input System package |
| Navigation | AI Navigation package (runtime NavMesh baking) |
| UI | UI Toolkit (menus, HUD) |
| Serialization | Newtonsoft JSON (`com.unity.nuget.newtonsoft-json`) |
| Tests | Unity Test Framework (EditMode + PlayMode) |
| Local multiplayer testing | Multiplayer Play Mode |

Reasoning lives in `DECISIONS.md` (T-01 to T-12).

## Setting up the project

The repo is a Unity project folder. `ProjectSettings/ProjectVersion.txt` pins **Unity 6000.6.6f1**, and `Packages/manifest.json` lists the packages the code needs today.

1. Install **Unity Hub** and editor **6000.6.6f1** with *Windows Build Support*.
2. Clone the repo, then in Unity Hub: **Add → Add project from disk** and pick the repo folder. Run `git lfs install` once.
3. Open it. Unity generates the rest of `ProjectSettings/`, `Packages/packages-lock.json`, and `.meta` files on first open (this takes a few minutes).
4. When the Input System asks to enable the new input backends, click **Yes** and let the editor restart.
5. If Package Manager reports that `com.unity.inputsystem` 1.20.1 isn't available for this editor, open **Window → Package Manager**, select Input System, and install the version it offers.
6. **Project Settings → Editor → Enter Play Mode Settings**: choose "Do not reload Domain or Scene" (our code is written to be safe with this).
7. Commit the generated `ProjectSettings/`, `Packages/packages-lock.json`, and every `.meta` file.

**Packages added later** (install from Package Manager when the milestone needs them, so versions match your editor):

| Package | Needed from |
|---|---|
| Universal RP (then **Assets → Create → Rendering → URP Asset (with Universal Renderer)** and assign it in **Project Settings → Graphics**) | Art and lighting work; optional for M1, the sandbox renders with either pipeline |
| Test Framework | Running the EditMode/PlayMode tests. The test assemblies are skipped until it is installed |
| Netcode for GameObjects | **Already in `manifest.json`** (2.13.3) from M1.5 |
| Multiplayer Play Mode (optional) | Testing two players inside one editor instead of editor + build |
| Multiplayer Services (Relay + Lobby) | M11 online sessions |
| AI Navigation | Not needed so far: the NavMesh is baked at runtime with Unity's built-in API (`DECISIONS.md` T-21) |
| Newtonsoft Json (`com.unity.nuget.newtonsoft-json`) | Not needed yet: the M10 save is plain text (T-54) |

## Running

**Milestone 1 sandbox** (no scene setup needed beyond this):

1. **File → New Scene** (Basic), save it as `Assets/_Project/Scenes/Sandbox/Sandbox_M1.unity`.
2. **GameObject → Create Empty**, then **Add Component → Sandbox Bootstrap**.
3. Press Play. FOB Halcyon (M10), the range, melee pit, movement course, and pads build themselves; the scene's own camera is switched off. You start in the FOB; the Training Range console (or its "Go to the firing range" button) takes you to the range.

Controls: WASD, mouse, Shift sprint, Space jump, Ctrl/C crouch, LMB fire, RMB aim, R reload, V melee, 1–4 or wheel to swap, Esc pause.
Settings (M13): Esc → Settings for volumes, sensitivity, FOV, VSync, frame cap, quality, window mode, FPS counter and damage numbers.
Debug (editor and development builds only, or a release build started with `-devtools`): F1/F2/F3/F4 cap 30/60/144/unlimited FPS and turn VSync off, F9 respawn, F10 god mode, H toggle help.
Enemies (M2): F5 Husk, F6 Bolter, F7 Bulwark, F8 a mixed horde of 12, F11 kill all, `[` / `]` lower or raise the alive cap.
Waves (M3): N starts or stops a survival run (host), G toggles ready during a lull (everyone ready ends it early).
Classes (M4): Esc shows the class list on the left of the pause screen; Q uses the class ability.

Maps (M5): Esc shows the map panel on the right: type a seed and press Generate (or Random) to play a generated Cinder Ridge map, M toggles the map overlay (F9 stays respawn), F uses pickups. Back to sandbox returns here.

Run economy (M6): F opens Supply Caches and takes weapon drops; T picks a Field Mod when one is offered (every third surge, and after a Specimen).

Enemies (M7): F12 spawns the next advanced enemy in the sandbox (hold Ctrl for a mutation variant).

Objectives (M8): F uses objective props; L toggles the flashlight. On a map, Esc → Start Operation Cold Start plays a mission that ends in an extraction defense.

Engineering (M9): B build mode (1-4 pick, R turn, click place), F repair/refill, U upgrade, X dismantle, Z drone mode. Free outside runs.

Enemy animation (T-76): enemy models have movable legs, arms and heads that animate in code (walk, run, charge, attack, spit, stagger, death).

Models: enemies, co-op teammates, the FOB booths and map props use low-poly models built by `Tools/Blender/build_models.py` (see `Tools/Blender/README.md`); the game falls back to primitives if a model is missing.

UI: every HUD element and menu shares one modern style that scales with resolution (see T-60). Hold F near a damaged barricade to keep repairing it, no need to aim at it.

Environments: generated rooms are themed by type (lab, servers, storage, canteen, atrium, reactor, containment) with tiles, trim, sector-coloured stripes, beams, pipes, fixtures, furniture and clutter; maps have a light haze and a few failing lights.

Map rework: doors show their price or what they need (switch, power relay); F opens them, pulls switches and starts relays. Enemies break in through boarded openings; F at one repairs a board for 10 credits during a run.

Co-op life (M11): at 0 HP you are downed, not dead. Teammates hold F near you for 4 s to revive you; while down you can hold F to spend one of the squad's 3 self-revive kits. Bleed out and you spectate until the surge ends. A dropped client can rejoin within 90 s.

FOB and progression (M10): F at a facility console opens it. Command Center deploys (Operation or Survival) and has a dev button that grants currency and levels; Armory sets class and loadout; Training Range holds skill trees and prestige; Research Lab, Engineering Workshop and Intelligence Center sell research; Trophy Room shows challenges. Runs end on a results screen with "Return to FOB".

**Co-op test (M1.5)**: press Esc in the sandbox to open the co-op panel. One copy presses **Host**, the other presses **Join** (address `127.0.0.1` on the same PC, or the host's LAN IP). Full steps are in `TESTING.md` §3.1. A build also accepts `-deadfall-host [port]` or `-deadfall-join [address] [port]` on the command line.

Audio (M13): every sound is synthesised at startup from recipes in `Rules/Common/Sfx.cs` (no audio files yet); see T-72.

**Builds (M13)**: **Deadfall → Build → Windows (Release)** (or Development, Linux, macOS) builds every enabled scene in Build Settings into `Builds/<Target>/` and writes `build_info.txt`. From the command line:
`Unity -batchmode -quit -projectPath . -buildTarget Win64 -executeMethod Deadfall.EditorTools.BuildScript.BuildFromCommandLine -deadfallTarget windows -deadfallVersion 0.13.0` (add `-deadfallDev` for a development build). Release builds hide the developer keys and readout.

**Tests**: **Window → General → Test Runner**, run EditMode (Rules unit tests) then PlayMode (movement and combat integration).

**Without Unity** (cloud/CI): `Tools/CompileCheck/setup.sh` then `Tools/CompileCheck/check.py` compiles every assembly and runs the Rules tests. See `Tools/CompileCheck/README.md`.

## Documentation

| File | Purpose |
|---|---|
| `ARCHITECTURE.md` | Systems, responsibilities, dependencies, data flow, networking |
| `GAME_DESIGN.md` | Modes, classes, enemies, weapons, progression, maps |
| `IMPLEMENTATION_ROADMAP.md` | Milestones and task status |
| `TESTING.md` | Test strategy, automated tests, manual and regression checklists |
| `DECISIONS.md` | Confirmed decisions, technical decisions, open questions |

## Originality

Deadfall uses wave survival as a broad genre idea only. All names, maps, systems, interfaces, and terminology are original to this project. Do not import assets, names, or mechanics from existing games.
