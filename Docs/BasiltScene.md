# Scene Basilt — basalt / vine battleground

Open `Assets/Scenes/Scene Basilt.unity`. This environment implements Concept 02. The scene now also contains the first playable Asura home/linkage encounter; see `Docs/AsuraTerritory.md`.

## Composition

- The existing 480 × 576 metre rolling landscape and off-center eastern fossil are preserved.
- 319 basalt columns now form a central stepped mountain and scattered interior outcrops rather than an enclosing wall.
- The main shelf is at Y=36, roughly 120 metres across. Rear terraces rise to Y=58, 81 and 104. Smaller outcrops vary in width and height.
- Two broad basalt approaches (22 and 24 metres wide) level out before joining the battle shelf. Open low routes remain around the mountain.
- Twenty major vines: six sculptable routes and fourteen mesh arches. Dense west and east groves contrast with the open shelf and low clearings. The eastern grove frames the existing fossil.
- Thorn belts avoid upper walking crowns. Smaller rootlets and fragments are excluded from the central battle area.
- Basalt and vine materials retain their existing contour/bark shaders with brighter base colors; fog is reduced for readable silhouettes.

## Authoring

`Tools > Capstone > Basilt > Build Megavine Jungle` rebuilds this layout in the active Basilt scene. It preserves the fossil and player rig and refuses to overwrite sculpt edits. Each basalt column and mesh arch is individually selectable under `Basilt - Megavine jungle`.

The two terrain volumes retain frozen sculpt assets and generated chunk caches in `Assets/Generated/Basilt`. Vine bounds extend to Y=116 to contain the summit route. Layout is authored in `BasiltDensity`, scene assembly in `BasiltJungleSetup`, and approach meshes/inspection captures in `BasiltBattleground`.

## Verification

`BasiltSceneChecks.Validate()` passes in Edit and Play mode: both caches load, 255 ground samples have support, 108 samples on the open battle shelf have coplanar support, 50 ramp samples hit their colliders with walkable slope normals, and player arrival has ground support. Play frames advanced and arrival remained supported. After the final ramp landing refinement, the same checks passed in Edit mode. No fresh console errors appeared in the final verification interval.

These checks do not prove full player traversal of every vine or Ashura pathfinding. Combat behavior and population systems are unchanged.

Review captures: `ArtCaptures/BasiltBattleground/overview.png`, `shelf.png`, and `fossil.png`. The live arrival capture is `Assets/ArtCaptures/BasiltBattleground/play-arrival.png`.

