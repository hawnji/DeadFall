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
- [ ] Press N: enemies spawn only in open sectors and path to you around closed doors. Buying a sector door with credits opens it; later surges also spawn in that sector.
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
- [ ] **Command Center**: Deploy on Operation Cold Start builds a map and starts the Operation; Deploy on Survival builds a map and starts a run.
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
- [ ] Spawn closets behind the boards are near-black voids (matte, black lining inside the opening); enemies inside are hard to make out until they reach the boards, but can still be shot through the gaps.
- [ ] Far rooms fade into a dark haze; going back to the FOB clears it and restores the normal ambient light.
- [ ] Frame rate on a big map is similar to before (trim is static-batched); report the map build time from the log line if it grows past about 300 ms.

### 3.16 Manual checklist: modern UI and hold-to-repair

- [ ] Layout (1080p, then 1440p or 4K and a small window): everything scales together and nothing overlaps. Top left: location card (map, sector, room, sectors and doors). Top centre: surge panel with a coloured rule and the credits pill; boss bar below it. Top right: objectives card. Bottom left, bottom up: health card, class card (ability bar with the Q key), scrap pill with B Build. Bottom right: weapon card (big magazine count, reserve, magazine bar), toasts stacked above it.
- [ ] Health bar is segmented, a white ghost trails damage, and the screen edge pulses red under 35% health. Damage shows a red vignette and an arc pointing at the attacker. The crosshair has a dark outline and still shows the real spread; hit markers turn yellow on crits and red on kills.
- [ ] Prompts are pills with a key cap ([F] Take ammo). Reload shows a centred bar; an empty magazine shows [R] Reload. Credit pops rise to the right of the crosshair.
- [ ] Fonts: Windows shows Bahnschrift headings and Segoe UI body text; elsewhere Unity's font is used and nothing is cut off.
- [ ] FOB: facility name plates float over consoles; the FOB window has facility tabs in each facility's colour, an operator level bar, skinned buttons, boxes and scroll bars; every tab still works (deploy, class and loadout, skills, research, dossiers, trophies).
- [ ] Supply Cache, Field Mod cards (they lift on hover), Run Results (stat tiles and banked rewards) and the pause panels use the same style and all buttons work. Teammates show a name plate with a health bar.
- [ ] Barricades: standing anywhere in front of a damaged breach (up to about 2.5 m, a little past each post) shows "Hold · Repair barricade" without aiming at it. Holding F repairs one board about every 0.7 s with a progress line in the prompt, paying credits each time during a run, and stops at 5/5. Aiming at another interactable still takes priority. It does not trigger from the next room through a wall.
- [ ] The dev readout (FPS, enemies) sits small under the location card; H toggles the controls list (off by default now).

### 3.17 Manual checklist: M11 multiplayer (needs 2 to 4 copies)

Use builds or Multiplayer Play Mode with up to 4 players. Run each line solo first where it applies, then with 2 and 4 players.

- [ ] Downed: at 0 HP you drop low and crawl, only the sidearm works, Q does nothing, enemies walk past you. The screen shows YOU'RE DOWN with a bleed-out bar: 30 s the first time, 20 s the second, 10 s after that in the same run.
- [ ] Teammates see you lower down with a red + marker, DOWN on the name plate and a draining bar. The squad roster (left edge) shows each teammate's state.
- [ ] Revive: stand within about 2 m of a downed teammate (no need to aim) and hold F for 4 s. Letting go starts over. Their bleed-out pauses while you hold. They stand up with 40% health. Try it from both host and client.
- [ ] Self-revive kits: while downed during a run, hold F for 2.5 s. The squad has 3 per run (roster heading and downed screen show the count); the host's count stays right when two players use kits at once.
- [ ] Bleed out: you are ELIMINATED, the camera follows a living teammate from behind (click to switch), the gun model is hidden. When the surge is cleared (or an objective completes in Operations) you come back next to a teammate with your class's starting loadout.
- [ ] Squad wipe: with everyone down and no kits left the run fails; with everyone down but a kit left it does not fail until the downed bleed out. Solo: the 3 kits are your only way back.
- [ ] Outside a run (sandbox, FOB) you get back up after 3 s like before. F9 still revives.
- [ ] Client drop (kill the client's network or close it mid-surge): the host and other clients see that player downed in place with RECONNECTING and a countdown. The dropped client sees Connection Lost with Rejoin and Return to FOB and retries on its own. Rejoining within 90 s puts them back where they were with the same health or downed state and the same Field Credits. After 90 s the stand-in disappears.
- [ ] Rewards: a dropped client that rejoins and finishes the run gets one results screen and one payout. Giving up (Return to FOB) or letting the window run out banks once with the failure share. Restarting the game afterwards doesn't pay again.
- [ ] Host drop: clients get Connection Lost, rejoin fails, and the run banks with the failure share when the window ends or they press Return to FOB. The host's own run carries on offline.
- [ ] Bandwidth: the dev readout and the co-op panel show bytes per second up and down and the busiest messages. Note the numbers for 2 and 4 players during a big surge (target in §5).
- [ ] 4-player pass of every system: classes and abilities, Supply Caches, Field Mods, objectives and Operations extraction, Engineering structures, doors and barricades, bosses, FOB deploy. Note anything that only one machine sees.

### 3.18 Manual checklist: M12 part 1 (Harrow Line, Endless, Last Train)

- [ ] Command Center: the deployment board has an environment row (Cinder Ridge, Harrow Line), and each has Operation, Survival and Endless. The seed field still works for both.
- [ ] Harrow Line builds: service-lift spawn, brick-red service tunnels with warm lights, cream-and-green platform halls, ticket concourse, maintenance yard, pump rooms, staff lockers, signal box, Line Control and the Flooded Turntable. The haze is greener and a little thicker. The location card says HARROW LINE.
- [ ] Doors, breach closets, loot, secrets and sector unlocks behave the same as Cinder Ridge. Try 5 or so seeds.
- [ ] Operation Last Train on Harrow Line: restore power, keycard to the locker, optional data core and nest, then extraction (about 170 s). Clients see the right mission name. Cold Start still runs on Cinder Ridge.
- [ ] Endless: the surge panel says ENDLESS instead of "/ 20". Surge 21 shows SURGE 21 · ESCALATION with "Armored"; 26 adds Swarming, and so on (use the debug surge skip if there is one, or a long session). Lulls get shorter once Relentless is active (surge 41).
- [ ] Endless results: the run ends on a wipe but the results screen keeps full rewards (no 50% cut). The board shows "Your best: surge N". Deep Run (25) and No Way Out (40) appear in the Trophy Room.
- [ ] Pause screen map panel: the Harrow Line toggle generates that environment; "Start this map's Operation" starts the right one.
- [ ] Co-op: a client sees the same environment as the host, and ENDLESS on the surge panel.

### 3.19 Manual checklist: M12 part 2 (weapons, Broodmother, mods, events, objectives)

- [ ] Armory and drops: Vesper DMR (semi-auto rifle), Bulldog .44 (heavy pistol), Kestrel PDW (fast SMG) drop and can be equipped. Hailstorm Auto-Shotgun and Railspike Maul need their research (Hailstorm Clearance, Railspike Maul Clearance) before they appear in loadouts.
- [ ] Each new gun fires, reloads and shows the right name on the HUD; the Maul swings slower and hits harder than the Cleaver. New guns use placeholder models for now.
- [ ] Harrow Line Specimen surge spawns the Broodmother (BossHud names her). She keeps her distance and spits, never charges, calls Creepers and Retchers in every phase (more often when enraged), and her egg sacs take bonus damage. She uses a placeholder body until a model is made.
- [ ] Field Mods: the new mods show up in offers (Trigger Discipline, Light Pack, Field Dressing, Hardened, Sleight, Combat Stims, Deadeye, Phoenix Rig). Three Combat Stims turn into Berserker Protocol.
- [ ] Frenzy event: banner says FRENZY, enemies are visibly faster and hit harder for that surge, and go back to normal after it.
- [ ] Toxic Leak event: two rooms fill with green haze and light; standing inside hurts (about 6 per second); it clears when the surge ends. A client sees the same two rooms.
- [ ] Defend Uplink side objective: one terminal, about 30 s upload that pauses when nobody is close, Husks keep rushing it while it runs, 600 credits each.
- [ ] Purge Hives side objective: two smaller nests in different rooms, the counter goes 0/2, 1/2, 2/2, 700 credits each.

### 3.20 Manual checklist: M13 part 1 (audio and settings)

- [ ] No background bed plays anywhere (removed at the user's request; the looping channel is kept for music later).
- [ ] Each weapon class sounds different: pistol, SMG, rifle, shotgun, heavy, launcher. Firing full auto does not distort or cut out other sounds.
- [ ] Reload, empty-click (dry fire) and melee swing sounds play. Hit, crit and kill markers each have their own tick.
- [ ] Nearby enemies growl now and then (a big horde should not become a wall of noise), and deaths, enemy attacks, Retcher spit, explosions, barricade breaks and turret fire play from where they happen and fade with distance.
- [ ] Surge start horn, surge clear chime, objective complete chime, event alarm on event warnings, credit ticks, boss roars on Specimen phase changes.
- [ ] Taking damage plays a thud; being downed plays a falling tone; being revived plays a rising chime.
- [ ] A client hears teammates' gunfire from their position.
- [ ] Pause (Esc): a Settings button sits next to Resume. The window has Audio (Master, Effects, Interface), Controls and Video. Every slider drags and clicks, volumes change sounds already playing, FOV and sensitivity apply immediately, VSync and frame cap work, Quality cycles the project's levels.
- [ ] Settings are kept after restarting the game. Esc while the window is open closes it and resumes. Reset to defaults works.
- [ ] Windowed toggle works in a standalone build (it does nothing in the editor).

### 3.21 Manual checklist: M13 part 2 (release mode, builds, new models)

- [ ] **Deadfall → Build → Windows (Development)** finishes and `Builds/Windows-Dev/Deadfall.exe` runs: dev readout and F-keys work, the pause screen shows "Deadfall v0.13.0 dev".
- [ ] **Deadfall → Build → Windows (Release)**: no dev readout, F5–F12, F9, F10 and N do nothing, G still readies up. Settings → Show FPS shows a small counter. `-devtools` on the command line brings the dev tools back. `build_info.txt` sits next to the exe.
- [ ] Settings → Damage numbers off hides the floating numbers.
- [ ] The Vesper DMR, Bulldog .44, Hailstorm, Kestrel PDW and Railspike Maul show their own models in first person, on teammates and as drops; muzzle flashes line up with the barrels.
- [ ] The Broodmother uses her spider-like model; the carapace, egg sacs and core still appear by phase and take hits.
- [ ] Full standalone pass: a 2-player co-op run in release builds from FOB to extraction, then quit and relaunch to check the profile and settings were kept.

### 3.22 Manual checklist: enemy animation

- [ ] Husks, Bolters, Spitters, Alphas, Bloaters, Bulwarks and Specimen Zero swing their legs and arms while walking; faster enemies stride faster, and standing enemies sway slightly instead of freezing.
- [ ] Charging enemies (Bulwark, Goliath, Specimen in Rampage) brace in the wind-up, then sprint with arms thrown back.
- [ ] Melee attacks: arms rise in the wind-up and slam down when the hit lands.
- [ ] Spitters and Retchers throw their head back before spitting. Exploders spread their arms and shake as they swell.
- [ ] Staggered enemies flail; dead enemies go limp as they fall.
- [ ] Creepers scuttle on four limbs and rear up to attack. The Broodmother walks on alternating legs and raises her front legs before attacking.
- [ ] Limbs stay attached at the hips and shoulders (no floating or detached parts), including on scaled variants.
- [ ] A client sees the same animation as the host.
- [ ] With a full horde (F8 a few times), frame rate holds up.

### 3.23 Manual checklist: title screen

- [ ] Pressing Play fades in from black to the DEADFALL title, with the camera drifting slowly inside FOB Halcyon. The in-game HUD, crosshair and pause menu are hidden.
- [ ] Esc and the mouse do nothing to the game while the title is up. The cursor stays free.
- [ ] Play solo fades into the game at the FOB with the cursor locked and the HUD back.
- [ ] Host co-op starts hosting and enters the game. The second copy's Join co-op connects to 127.0.0.1 and enters the game once connected. A wrong address shows an error and Cancel stops the attempt.
- [ ] Settings opens the settings window and Done returns to the title.
- [ ] Quit closes a build. In the editor it shows as disabled.
- [ ] The title theme ("Rotting Circuitry") fades in with the title, loops, follows the Music slider in Settings, and fades out when you enter the game.
- [ ] Pause shows "Quit to title". The first click arms it ("Click again to quit") for 3 seconds, and the second click returns to the title.
- [ ] Quitting to title from a map run puts you back at the FOB with a fresh loadout and full health. In co-op the session ends. The host's clients drop to their own FOB, so check that they can still play or quit to title too.
- [ ] Play solo after quitting to title works normally (no double HUD, no frozen input).
- [ ] `-skiptitle`, `-deadfall-host` or `-deadfall-join` on the command line go straight into the game.

### 3.24 Manual checklist: online play (Relay)

Needs the project linked to Unity Cloud and the Multiplayer Services package installed (README, Online play).

- [ ] With the package installed, the title shows Host online and Join online enabled. Without it they are disabled with a hint, and LAN / IP still works.
- [ ] Host online shows "Going online...", then enters the game. The bottom line reads "HOSTING ONLINE · join code XXXXXX", and the pause screen shows the code with a Copy button.
- [ ] A second copy (same PC or another network) joins with Join online and the code, and both players see each other, enemies and objectives as on LAN.
- [ ] A wrong code shows "No game found for code ...". Cancel stops a join in progress.
- [ ] A dropped client's rejoin prompt reconnects with the same code while the host is still up.
- [ ] After an online session, LAN host and join still work (the transport switches back).

### 3.25 Manual checklist: surface textures

- [ ] Maps, the FOB and the sandbox show textured concrete floors and ceilings, plaster walls, metal doors, consoles and racks, wooden crates and barricades, and dirt ground. The textures repeat at a sensible scale on large walls instead of stretching.
- [ ] Lighting shows bumps (panel seams and rivets on metal, plank gaps on wood) and nothing looks inside-out or black.
- [ ] Coloured markings (deployment lane, booth headers, hazard pads, sector marks) stay flat colours.
- [ ] A development build shows the same textures (no pink, and the normal maps still work).

### 3.26 Manual checklist: first-person arms

- [ ] Every weapon is held by two gloved hands: the right hand on the grip, the left on the handguard or pump (both hands on the pistol and on melee handles). Hands follow recoil, reload, swap, sprint and melee swings without drifting off the weapon.
- [ ] The sleeves never end in view, at hip or aiming, at FOV 70 to 110.
- [ ] Arms don't clip through the camera or show through walls more than the weapon does.

### 3.27 Manual checklist: rigged zombies

- [ ] The Unity import of every `zm_*` model has no errors; each has 8 clips (Idle, Walk, Run, Attack, Hit, Spit, Convulse, Death) and stands upright facing the player, at the same size as before.
- [ ] Standard, lean, plated, bloated, spitter, alpha and specimen zombies use the new skinned models; crawlers and the Broodmother keep their old ones.
- [ ] Idle when standing still, shambling walk when chasing, a run for fast movers and charges; feet don't skate badly at normal speeds.
- [ ] The attack raises both arms in the wind-up and slams down exactly when the hit lands; spitters rear back and lurch forward as the spit fires; staggered zombies flinch; bloated zombies convulse before bursting.
- [ ] Death plays a buckle-and-fall onto the floor with no extra tilt on top, and the body doesn't vanish when seen at the screen edge.
- [ ] Glowing weak points still line up with their hurtboxes closely enough to aim at while the zombie moves.
- [ ] In co-op, client zombies animate the same as on the host.
- [ ] 40+ zombies on screen keep frame rate acceptable.

### 3.28 Manual checklist: lull ambushes, Red Sky, Containment Breach

- [ ] From surge 3 on, some lulls (at most one every 5 surges, never before a Specimen, never in short extraction lulls) play "WARNING: AMBUSH" with the alarm, then 3 s later Husks and Creepers come out of the closets nearest the squad. Ambushers left alive count toward the next surge.
- [ ] Red Sky: lights and fog turn red for the surge, infected are faster and hit harder, kill credits are 1.5x and weapon drops are more frequent; everything goes back to normal at surge end, also on clients.
- [ ] Containment Breach: one room's floor grid glows, flickers yellow, then arcs blue every 3.2 s; standing on it during an arc hurts (jumping over it doesn't), and infected caught on it are shocked and staggered. A console in that room ("Shut down the containment system", 7 s channel) ends it; otherwise it vents by itself after two surges. No other event starts while it runs.
- [ ] A client that joins during Toxic Leak or Containment Breach sees the same rooms as the host.

### 3.29 Manual checklist: boss arena hazards

- [ ] While Specimen Zero or the Broodmother is still stalking, nothing happens. Once enraged (Rampage) the floor under players within ~26 m of it gets a pulsing orange (Specimen) or green (Broodmother) ring every 7 s; in the exposed-core phase every 5 s, with an extra ring beside a lone player.
- [ ] After 1.5 s the ring bursts (25 damage inside it, nothing just outside) and leaves a burning pool for 4 s (10 damage a second inside). Moving off the ring avoids it.
- [ ] Each Broodmother rupture hatches one Creeper (host), counted toward the surge.
- [ ] Clients see the same rings at the same spots and only take damage themselves.

### 3.30 Manual checklist: Creepers using vents

- [ ] Generated maps have dark floor grates by a wall in every room (two in big rooms), never in secret rooms and never at doorways.
- [ ] A Creeper more than ~14 m from the squad that passes within 3 m of a grate sometimes sinks into it (growl), disappears for 1 to 5 s, and climbs out of an open-room grate 3.5 to 13 m from its target. The exit grate rattles just before (host).
- [ ] While inside it can't be shot or targeted by sentries; it counts toward the surge. It never comes out in a closed sector.
- [ ] Clients see it sink and rise at the same grates.

### 3.31 Manual checklist: power grid

- [ ] Generated maps have 2 or 3 yellow grid generators (green lamp, spinning fan), one per sector on the main route, never behind a locked side door. Host and clients see them in the same spots.
- [ ] From surge 4 a surge sometimes trips one generator 12 to 35 s in (red blinking lamp, toast). It never trips the last generator still up.
- [ ] Pressing F on a tripped generator starts a 4 s restart that only advances with someone within 3.5 m (lamp red to yellow); it drains at half speed when everyone leaves. A client can start it too.
- [ ] Each online generator in an open sector gives 4 power. Arc Fences draw 2 and Sentries 1; when power runs short the newest ones brown out (no arc, sentry idle, prompt says NO POWER) and come back once a generator is restarted.
- [ ] A Blackout trips every generator in the open sectors: lights go dark, the Restore Power objective counts generator restarts (1/3 ...), and the lights come back as soon as one generator is up. The event ends when all are restarted, or at the end of the surge (all generators come back).
- [ ] With the grid down, power-door relays refuse to work and say why. Generators in sealed sectors give no power until the sector opens.
- [ ] Sandbox (no generated map): Blackout still places its own objective generators, and fences and sentries always have power.

### 3.32 Manual checklist: new Operations and objectives you can fail

- [ ] The deploy screen lists every Operation under the environment buttons; the environment's own one is picked by default and switching environment resets it. Deploying starts the picked Operation (its name and briefing show).
- [ ] Operation Quarantine: three cryo containers (blue tanks) are placed. Infected within ~8 m go for an unsealed container and hits lower its integrity (shown in the prompt); a "CONTAINER UNDER ATTACK" warning shows at most every 10 s. Holding F for 5 s near one seals it (infected lose interest). If one breaks: burst, "CONTAINER BREACHED", a Bulwark climbs out, and the run ends as MISSION FAILED with the end screen saying why.
- [ ] Operation Meltdown: two coolant valves and a 5:00 clock on the objective line that counts down for host and clients. Opening both in time completes it; running out shows OUT OF TIME and fails the run.
- [ ] Operation Dead Air: purge the hives and hold the uplink (both required), with an optional container step. Losing an optional container fails only that step.
- [ ] Quarantine's optional data core has a 4:00 clock; timing it out says OUT OF TIME but the run goes on.
- [ ] Survival: side objectives can now be Seal the cryo containers or Open the reactor coolant valves (2:30 clock). Failing them never ends the run.
- [ ] Clients see the same end title (MISSION FAILED / OPERATION COMPLETE) as the host.

### 3.33 Manual checklist: 50-room Survival maps

- [ ] A Survival map (both environments) loads without a long hitch; note how long generation plus the NavMesh bake takes (the log line `Seed ...: N rooms`).
- [ ] Rooms counted in the log are around 45 to 55, across 6 sectors; the start is still small (airlock plus one room, at most one side room).
- [ ] Side branches now fork off other side rooms, so the map spreads out rather than running as one long line.
- [ ] Frame rate stays at target with the whole map open (Operation mode opens every sector): roughly 4x the room lights of before.
- [ ] Enemies still spawn near the squad (only open rooms feed spawns) and walk the whole map; no NavMesh holes at the far edges.
- [ ] Multiplayer: a client joins a big map and gets the same layout, doors and breach boards.

### 3.34 Manual checklist: Gun Dispenser

- [ ] Survival and Endless runs on a generated map: a dark cabinet with a glowing window and an orange beacon appears in the first sector. An orange "GUN DISPENSER  N m" marker shows it from up to 80 m. Operations and the sandbox have none.
- [ ] F with 950+ Field Credits: credits go down, weapons cycle in the window for 4 s (slowing down), then one weapon sits there for 12 s with its tier colour. Only the buyer can take it ("Take ... (12 s)"); it replaces the weapon in hand like a drop. Without enough credits: "Not enough Field Credits".
- [ ] The result is never the weapon the buyer is holding, never a melee weapon, and is a better tier on average than enemy drops.
- [ ] Left untaken, the weapon disappears after 12 s and the dispenser is usable again.
- [ ] After 4 or more pulls at one spot, a pull can come up empty: red window, "The Gun Dispenser is moving. Pull refunded" (credits back), and 5 s later it reappears in another open sector with a toast naming it. It never moves while only one sector is open.
- [ ] Multiplayer: everyone sees the same spin, result, position and toasts; a client can buy, and only that client can take the weapon. A player joining mid-run sees it in the right state.

### 3.35 Manual checklist: minimap and interaction glows

- [ ] On a generated map a square minimap sits under the location card (top left). Forward is always up: turning the camera turns the map, the player arrow stays pointing up.
- [ ] Rooms are tinted by sector with light wall outlines and gaps at doorways; sealed sectors and rooms behind closed side doors are dark, and secret rooms only appear once opened. Opening a door or sector repaints it.
- [ ] Markers: closed doors (orange), objectives and the extraction zone (blue, pinned to the edge when far), grid generators (blue online, red tripped), the Gun Dispenser (green, pinned), Supply Caches (green), squad mates (white, pinned).
- [ ] M hides and shows it (the debug map overlay moved to J). Nothing shows in the FOB or sandbox without a map.
- [ ] Within ~16 m, usable things get a pulsing coloured outline: green on weapon drops, Supply Caches and the Gun Dispenser (only while it can be used); blue on credit doors, switches and relays, objective props and tripped grid generators; yellow on ammo crates, medkits, secret stashes and supply-drop crates. The outline goes away once used (door open, prop done, pickup taken).
- [ ] Outlines follow moving parts (spinning drops, opening doors) and don't cast shadows. Check frame rate in a busy room.
- [ ] Build check: the minimap draws in a player build (it uses the built-in UI/Default shader).

### 3.36 Barricade nailing sound

- [ ] Repair a barricade board: the nail sound plays three times in quick succession at the barricade.
- [ ] Each board repaired plays its own three hits; holding repair through several boards repeats it per board.
- [ ] A client standing near the barricade hears the three hits when the host (or another player) repairs it.
- [ ] Starting a new run (all boards reset to full) does not play the sound.
- [ ] Boards breaking still play the break sound, not the nail sound.

### 3.37 Remaining passives and doctrines

- [ ] Class HUD: no passive shows "(soon)" and no doctrine shows "*" for any class.
- [ ] Engineer Salvager: Engineer kills give about 1.5x the Scrap of another class's kills.
- [ ] Engineer Handy: holding F on a barricade puts boards up visibly faster than other classes (about 0.47 s vs 0.7 s per board), and none get refused.
- [ ] Demolitionist Bandolier: the Mule Launcher shows an 8-round magazine and 24 reserve (6 / 18 for other classes). Swapping class back removes the bonus.
- [ ] Covering Fire (Gunner + Tank in a co-op squad): enemies the Gunner shot in the last 4 s hit the Tank about 30% softer.
- [ ] Overwatch (Gunner + Engineer): enemies a sentry hit in the last 5 s take about 20% more damage from the Gunner.
- [ ] Clients: both doctrines work when the Gunner, Tank or Engineer is a client (host applies them).

### 3.38 Effects, shield wall and objective models

- [ ] Shots: each tracer is a short bright streak that flies to the hit point (not an instant line). Hitting walls throws sparks and dust and leaves a bullet hole that fades after about 10 s; hitting enemies sprays dark blood and leaves no hole.
- [ ] Other players', sentries' and drones' shots show impacts too.
- [ ] Explosions (Mule Launcher, Cluster Charge, Bloater, ruptures, objective blasts): fireball, rising smoke, sparks and debris that bounce, an orange light flash, and a scorch mark on the floor that fades after about 18 s. No orange sphere.
- [ ] No pink particles or decals in a player build (they use Sprites/Default).
- [ ] Tank shield: translucent blue hex field between two posts, grows in when deployed, pulses when hit, turns red as it weakens, flickers when low or about to expire, and bursts into sparks when it breaks. Enemies still attack it and player bullets still pass through.
- [ ] Objectives use models: generator, data/lockdown terminal, containment console, cryo tank, reactor valve, sample case, keycard pedestal and security locker. Each status light still changes colour with progress, and the objective glow outlines the model.

### 3.39 Balance pass (T-77)

- [ ] FOB skill tree: at class rank 2 you have 1 point, at rank 10 you have 6; the whole tree only fits at rank 28.
- [ ] Last Stand card says +60%; Frenzy (Warrior) says +12% melee damage; Field Repairs (Engineer) says heal 2 on kill and 12 after each surge; Gunner Marksman shows 8% / 12%, Demolitionist Sapper 5% / 7% / 10%.
- [ ] Stacking damage reduction mods stops helping past 45% (watch the damage you take as Tank with several Kevlar/Hardened/Juggernaut).
- [ ] Stacking cooldown mods stops past +75% faster.

### 3.40 Jump, enemy steps, doors and heartbeat sounds

- [ ] Jumping plays PlayerJump (landing still plays the heavy footstep).
- [ ] Infected within about 20 m make footsteps as they move; big ones (Bulwark, bosses) step slower, lower and louder. A big horde doesn't drown everything out.
- [ ] Buying a door with credits plays PurchaseDoor then DoorOpening at the door; security switch and power relay doors play only DoorOpening.
- [ ] A client hears door sounds when someone else opens a door, but not a burst of them on joining a run or when an Operation opens every door.
- [ ] Below 30% health a heartbeat plays every 0.9 s, every 0.62 s below 15% and while downed; it stops when healed or revived.

### 3.41 Level music and music volume

- [ ] Entering a generated map (Survival, Endless or an Operation) fades in "Endless Infestation"; near its end it crossfades into "Endless Infestation 2", then back, for as long as you stay on the map.
- [ ] Going back to the FOB fades the level music out. Quit to title: the title theme plays instead, and the level music resumes after you close the title while still on a map.
- [ ] Fresh install (or a settings file still on the old 70% or 25% default): the Music slider starts at 10%. A Music value you set yourself is kept.

### 3.42 Stats readout, dispenser placement, closets

- [ ] The green dev stats text (FPS, speed, enemies, waves, network) is hidden by default; ` (backquote) shows and hides it, and H toggles the controls help only while it is shown. The Settings FPS counter still works.
- [ ] Gun Dispenser always stands on the floor of its room (never on the ceiling, a shelf or inside furniture), in Survival and Endless.
- [ ] L-shaped / notched rooms: no stray walls in the notch; every breach closet behind the boards is an open dark void that enemies come out of.
- [ ] Entering a map logs `[Music] Level music: Music/EndlessInfestation (195 s)` in the Console.

### 3.43 Breach spawn pacing, far barricades, Gun Dispenser on a wall

- [ ] During a surge, each barricade lets in one zombie at a time, sometimes two together, then waits a few seconds before the next.
- [ ] Zombies that spawn in closets far from you still tear their barricade down (boards break and the break sound plays even when you are across the map).
- [ ] The Gun Dispenser always stands on the floor with its back against a wall, facing into the room.
- [ ] It is never in front of a doorway, sector door, breach closet, switch or vent, and you can walk past it on both sides.

### 3.44 Map textures, floor clutter, ammo stations

- [ ] Room walls show steel panels with seams and bolts, floors show worn 1 m tiles with grout lines, ceilings show a drop-ceiling grid with a few stained tiles. Service tunnels show sooty brick walls with bare concrete floors and ceilings.
- [ ] Room colours still differ by theme (the textures are tinted, not replacing the colours). Tile grout lines line up across each floor plate.
- [ ] No grime squares, blood smears, paper sheets or rubble piles on floors. Crates, lockers and furniture are still there.
- [ ] Each sector has one ammo station on a wall (olive cabinet, yellow bands, glowing strip). It doesn't block a doorway, closet or the Gun Dispenser.
- [ ] Outside a run its prompt says to start a run. During a run, F with 200 or more credits takes 200 and shows "Ammo refilled", and every gun's reserve is full. With fewer credits it says "Not enough Field Credits". In co-op a client gets the same.

### 3.45 One minimap, wall furniture, no sector squares

- [ ] Only one minimap, at the top left under the location card: the map, its frame and the markers all in the same square. Nothing at the bottom left. It turns with you (forward is up). M opens the full map instead (see §3.54). It only shows open sectors and opened side rooms; a sector appears when its door is bought, and markers (objectives, generators, caches) in locked areas stay hidden.
- [ ] No coloured squares in the middle of room floors.
- [ ] Server racks (singles and rows of three) and lockers stand with their backs to a wall, facing into the room. None sit in front of a doorway, breach, switch, crawl vent, loot spot, fuse box or extinguisher.

### 3.46 Power-ups

- [ ] During a run, now and then (at most 4 per surge, at least 12 s apart) a kill drops a glowing spinning token with a coloured light and a name over it: MAX AMMO (three rounds), DOUBLE CREDITS (two coins), INSTA-KILL (skull), NUKE (bomb). It blinks for its last 8 s and vanishes after 30 s.
- [ ] Walking over it shows a big banner for everyone. Max Ammo refills every player's guns. Double Credits: kill credits are doubled for 30 s, with a timer at the bottom centre. Insta-Kill: any hit kills an infected (not a Specimen) for 30 s, with a timer. Nuke: white flash and boom, every infected dies, each player gets 400 credits, and those kills drop nothing.
- [ ] Co-op: drops, banners and timers match on both screens; a client can pick one up.
- [ ] Ending the run removes any drops left.

### 3.47 Thrown grenades

- [ ] E (or the left shoulder button) lobs a dark green grenade where you look. It rolls and goes off about 2 s later (no blast on contact), hurting infected within about 5 m and you at half damage.
- [ ] You start with 2, shown above the weapon card ("● 2  E"). At 0 nothing happens. Ammo stations, Supply Cache refills and Max Ammo bring you back to full.
- [ ] Engineer with Bandolier carries 4.
- [ ] Co-op: the other player sees your grenade fly and explode; a client's grenade kills and earns credits.

### 3.48 Perk machines and the Weapon Forge

- [ ] Every map has four perk machines standing against walls (one of each, the first in the starting sector) and one Weapon Forge workbench mid-map. None blocks a doorway, breach closet, ammo station or the Gun Dispenser.
- [ ] Swift Hands (1,200): reloads 1.5x and swaps 1.3x faster. Iron Hide (1,500): max health 150. Field Medic (1,000): reviving a teammate takes 2 s instead of 4. Fleet Foot (1,000): sprint about 15% faster.
- [ ] Buying shows a toast and a badge right of the health card. An owned perk's machine says "(owned)" and can't be bought again. Too few credits shows "Not enough Field Credits".
- [ ] Going down loses every perk ("Perks lost"). Ending the run clears them too.
- [ ] The forge prompt names the gun in your hands: Standard to Refined costs 1,000, Refined to Prototype 2,000, and a Prototype says it's already maxed. The upgraded gun keeps its attachments, gains one, and comes back with full ammo.
- [ ] Co-op: a client can buy perks and forge; the host takes the credits and only the buyer gets the perk.

### 3.49 Pause menu, keybinds and map tidy-ups

- [ ] Esc: PAUSED and Resume in the middle, then a wide Settings button, then Quit to title and Quit to HUB side by side. The Co-op host/join panel sits top right.
- [ ] Quit to HUB in a run takes two clicks and puts the squad back at FOB Halcyon (run ended). It's greyed out at the FOB and for co-op clients (hover says why).
- [ ] Settings has General and Keybinds tabs. Click an action, press a key or mouse button (Mouse 4/5 work), and it applies straight away. Taking a key another action uses swaps them. Esc cancels the rebind without closing the menu. Reset keybinds restores WASD etc., and binds survive a restart.
- [ ] The backquote dev readout (FPS / speed / enemies) is gone; Settings › Show FPS still shows the small counter.
- [ ] Command center: you can switch between every Operation in the row, back and forth, including earlier ones.
- [ ] Supply caches sit square to the wall with their backs against it.
- [ ] A power-up from a zombie killed inside a breach closet lands in the room just in front of the boards.
- [ ] Storage shelves and desks stand against walls, clear of doorways, closets, switches and loot.

### 3.50 Syringes, pings, squad stats and corner spreading

- [ ] Each ammo station has a white syringe box on its right. F buys one self-revive kit for the squad (1,500), up to 6 kits ("SQUAD · N KITS" goes up). At 6 it refuses.
- [ ] Middle mouse (rebindable, D-pad up on a controller) pings what you look at: a red ENEMY marker that follows that infected, a yellow marker naming an item (perk machine, ammo station, door...), or a blue HERE marker on a surface. Markers show distance, stick to the screen edge when off screen and fade after a few seconds. Max 3 per player.
- [ ] Co-op: your teammate sees your pings labelled "P<id>", and you see theirs.
- [ ] Middle mouse no longer melees (V still does).
- [ ] Hold Tab during a run: SQUAD STATS with surge, run time and per-player kills, headshots, revives, downs, credits spent and current credits. With 2+ players the top scorer has ★ MVP.
- [ ] The end-of-run screen has a Squad table with the same stats and the MVP in its header.
- [ ] Infected coming around corners no longer all file along the inside edge: most swing wide by different amounts, and they move at slightly different paces, so a group arrives spread out. Bosses and barricade-breakers path as before.

### 3.51 Mutation glow and round speed-up

- [ ] Mutated infected show a glowing ring on the floor around their feet (green regenerating, purple splitting, pale grey hardened, red swift; several traits give concentric rings) and a soft light of the first trait's colour. No floating squares through the body. The glow goes out when they die.
- [ ] Infected get faster as surges go by in Survival, Endless and Operations: about 7% faster by surge 5, 15% by 12, 35% by surge 25 (on top of health growing to ×2.5 by surge 30). Nothing outruns a sprinting player from the speed-up alone; bosses keep their own pace.

### 3.52 Wall fixtures actually appear, glow on everything usable

- [ ] Every generated map now has an ammo station (with the syringe box) in each sector, four perk machines and the Weapon Forge, all flush against walls. The Gun Dispenser shows up in Survival and Endless. (Before this fix the wall-spot finder aimed inside the wall itself, so none of these could be placed.)
- [ ] Player.log on map load: "Ammo: N ammo stations placed." with N = the sector count, and "Perks: 4 perk machines placed, forge placed."
- [ ] Supply caches sit just off the wall, not clipping into it.
- [ ] Glow outline on every interactable: FOB facility consoles and the range's return console, Engineering structures (when there's something to do), downed teammates (blue while down), plus everything that already had it.

### 3.53 Hub music

- [ ] After the title screen, "Bunker Pulse" fades in at FOB Halcyon and the range and loops. Deploying to a map crossfades to the level tracks; returning to the FOB (or Quit to HUB) crossfades back. Quit to title still plays the title theme.

### 3.54 Full map and grenade icon
- [ ] Press M in a run: the minimap disappears and the whole open map shows in the middle of the screen at half opacity, north up, with an orange arrow for you that turns as you look around. Doors, objectives, generators, the Gun Dispenser, caches and teammates show as dots. Press M again: it closes and the minimap is back.
- [ ] The grenade pane above the weapon card shows the grenade icon, the count, and the bound throw key in a little key cap (rebind Throw grenade in Settings › Keybinds and it updates). With 0 grenades the icon greys out.

### 3.55 Title attract mode, centred pause menu, restock on game start
- [ ] Open the game: behind the title menu a random map plays, with a horde of infected breaking out of closets and chasing from room to room while the camera follows. Reopen the game: a different map. Play solo, Host and Join all close it and you land in the FOB with no map or zombies left over. Quit to title from a run shows a fresh one.
- [ ] Pause (Esc): PAUSED, Resume, Settings and the Quit to title / Quit to HUB row sit in the middle of the screen. Resume closes the pause screen.
- [ ] In the hub, fire off some ammo and throw grenades, then start a game mode: every magazine, reserve and grenade is full again.

### 3.56 Gameplay fixes batch (wipe, respawn, pings, kits, grenades, barricades, drops, field mods)
- [ ] A run starts with 0 squad self-revive kits; syringes at ammo stations add them. With no kits, the run ends as soon as every player is downed.
- [ ] Respawning next to a teammate (end of surge) never drops you under the map, even if they stand against a wall. Falling 25 m below your last footing puts you back on it.
- [ ] Each player has one ping at a time: a new ping replaces your last.
- [ ] Grenades throw a little shorter, drop faster, barely bounce and stop rolling quickly.
- [ ] While a zombie is hitting a barricade (and 1.6 s after), it can't be repaired; the prompt says it's under attack.
- [ ] A weapon dropped by a zombie killed inside a closet lands in the room in front of the opening.
- [ ] With a Field Mod waiting, the picker pops up by itself when the lull starts (once; Esc puts it away and T brings it back).

### 3.57 Objective pop-ups, rainbow quest glow, scrap and credit pops
- [ ] When a new objective appears, a "NEW OBJECTIVE" card shows its name in the middle of the screen for about 2.5 s, then the name glides into its row in the objectives card (top right).
- [ ] Timed objectives have a bar under their row that drains with the timer (green, then yellow, then red); counted objectives show a progress bar.
- [ ] Objective props and offline generators glow in cycling rainbow colours and can be seen from about 45 m away. Required rows have a rainbow dot.
- [ ] Gaining scrap shows "+N scrap" just left of centre, credits show "+N" just right of centre, both below the crosshair (never on the aim point).

### 3.58 Scopes, attachments, sights on the aim point, Armory try-out
- [ ] Aim with any gun: the sights (iron, built-in or fitted optic) line up exactly with the aiming dot in the middle of the screen, and shots land on it.
- [ ] Vesper DMR (or any gun with a Sniper Scope): aiming switches to a full-screen scope view (black edges, crosshair, red dot in the centre, strong zoom) and the gun model disappears; letting go of aim brings it back.
- [ ] Armory: under each firearm there are slot buttons (Optic, Barrel, Underbarrel, Magazine, Stock). Clicking cycles through none and each option, right-click goes back; at most 3 fitted. Guns with a built-in optic (Wasp, Vesper, Hailstorm, Mule) have no Optic button.
- [ ] Fitted attachments show on the gun: red dot / holo / sniper scope on top, compensator or long barrel on the muzzle, foregrip or laser underneath, extended mag. They persist after restarting the game.
- [ ] Changing a weapon (or its attachments) at the Armory puts that weapon straight into your hands.

### 3.59 Hub: class pick, one section per booth, textures, smaller hangar
- [ ] After the title screen, a "CHOOSE YOUR CLASS" picker comes up in the hub (once per session) with a card per class (role, ability, passives). Locked classes show their unlock level. Picking sets the class and its saved loadout; Esc keeps the current one.
- [ ] Each booth opens only its own section (Armory shows only the Armory, Command Center only deployment, and so on); there is no tab bar.
- [ ] The hangar is smaller (32 x 24 m) with tiled floor, steel wall panels, concrete skirting, a drop ceiling with girders, metal booths and a coloured floor mat in front of each booth. Nothing overlaps; the spawn is clear.

### 3.60 Supply Cache supplies, engineer structure and nest models
- [ ] Open a Supply Cache during a run: three rows, Ammo refill, Med kit and Grenade bundle, each with a price. The med kit row shows your HP and greys out at full health; the grenade row shows carried/capacity and greys out when full.
- [ ] Buy each one (as host and as a client): ammo fills gun reserves but not grenades, the med kit heals to full, the bundle refills grenades. A toast confirms each; not enough credits shows the red refusal.
- [ ] Build a sentry, barricade and arc fence, and deploy the drone: each uses its new model (tripod sentry with twin-barrel head that still turns and fires from the muzzle, armoured barricade wall with back braces, two insulator pylons with the blue arc between them, quad drone with spinning rotors that faces where you look). Damage one: its accent panels darken toward red; upgrade it: they shift to the tier colour. Shots and enemy hits still land on the same shapes as before.
- [ ] Start a nest objective: the nest is a lumpy flesh mound with glowing pustules and bone spines, pulses, has the rainbow quest glow, takes damage anywhere on it and vanishes when destroyed.

### 3.61 Teammate animations, melee grip and swing
- [ ] With a second player, watch them walk: legs stride with bending knees, the body bobs, the torso twists a little. Sprinting: longer strides, a forward lean, and the weapon lowered across the chest. Walking backwards plays the cycle in reverse.
- [ ] Jumping tucks the knees until they land; crouching folds the legs under the hips and leans the torso (no squashed body).
- [ ] Aiming down sights shoulders the weapon toward their eye line; it goes back to the hip when they stop. The held weapon swaps correctly while in any pose.
- [ ] First person melee: the Saber-7 and Tether Cleaver are held blade-up in the right hand only (no second hand floating on the handle); the Railspike Maul is held two-handed, angled across the body.
- [ ] A swing winds up, cuts quickly across to the lower left (one-handed) or chops down through the centre (maul), then eases back to the rest pose. Firearms still sprint, reload and kick as before.

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

Status 2026-11-03: 203/203 EditMode tests pass in the compile-check runner. PlayMode tests compile but have not been run in the editor yet.

## 7. Known issues

- Not yet verified in the Unity editor (written in an environment without Unity).
- HUD is IMGUI and allocates a little each frame; it is a placeholder for the UI Toolkit HUD.
- Viewmodel is primitive boxes rendered by the world camera, so it can clip into walls when pressed against them.
- Melee hits use the camera position; a target directly overlapping the player's camera may be missed.
- No audio yet.
- M2 co-op: the host sends all enemies to every client (no distance-based priority yet); enemy types are numbered by their order in `PrototypeEnemies` until definitions are registered assets. Clients see enemies about 100 ms behind the host.
- M3: the sandbox is one open arena, so most spawn points are visible; real maps (M5) provide hidden spawn spots. Credits cannot be spent until Supply Caches (M6).
- M4: a client's shield copy doesn't darken with damage (only the host's copy tracks health). Explosions ignore walls (no line-of-sight check yet). Bomblet copies on clients may land slightly differently from the host's; the damage follows the host's.
- M7: Creepers don't use vents yet. Specimen Zero has no arena hazards or damage-type armor yet. Supply crates aren't sent to players who join mid-surge. Acid globs fly through walls on their arc (Retchers only fire with a clear line of sight).
- M8: objective props are placed after the NavMesh bake, so enemies can bump into them. Generators don't power doors or turrets yet. Operations open every sector at once.
- M9: structures use the Ignore Raycast layer so player bullets pass through them; Retcher globs and Pyre blasts don't hurt structures yet. Arc Fences don't use the map's power grid.
- M10: the sandbox pause-screen class buttons ignore class locks (dev override). The client in co-op deploys wherever the host deploys, with no ready check yet. Research doesn't affect host-run systems (Scrap, credits). Kill counts per enemy type (for dossiers) only count on the host or solo.
- Map rework: doors, switches and barricades are placed after the NavMesh bake (like M8 props). Power relays don't link to M8 generators. An enemy that spawns while a closet is still sealed waits there until it breaks the boards, so a fully repaired room can hold a small queue.
- Enemies use scripted low-poly models (T-58) and code-driven poses instead of animations; models aren't rigged. If a model faces the wrong way or lies on its side after import, the FBX axis settings in `Tools/Blender/build_models.py` are the place to fix it.
- M1.5 spike: remote players are smoothed toward the latest position (no interpolation buffer), so they can look slightly floaty at high ping. Strafing dummies move on each peer's own clock and can be at different spots on each screen; the host widens its hit check for them.
- M1.5 spike: players cannot damage each other (by design for PvE), and remote capsules don't block movement or bullets.
- M11: Lobby and Relay (join codes over the internet) are not in yet; they need the Unity Gaming Services package and a Unity Cloud project linked in the editor. Direct IP host/join still works. A rejoining player gets a new player number. Reviving uses each player's own reported position, so the 1.5 m slack is the only latency allowance.
- The networking code compiles here against hand-written stand-ins for Netcode 2.13.3, so the first editor compile is the real check.

