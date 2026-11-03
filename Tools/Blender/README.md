# Blender models

`build_models.py` builds every model in code and exports FBX files to `Assets/_Project/Resources/Models`.
The game loads them through `ModelLibrary` (`Assets/_Project/Scripts/Core/ModelLibrary.cs`) and falls back
to the primitive placeholders when a file is missing.

## Run it

```
blender -b --factory-startup --python Tools/Blender/build_models.py -- --preview /tmp/model_previews
```

- `--preview <folder>` also renders contact sheets (`models_characters.png`, `models_fob.png`, `models_map.png`).
  On a headless Linux box the Workbench renderer needs EGL (`apt install libegl1 libgl1-mesa-dri`), and the FBX
  exporter needs `python3-numpy`.
- `--only enemy_lean,prop_door` rebuilds just those models.

Blender 4.0 is what was used (Ubuntu `apt install blender`). Newer versions should work.

## Conventions

- Coordinates in the script are Unity space: +X right, +Y up, +Z forward, metres. Characters face +Z.
  The FBX export uses forward -Z, up Y and baked space transforms, so models import upright, facing +Z, at scale 1.
- Material names are the colour: `C_RRGGBB` (fixed) or `G_RRGGBB` (fixed, used for glowing weak points).
  `Skin`, `SkinDark`, `Accent` and `AccentDim` are tinted by the game (enemy colour, class/player colour,
  facility colour, door lock colour). `ModelLibrary` builds the materials at runtime from these names, so the
  importer's material settings don't matter and URP builds don't turn pink.
- Enemy models are built at the prototype definition's height and radius for their body type and scaled by the
  game for other definitions. Glowing weak points sit where the hurtboxes are (`Enemy.BuildVisual`); the
  primitives stay as invisible hurtboxes. Specimen phase parts stay as primitives.
- "Fit" models (props, doors, the transport) are normalised to a 1x1x1 box around the origin and placed under the
  placeholder cube, so they take its size. Models long along X are turned by the game when the cube runs along Z.

## Models

| Model | Used by |
|---|---|
| `enemy_standard`, `enemy_lean`, `enemy_plated`, `enemy_crawler`, `enemy_bloated`, `enemy_spitter`, `enemy_alpha`, `enemy_specimen` | `Enemy` (one per `EnemyBodyType`) |
| `player_body`, `player_head` | `RemotePlayerProxy` (co-op teammates; the head, arms and rifle pitch with aim) |
| `fob_booth`, `fob_booth_wide` | `FobBuilder` facility booths (Command Center is the wide one) |
| `fob_transport`, `fob_crates` | `FobBuilder` hangar props |
| `prop_crate`, `prop_locker`, `prop_column`, `prop_partition` | `MapBuilder` room props |
| `prop_door` | `SectorDoor` slab |
| `prop_relay`, `prop_switch` | `DoorSwitch` bodies (the lamp and lever stay game-built) |
| `supply_cache` | `SupplyCache` |
