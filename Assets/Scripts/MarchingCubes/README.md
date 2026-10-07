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
