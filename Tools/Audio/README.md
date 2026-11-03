# Sound files

Every sound in Deadfall has an id (`SfxId` in `Assets/_Project/Scripts/Rules/Common/Sfx.cs`). With no file, the game
synthesises a placeholder. To use a recorded sound, put it in `Assets/_Project/Resources/Audio/` named after the id:

- `ShotRifle.wav` — one version, or
- `ShotRifle_1.wav`, `ShotRifle_2.wav`, `ShotRifle_3.wav` — variants, played in turn so repeats don't sound identical.

WAV or OGG. Mono is best for anything heard in the world (Unity can only position mono properly). Keep the start of the
sound at the very start of the file (no leading silence) and leave a little headroom (peaks around -1 dB). Volume per
id and pitch variation are set in `SfxBank.Info`, so files don't need to be balanced against each other exactly.

Converting with ffmpeg: `ffmpeg -i in.flac -ac 1 -ar 44100 -af "volume=-1.5dB" -c:a pcm_s16le ShotRifle_1.wav`

Files in `Resources/Audio` are stored in plain git (not LFS) so they can be pushed from environments without LFS.

## Sound list

| Id | What it is | Heard | Suggested length |
|---|---|---|---|
| ShotPistol | Pistol shot (Ranger 9, Bulldog .44) | yours + teammates | 0.3–0.5 s |
| ShotSmg | SMG shot (Wasp, Kestrel PDW) | yours | 0.2–0.4 s |
| ShotRifle | Rifle shot (Halberd, Vesper DMR); also teammates' gunfire | yours + teammates | 0.4–0.7 s |
| ShotShotgun | Shotgun blast (Breacher 12, Hailstorm) | yours | 0.5–0.9 s |
| ShotHeavy | LMG / heavy (Anvil) | yours | 0.4–0.8 s |
| ShotLauncher | Launcher thump (Mule) | yours | 0.4–0.6 s |
| ShotTurret | Sentry turret and drone fire | world | 0.2–0.3 s |
| MeleeSwing | Melee whoosh | yours | 0.2–0.4 s |
| Reload | Magazine out/in | yours | 0.4–1.0 s |
| DryFire | Empty click | yours | < 0.1 s |
| HitMarker | Hit tick | yours | < 0.1 s |
| CritMarker | Critical hit tick | yours | < 0.1 s |
| KillMarker | Kill confirm | yours | 0.1–0.2 s |
| PlayerHurt | Taking damage (thud/grunt) | yours | 0.2–0.4 s |
| PlayerDowned | Going down | yours | 1–1.5 s |
| PlayerRevived | Back up | yours | 0.5–0.8 s |
| EnemyGrowl | Idle zombie groan (4 variants recommended) | world | 0.6–1.2 s |
| EnemyAttack | Zombie attack snarl | world | 0.3–0.5 s |
| EnemyDeath | Zombie death | world | 0.5–1.0 s |
| EnemySpit | Retcher/Spitter spit | world | 0.2–0.4 s |
| BossRoar | Boss roar (phase change, death) | world | 1.5–2.5 s |
| Explosion | Explosion | world | 1–2 s |
| BarricadeBreak | Wood board breaking | world | 0.3–0.5 s |
| Pickup | Item pickup (not hooked up yet) | yours | 0.1–0.2 s |
| Credits | Field Credits gained | yours | 0.1–0.2 s |
| SurgeStart | Surge starting horn | yours | 1.5–2 s |
| SurgeClear | Surge cleared | yours | 0.8–1.2 s |
| ObjectiveDone | Objective complete | yours | 0.6–0.9 s |
| EventAlarm | Event warning alarm | yours | 1–2 s |
| UiClick | Menu click | yours | < 0.1 s |
| UiConfirm | Menu confirm | yours | 0.2 s |
| UiDeny | Can't do that | yours | 0.2 s |
