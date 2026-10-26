# World Generation (Marching Cubes)

Two separate uses of Marching Cubes live here:

1. **ProceduralWorld** builds the whole level: surface, caves and an upside-down inner world.
2. **MarchingCubesVolume** is a small live density field that shapes can push into (used by the Fire Flower).

`MarchingCubesTables` holds the standard lookup tables both use.

## ProceduralWorld

Replaces the painted Unity Terrain. The world is a density field (positive = rock) meshed in chunks, so it
can have overhangs, tunnels and a second world underneath.

```text
Surface         y = h                          walked on from above
Crust underside y = -crust - h * mirror        the surface hanging upside down (inner ceiling)
Inner world     y = -crust - gap - h * invert  hills above become pits below
```

Sinkholes and worm tunnels connect the two. Gravity is normal everywhere.

- **Generation:** chunks are computed on worker threads; meshes and MeshColliders are made on the main
  thread, `meshesPerFrame` at a time. Chunks within `blockingRadius` of spawn finish before the first frame.
- **Spawn:** the player is stood on the surface under this transform. Ground is flat within
  `spawnFlatRadius`. With `respawnFallingPlayer` on, falling out of the world teleports back to spawn.
- **Main settings:**
  - Size: `worldSize` (288 x 288 m), `cellSize`, `chunkCells`, `seed`.
  - Surface: `surfaceAmplitude`, `surfaceFrequency`, `ridges`, `overhangAmplitude`, `rimHeight` (edge cliffs).
  - Basin: `basin` turns the square edge cliffs into an irregular ring of mountains `rimHeight` tall;
    `basinFloor` / `basinSlope` (shares of the half size) set where the slopes start and how wide they are.
  - Inner World: `crustThickness`, `innerGap`, `ceilingMirror`, `innerInvert`.
  - Caves: `caveWidth` (0 = no worm caves), `caveDepth`, `sinkholeCount`, `sinkholeRadius`,
    `firstSinkholeDistance` (a guaranteed sinkhole ahead of spawn).
  - Rendering: `surfaceMaterial`, `innerMaterial`, `chunkLayer` (7 = Ground, so the player can jump and
    creatures and hands can grip).

**Used by other systems**

| API | Used by |
| --- | --- |
| `ChunkCreated` event (chunk object + mesh) | `WorldDecorator`, `CaveHarvest` |
| `LayoutReady`, `SinkholeCount`, `SinkholeCentre()` | `HangingSpire` |
| `ColumnHeights()` (surface, ceiling, inner ground) | `HangingSpire`, `CaveMusicChannel` |

Materials and decorations are set up by **Tools > Capstone > Apply World Look And Camera Guard To Open Scene**
(see `Scripts/World/README.md`).

## Sculpted terrain (saved, hand edited)

The procedural world can be frozen into an asset and then sculpted by hand in the editor. The current layout is
the starting point; nothing changes until you sculpt.

**Steps**

1. Open the scene, run **Tools > Capstone > Terrain > Bake Open Scene World To Sculpted Terrain**.
   This makes `Assets/Terrain/Sculpted/<Scene>_Terrain.asset` (`TerrainSculptData`), puts it in the
   ProceduralWorld's `sculptData` slot and builds the world in the Scene view. Save the scene.
2. Open **Tools > Capstone > Terrain > Sculpt And Place Window** and use the **Sculpt** tab with the left mouse
   in the Scene view (Alt still orbits). Holding still keeps sculpting.

| Brush | Does |
| --- | --- |
| Raise / Lower | Moves the ground up or down inside a vertical cylinder |
| Smooth | Evens out bumps and sharp edges |
| Flatten | Pulls the ground to the height where the stroke started (or a fixed height) |
| Add / Carve | Adds or removes rock in a ball: overhangs, arches, tunnels, caves |

Every stroke is one undo step. The asset is saved with the scene (Ctrl+S) or the window's **Save** button.

**How it works**

- `TerrainSculptData` holds the frozen layout (all generation settings, noise offset and sinkholes as JSON)
  and the brush edits: a density change per grid sample, packed (gzip + base64) when Unity saves it.
- With the asset assigned, `Generate` builds from the frozen layout, so the **inspector's Size / Surface /
  Inner World / Caves settings are ignored** (the inspector says so). Baking again freezes new settings but
  drops all sculpting. `headroom` on the asset adds empty space above the hills so tall sculpts are not cut off.
- Edits are added in the same place as erosion (`CopyModifications`), so only chunks that contain edits do any
  extra work; all other chunks build exactly as before, at the same speed.
- `ColumnHeights()` (used to stand things on the ground: spire, kiln, flora, creatures, cave music) walks
  down the real density in sculpted columns, so things snap to the sculpted ground. Unsculpted columns are unchanged.
- The asset stores a few check densities. If the generator code (`ProceduralWorld.cs` noise / density) is
  changed later, a warning says the unsculpted parts may look different. Avoid editing the density code once a
  level is sculpted; the sculpt edits themselves still apply.
- In the editor, brushes rebuild only the touched chunks, and cache their generated density so later dabs are
  fast. Decorations on those chunks are redone once, when the stroke ends.

| API | Used by |
| --- | --- |
| `Sculpt(tool, point, radius, amount, flattenHeight)`, `BeginSculptStroke()` / `EndSculptStroke()` | Sculpt window |
| `CaptureLayout()`, `SyncSculptFromData()` (after undo / clear), `UsingSculptedTerrain`, `CanSculpt` | Setup menu, window, undo hook |

`Editor/TerrainSculptValidation.Run` is a batch-mode check of the brushes, saving and clearing.

## MarchingCubesVolume

A small grid of densities that starts at 0. Every `simulationInterval`:
decay toward 0 → registered overlays inject density → dirty chunks rebuild.

- Grid: `numPointsPerAxis`, `spacing`, `chunkSizeCubes`. Mesh: `chunkMaterial`, `generateChunkColliders`.
- Simulation: `decaySpeed`, `densityEpsilon`, `forceBoundaryEmpty` (keeps the surface off the grid edge).
- `MarchingCubesChunk` owns one chunk's mesh and collider; density stays on the volume.

**DensityShapeOverlay** — a Sphere, Capsule or Cylinder that injects density into a volume with a smooth
falloff (`injectRate`, `maxDensity`, or `stampDensity`). Sizes are world units; transform scale is ignored.
`FireFlower` moves and resizes these to grow and bloom. Custom inspectors are in `Editor/`.

**MarchingCubesBrush** — test tool: left mouse digs, right mouse grows, at the point under the mouse.

## CaveFormation

Older, standalone: a rocky hill with a walk-through tunnel and chamber, meshed separately to sit on a Unity
Terrain (which can't have overhangs). Call `Generate()`. Not needed with ProceduralWorld.
