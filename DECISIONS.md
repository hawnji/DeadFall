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
