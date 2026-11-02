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
| M9–M13 | See `IMPLEMENTATION_ROADMAP.md` |

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
| Newtonsoft Json (`com.unity.nuget.newtonsoft-json`) | M10 save system |

## Running

**Milestone 1 sandbox** (no scene setup needed beyond this):

1. **File → New Scene** (Basic), save it as `Assets/_Project/Scenes/Sandbox/Sandbox_M1.unity`.
2. **GameObject → Create Empty**, then **Add Component → Sandbox Bootstrap**.
3. Press Play. The range, melee pit, movement course, and pads build themselves; the scene's own camera is switched off.

Controls: WASD, mouse, Shift sprint, Space jump, Ctrl/C crouch, LMB fire, RMB aim, R reload, V melee, 1–4 or wheel to swap, Esc pause.
Debug: F1/F2/F3/F4 cap 30/60/144/unlimited FPS, F9 respawn, F10 god mode, H toggle help.
Enemies (M2): F5 Husk, F6 Bolter, F7 Bulwark, F8 a mixed horde of 12, F11 kill all, `[` / `]` lower or raise the alive cap.
Waves (M3): N starts or stops a survival run (host), G toggles ready during a lull (everyone ready ends it early).
Classes (M4): Esc shows the class list on the left of the pause screen; Q uses the class ability.

Maps (M5): Esc shows the map panel on the right: type a seed and press Generate (or Random) to play a generated Cinder Ridge map, M toggles the map overlay (F9 stays respawn), F uses pickups. Back to sandbox returns here.

Run economy (M6): F opens Supply Caches and takes weapon drops; T picks a Field Mod when one is offered (every third surge, and after a Specimen).

Enemies (M7): F12 spawns the next advanced enemy in the sandbox (hold Ctrl for a mutation variant).

Objectives (M8): F uses objective props; L toggles the flashlight. On a map, Esc → Start Operation Cold Start plays a mission that ends in an extraction defense.

**Co-op test (M1.5)**: press Esc in the sandbox to open the co-op panel. One copy presses **Host**, the other presses **Join** (address `127.0.0.1` on the same PC, or the host's LAN IP). Full steps are in `TESTING.md` §3.1. A build also accepts `-deadfall-host [port]` or `-deadfall-join [address] [port]` on the command line.

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
