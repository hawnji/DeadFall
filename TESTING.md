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
| Network messages | MP smoke test (2 and 4 players), duplication checks |

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

## 6. Known issues

None yet (no code).
