# Blender models

`build_models.py` builds every model in code and exports FBX files to `Assets/_Project/Resources/Models`.
The game loads them through `ModelLibrary` (`Assets/_Project/Scripts/Core/ModelLibrary.cs`) and falls back
to the primitive placeholders when a file is missing.

## Run it

```
blender -b --factory-startup --python Tools/Blender/build_models.py -- --preview /tmp/model_previews
```

- `--preview <folder>` also renders contact sheets (`models_characters.png`, `models_fob.png`, `models_map.png`, `models_weapons.png`).
  On a headless Linux box the Workbench renderer needs EGL (`apt install libegl1 libgl1-mesa-dri`), and the FBX
  exporter needs `python3-numpy`.
- `--only enemy_lean,prop_door` rebuilds just those models.
- New models get a `.meta` from `fbx_meta_template.txt` (a full ModelImporter section; a guid-only meta makes Unity refuse the import).

Blender 4.0 is what was used (Ubuntu `apt install blender`). Newer versions should work.

## Conventions

- Coordinates in the script are Unity space: +X right, +Y up, +Z forward, metres. Characters face +Z.
  The FBX export uses forward -Z, up Y and baked space transforms, so models import upright, facing +Z, at scale 1.
- Material names are the colour: `C_RRGGBB` (fixed) or `G_RRGGBB` (fixed and emissive: weak points, lamps, screens, LEDs).
  `Skin`, `SkinDark`, `Accent` and `AccentDim` are tinted by the game (enemy colour, class/player colour,
  facility colour, door lock colour). `ModelLibrary` builds the materials at runtime from these names, so the
  importer's material settings don't matter and URP builds don't turn pink.
- Weapons are built from extruded side outlines (`Model.prof` / `Model.sym`) with one-segment chamfers for a faceted
  look, and use fixed wood / steel / polymer colours instead of tint slots. `--preview` also writes a close 3/4 shot
  of each weapon to `<folder>/weapons/`.
- Enemy models are built at the prototype definition's height and radius for their body type and scaled by the
  game for other definitions. Glowing weak points sit where the hurtboxes are (`Enemy.BuildVisual`); the
  primitives stay as invisible hurtboxes. Specimen phase parts stay as primitives.
- "Fit" models (props, doors, the transport) are normalised to a 1x1x1 box around the origin and placed under the
  placeholder cube, so they take its size. Models long along X are turned by the game when the cube runs along Z.

## Models

| Model | Used by |
|---|---|
| `enemy_standard`, `enemy_lean`, `enemy_plated`, `enemy_crawler`, `enemy_bloated`, `enemy_spitter`, `enemy_alpha`, `enemy_specimen` | `Enemy` (one per `EnemyBodyType`) |
| `player_body`, `player_head` | `RemotePlayerProxy` (co-op teammates; the head and arms pitch with aim, and the game adds the model of the weapon they hold) |
| `fob_booth`, `fob_booth_wide` | `FobBuilder` facility booths (Command Center is the wide one) |
| `fob_transport`, `fob_crates` | `FobBuilder` hangar props |
| `prop_crate`, `prop_locker`, `prop_column`, `prop_partition` | `MapBuilder` room props |
| `prop_lab_bench`, `prop_tank`, `prop_server_rack`, `prop_shelf`, `prop_table`, `prop_planter`, `prop_desk`, `prop_generator` | `MapBuilder.Dressing` themed furniture (fit models on collider boxes) |
| `prop_barrel` | `MapBuilder.Dressing` barrel clusters (real size, origin on the floor) |
| `prop_light_fixture` | `MapBuilder.Dressing` ceiling fixture under each room light (fit, long along X) |
| `prop_fusebox`, `prop_wall_vent`, `prop_extinguisher` | `MapBuilder.Dressing` wall clutter (real size, origin at the wall, front +Z) |
| `prop_rubble` | `MapBuilder.Dressing` corner debris (real size, no collider) |
| `prop_door` | `SectorDoor` slab |
| `prop_relay`, `prop_switch` | `DoorSwitch` bodies (the lamp and lever stay game-built) |
| `supply_cache` | `SupplyCache` |
| `weapon_halberd_ar`, `weapon_breacher_12`, `weapon_ranger_9`, `weapon_saber_7`, `weapon_wasp_smg`, `weapon_anvil_lmg`, `weapon_tether_cleaver`, `weapon_mule_launcher` | `WeaponViewmodel` (first person) and `WeaponDrop` (pickups). Named after the weapon's content id; origin at the rear of the receiver, barrel along +Z, muzzle tip where the placeholder muzzle was |
