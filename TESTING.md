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

### 3.4 Manual checklist: M3 waves

Solo, in the sandbox:

- [ ] Press N: "DEPLOYMENT STARTED" appears, the top panel shows LULL with a 10 s timer and Field Credits 0.
- [ ] When the timer runs out, "SURGE 1" appears and Husks walk in from one wall, out of sight where possible and never closer than 15 m. The panel counts enemies left down to 0.
- [ ] Each kill shows "+10 Husk" (Bolter +15, Bulwark +60) and the credit total goes up.
- [ ] Clearing a surge shows "SURGE 1 CLEARED, +60 Field Credits", then a 30 s LULL. G during the lull starts the next surge at once; G again before that cancels ready.
- [ ] Bolters appear from surge 3, Bulwarks from surge 6. Surge 4 is a SWARM (only Husks, more of them at once). Surge 5 is a SIEGE (3 or more walls at once, a Bulwark may appear early).
- [ ] Later surges have more enemies and they take more hits (the overlay line shows the plan: count, budget, at once, directions, health scale).
- [ ] Die during a run: "SQUAD WIPED". N starts a new run (remaining enemies collapse).
- [ ] N during a run stops it and removes its enemies. F5–F8 debug enemies don't count toward a surge and don't block it from ending.
- [ ] Stuck enemy check: in a surge, turn on F10 god mode and stop shooting once everything has spawned. 40 s after the last kill the leftovers collapse and the surge clears.
- [ ] No errors or warnings in the console except "could not place" lines if a spawn point is unusable.

Two players (set up as in §3.1):

- [ ] Host presses N. The client sees the same panel, timer and announcements. Client pressing N says the host starts runs.
- [ ] Lull shows "0/2 ready". G on the client shows 1/2; G on the host starts the surge.
- [ ] Kills by the client give the client the credits (shown on the client), kills by the host give the host credits. Surge clear pays both.
- [ ] Spawn count is higher than solo for the same surge (the overlay plan line on the host).
- [ ] One player dies: the run continues. Both dead at the same time: SQUAD WIPED on both screens.
- [ ] A client joining during a surge sees the right surge, enemies left and their credits (0).

### 3.5 Manual checklist: M4 classes (part 1)

In the sandbox, Esc shows the class list on the left. The class name, ability bar and passives sit above the health bar.

- [ ] Picking a class updates the HUD at once and is remembered after stopping and starting Play.
- [ ] Any class can still use every weapon. The class's preferred weapons reload and swap a little faster (Gunner: Halberd AR; Tank: Breacher-12; Warrior: melee).
- [ ] **Gunner**: reserve ammo shows 25% more after picking it. Holding fire on the Halberd, recoil settles noticeably after 2–3 s. Q: the bar counts down 8 s, the Halberd fires faster and reloading doesn't lower the reserve; then a 45 s cooldown. Q during cooldown does nothing.
- [ ] **Tank**: Q plants a blue wall 2 m ahead. Enemies within about 12 m go for the wall and hit it until it breaks (it darkens as it takes damage); breaking it starts the 40 s cooldown early. Your bullets pass through it. Taking hits above half health hurts noticeably less than as Gunner.
- [ ] **Warrior**: Q dashes about 8 m forward and swings at the end; a Husk hit by it dies or staggers (a Bulwark staggers unless it is charging). Dashing into a wall stops the dash. Three quick melee hits: the third hits more enemies in a wider arc. Melee kills heal 5.
- [ ] **Engineer** and **Demolitionist**: selectable, the HUD says their ability is coming soon, Q does nothing.
- [ ] Switching class during Overdrive or with a shield up ends the effect cleanly (shield disappears, fire rate back to normal).
- [ ] Co-op (as §3.1): a client Warrior's Lunge strike staggers and does triple damage to host enemies; a client Gunner's Overdrive fires faster with no rejected-claim warnings on the host. Known gap: a client's Tank shield doesn't pull host enemies yet (part 2).

### 3.6 Manual checklist: M4 classes (part 2)

Solo:

- [ ] **Demolitionist**: Q throws a purple charge along your aim. It bursts on hitting something (or after about 1 s) and scatters 5 small bomblets that pop a moment later. Husks in the main blast die; ones at the edge take less. Enemies get pushed a little. Standing in your own blast hurts, but only half as much as it would for another class (Blast Padding). 25 s cooldown.
- [ ] **Engineer**: Q places a green turret in front of you (only on the ground). It turns and shoots the nearest enemy in range it can see, with tracers. Enemies sometimes attack it; it darkens and breaks after enough hits, which starts the 35 s cooldown early. It disappears after 60 s. Kills by the sentry give you credits in a survival run.
- [ ] **Tank**: melee pushes Husks back a couple of metres (a Bulwark less).
- [ ] The HUD no longer says "soon" for the Engineer and Demolitionist abilities (Salvager, Handy and Bandolier still say soon).

Two players (as §3.1):

- [ ] Each player's label above their head shows their class, and changes when they switch.
- [ ] Tank + Gunner: both HUDs show "Doctrine: Covering Fire (effect after playtests)". Tank + Warrior: Vanguard, and the Warrior's Lunge goes visibly further.
- [ ] A client Tank's shield appears on both screens and host enemies attack it. When it breaks, it disappears for both and the client's cooldown starts.
- [ ] A client Engineer's sentry appears for both, kills enemies (the kill credits go to the client), and disappears for both after 60 s.
- [ ] A client Demolitionist's charge and bomblets are seen by both; enemies die on both screens. The client is hurt by their own blast; the host is not hurt by it.
- [ ] Client Tank melee pushes host enemies.

### 3.7 Manual checklist: M5 maps

Solo:

- [ ] Esc, then on the right **Generate** (seed 1): the sandbox is replaced by a grey underground facility and you stand in the airlock. **Random** gives a different layout each time; the same seed always gives the same one (compare the hash in the M overlay).
- [ ] Walls have doorways only where rooms connect. Rooms have a light, a few crates, and a coloured square showing their sector.
- [ ] Doors to later sectors are closed and tinted. **Open next sector (debug)** slides the next set open.
- [ ] M shows the minimap: rooms in sector colours (faded while locked), red/green door dots, orange spawn points, yellow loot, you in green, enemies in red. It says "Validation: OK".
- [ ] Press N: enemies spawn only in open sectors and path to you around closed doors. After surge 3 a "SECTOR 1 UNLOCKED" banner shows and those doors open; later surges also spawn there.
- [ ] Look at an ammo crate or medkit: "[F] Take ammo" / "[F] Take medkit". F uses it once.
- [ ] Find the cyan dot on the M map (a vent cover low on a wall). Shoot it; crouch through into the vent crawl and open the gold stash (full refill, +250 credits during a run).
- [ ] A Cluster Charge thrown on the other side of a wall doesn't hurt enemies behind it.
- [ ] **Back to sandbox** restores the M1 sandbox with its dummies.

Two players (as §3.1):

- [ ] The host generates a map: the client switches to the same layout and appears in the airlock. A client joining later gets the same map with the same doors open.
- [ ] Only one player can take each pickup. A vent broken by either player opens for both.
- [ ] Sector unlocks happen on both screens at once.

### 3.8 Manual checklist: M6 weapons and run progression

Solo:

- [ ] Picking a class on the pause screen hands out its starting loadout (Tank: Breacher-12, Anvil LMG, Ranger-9, Tether Cleaver; Demolitionist gets the Mule Launcher).
- [ ] Mule Launcher: grenades arc a little and explode on impact; enemies in the blast die or get pushed; your own blast hurts you (half as Demolitionist).
- [ ] Wasp SMG, Anvil LMG and Tether Cleaver each feel distinct (rate of fire, reload length, swing speed).
- [ ] On a generated map, find the green Supply Cache in the airlock sector and press F. The menu shows three weapons with tier colours, attachments, prices and what they'd replace, plus Ammo refill. Without a run it says credits come from kills.
- [ ] During a run (N), buy something once you have the credits: it goes into the right slot (pistol to slot 3, melee to slot 4, others replace the primary in hand) and the HUD name shows the tier and attachments.
- [ ] Kill enemies until a weapon drops (a spinning box on a coloured disc). F takes it.
- [ ] After surge 3 a toast and a hint say a Field Mod is ready. T shows three cards; clicking one applies it (Thick Skin raises max health, Fleet Foot speeds you up, Overclock shortens the ability cooldown...). Esc closes and the offer waits.
- [ ] Starting a new run (or generating a new map) puts you back on the class loadout with no mods.
- [ ] Kills raise the weapon's level now and then (toast).

Two players (as §3.1):

- [ ] A client buys from a cache: the host's credits for that client go down and only the client gets the weapon.
- [ ] A drop from the host's kill can't be taken by the client for 10 s, then can. Once taken it disappears for both.
- [ ] A client's launcher grenades hurt host enemies and the client gets the kill credits.

### 3.9 Manual checklist: M7 advanced enemies and encounters

Solo, in the sandbox (F12 spawns the next advanced enemy; hold Ctrl for a variant):

- [ ] **Creeper**: low and quick, bites at ankle height; you have to aim down. A shotgun blast drops it.
- [ ] **Pyre**: runs at you, swells and shakes for under a second, then bursts (orange flash) and hurts you if you're close and in the open; a wall between you blocks it. Shot at range it bursts where it dies and kills Husks around it (you get the credits during a run).
- [ ] **Retcher**: stops about 13 m away when it can see you, rears back and lobs a green glob; where it lands a green pool hurts you while you stand in it and shrinks away after ~5 s. Break line of sight and it walks closer.
- [ ] **Alpha**: Husks near it move faster and take less damage. The blue growth on its shoulders takes triple damage.
- [ ] **Goliath**: a bigger Bulwark with a wide slam; flank it for the back pack.
- [ ] **The Stalker**: very fast, weaves, pounces; a purple bar at the top says it's hunting the squad.
- [ ] **Specimen Zero**: red boss bar with two ticks. At 66% it roars, a banner says infected are coming, Husks and a Bolter appear around it every ~9 s and two orange growths open on its back. At 33% the chest plate is gone and a red core is exposed (shoot it for big damage); it spits acid. The bar shows the phase.
- [ ] Ctrl+F12 variants show coloured bands: green Regenerating (heals after 3 s untouched), purple Splitting (two Creepers on death), grey Hardened (tougher), red Swift (faster).

During a run (N; sandbox or a generated map):

- [ ] Surge 4 announces Swarm, 5 Siege, 6 Hunt (the Stalker arrives first), 7 Elite (Alphas, more variants), 9 Supply (a toast says crates dropped; ammo crates and medkits sit within ~20 m), 10 Specimen.
- [ ] Killing Specimen Zero announces SPECIMEN DOWN with +1000 credits, drops a Prototype weapon anyone can take, and a Field Mod is offered after the surge.

Two players (as §3.1):

- [ ] The client sees the same enemies with the same variant bands, the same boss phase and modifiers on the bar, and the globs, pools and blasts the host's enemies make. Damage to the client from globs, pools and blasts matches what it sees.
- [ ] Supply crates appear in the same places for both; only one player gets each.

### 3.10 Manual checklist: M8 objectives, events, Operations

On a generated map (Esc, Generate):

- [ ] Survival (N): in the lull before surge 3 a SIDE OBJECTIVE banner appears and the list at the top right shows it. Each type works: generators (F, then stay close; the light goes red to yellow to green; stepping away drains it slowly), sample cases (F takes one), the nest (shoot it; it births Creepers during surges), the data terminal (F and stay close), keycard (take it, then F on the locker: a weapon drops). Completion pays credits to everyone. Left alone it expires after two surges.
- [ ] Events are warned in the lull ("WARNING: ... NEXT SURGE"). **Blackout**: lights drop to almost nothing, your flashlight comes on (L toggles it), a generator objective appears and finishing it brings the lights back. **The Stalker**: it arrives with the surge and drops a Prototype weapon. **Lockdown** (needs two sectors open): doors slam shut, a terminal appears in your room; hacking it reopens them. Events end when the surge is cleared.
- [ ] Esc → **Start Operation Cold Start**: all sectors open, the list shows the mission (generators and data core required; nest and samples optional), surges come every 35 s without pressing G. Finishing the required ones announces EXTRACTION: a blue ring in the airlock and a countdown at the bottom that runs only with someone inside. Holding it ends the run with OPERATION COMPLETE and the bonus.

Two players (as §3.1):

- [ ] Both see the same objectives, props, progress and banners; either player can use a prop and both see it advance. A client's shots damage the nest.
- [ ] Blackout darkens both screens and Lockdown seals the doors for both; both end together. Extraction counts either player standing in the ring.

### 3.11 Manual checklist: M9 engineering

Solo, as Engineer (pick it on the pause screen), in the sandbox (building is free outside a run):

- [ ] B shows the build menu at the bottom and a box ghost where you look. It's green on open floor and red with a reason on a wall, a slope, too far away, or next to a spawn point. 1-4 or the wheel switch type, R turns it, click places it, right click or B leaves. Weapons don't fire in build mode.
- [ ] **Sentry**: turns to and shoots infected (F5 spawns Husks), ammo counts down in its prompt; at zero it stops and F refills it.
- [ ] **Drone** (key 2): appears above your shoulder and follows you, shooting infected. Z switches it to repair mode: a green beam heals a damaged structure nearby.
- [ ] **Arc Fence**: a blue arc between two posts; Husks crossing it slow down and take damage, the arc flickers white, the charge drops. Out of charge it goes dark until F refills it.
- [ ] **Barricade**: block a corridor. Husks walk round if there's another way; if not they attack the barricade until it breaks (small burst) and then come through.
- [ ] Looking at a structure shows its tier, health and actions: F repairs a damaged one, U upgrades (colour shifts blue at tier 2, gold at 3), X dismantles your own.
- [ ] As another class: only the Barricade can be built, and it costs more during a run.
- [ ] In a run (N): Scrap starts at 60, goes up with kills (faster as Engineer), surge clears and objectives; costs are taken and refused with "Not enough Scrap" when short.
- [ ] Demolitionist + Engineer in co-op: the Demolitionist's grenades near damaged structures patch them up.

Two players (as §3.1):

- [ ] Both see the same structures, tiers, health and Scrap; a client can build, repair, upgrade and dismantle (its refusals show as toasts). A client's drone follows the client on both screens.

### 3.12 Manual checklist: M10 FOB and permanent progression

Solo:

- [ ] Play starts in FOB Halcyon: a hangar with seven coloured booths and floating names. Locked ones show the operator level they need.
- [ ] F at a console opens the FOB screen on that facility's tab; Esc, Close or walking away closes it. Tabs switch between facilities.
- [ ] **Command Center**: "Dev: grant..." gives 20,000 REQ, 100 Intel and operator level 6 (use it to open everything). Deploy on Operation Cold Start builds a map and starts the Operation; Deploy on Survival builds a map and starts a run.
- [ ] **Armory**: Engineer and Demolitionist are locked at operator level 1 and open at 2 and 3. Picking a class equips its loadout. < and > change each slot; the Anvil LMG, Tether Cleaver and Mule Launcher only appear after their research (or when the class starts with them). The new weapons are in your hands at once and on the next deployment.
- [ ] **Training Range**: rank, XP to next rank and skill points. Unlock a tier 1 node (after a run or two, or with dev XP), see its effect (for example Tank Plating: max health goes up on the HUD). Respec refunds for 500 REQ. "Go to the firing range" teleports to the M1 range; the gold console near the range spawn brings you back.
- [ ] **Research Lab / Workshop / Intelligence Center**: research a project, REQ and Intel drop, it shows ✔; locked ones say why. Field Medicine raises max health. Field Dossiers reveal the common infected in the Intelligence Center.
- [ ] **Trophy Room**: lifetime stats, challenge progress, achievements (First Deployment ticks after starting a run).
- [ ] Finish a run (win and lose one): the results screen shows kills, surges, objectives, rewards (half on a failure) and any level-ups or challenges; "Return to FOB" goes back to the hangar.
- [ ] Toasts appear for challenge tiers and achievements during a run.
- [ ] Quit and restart: currencies, levels, skills, research, loadouts, stats, weapon XP and the chosen class are all kept. The profile is `deadfall_profile.txt` in `Application.persistentDataPath`, with a `.bak` copy.
- [ ] Weapon XP from before M10 (PlayerPrefs) appears in the Armory weapon levels after the first launch.
- [ ] Edit a number in the profile file by hand and restart: the game warns and loads the backup.

Two players (as §3.1):

- [ ] Both start in the FOB. The host deploys from Command Center and the client goes to the same map; the client's Deploy buttons are disabled. After the run each player sees their own results, and "Return to FOB" on the host brings both back.

### 3.13 Manual checklist: map rework (rooms, doors, breaches)

Solo, deploy Survival from the FOB (or generate a map from the pause panel and start a run):

- [ ] FOB Halcyon is smaller than before and every booth is still reachable and usable.
- [ ] Rooms are bigger and many are L, T, Z or plus shaped; big rooms have columns or a partition wall. No floor holes or missing walls at the shaped corners; ceilings cover every floor cell.
- [ ] Doors on the main route show a price ("Open door (750 credits)"); pressing F with enough credits opens it, without enough it says so. Later sectors cost more.
- [ ] Some doors say they need a switch or power: find the wall panel with a lever and red lamp (one press) or the relay cabinet (F starts a 6 s restore) in an earlier room, and the door opens with an announcement.
- [ ] Some side rooms sit behind a 500-credit door, and nothing spawns in them until it's open.
- [ ] Outside a run (before starting) paid doors open for free.
- [ ] Each room has boarded openings in the walls. Enemies appear behind them, tear boards off (you see boards disappear), then climb in.
- [ ] F at a damaged opening puts one board back and pays 10 credits during a run. You can't walk or jump into an opening, even with all boards gone. Your bullets don't knock boards off.
- [ ] Enemies come from openings near you and from others nearby, never from the far end of the map.
- [ ] No enemy appears inside a room hugging a wall or keeps walking into a wall (including openings on the outer edge of the map and in the start room).
- [ ] M overlay: open rooms are bright, rooms behind closed doors are dim.

Two players (as §3.1):

- [ ] A door opened by either player opens for both; credits come from the player who paid. Board repairs and breaks show on both screens; a client joining late sees the right doors and boards.

### 3.14 Manual checklist: Blender models

- [ ] No "Model '...' not found" warnings in the console at start or when a map is built.
- [ ] Enemies of every type show their model (not capsules), stand upright, face the way they walk, and touch the ground. Each type keeps its own colour. Weak points glow where they did before (Bulwark back, Pyre belly sac, Retcher throat, Alpha upper back), and shots there still count as weak point hits. Specimen plates, growths and core still appear by phase.
- [ ] Wind-up, stagger and death poses still read (the whole model tilts).
- [ ] FOB Halcyon: booths show panels, screens, partitions and consoles with each facility's colour; consoles still open with F. The transport and crates look right and still block movement.
- [ ] Generated maps: columns, partitions, crates and lockers show models and keep their collision; partitions running both ways look right. Doors show the blast door with the lock colour band and still slide open. Switch panels and power relays show their models with the lever and lamp still working. Supply Caches show the footlocker.
- [ ] Weapons: every gun and melee weapon shows its model in first person (hip, aim, reload, swap, sprint, melee poses still work), with the barrel pointing forward and no clipping into the camera. Muzzle flashes and tracers start at the muzzle. Aiming puts the sight near the crosshair. Weapon drops show the spinning model above the tier-coloured disc and can still be picked up.
- [ ] Nothing is pink in a player build.
- [ ] Two players: the teammate shows a soldier with their player colour on shoulders, chest and helmet; the head, arms and weapon tilt with their aim; crouching shortens the body. The teammate holds the model of the weapon they have out, and it changes when they swap (melee included).

### 3.15 Manual checklist: environment dressing

Generate a few maps (different seeds) and walk them:

- [ ] Rooms look different by type: Lab Bay (white walls, lab benches, glowing specimen tanks), Server Hall (dark, rows of racks with lit LEDs), Cold Storage (blue-grey, shelving and barrels), Cafeteria (warm light, checkered floor, canteen tables), Atrium (planters with dead trees), Security Checkpoint (desk), Reactor Control (green tint, generators, pipes), Specimen Containment (red light, tanks).
- [ ] Every room has floor tiles, a lower wall panel, a coloured stripe at waist height that matches the sector, pilasters, ceiling beams and a fixture under each light. Doorways have dark frames with a sector-coloured bar above; breaches have yellow and black stripes on the floor in front.
- [ ] Pipes run high along one wall in corridors and some rooms, with brackets; fuse boxes, vents and extinguishers sit on walls; grime, blood and papers on floors; rubble in some corners. Nothing floats, sticks out of a wall's back side or overlaps a door, switch or loot.
- [ ] Furniture blocks movement and bullets; enemies path around it; nothing blocks a doorway, a breach or the room centre. You can't get stuck between pieces.
- [ ] Lamps, screens, LEDs, tank fluid and enemy weak points glow (in the editor and in a build). A few lights stutter now and then; a blackout still dims them and restores them after.
- [ ] Far rooms fade into a dark haze; going back to the FOB clears it and restores the normal ambient light.
- [ ] Frame rate on a big map is similar to before (trim is static-batched); report the map build time from the log line if it grows past about 300 ms.

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
| EditMode | `Tests/EditMode/Rules/ClassRulesTests.cs` | Ability timer phases, instant abilities, early end, long frames, cooldown rate and refunds; Guard threshold, sustained recoil floor, combined reduction; combo chain; doctrine pairs, duplicates, two-per-player cap, four distinct; weapon fire rate, reload speed, free reloads, reserve capacity |
| EditMode | `Tests/EditMode/Rules/WaveTests.cs` | Budget growth by surge and players, adaptive ±10%, health cap, unlock surges, same seed same plan, budget never exceeded, Swarm cheap-only, Siege directions and early heavies, share cap, minimum one enemy, spawn cap, type rotation; surge loop (lull, surge, reward), clear reported once, ready and skip, final surge, endless, squad wipe, straggler cull, no cull while spawning, unspawnable surge ends, credit numbers, ledger |
| EditMode | `Tests/EditMode/Rules/ProgressionTests.cs` | Wallet, currency list, operator and class rank curves, facility and class gates; skill catalog shape, unlock rules (rank, prerequisite, points, per class), full tree fits; prestige reset and cap; research catalog, unlock rules, permanent totals by class, dossiers; loadout slots, research gates, class defaults, fallback; run rewards and D-03 retention, banking a run; challenge tiers, progress text, level and research achievements; save round trip, determinism, corrupt and tampered files, newer versions refused, migration, unknown keys and bad ids, legacy weapon XP import |
| EditMode | `Tests/EditMode/Rules/EngineeringTests.cs` | Placement validator (every refusal reason, drones skip ground checks, class limits, caps, free mode), Scrap economy (kills, Engineer bonus, upgrades, investment and refund, ledger cap), chunked repairs, tiers improve every stat |
| EditMode | `Tests/EditMode/Rules/ObjectiveTests.cs` | Tracker progress and single completion, expiry, required-complete check, channel fill and drain, hold timer pause, side objective schedule and picks, mission template; event first surge, Specimen/Hunt exclusion, cooldown, Lockdown needs a map with two sectors |
| EditMode | `Tests/EditMode/Rules/AdvancedEnemyTests.cs` | Spitter holds at range with line of sight, shoots after wind-up, closes without sight, still melees; exploder commits and can't be staggered; stat copies; variant chance curve, no traits on bosses and hunters, no Splitting Creepers, double traits late, trait stats and credit multiplier; Specimen phases, phase stats, modifiers and packing, summon counts; hunters and bosses never bought, Specimen and Hunt lead, Elite guarantees early elites, Supply crates, missing roles fall back, deterministic traits |
| EditMode | `Tests/EditMode/Rules/EnemyBrainTests.cs` | Detection and alerting, strike lands once after wind-up, same attack timing at 8/30/144 Hz, stagger interrupt and immunity, meter decay, charge range/cooldown/contact/unstaggerable, death, armor zone multiplier |
| PlayMode | `Tests/PlayMode/MovementFrameRateTests.cs` | Walk distance equal at 30/60/144 FPS (±2%), sprint faster, strafe can't sprint, stops in 0.15 s |
| PlayMode | `Tests/PlayMode/CombatIntegrationTests.cs` | Headshot via raycast + resolver, shooter ignores own collider, melee arc, one hit per target, dead targets |

Status 2026-11-02: 180/180 EditMode tests pass in the compile-check runner. PlayMode tests compile but have not been run in the editor yet.

## 7. Known issues

- Not yet verified in the Unity editor (written in an environment without Unity).
- HUD is IMGUI and allocates a little each frame; it is a placeholder for the UI Toolkit HUD.
- Viewmodel is primitive boxes rendered by the world camera, so it can clip into walls when pressed against them.
- Melee hits use the camera position; a target directly overlapping the player's camera may be missed.
- No audio yet.
- M2 co-op: the host sends all enemies to every client (no distance-based priority yet); enemy types are numbered by their order in `PrototypeEnemies` until definitions are registered assets. Clients see enemies about 100 ms behind the host.
- M3: the sandbox is one open arena, so most spawn points are visible; real maps (M5) provide hidden spawn spots. Solo death ends the run right away because there are no revives yet (M11). Credits cannot be spent until Supply Caches (M6).
- M4: a client's shield copy doesn't darken with damage (only the host's copy tracks health). Explosions ignore walls (no line-of-sight check yet). Bomblet copies on clients may land slightly differently from the host's; the damage follows the host's.
- M7: Creepers don't use vents yet. Specimen Zero has no arena hazards or damage-type armor yet. Supply crates aren't sent to players who join mid-surge. Acid globs fly through walls on their arc (Retchers only fire with a clear line of sight).
- M8: objective props are placed after the NavMesh bake, so enemies can bump into them. Generators don't power doors or turrets yet. Operations open every sector at once.
- M9: structures use the Ignore Raycast layer so player bullets pass through them; Retcher globs and Pyre blasts don't hurt structures yet. Arc Fences don't use the map's power grid.
- M10: the sandbox pause-screen class buttons ignore class locks (dev override). The client in co-op deploys wherever the host deploys, with no ready check yet. Research doesn't affect host-run systems (Scrap, credits). Kill counts per enemy type (for dossiers) only count on the host or solo.
- Map rework: doors, switches and barricades are placed after the NavMesh bake (like M8 props). Power relays don't link to M8 generators. An enemy that spawns while a closet is still sealed waits there until it breaks the boards, so a fully repaired room can hold a small queue.
- Enemies use scripted low-poly models (T-58) and code-driven poses instead of animations; models aren't rigged. If a model faces the wrong way or lies on its side after import, the FBX axis settings in `Tools/Blender/build_models.py` are the place to fix it.
- M1.5 spike: remote players are smoothed toward the latest position (no interpolation buffer), so they can look slightly floaty at high ping. Strafing dummies move on each peer's own clock and can be at different spots on each screen; the host widens its hit check for them.
- M1.5 spike: players cannot damage each other (by design for PvE), and remote capsules don't block movement or bullets.
- The networking code compiles here against hand-written stand-ins for Netcode 2.13.3, so the first editor compile is the real check.
