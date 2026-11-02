# Decisions

Three lists: what the brief already confirmed, technical choices made in M0, and open design questions with a **recommended default**. A recommendation is not a decision until the user approves it. All open items are implemented as configurable data so they can change later.

## Confirmed (from the project brief)

| ID | Decision |
|---|---|
| C-01 | PC first, first-person, mouse and keyboard primary |
| C-02 | Player-hosted co-op, 2–4 players |
| C-03 | Five classes at launch: Gunner, Tank, Warrior, Engineer, Demolitionist |
| C-04 | Duplicate classes allowed; diverse squads get synergy bonuses |
| C-05 | All classes can use all weapon categories; preferences, not restrictions |
| C-06 | Downed state with limited revives; eliminated players spectate |
| C-07 | Temporary run upgrades are lost when a deployment ends; some permanent rewards are kept on failure |
| C-08 | Players gather at the home base before deploying |
| C-09 | Three modes: round-based survival, mission deployments, endless |
| C-10 | Modular handcrafted rooms assembled procedurally; first environment is an underground research facility |
| C-11 | Field Mod choice: 3 options, Common/Rare/Legendary, every few surges and after bosses |
| C-12 | Extraction is a timed defense objective, not voluntary early extraction |
| C-13 | Stylized low-poly art, performance first |
| C-14 | Original names, systems, and terminology throughout |

## Technical decisions (M0)

| ID | Decision | Reasoning |
|---|---|---|
| T-01 | **Unity 6 LTS** (6000.0.x) | Mature C# workflow, strong FPS and co-op ecosystem, ScriptableObjects fit the data-driven design, the brief prefers it, LTS gives two years of fixes. Unreal would add C++/Blueprint overhead and heavier rendering defaults for a low-poly game; Godot's 3D networking and tooling are less proven for this scope. |
| T-02 | **URP, Forward+** | Lightweight, scales to low-end PCs, Forward+ allows many cheap lights (emergency strips, muzzle flashes) with few shadow casters. HDRP is overkill for stylized low-poly. |
| T-03 | **Netcode for GameObjects 2.x**, host mode | See comparison below. |
| T-04 | **Unity Multiplayer Services (Relay + Lobby)** for session discovery and NAT traversal; direct IP for development | Player hosting without port forwarding; free tier is enough to develop. A Steam transport can replace Relay at release. |
| T-05 | **No host migration** | Complex with NGO, rarely worth it for 20–40 minute co-op runs. Host drop ends the run with failure retention (see D-16). |
| T-06 | **Owner-authoritative player movement, host-validated** | Zero input latency for the most important feel priority. Co-op PvE makes cheating a low concern; host still clamps speed and teleports. |
| T-07 | **Client-claimed, host-validated hits** | Instant hit feedback; host checks rate, ammo budget, range, line-of-sight and resolves damage. No rewind/lag compensation. |
| T-08 | **Custom compact enemy snapshots** instead of per-zombie NetworkObjects | Bandwidth and CPU with large hordes (risk R3). |
| T-09 | **Seed-based map sync** | Every peer builds the same layout from seed + environment id; no geometry over the network. |
| T-10 | **Rules assemblies with `noEngineReferences`** | Fast EditMode tests, deterministic logic, safe for future dedicated server or headless simulation. |
| T-11 | **Input System, UI Toolkit, AI Navigation, Newtonsoft JSON** | Rebinding and controller-ready input; UI Toolkit scales for many menu screens; runtime NavMesh baking for generated rooms; robust versioned JSON. |
| T-12 | **Git + Git LFS**, branch per feature, PR into `main` | Recoverable history, binary assets in LFS. |

| T-13 | **Sprinting does not cancel a reload; firing or aiming stops a sprint** | Keeps reloads from being lost to habitual sprinting. Weapons need a short sprint-to-fire delay (per weapon) to keep sprinting a commitment. Revisit after the feel pass. |
| T-14 | **Reload commit point** (default 75% of the reload) | Interrupting before it keeps the old magazine; after it keeps the new one. Clear and testable, avoids "lost" ammo. |
| T-15 | **Input actions defined in code for M1** | Works without asset authoring; the same actions move into an `.inputactions` asset when the rebinding menu is built. |
| T-16 | **IMGUI placeholder HUD for M1** | Zero asset setup; the real HUD uses UI Toolkit and only reads the same state and events. |
| T-18 | **Editor pinned to 6000.6.6f1**; packages added from Package Manager as milestones need them | The user's installed editor. Adding packages through Package Manager picks versions that match it instead of guessed versions. |
| T-19 | **M1.5 spike syncs with named messages, not NetworkObjects** | Players and dummies are built from code with no prefabs yet, and NGO needs registered prefabs for NetworkObjects. Named messages through `SessionService` prove the authority model (owner movement, host-validated hits) with no asset setup. Players move to NetworkObject prefabs with owner-authoritative NetworkTransform when the player prefab exists (M4/M11); enemies keep a custom snapshot channel (T-08). |
| T-20 | **Netcode for GameObjects 2.13.3** pinned in the manifest | Latest 2.x release whose changelog lists Unity 6 support at the time of M1.5. If Package Manager offers a newer verified version for 6000.6, take it. |
| T-21 | **Runtime NavMesh baking with Unity's built-in `NavMeshBuilder`**, no AI Navigation package yet | Generated maps need runtime baking anyway, and the built-in API (`NavMeshBuilder.BuildNavMeshData`, `NavMeshAgent`) ships with the AI module, so there is no package version to match to the editor. Add AI Navigation later if we want editor-baked NavMeshSurfaces or NavMeshLinks. |
| T-22 | **`HitZone.Armor`** for plating | Armor uses the hurtbox's own multiplier, capped at 1, and never counts as critical. Directional armor (Bulwark's front plate) is just a hurtbox in front of the body, so a flank shot hits the body or the weak point instead. |
| T-23 | **Surge rotation**: surges 1–3 Standard, then every 5th a Siege and every other 4th a Swarm; Standard otherwise | Teaches the basics first and gives a predictable rhythm. A rotation table in data replaces it when Elite, Hunt and Supply surges arrive (M7). |
| T-24 | **Field Credit numbers (placeholder)**: kill = 10 × threat cost to the killer (Husk 10, Bolter 15, Bulwark 60); surge clear = 50 + 10 × surge to every player; no assist credit yet | Gives prices a scale for M6 Supply Caches. All numbers live in `SurgeRunSettings` and will be tuned with prices. Assists need damage tracking per player and come with M6/M11. |
| T-25 | **Waves run on the host and reach clients as one small state message** ("df.wave", reliable, 2 Hz plus on change); clients send ready votes | Same named-message pattern as T-19. Clients never decide spawns or rewards, so there is nothing to duplicate or cheat. |
| T-26 | **Ability cooldowns start when the effect ends** | Overdrive (8 s) then 45 s of cooldown, not 37 s. Lengthening an effect (mods, doctrines) can never make it overlap the next use, and the HUD reads simply: effect time, then cooldown. |
| T-27 | **Preferred-weapon handling (placeholder)**: reload ×1.15, swap ×1.25, recoil ×0.85; no damage bonus | "Handling bonus" per GAME_DESIGN §5 without making off-class weapons feel weak (C-05). Per-class numbers in `ClassDefinition`. |
| T-28 | **Client hit claims carry a class damage bonus, capped at ×3, plus melee and stagger flags** | The host still uses its own weapon stats for everything else. The cap matches the biggest class bonus (Lunge); a modified client could at most always hit like a Lunge, acceptable for PvE co-op until abilities are host-validated in M11. |
| T-29 | **Ability spawns are host-authoritative**: the owner asks, the host creates the shield, sentry or charge and deals its damage; clients get visual copies; each spawn expires by itself | One owner for damage and aggro, so enemies (which run on the host) react to it and nothing is applied twice. Requests are checked to be within a few metres of the asking player. Same named-message pattern as T-19. |
| T-30 | **Explosions**: damage falls off linearly to 30% at the edge, no line-of-sight check yet, never hurts other players, the thrower takes full damage unless a passive reduces it | PvE co-op (C-02) without friendly fire keeps explosives fun; self damage keeps them from being free. Line of sight comes with real maps (M5). |
| T-31 | **Part 2 numbers (placeholder)**: Sentry 9 dmg × 6/s, 22 m, 300 HP, 60 s, 35 s cooldown; Cluster Charge 90 dmg / 4.5 m + 5 × 45 dmg / 3 m, 25 s; Shove 2.5 m; Vanguard +25% Lunge; Combined Arms ×1.15 cooldown speed | First pass to feel out the kits. All live in the class data (`PrototypeClasses`) and `PlayerClass`. |
| T-32 | **One NavMesh bake per map**, with doorways open; locked doors are `NavMeshObstacle`s that carve while closed | Separate NavMesh data per sector doesn't connect without NavMeshLinks (AI Navigation package, T-21). A whole-map bake of a greybox map takes milliseconds, and carving keeps enemies out of locked sectors. Revisit if bake times grow with art. |
| T-33 | **Sector unlocks (placeholder)**: Survival opens sector 1 after surge 3, sector 2 after 7, sector 3 after 12, then every 5 surges; a new run closes them again | Matches "starts small and expands" (GAME_DESIGN §3.1) with a predictable rhythm. Buying a door early with credits and objective-driven unlocks (Operations) come with M8. |
| T-34 | **Networked world objects by id** (`INetTarget`): pieces built from the seed or spawned by the host have the same id on every peer; client hits on them go to the host by id | Keeps the "no NetworkObjects" approach (T-19) for secret panels now and generators, nests and structures later. Id ranges: 0x1000 map, 0x2000 objectives, 0x4000 structures. |
| T-35 | **Interact on F; pickups are host-authoritative** | One player gets each pickup; the host checks distance. Ammo crates, medkits and stashes are single use. |
| T-36 | **Run economy numbers (placeholder)**: drop chance 1.2% × threat cost (max 20%), Refined 12% + 2%/surge (max 50%), Prototype from surge 5 (max 20%); prices Pistol 250 · SMG 600 · AR 800 · Shotgun 750 · LMG 1000 · Heavy 1300 · Melee 450, ×1.6 Refined, ×2.4 Prototype; ammo refill 200; cache tiers roll as if surge = sector × 4 | First pass so caches and drops matter without flooding the run. All in `LootRules`. |
| T-37 | **Field Mods are rolled and applied on each player's own machine** | Picks are individual (D-13) and effects are local stats; damage bonuses already go through the host's ×3 cap on claims. Avoids a sync layer for something that can't affect other players. |
| T-38 | **Projectile weapons go through the host** like hits: the client flies a harmless copy and sends origin, velocity and its damage multiplier; the host fires the real grenade from the client's body | Same trust model as hit claims (T-19/T-28); kill credit and self-damage work through the proxy. |
| T-39 | **Weapon modifications change the client's local stats; damage changes ride the outgoing multiplier** | The host keeps computing damage from its own catalog (T-19); tier and attachment damage stay inside the existing ×3 claim cap. |
| T-40 | **Each enemy owns a copy of its stats**; traits, Alpha auras and Specimen phases change the copy, never the definition | Variants and phases need per-enemy numbers. The brain keeps the same object, so a phase change takes effect on the next tick with no rebuild. |
| T-41 | **Variants, the boss phase and modifiers ride one extra snapshot byte** (13 bytes per enemy) | Clients need the visual tells and the boss bar's phase; one byte keeps 48 enemies at about 7.5 KB/s. Traits are rolled by the planner from the run seed, so surges stay reproducible. |
| T-42 | **Enemy attack visuals are relayed, damage is not** ("df.efx"): acid globs fly a fixed arc, pools and Pyre blasts are replayed on clients; the host applies all damage through each target's `ICombatTarget` | Same host-authoritative model as melee (T-08). A glob's arc is deterministic from start, end and flight time, so the replay matches without per-frame updates. |
| T-43 | **Advanced enemy numbers (placeholder)**: Creeper 55 HP from surge 4; Pyre 80 HP, 55 dmg / 4.5 m blast, from 5; Retcher 130 HP, 12 dmg glob + 8 dps pool, from 8; Alpha 520 HP, aura +25% speed / +20% reduction in 9 m, from 10; Goliath 2400 HP, from 14; Stalker 900 HP; Specimen Zero 6000 HP, phases at 66% and 33%, summons every 9 s. One-per-squad enemies get +60% health per extra player | First pass for feel. All in `PrototypeEnemies`, `VariantRules` and `SpecimenRules`. |
| T-44 | **Surge rotation (placeholder)**: after three Standard surges, every 10th is Specimen, 5th Siege, 7th Elite, 6th Hunt, 4th Swarm, 9th Supply. Variants from surge 5 (3% per surge, max 35%, ×2 in Elite surges, second trait from 12); Specimen modifiers from surge 20 (two from 40); bounty 1000 credits each plus a guaranteed Prototype drop and a Field Mod offer | Matches D-10 (Specimen every 10 surges in Endless) and GAME_DESIGN §8's surge list. Objective surges come with M8. |
| T-45 | **Objectives and events are one host-owned state** (`ObjectiveDirector`, message `df.obj`, sent on change at most 4 times a second and to late joiners); clients use props through the host | Same host-authoritative model as loot and purchases. One message keeps late joins and event side effects (lights, sealed doors) consistent. |
| T-46 | **Objective numbers (placeholder)**: 3 generators (5 s each), 3 samples, data core 8 s, lockdown hack 6 s, channel range 3.5 m, nest 1500 HP × surge health scale and a Creeper every 6 s (5 alive max); rewards 400 / 350 / 500 / 450 / 300 / 200 credits each; side objectives before surges 3, 7, 11... for two surges | First pass, all in `ObjectiveRules`. Channels drain at half speed so stepping away to fight costs a little, not everything. |
| T-47 | **Operations open the whole map at mission start** and run surges on a timer; extraction (150 s, airlock) starts when the required objectives are done and pauses while nobody is inside | Objective-gated sectors need more mission content; this gets the full mission loop playable. D-08's objective unlocks come with more templates. |
| T-48 | **Events (placeholder)**: from surge 4, 30% per lull, 3-surge cooldown, weights Blackout 3 / Stalker 2 / Lockdown 2 (Lockdown only on a map with two sectors open), never on Specimen or Hunt surges; an event lasts one surge | GAME_DESIGN §12: weighted, at most one, cooldown. Warning a lull ahead gives the squad time to prepare. |
| T-17 | **Players use the built-in Ignore Raycast layer** | Their own capsule never blocks their bullets or melee, with no custom layer setup. Replaced by dedicated layers when the physics layer matrix is defined. |

### T-03 detail: NGO vs FishNet

| | Netcode for GameObjects | FishNet |
|---|---|---|
| Ownership | Unity first-party, tied to Unity 6 roadmap | Community (one main maintainer + contributors) |
| Services | Built-in Relay/Lobby integration, Multiplayer Play Mode, multiplayer tools | Works with Relay via transport, fewer first-party integrations |
| Prediction | Basic; we don't need rollback for PvE | Built-in client-side prediction and lag compensation |
| Performance | Adequate for 4 players if hordes are not NetworkObjects | Generally lighter per object |
| Docs/samples | Large official docs and samples, big user base | Good docs, smaller community |
| Fit | Our hard part (horde sync) is custom either way | Prediction features we would not use |

**Recommendation: NGO.** FishNet's main advantages (prediction, lag compensation) matter most for competitive PvP; Deadfall is co-op PvE with owner-authoritative movement. NGO's first-party services and Multiplayer Play Mode reduce setup and testing friction, which matters more for a small team. The horde bandwidth problem needs a custom snapshot channel under either framework.

## Design decisions D-01 to D-21 (confirmed 2026-11-01)

Approved by Hawnji on 2026-11-01 as recommended. All remain configurable data and can be retuned after playtesting.

| ID | Question | Decision | Tradeoff |
|---|---|---|---|
| D-01 | Revives per player/team | Unlimited teammate revives, but each down raises bleed-out speed (30 s → 20 s → 10 s). **3 self-revive kits per team** findable/purchasable | Encourages teamwork without hard caps; a per-player cap (e.g. 3 downs = eliminated) is harsher and easier to read |
| D-02 | Can eliminated players return? | **Yes**, at the end of the next surge (Survival/Endless) or at the next objective checkpoint (Operations), with base loadout | Keeps friends playing; slightly lowers stakes |
| D-03 | Reward retention on failure | **50%** of permanent rewards earned, plus 100% of challenge progress | 25% feels punishing; 75% removes tension |
| D-04 | Currencies | **4**: Field Credits + Scrap (temporary), Requisition + Intel (permanent) | Fewer is simpler; Intel ties research to playing objectives |
| D-05 | Supply Caches fixed or random? | **Randomized from tagged slots** in rooms, with one guaranteed in the spawn sector | Fixed is learnable; random fits procedural maps |
| D-06 | Share dropped weapons? | **Yes**, any player can pick up a dropped weapon; drops show who found them for 10 s first | Encourages sharing; brief griefing window removed by the timer |
| D-07 | Weapon slots | **2 primaries + 1 sidearm + 1 melee**, plus grenades/ability | 2 total is tighter; more slots weaken choice |
| D-08 | Sector unlocks | **Both**: Field Credits doors in Survival/Endless; objectives in Operations; events can force-open or seal | Waves-only is simpler; objectives-only stalls Survival |
| D-09 | Deployment length | Survival 25–35 min; Operations 20–30 min; Endless open | Shorter runs suit roguelite pacing |
| D-10 | Boss frequency | Survival: final surge + one mini-boss mid-run. Endless: Specimen every 10 surges. Operations: optional boss objective | Frequent bosses lose impact |
| D-11 | Active abilities per class | **1 at prototype; 2 at launch** (third slot reserved for a specialization capstone) | More abilities = more balance and UI work |
| D-12 | Specialization tree size | **3 paths × 6 nodes + 1 capstone** (19 per class) | Larger trees dilute choices early |
| D-13 | Shared vs individual loot | **Individual** Field Credits, **shared** Scrap, individual Field Mod picks, **individual permanent rewards** with a team-wide objective bonus | Individual credits prevent one player spending everything; shared scrap helps Engineers |
| D-14 | Adaptive difficulty factors | **Player count** (always) + **squad downs/health trend** (small ±10% budget adjustment), shown in end-of-run stats. No adjustment based on damage output or progression | Visible and modest so it never feels unfair |
| D-15 | Endless ramp | Comfortable to surge 10, hard by 20, very hard by 30, escalation modifiers make 40+ extreme for a full squad | Steeper curves shorten runs; flatter ones bore experienced players |
| D-16 | Host / client disconnect | No host migration. Host drop ends the run for all with D-03 retention. Client drop: character downed in place, can rejoin within **90 s** with state restored, otherwise treated as eliminated with retention | Host migration is a large, fragile system |
| D-17 | Class synergy effects | Named doctrines (see `GAME_DESIGN.md` §5.1), max 2 active per player, magnitudes set after M4 playtests | Fixed numbers now would be guesses |
| D-18 | Weapon/attachment roster | The 8 prototype weapons in `GAME_DESIGN.md` §6; full roster after M6 | — |
| D-19 | Enemy population/perf targets | Active cap **24 solo → 48 at 4 players** on baseline PC, raise if profiling allows; targets in `TESTING.md` §5 | Higher caps risk frame drops |
| D-20 | Final title | Keep **Project Deadfall** as working title; decide before any store page | — |
| D-21 | Jump and crouch | **Include both** in M1 (low jump for small obstacles, crouch hold for accuracy), evaluate in playtests | Removing later is easy; adding later affects level design |

## Change log

- 2026-11-01: M0 created. Technical decisions T-01 to T-12 recorded. D-01 to D-21 proposed.
- 2026-11-01: D-01 to D-21 approved as recommended.
- 2026-11-01: T-13 to T-17 recorded during M1.
- 2026-11-02: T-18 recorded (editor pin). T-19 and T-20 recorded for the M1.5 networking spike.
- 2026-11-02: T-21 and T-22 recorded for M2 enemies.
- 2026-11-02: T-23 to T-25 recorded for M3 waves.
- 2026-11-02: T-26 to T-28 recorded for M4 classes.
- 2026-11-02: T-29 to T-31 recorded for M4 part 2.
- 2026-11-02: T-32 to T-35 recorded for M5.
- 2026-11-02: T-36 to T-39 recorded for M6. The user is holding editor testing until M10 and asked for M5–M10 to be merged as each passes the compile check.
- 2026-11-02: T-40 to T-44 recorded for M7.
- 2026-11-02: T-45 to T-48 recorded for M8.
