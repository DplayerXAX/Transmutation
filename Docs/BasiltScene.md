# Scene Basilt — megavine jungle

Open `Assets/Scenes/Scene Basilt.unity`.

The scene is now a 480 × 576 metre environment with a winding low valley, irregular ridges, basalt terraces and a dense roof of independent giant vine arches. It is composed for the first-person view inside the jungle, rather than as a small group of props viewed from above.

## Composition

- **Arrival:** a gap in the southern basalt colonnade reveals the first massive roots. The player starts before the first arch, rather than inside the vine volume.
- **Central valley:** a winding low channel and intersecting giant roots make a ground route with changing sightlines. Some vines connect; most canopy arches independently rise from and return into the landscape.
- **Western terraces:** groups of individual hexagonal columns share broad flat tops, with much larger monolithic platforms behind them.
- **Canopy:** 42 major vines, including 36 independent mesh arches and six sculptable root paths. Major trunks are roughly 11–30 metres in diameter, and the highest sweeps rise over 100 metres.
- **Fossil clearing:** the earlier incomplete, branching rib vault remains in the eastern clearing. Its footing and relative open space are preserved.
- **Surrounding landscape:** 748 individual basalt columns form broken enclosing walls, mesas and terraces. Scattered fragments and smaller ground roots tie these masses into the rolling terrain.

## Materials and geometry

| Part | Geometry | Material |
|---|---|---|
| Rolling ground | Cached sculptable ProceduralWorld | Existing TerrainLinesLit, with quieter lines |
| Basalt | Individual flat-shaded hexagonal meshes; overlapping rims form continuous shared platforms | Existing BlackFlowContours, with opt-in facet shading |
| Six primary roots | Separate cached sculptable ProceduralWorld | BasiltVine |
| 36 additional giant arches | Smooth tube meshes with continuous transported frames and MeshColliders | BasiltVine |
| Hooked spikes | Batched spiral belts distributed around the full circumference, including crowns and undersides | BasiltVine |
| Fossil | Existing branching rib and spur meshes | BoneMarble |

Spikes are staggered around the trunks, with spacing that lets the overall vine shapes remain readable. The bark shader fades unresolved fine lines at distance. The ground and vines retain separate materials. BlackFlowContours' new directional facet setting defaults to zero, preserving other materials that use it.

## Authoring

`Tools > Capstone > Basilt > Build Megavine Jungle` rebuilds this generated environment in the current Basilt scene. It preserves the existing fossil and player rig. It refuses to replace terrain with sculpt edits. The generated environment is under `Basilt - Megavine jungle` in the hierarchy. Each basalt column and canopy arch remains separately selectable.

The ground and six main roots have frozen sculpt assets and saved chunk caches in `Assets/Generated/Basilt`. Select either world in the project's terrain tools to sculpt it. Additional arches and basalt are mesh objects, so edit their transforms or generator paths.

`BasiltSceneChecks.Validate()` checks cached terrain completion, the enclosing mesh field, canopy and thorn groups, material assignment, the ground height range, sampled ground colliders and arrival support. It is not a complete manual traversal of every vine, and the independent canopy arches are not all intended as continuous walking routes.

Verified in Unity: 255 terrain collider samples, 748 main basalt columns, 42 major vines and radial thorn belts, a ground height range of -4.45 to 36.95 metres, supported player arrival, both terrain caches loading, advancing Play-mode frames, and no errors reported by the two affected shaders. All 8,192 compatibility samples and bounds from the older terrain layouts remain identical.

