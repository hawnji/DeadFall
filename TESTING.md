# Testing

Testing runs from M1 onward. Nothing is called done until it is verified in the editor *and*, at milestone ends, in a standalone build.

## 1. Layers

| Layer | Tool | Scope | When |
|---|---|---|---|
| Unit | Unity Test Framework, EditMode | `Deadfall.Rules.*` and pure Core logic: damage, ammo/reload, waves, upgrades, economy, retention, synergy, save migrations, map generation validity | Every change; must run in < 30 s |
| Integration | UTF PlayMode | Systems together in test scenes: movement at fixed frame rates, weapon vs. enemy, waves with spawns, generation + NavMesh, objectives during surges | Every PR touching those systems |
| Multiplayer | Multiplayer Play Mode (up to 4 virtual players) + LAN/Relay builds | Sync, authority, disconnects, duplication exploits | End of every milestone from M1 |
| Performance | Unity Profiler + Profile Analyzer on Development builds, in-game perf overlay | Frame time, CPU/GPU, GC alloc, memory, enemy count, AI cost, bandwidth | M2, M3, M7, M9, M11, M13 and any perf-sensitive change |
| Manual | Checklists below | Feel, readability, UX | Every milestone |

**Compile check without Unity**: `Tools/CompileCheck/check.py` compiles each asmdef against only its declared references and runs the Rules tests with NUnitLite. It uses Unity 2021.3 reference DLLs and package stubs, so it is a fast pre-check, not a replacement for the editor.

**CI** (planned): GitHub Actions with GameCI `unity-test-runner` (EditMode + PlayMode) and `unity-builder` (Windows build) on every PR. Requires a `UNITY_LICENSE` secret (Personal license works). Until then, the developer runs the Test Runner locally before merge.

**Generation fuzzing**: an EditMode test generates 5,000 layouts from sequential seeds and asserts every one validates (or falls back cleanly). Failing seeds are added to a regression list.

## 2. Planned automated tests by milestone

| Milestone | Tests |
|---|---|
| M1 | Fire rate cap; semi vs. auto; mag decrement; dry fire; tactical vs. empty reload refill; reload interrupted by swap/sprint keeps ammo correct; reserve never negative; damage falloff by range; head multiplier; movement distance over 2 s equal (±2%) at 30/60/144 FPS; look delta independent of frame rate; melee hits target in arc, misses behind |
| M2 | Enemy takes zone damage; dies once (no double death event); pool reuse resets state; perception interval respected |
| M3 | Wave budget monotonic; composition respects unlock surge; enemy count tracked to zero; no deadlock when an enemy is stuck (timeout despawn); lull → surge transitions; rewards granted once |
| M4 | Passive modifiers stack correctly; ability cooldown; doctrine resolution with duplicates and 4 distinct |
| M5 | Seed determinism (same seed → same graph hash); no overlaps; all required rooms reachable; sockets compatible; sector unlock order valid |
| M6 | Upgrade roll respects rarity weights and class pool; stacking caps; evolution trigger; run reset clears all temporary state |
| M7–M9 | Boss phase thresholds; objective state machine (start/progress/complete/fail); placement validator rules; structure repair caps |
| M10 | Save round-trip; migration v1→v2; corrupted file falls back to backup; reward retention calculations |
| M11 | Damage applied once per claim; reward not duplicated on reconnect; rejected invalid claims (rate, range) |

## 3. Manual checklist: M1 feel

- [ ] Walk and strafe in all 8 directions; no drift, no sticky stop
- [ ] Sprint start/stop and sprint → fire delay feel right
- [ ] Rapid direction changes are crisp
- [ ] Sensitivity slider changes look speed linearly; no smoothing lag
- [ ] No camera jitter while moving and turning (check at 30, 60, 144, unlocked)
- [ ] Weapon swap mid-reload cancels the reload correctly
- [ ] Reload interrupted by sprint (if allowed) behaves as designed
- [ ] Melee feels immediate and hits what the crosshair suggests
- [ ] Firing while moving has the intended spread penalty
- [ ] Stairs, ramps, and small ledges do not snag
- [ ] Host + client: both see each other move and shoot; client hits register on host

### 3.1 Two-player test (M1.5)

You need two copies of the game running. Easiest is the editor plus a Development Build:

1. **File → Build Profiles** (or Build Settings): add `Assets/_Project/Scenes/Sandbox/Sandbox_M1.unity` to the scene list, tick **Development Build**, and build to a folder outside `Assets` (for example `Builds/`, which git ignores).
2. **Project Settings → Player → Resolution and Presentation**: set Fullscreen Mode to **Windowed** so both fit on screen (optional but easier).
3. Run the build. Press Esc and click **Host**. Windows may ask to allow network access: allow it on private networks.
4. Press Play in the editor. Press Esc and click **Join** with address `127.0.0.1`, port `7777`. The corner should show `CLIENT (player 1) · connected`, and the host's corner `1 other player connected`.
5. Press Esc on each to resume. Check:
   - [ ] Each player sees the other as a coloured capsule with a "Player N" label that moves, turns, looks up and down, and crouches smoothly.
   - [ ] Each sees the other's tracers when they shoot.
   - [ ] Client shoots a dummy: client gets a hit marker and damage number; the dummy falls on **both** screens at the same time.
   - [ ] Host shoots a dummy: same result, and the client sees it fall.
   - [ ] Shoot the 1000 HP dummy from both players at once: it dies once, on both screens.
   - [ ] Client melee (V) on a melee-pit dummy registers.
   - [ ] Client clicks **Leave**: the capsule disappears on the host; the client's dummies work offline again.
   - [ ] Host clicks **Close session** while the client is connected: client shows "The host closed the session..." and keeps playing offline.
   - [ ] Join with no host running: after a few seconds the panel says it could not reach a host.
6. The Console should show no errors on either side. Rejected hit claims appear as `[NetSpike] Rejected hit claim` warnings; there should be none in normal play.

### 3.2 Manual checklist: M2 enemies (part 1)

Play the sandbox in the editor. The overlay shows `Enemies alive / cap · AI ms` once something spawns.

- [ ] F5 spawns a Husk 18–35 m away; it walks to you around obstacles (cover, stairs, tunnel) and swipes after a visible rear-back.
- [ ] Backing off during the rear-back makes the swipe miss.
- [ ] Head shots show crit markers and kill a Husk in 2 Halberd hits; body shots take about 5.
- [ ] Sustained fire staggers a Husk (it tilts back); it can't be stagger-locked forever.
- [ ] F6 Bolter weaves while running, then crouches briefly and lunges from 4–8 m. Sidestepping the lunge makes it miss.
- [ ] F7 Bulwark: front shots hit the dark plate (small numbers, no crit); the orange growth on its back takes big crits. Its charge is telegraphed (it crouches about a second first) and can't be staggered.
- [ ] Melee (V, Saber-7) works on all three.
- [ ] Dead enemies fall over, stop blocking bullets, and disappear after about 4 s.
- [ ] F8 three times: the alive count stops at the cap (24) and a message says so; `]` raises the cap.
- [ ] When you die, enemies stop attacking; after respawn they come for you again.
- [ ] Performance: with `]` to 120 and repeated F8, note FPS and AI ms on the overlay (target: AI under 2 ms at the cap, `TESTING.md` §5).

### 3.3 Two-player check: M2 enemies in co-op

Set up host and client as in §3.1, then on the **host**:

- [ ] F8 spawns a horde. The client sees the same enemies in the same places, moving smoothly, with wind-ups and staggers visible.
- [ ] Enemies go for whichever player is nearest. Stand near them on the client: they chase and hit the client, whose health bar drops and damage direction shows.
- [ ] Client shoots and melees enemies: hit markers and damage numbers on the client; the enemy dies on both screens. Bulwark plate and back weak point behave the same for the client.
- [ ] Both shoot the same Bulwark: it dies once.
- [ ] Corpses disappear on both screens after about 4 s. F11 on the host kills everything on both.
- [ ] The alive cap on the host overlay reads 32 with one client connected.
- [ ] Client presses F5: "Enemies are controlled by the host."
- [ ] A client that joins while enemies are already alive sees them right away.
- [ ] Client leaves: the client's enemies vanish; the host's keep going. Host closes the session: the client's enemies vanish.
- [ ] No `Rejected hit claim` warnings on the host in normal play.

Alternative without a build: install **Multiplayer Play Mode** from Package Manager, open **Window → Multiplayer → Multiplayer Play Mode**, enable Player 2, and use the panel in both windows. Two PCs on the same network also work: join with the host PC's local IP and allow port 7777 through the firewall.

## 4. Regression map

When you change the left column, retest the right.

| Change | Retest |
|---|---|
| Player movement | Weapons, melee, interaction, networking, stairs/ledges sandbox |
| Weapon runtime or damage maths | All weapons, crits, enemies, bosses, upgrades, rewards, net validation |
| Enemy health/armor | Waves, bosses, upgrades, rewards, difficulty curves |
| Map generation | NavMesh, objectives, spawning, sectors, multiplayer seed sync, fuzz test |
| Wave/difficulty data | Deadlock tests, rewards, endless curve |
| Save schema | Migrations, round-trip, corrupted-file recovery |
| Network messages | MP smoke test (2 and 4 players, `TESTING.md` §3.1), duplication checks |

## 5. Performance targets (draft, confirm at M2)

Baseline PC: 6-core CPU (Ryzen 5 3600 / i5-9600K class), GTX 1660 / RX 5600 class GPU, 16 GB RAM, 1080p.

| Metric | Target |
|---|---|
| Frame rate (baseline, Medium) | 60 FPS minimum, frame time p99 < 20 ms, with max active enemies |
| Frame rate (mid-high PC) | 144 FPS typical |
| GC allocation in combat | 0 B/frame in steady state |
| AI CPU cost | < 2 ms/frame at the enemy cap |
| Bandwidth per client | < 64 kbps average, < 128 kbps peak at 4 players |
| Memory | < 3 GB in a deployment |

## 6. Automated tests in the project

| Suite | File | Covers |
|---|---|---|
| EditMode | `Tests/EditMode/Rules/WeaponStateMachineTests.cs` | Equip delay, full-auto rate at 30/60/144/4 FPS, semi-auto per press and rate cap, mag/dry fire, tactical vs empty reload, interruption before/after commit, reserve clamps, cadence reset, hitch cap |
| EditMode | `Tests/EditMode/Rules/DamageCalculatorTests.cs` | Falloff, head/limb/weak point, crits, outgoing and reduction, melee |
| EditMode | `Tests/EditMode/Rules/HealthModelTests.cs` | Damage clamp, death, regen delay, segment cap |
| EditMode | `Tests/EditMode/Rules/MotionMathTests.cs` | Frame-rate independent distance, sharpness, ballistic, jump height |
| EditMode | `Tests/EditMode/Rules/CommonTests.cs` | RNG determinism and bounds, modifier stacking, spread |
| EditMode | `Tests/EditMode/Rules/EnemyBrainTests.cs` | Detection and alerting, strike lands once after wind-up, same attack timing at 8/30/144 Hz, stagger interrupt and immunity, meter decay, charge range/cooldown/contact/unstaggerable, death, armor zone multiplier |
| PlayMode | `Tests/PlayMode/MovementFrameRateTests.cs` | Walk distance equal at 30/60/144 FPS (±2%), sprint faster, strafe can't sprint, stops in 0.15 s |
| PlayMode | `Tests/PlayMode/CombatIntegrationTests.cs` | Headshot via raycast + resolver, shooter ignores own collider, melee arc, one hit per target, dead targets |

Status 2026-11-02: 54/54 EditMode tests pass in the compile-check runner. PlayMode tests compile but have not been run in the editor yet.

## 7. Known issues

- Not yet verified in the Unity editor (written in an environment without Unity).
- HUD is IMGUI and allocates a little each frame; it is a placeholder for the UI Toolkit HUD.
- Viewmodel is primitive boxes rendered by the world camera, so it can clip into walls when pressed against them.
- Melee hits use the camera position; a target directly overlapping the player's camera may be missed.
- No audio yet.
- M2 co-op: the host sends all enemies to every client (no distance-based priority yet); enemy types are numbered by their order in `PrototypeEnemies` until definitions are registered assets. Clients see enemies about 100 ms behind the host.
- Enemies use placeholder primitives and code-driven poses instead of animations.
- M1.5 spike: remote players are smoothed toward the latest position (no interpolation buffer), so they can look slightly floaty at high ping. Strafing dummies move on each peer's own clock and can be at different spots on each screen; the host widens its hit check for them.
- M1.5 spike: players cannot damage each other (by design for PvE), and remote capsules don't block movement or bullets.
- The networking code compiles here against hand-written stand-ins for Netcode 2.13.3, so the first editor compile is the real check.
