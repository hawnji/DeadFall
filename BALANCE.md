# Balance notes (first pass, T-77)

This is a paper pass: nobody has played a full run yet, so it only fixes things the numbers show are out of line. Every number below lives in code (`Rules.*`), so a playtest can change it in one place.

## Credits (Survival, per player)

Income per surge = kills × 10 credits per threat point + clear bonus (50 + 10 × surge) + barricade repairs (10 each, ignored below). Threat budget per surge is 6 + 4n + 0.25n² (n = surge − 1), × 1 / 1.75 / 2.4 / 3 for 1–4 players.

| Surge | Solo income | Solo total so far | 4-player income each |
|---|---|---|---|
| 1 | 120 | 120 | 105 |
| 3 | 230 | 520 | 190 |
| 5 | 360 | 1,175 | 295 |
| 10 | 770 | 4,160 | 615 |
| 15 | 1,310 | 9,590 | 1,030 |
| 20 | 1,970 | 18,075 | 1,540 |

What things cost: first sector door 750 (+250 for each later one; the five sector doors on a six-sector map total 6,250), branch doors 500, weapons 250–1,300 (Refined ×1.6, Prototype ×2.4), Gun Dispenser 950, ammo refill 200.

**Verdict: unchanged.** A solo player opens the first door around surge 4 and the Gun Dispenser around surge 5, which fits "start small and expand". Opening every sector alone takes until about surge 12 if you buy nothing else, so early on doors compete with guns, the Gun Dispenser and ammo. Late in a run there is enough money to do both, which suits the final push. In a squad each player earns a little less, but four wallets open the map much faster, which is the point of co-op.

## Weapons

| Weapon | Price | Burst DPS | Sustained DPS (with reloads) |
|---|---|---|---|
| Ranger-9 (pistol) | 250 | 200 | 106 |
| Bulldog .44 (pistol) | 250 | 180 | 92 |
| Wasp SMG | 600 | 270 | 139 |
| Kestrel PDW | 600 | 262 | 137 |
| Breacher-12 (9 pellets) | 750 | 157 | ~100 |
| Hailstorm (8 pellets) | 750 | 307 | ~150 |
| Halberd AR | 800 | 286 | 152 |
| Vesper DMR | 800 | 277 | 156 |
| Anvil LMG | 1,000 | 288 | 175 |
| Mule Launcher (area) | 1,300 | 140 single target | 77 |

A Husk (110 HP) takes about 5 rifle body shots at surge 1 and about 11 at surge 30 (health ×2.5). **Verdict: unchanged.** The spread is narrow, and price tracks sustained damage or utility.

## Loot

Drop chance is 1.2% per threat point, at most 20%, which works out to about one weapon every 1–2 surges in mid-game. By surge 20 the tier odds are 20% Prototype, 50% Refined and 30% Standard. **Verdict: unchanged.**

## Mod Bench attachments (first pass, 2026-11-07)

Prices (`ModBenchRules.Price`): Red Dot 350, Holo 500, Sniper Scope 900, Compensator 450, Long Barrel 500, Foregrip 400, Laser 300, Extended Mag 600, Fast Mag 450, Light Stock 350, Heavy Stock 400. A fully kitted rifle costs about 2,300, a bit over two Forge steps' worth, so it competes with forging and perks. You only pay for new parts, and removing parts is free. Bench parts don't count against a tier's attachment slots (tiers only limit what loot rolls). Which gun takes which part is in `AttachmentFit`.

## Gauntlet (first pass, 2026-11-08)
- Six surges planned as surges 5-10 (Siege, Hunt, Elite, Swarm, Supply, then the Specimen), 8 s fixed lulls (`GauntletRules`).
- Map: 5-6 main rooms, 3-5 side rooms, 3 sectors, no secrets, fixed seed per environment. Doors are free.
- Loot: no Supply Caches; free slots get loot at 50% (Survival 35%) and 72% of that is ammo (Survival 65%), about 1.6x the ammo crates per slot.
- Loadout: Halberd AR + Red Dot, Breacher 12, Ranger 9, Saber-7. No dispenser, perks, Mod Bench, forge, ammo stations or weapon drops. Field Mod picks between surges stay.
- Par 8:00 is a guess; set it from real clear times. Rewards are a normal won run.

## Scrap and structures

Scrap comes from kills (3 per threat point, ×1.5 for Engineers) plus 20 per surge and 40 per objective. That is roughly 200 a surge at surge 10. A sentry costs 120 (54 DPS, 300 rounds, refilling costs 30) and an Arc Fence costs 90. **Verdict: unchanged.** The drone is weak in attack mode (12–24 DPS), but it follows you and repairs things, so that's acceptable for now.

## Field Mods

| Change | Why |
|---|---|
| Last Stand: +60% damage below 30% health (was +40%) | It was strictly worse than Deadeye (+25% always, +20% more when low). |
| Mod and skill damage reduction capped at 45% (was 60%) | Kevlar ×3, Hardened ×3, Juggernaut and Bulwark Plating ×2 reached the 60% cap, and Guard's 30% on top made the Tank take about 28% damage. Now the floor is about 38%. |
| Mod and skill cooldown speed capped at +75% | An Engineer could stack Fabricator (35%), Rapid Assembly (25%), Overclock (30%) and Phoenix Rig (30%) for 2.2× cooldowns, before Combined Arms. |

## Skill trees

| Change | Why |
|---|---|
| Skill points are about 2 per 3 ranks (1 at rank 2, 6 at rank 10, the full 18 at rank 28). It used to be 1 per rank. | Under the old rule the whole tree was owned by rank 19, so there was never a choice and ranks 20–30 gave nothing. Old saves that already spent more points keep their skills. |
| Gunner Marksman: +8% and +12% damage (was 6% and 10%) | The sustained-fire class had less weapon damage in its tree (16%) than the Demolitionist (26%). |
| Demolitionist Sapper: +5%, +7% and +10% (was 6%, 8% and 12%) | Same comparison. Explosives already hit groups. |
| Warrior Frenzy: +12% melee damage (was +10% fire rate for 2 s after a kill) | The fire-rate burst did nothing for a melee class. |
| Engineer Field Repairs: also heals 2 per kill | It was the weakest tier-3 node, giving only 12 HP per surge. |

## Next, after playtests

- Check whether solo surges 15–20 can be survived without the Gun Dispenser.
- Measure how long a full Survival run takes, then check how many runs it takes to reach rank 10 and rank 28 (about 4 and 31 winning runs on paper).
- Revisit the drone's attack damage and the Breacher's falloff.
