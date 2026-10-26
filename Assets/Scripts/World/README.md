# World Look: Decorations, Harvest, Terrain Shading, Sky

How the generated world looks: the lit line shading on the ground, procedural decorations, pickable cave
fruit and flowers, and the skybox. The terrain shape itself is in `Scripts/MarchingCubes`.

## One-click setup

**Tools > Capstone > Apply World Look And Camera Guard To Open Scene** (`Editor/WorldLookSetup.cs`):

1. Puts `MAT_World_Surface` / `MAT_World_Inner` on the `ProceduralWorld`.
2. Adds `WorldDecorator` with the four decor materials and resets its kinds to the defaults.
3. Adds `CaveHarvest` with the fruit / flower / stalk materials.
4. Sets the skybox to `MAT_Sky_FractalCalm`.
5. Adds `CameraWallGuard` to the player.

All materials are in `Assets/Materials/World/`. Save the scene afterwards.

## WorldDecorator

Listens to `ProceduralWorld.ChunkCreated` and grows decorations on each chunk as it is built. All pieces of
one chunk are merged into one mesh per material and parented to the chunk.

- **Kinds** (`kinds` list): each kind has a shape, a material group, clusters per 1000 m², members per
  cluster, scale range, the ground normal range it grows on (1 = floor, -1 = ceiling), how much it aligns to
  the normal, and patch size / coverage so the land has bare and crowded areas.
  - Surface stone: shards, arches, cairns, cages, totems, halos, pebbles, obelisks.
  - Surface organic: polyps, bladders, creepers, needles, ribbons, mushrooms, fronds.
  - Inner world: shell tentacle colonies and carpets, needle beards, hanging veils, glowing crystals and sacs.
- **Groups / materials:** Stone, Organic, Inner, InnerGlow.
- **Placement:** `seed`, `variantsPerShape` (meshes are made once per variant and reused),
  `spawnClearRadius`.
- **Physics:** `stoneColliders` on, `innerColliders` off (many small colliders slow loading),
  `decorLayer` 7 (Ground, so the player can stand on stones).
- The setup menu calls `ResetKinds()` to restore the default kinds list.

**DecorShapes** builds the raw meshes: bent tubes and lumpy blobs with random harmonics.
Mesh convention: stands on y = 0, grows along +y, `uv.x` = 0 at the base to 1 at the tip.

## Hand-placed decor and creatures

Decor and creatures can also be placed by hand instead of (or as well as) by the runtime spawners.

- **Tools > Capstone > Terrain > Make Decor Prefabs (From World Decorator)** saves every decorator kind as
  3 prefabs in `Assets/Prefabs/WorldDecor/` (meshes in `Meshes/`), with the same shapes, materials, layer
  and colliders as the procedural pieces. Running it again overwrites them in place (scenes keep their links).
- **Sculpt And Place Window > Place**: build a palette (buttons add all decor or all creature prefabs),
  click in the Scene view to place, Shift + click to remove. Options: several per click with a scatter radius,
  scale range, random turn, align to slope (1 = along the ground normal, e.g. for ceilings), sink.
  Placed objects go under the root object `Placed World Objects`. Prefabs with a Rigidbody get
  `SettleWhenGroundReady`, so at runtime they wait for their chunk instead of falling through.
- **Drop Selected Objects To The Ground** snaps any selection onto the terrain below it.
- With **Keep Placed Objects On Ground** on, sculpting moves placed objects up or down with the ground.
- **Spawners tab**: switches the runtime spawners on or off (disables the component): `WorldDecorator`,
  `CaveHarvest`, `RealmFlora`, `RealmDressing`, `SpireOvergrowth`, `TowerGroves`, `SpireCreatures`, `BasinLevel`.
  All stay on unless you switch them off.

Hand placing needs the terrain visible in the Scene view: the window builds it (Show Terrain In Scene) and, with
**Auto Show Terrain** on, rebuilds it after opening the scene, leaving Play Mode or reloading scripts.
Landmarks (spire, kiln) and spawned flora still appear only in Play Mode.

## CaveHarvest

Also listens to `ChunkCreated`, Play Mode only. In the inner world it hangs fruit bunches on thin stalks
from ceilings and stands flowers on floors and walls.

- Each fruit / flower is a `CaveHarvestItem` (a `Creature`, so normal pickup, prompt and carry work).
  Once picked it becomes a normal falling object. Stalks stay and are merged per chunk.
- Settings: clusters per 1000 m² for fruit and flowers, items per cluster, `stalkLength`,
  `clusterRadius`, `seed`, `variants`.

## Shaders (`Assets/Shaders/World`)

| Shader | Used by | Notes |
| --- | --- | --- |
| `Capstone/World/TerrainLinesLit` | `MAT_World_Surface`, `MAT_World_Inner` | Polar ray / ring line pattern, lit in a few flat steps with shadows, SSAO and hatching in dark areas. The inner material uses no sun, a fake fill light (`_FakeLightDir`), line sheen and distance haze. |
| `Capstone/World/Decor` | decor and harvest materials | Same line lighting. Organic pieces sway and breathe in the vertex stage; stone stays still. |
| `LineWorldLighting.hlsl` | both above | Shared stepped light + hatching. |

Key TerrainLinesLit properties: base / shadow / line colours, `_ShadeLevels`, Hatching group,
`_SheenAmount`, `_HazeColor` (alpha = strength) with `_HazeStart` / `_HazeEnd`.

## Sky

The sky is just a skybox material (`RenderSettings.skybox`).

| Material | Shader | Look |
| --- | --- | --- |
| `Materials/World/MAT_Sky_FractalCalm` | `Capstone/Skybox/Morphing Fractal` | Calm copy of the white morphing fractal (current default). |
| `Shaders/FractalSky.mat` | `Capstone/Skybox/Morphing Fractal` | Original, busier version. Tune scale, speed, warp, twist, contours, contrast. |
| `Materials/World/MAT_Sky_QuietLines` | `Capstone/Skybox/Quiet Lines` | Dark gradient with a few thin drifting contour lines that fade near the horizon. |

Menus: **Tools > Capstone > Apply Fractal Sky to Current Scene** sets `FractalSky.mat`.
Meditation erases the sky together with the world (see `Scripts/Meditation/README.md`).

## Older terrain look

`Shaders/Terrain/FBMRisingTerrain` is for Unity Terrain scenes (e.g. `Scene_Lam`).
**Tools > Capstone > Apply Rising FBM to Scene Terrain** assigns it.

## AerialPerspective (haze)

On the Procedural World object. There is no haze near the player, then a long gentle linear ramp (Unity fog mode Linear) to fully hidden. Settings: **Clear Distance** (no haze nearer than this, default 300 m) and **Haze Depth** (from there to fully hidden, default 420 m),
**Scale Height** (the air thins as the eye climbs, so the far land clears from up the spire), a sun tint (brighter
and warmer looking into the sun, cooler away; Mie-style phase), and a separate violet haze in the inner world.
Line detail fades at given haze strengths. While chunks load, the haze is thickened to hide the unloaded edge.
