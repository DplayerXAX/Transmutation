# GPU Grass

Dense grass drawn entirely on the GPU around the camera. A compute shader places blades on **Unity
Terrain** each frame and the blades are drawn with one indirect draw call. It does not grow on the
`ProceduralWorld` (marching cubes) mesh. Test scene: `Assets/Scenes/GrassLODSmooth.unity`.

Namespace: `Tutorial604`.

## Files

| File | Job |
| --- | --- |
| `Grass.cs` | The component. Collects terrains, picks tiles, runs the compute shader, draws. |
| `GrassTile.cs` | The `Tile` struct (terrain, bounds, grid position, spacing / resolution divisors). |
| `GrassMesh.cs` | Builds the blade meshes (high LOD blade: 8 height levels × 2 sides). |
| `GrassCS.compute` | Kernel `Main`: one blade per grid point; jitter, noise mask, slope, clumping, wind, culling. Appends blades to a buffer. |
| `GrassBlade.shader` / `.mat` | Draws blades from the blade buffer (procedural instancing). |
| `Voronoi.shader` | Renders the clumping texture. |
| `Textures/` | Blade albedo and smoothness. |

## How it works

1. **Awake:** finds every `Terrain` in the scene, sets blade spacing from the first terrain's size
   (`terrain width / (tileCount × tileResolution)`), creates the buffers and the Voronoi clump texture.
2. **Update:** chooses tiles around the camera. Near tiles are full resolution; farther tiles are merged
   with lower resolution (LOD). Tiles outside the camera frustum are dropped. For each visible tile the
   compute shader writes the surviving blades into an append buffer.
3. **LateUpdate:** copies the blade count into the args buffer and calls
   `Graphics.DrawProceduralIndirect` (no shadows cast).

Each blade stores position, rotation, hash, height, width, tilt, bend, surface normal, wind force and
side bend.

## Inspector

- **References:** `computeShader` (GrassCS), `material` (GrassBlade), `cam` (defaults to `Camera.main`).
- **Grid:** `tileResolution` (blades per tile side), `tileCount`, `jitterStrength`.
- **Culling:** distance cull start / end for LOD0 and LOD1, frustum near / edge offsets,
  `frustumCullBypassDistance` (no frustum cull near the feet), `maxSlopeAngle` (no grass on steep ground).
- **Noise Mask:** FBM Perlin mask for patchy coverage (`noiseThreshold`, `noiseScale`, `noiseOffset`,
  octaves, persistence, lacunarity).
- **Clumping:** Voronoi texture size, `clumpScale`, and a list of `ClumpParameters` (per-clump blade look).
- **Wind:** `localWindTex`, strength, scale, speed, rotate amount.

## Links to other systems

None in code. It only needs a camera and at least one Unity Terrain (it logs "Terrain count = 0" otherwise).
