# Sky Spider prototype

Run **Tools > Creatures > Create Sky Spider Assets** once, then place
`Assets/Prefabs/Creatures/SkySpider.prefab` at the desired airborne web center.
The setup command creates assets only; it never opens or saves a scene.
Repeated setup runs preserve existing prefab and material edits.

## Behavior and controls

- `SkySpider`: return range (12 m), movement speed, turning speed, and patrol pauses.
  Pickup uses the existing creature carrier. A nearby drop returns to one of that
  spider's webs; a distant drop starts a new one there.
- `SkyWeb`: maximum radius (6 m), growth duration (45 s), random seed, cell count
  (7), strand budget (256), patch budget (6), thicknesses, and trapping layer mask.
  Cluster Spacing sets the distance between groups; Cluster Spread sets spacing
  within each group. The default layout has two small pairs and three individual
  cages on separate branches. Cell Size controls cage size relative to the web.
  Loose Strands Per Cell adds long individual threads within the existing budget.
  Web Scale uniformly scales newly generated geometry: 0.5 is half size, 1 is
  normal, and 2 is double size. It scales positions, thicknesses, patches, patrol
  and trapping together without changing the seed, growth time, or geometry count.
  Maximum Radius remains the base construction radius; actual radius is
  Maximum Radius times Web Scale. Set these on the prefab before generating a web.
  Edit these on the web prefab referenced by the spider.
- Web geometry stays at its starting world position and uses two combined meshes:
  open D12 cages (12 pentagonal faces, 20 corners, 30 edges), joined by strands,
  and sparse transparent pentagonal patches. The thin strands use six-sided tubes
  for thickness; the cells themselves are dodecahedrons. No rope or cloth
  physics is simulated. Meshes update when a strand is added and stay static when
  complete. The first cell exists immediately so the spider has a patrol volume.
- Only the current web grows. Carrying, returning, or pausing the spider pauses its
  construction. Abandoned webs remain in the play session and continue trapping.

## Colors

Select `Assets/Materials/Creatures/SkyWebStrands.mat` and change **Silk Color**
(fine strands) and **Support Color** (cage edges and connections) in the Inspector.
Both default to black. **Tools > Creatures > Set Sky Web Strands Black** resets
both colors explicitly. Patches use the separate `SkyWebPatches.mat` material.
Player movement is unchanged; slow falling is deferred.

## Trapping

Silk captures dynamic Rigidbody objects with solid colliders. The player,
spiders, carried creatures, static/kinematic bodies, and trigger-only colliders
are excluded. Transparent patches do not trap.

Contact uses a conservative sphere around collider bounds and the swept body
center against completed strand segments. Wide or elongated objects can stick
slightly before their actual surface touches a strand. This is an intentional
prototype approximation; there is no collider or GameObject for each strand.
Bodies beyond the web's broad-phase region in both samples can miss detection
at extreme speed. The overlap buffer grows on unusually crowded frames.

`WebCapture` creates a world-connected FixedJoint and temporarily pauses an
attached Creature's simulation. Pickup releases the attachment before carry
physics takes ownership. Disable/remove the capture or web to release it;
the prior creature simulation setting is restored. A short capture cooldown
prevents pickup or cleanup from immediately sticking the object again.

`SkyWeb.Captured` supplies the `WebCapture` with `Body`, `ContactPoint`, and
`OwningWeb` for future prey responses. `SkyWeb` itself derives from `Creature`
and cannot be carried. Birds, diet/consumability, feeding, escape, destruction,
terrain-aware placement, and following individual strands are deferred.

## Validation

- **Tools > Creatures > Validate Sky Spider Geometry** checks deterministic seeds,
  geometry limits, 3D depth, stopped rebuilding, and segment contact math.
- In unpaused Play Mode, **Tools > Creatures > Validate Sky Spider Runtime**
  checks growth, roaming, nearby/distant carry release, compound and swept-body
  trapping, open gaps, exclusions, simulation restoration, and web persistence.
  It also measures trapping scans for one and three webs after warmup and renders
  three camera views. Test objects use a temporary unsaved scene; scene files are
  not changed. Reports and images are written to `Temp/SkySpiderValidation`.

Each web is bounded individually. Repeated distant drops intentionally preserve
old webs, so total scene cost grows with their number until a later web-lifetime
system is added. Transparent faces in a combined mesh have approximate sorting;
the deliberately sparse patches keep this artifact small.
