# Game Design

Working design for Project Deadfall. Numbers here are **starting values for playtesting**, not final balance. Anything marked *(decided, D-xx)* points to the confirmed decision in `DECISIONS.md`.

## 1. Fiction and tone

The Blight is an engineered pathogen that escaped a classified research program. Task Force **Ashgrave**, a small rapid-response unit, operates from **FOB Halcyon**, a requisitioned airbase on the edge of the quarantine zone. Each deployment sends a squad into an infected site to recover research, restore systems, and contain outbreaks before they spread.

Tone: grounded military horror. Dark, readable spaces, practical lighting (emergency strips, flashlights, sodium lamps), radio chatter, and a stylized low-poly look with strong silhouettes.

### Glossary (original terms used in UI and code)

| Term | Meaning |
|---|---|
| Deployment | One run into an infected site |
| Field Credits | Temporary currency earned and spent during a deployment |
| Requisition (REQ) | Permanent currency spent at the FOB |
| Scrap | Temporary construction resource for Engineer structures |
| Intel | Permanent research resource from samples and data |
| Field Mods | Temporary roguelite upgrades chosen during a deployment |
| Squad Doctrine | Team composition bonuses |
| Supply Cache | In-map station that sells weapons, ammo, and gear |
| Surge | A wave |
| Lull | The preparation period between surges |
| Sector | An unlockable section of a generated map |
| Specimen | Boss-class infected |

## 2. Core loop

```
FOB Halcyon ──► pick mission + loadout ──► Deployment ──► results ──► FOB
   ▲  spend REQ / Intel on unlocks,          (surges, objectives,           │
   │  research, skill trees, mastery          Field Mods, sectors)          │
   └────────────────────────────────────────────────────────────────────────┘
```

Inside a deployment: **Lull** (buy, build, repair, explore, choose Field Mods) → **Surge** (fight) → reward → occasionally unlock a **Sector** or trigger an **event** → repeat until the mission ends.

## 3. Game modes

### 3.1 Survival (round-based)
- Fixed number of surges per difficulty (initial: 20 on Standard), ending with a Specimen surge.
- Map starts small (3–4 rooms) and expands through sector unlocks.
- Surge types rotate (see §7). Every few surges an optional side objective appears with a bonus.
- Win: clear the final surge. Lose: whole squad eliminated.

### 3.2 Operations (mission-based)
- A mission template defines required and optional objectives, a map graph shape, and surge pacing.
- Objectives pull players through the map; surges trigger on timers and objective milestones rather than strictly between rounds.
- Ends with a **timed extraction defense**: hold the extraction point for N seconds (initial 120–180 s) under heavy attack. There is no voluntary early extraction.
- Optional objectives (e.g. "recover a second data core from a nest room") raise rewards and risk.
- Win: extraction timer completes with at least one player alive in the zone. Lose: squad eliminated, or a required objective fails (e.g. a sample container is destroyed).

### 3.3 Endless
- No final surge. Difficulty rises along a curve with milestone rewards every 5 surges and a Specimen every 10.
- After surge 30, **escalation modifiers** stack (one new permanent run modifier every 5 surges: e.g. armored variants, faster spawns, fewer drops).
- Leaderboard-style stats tracked locally: highest surge per class and squad size.

## 4. Player

### 4.1 Movement (M1 targets)
| Property | Starting value |
|---|---|
| Walk speed | 4.5 m/s |
| Sprint speed | 7.0 m/s (forward cone ±45°), no firing while sprinting |
| Ground acceleration | Reaches 90% speed in ~0.12 s |
| Ground deceleration | Stops in ~0.10 s |
| Sprint-to-fire delay | 0.15–0.25 s by weapon weight |
| Jump | Included, low (≈0.9 m) for vaulting small obstacles (decided: D-21) |
| Crouch | Included as a hold for accuracy and low cover (decided: D-21) |
| Stamina | None in prototype; sprint is unlimited. Revisit if kiting dominates |

Camera: no head-bob by default (accessibility toggle for subtle bob), FOV slider 70–110, raw mouse input, independent ADS sensitivity multiplier.

### 4.2 Health and downed state
- Health 100 base (class modifies). Short regen delay (5 s) then slow regen to the nearest 25% segment, so healing items and support still matter.
- At 0 HP a player is **downed**: can crawl slowly and fire a sidearm. Bleed-out 30 s. A teammate revives by holding interact for 4 s.
- Revive limits and elimination rules: *(decided, D-01, D-02)*.

## 5. Classes

All classes can use every weapon. Each class has preferred categories that get a handling bonus (faster reload/swap, lower recoil). Three specialization paths each. Initial prototype: one active ability and two passives per class. The framework supports up to three actives (*decided, D-11*).

| Class | Role | Preferred | Prototype ability | Passives |
|---|---|---|---|---|
| **Gunner** | Sustained fire, suppression | AR, SMG, LMG | **Overdrive**: 8 s of +40% fire rate and no ammo consumption from reserve on preferred weapons. 45 s cooldown | +25% reserve ammo; sustained fire slowly reduces recoil |
| **Tank** | Durability, aggro, protection | Shotgun, Heavy | **Bulwark Shield**: deployable front shield (8 s, 600 HP) that draws nearby aggro. 40 s | 30% damage reduction while above 50% HP; melee knocks back |
| **Warrior** | Melee, mobility, executions | Swords, axes | **Lunge**: dash 8 m, the next melee hit deals triple damage and staggers. 12 s | Melee kills restore 5 HP; combo chain (3rd hit cleaves) |
| **Engineer** | Construction, automation | SMG, Pistol | **Deploy Sentry**: places a light turret (60 s duration, scrap upgrades it). 35 s | Earns +50% Scrap; repairs structures faster |
| **Demolitionist** | Explosives, area control | Launchers, Shotgun | **Cluster Charge**: thrown charge that splits into 5 bomblets. 25 s | +2 grenade capacity; 50% self-damage reduction from own explosives |

Specialization paths (from the brief): Gunner – Suppression / Heavy Weapons / Efficiency. Tank – Armor / Aggression / Guardian. Warrior – Berserker / Weapon Mastery / Mobility. Engineer – Turrets / Drones / Fortification. Demolitionist – Explosives / Heavy Ordnance / Crowd Control. Tree size is *(decided, D-12)*.

### 5.1 Squad Doctrine (synergy)
Pairs of different classes enable a named doctrine. Each player benefits from at most two doctrines so duplicates still play normally; a full five-class team is impossible at 4 players, so the "diverse" reward is **four distinct classes**.

| Doctrine | Pair | Placeholder effect |
|---|---|---|
| Covering Fire | Gunner + Tank | Enemies hit by the Gunner deal less damage to the Tank |
| Demolition Crew | Engineer + Demolitionist | Explosives repair nearby structures slightly |
| Vanguard | Warrior + Tank | Shield blocks extend Lunge range |
| Overwatch | Gunner + Engineer | Turrets mark targets for bonus Gunner damage |
| Combined Arms | 4 distinct classes | Small squad-wide cooldown reduction |

Effects and magnitudes stay data-driven and unbalanced until playtested *(decided, D-17)*.

## 6. Weapons

Every weapon is a `WeaponDefinition` with: category, damage, fire rate, fire mode, magazine, reserve, reload (tactical/empty), effective range and falloff, recoil pattern, spread (hip/ADS/moving), ammo type, weight (sprint-to-fire, swap time), attachment slots, and crit/weak-point multiplier.

**Prototype roster (8)** *(names original; final roster after M6, D-18)*

| Name | Category | Feel |
|---|---|---|
| Ranger-9 | Pistol | Reliable sidearm, quick swap |
| Wasp SMG | SMG | High rate, close range, low recoil |
| Halberd AR | Assault rifle | All-rounder |
| Breacher-12 | Shotgun | Pump, wide pellets, stagger |
| Anvil LMG | LMG | Large mag, slow reload, sustained fire |
| Tether Cleaver | Melee (axe) | Heavy, cleaves |
| Saber-7 | Melee (sword) | Fast combos |
| Mule Launcher | Heavy | Grenade launcher, arcing explosive |

Experimental category arrives post-prototype (e.g. an arc projector that chains between targets).

**Acquisition**: loadout from FOB, Supply Caches using Field Credits, rare drops. **Quality tiers**: Standard / Refined / Prototype (affects attachment slots and stat rolls on drops). **Weapon slots** *(decided, D-07)*.

**Attachments**: Optic, Barrel, Underbarrel, Magazine, Stock, Special. Attachments are `StatModifier` sets plus optional behaviour hooks.

**Special ammo** (purchasable in-run, limited): Incendiary (DoT), Shock (chain stagger), Armor-piercing (ignores armor). Intel about enemy armor at the FOB informs this choice.

## 7. Enemies

Common framework: `EnemyDefinition` (health, armor, speed, hurtbox zones, attacks, perception, threat cost, animation set, audio set) + a behaviour profile.

| Name | Type | Behaviour | Counter |
|---|---|---|---|
| **Husk** | Standard | Walks, grabs, swarms | Anything |
| **Bolter** | Fast | Erratic sprinting approach, low HP | Precision, area denial |
| **Bulwark** | Armored brute | Slow, plated front, charge attack | Flank, AP ammo, explosives |
| **Creeper** | Crawler | Low profile, uses vents, ankle bites | Look down, shotguns |
| **Pyre** | Exploder | Runs at clusters, detonates on death or contact | Kill at range |
| **Retcher** | Ranged | Lobs acid that leaves pools, keeps distance | Close gap, cover |
| **Alpha** | Elite | Buffs nearby infected, more HP, glowing weak point | Focus fire |
| **Variant** | Mutation | Any base type with 1–2 mutation traits (e.g. regenerating, splitting, hardened) | Read the visual tell |

**Weak points**: every enemy has a head zone (×2.0 default); some have extra zones (Bulwark back pack ×2.5, Alpha glowing growth ×3.0).

**Specimen (boss) framework**: phases with health thresholds, weak points that open per phase, summons, arena hazards, armor that needs a damage type. Prototype boss: **Specimen Zero**, the research facility's escaped test subject, with three phases (stalk → rampage with summons → exposed core).

## 8. Surges and the encounter director

- **Threat budget**: each surge gets a budget from `DifficultyCurve(surge, playerCount, difficulty)`. Enemy types have a threat cost and an unlock surge. The `WavePlanner` spends the budget on a composition.
- **Pressure, not just HP**: difficulty raises budget, mix, spawn concurrency, spawn-direction count, elite/variant chance, and event chance. Enemy HP scaling is capped (initial cap ×2.5 by surge 30 in Survival).
- **Surge types**: Standard, Swarm (many weak), Elite, Siege (3+ spawn directions), Hunt (one pursuer + light support), Supply (resource crates spawn during the surge), Specimen, Objective.
- **Lull**: 30 s default, skippable when all players ready. Occasional **ambush** (a short surprise spawn during a lull, max once per 5 surges, announced 3 s ahead by audio).
- **Concurrency cap**: max active enemies, e.g. 24 solo / 48 at four players (*decided, D-19*). The director queues the rest.
- **Adaptive difficulty** *(decided, D-14)*: proposed factors are player count and current squad health/downs only, visible in the end-of-run breakdown.

## 9. Progression and economy

### 9.1 Temporary (per deployment)
Field Credits (kills, objectives) buy weapons/ammo/gear at Supply Caches and open some doors. Scrap (drops, salvage) builds structures. Field Mods: offered every 3 surges and after Specimens; choose 1 of 3; rarities Common/Rare/Legendary; class-specific pools; stacking rules per mod (`Unique`, `Stacks(n)`, `Evolves`). All temporary state is cleared on deployment end.

### 9.2 Permanent
- **Operator level** (account XP) unlocks classes, features, and FOB facilities.
- **Class rank** unlocks skill tree points and cosmetics. **Mastery** and **prestige** extend it.
- **Weapon level** unlocks attachments and camos.
- **Research** (Intel + REQ) unlocks technologies: new structures, ammo types, enemy dossiers.
- **Challenges and achievements**, displayed in the Trophy Room.

### 9.3 Currencies *(decided, D-04)*
Proposed: **two temporary** (Field Credits, Scrap) and **two permanent** (Requisition, Intel). The economy uses a generic `CurrencyDefinition` so more can be added.

### 9.4 Failure
Temporary upgrades and resources are always lost. Permanent rewards earned so far are kept at a configurable retention rate *(decided, D-03)*.

## 10. Maps

### 10.1 Generation
Hand-built room and corridor prefabs with **sockets** (typed doorway connectors: size, type) are assembled into a graph:
1. Pick a **graph template** for the mode/mission (critical path length, branch count, required room tags).
2. Place spawn room → grow critical path → attach branches (side, loot, secret) → place objective and boss rooms by tag and distance.
3. Validate (bounds overlap, socket compatibility, connectivity, reachability of required rooms, spawn point counts). Retry with next sub-seed on failure, up to N attempts, then fall back to a known-good seed.
4. Group rooms into **sectors** separated by locked doors that open during the run.

Debug: seed field in a dev menu, gizmo overlay showing graph, sockets, sector colours, and validation errors.

### 10.2 First environment: Cinder Ridge Research Complex (underground facility)
Room kit: decon airlock (spawn), lab bay, server hall, cold storage, maintenance tunnels, reactor control (objective), specimen containment (boss arena), cafeteria, security checkpoint, vent crawl (secret), corridor pieces (straight, L, T, stairs).

Environmental systems: power grid (generators feed doors, lights, and turrets), security lockdown (sector seals until a terminal is hacked), keycard doors, blast doors.

### 10.3 Planned environments
Underground research facility (first), abandoned city, military bunker, industrial factory, abandoned airport, arctic installation, nuclear power station, secret underground laboratory, abandoned cargo ship. Each is an `EnvironmentDefinition` (room kit, lighting profile, enemy weights, ambience).

## 11. Engineering

Structures: **Sentry** (auto-target, ammo-limited, refill with scrap), **Drone** (follows the Engineer, configurable attack/repair mode), **Arc Fence** (slows and damages, power draw), **Barricade** (blocks or slows, durability). Placement uses a ghost preview with validation (ground angle, clearance, distance from spawn points and objectives, NavMesh-carving rules so paths are never fully blocked). Upgrades are per-structure tiers bought with Scrap.

Non-Engineers can repair and build basic barricades at higher cost; Engineers are better, not exclusive.

## 12. Random events

Weighted, at most one active major event, cooldown between events.

| Event | Effect |
|---|---|
| Red Sky | Infected move and hit harder for one surge; better drops |
| Blackout | Lights fail for a surge; flashlights and muzzle flash matter; power objective restores early |
| The Stalker | A pursuer hunts the squad until killed; drops a rare reward |
| Containment Breach | An experimental system fails and spawns a hazard (gas, electrified floor) until shut down |
| Emergency Lockdown | Sectors seal; the squad must reach a terminal to lift it |
| Secret | Rare hidden encounters triggered by exploration |

## 13. FOB Halcyon (home base)

Walkable hub with: Command Center (missions), Armory (loadouts, attachments), Research Lab, Engineering Workshop, Intelligence Center (enemy and Specimen dossiers, mission intel), Training Range (class ranks, practice), Trophy Room. Co-op players appear in the FOB together; the host launches from Command Center once everyone is ready at the deployment board.

Intel matters: dossiers reveal enemy compositions and armor so players can pick AP ammo, explosives, or a Tank.

## 14. HUD

Health and downed timer, ammo (mag/reserve) and weapon, ability cooldowns, current surge and enemies remaining, objective tracker, teammate panels (class icon, health, downed/revive state), resources (Field Credits, Scrap), boss bar when relevant, hit markers with crit variant, directional damage indicator. High-contrast mode and scalable HUD.
