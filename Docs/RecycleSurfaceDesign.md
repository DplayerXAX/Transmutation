# Recycle and goo: surface geometry and procedural growth

## Intent and visual references

Goo has a nominal eight-metre diameter. It is an irregular, shallow coating following the collision surface. Recycle is an immovable fungal colony: blue cords, fine branching threads, small green nodules and growing tips. Its flowers have turquoise veined outer leaves, curled purple petals, and orange/gold central filaments. These are the three user-provided references, not instructions to introduce Portal mechanics or fire damage.

The existing `CompostCreature` script and prefab GUIDs are retained. Its platform, ramp, roaming, and rider behavior have been retired. Legacy geometry and references remain disabled to preserve scene overrides. `RecycleFlower` is a separate child Creature before pickup; detachment leaves its parent colony in place.

## Surface implementation

`SurfaceGeometry` caches collider vertices, triangles, and shared-edge adjacency. `SurfaceAnchor` stores a face and barycentric coordinates. Resolve transforms the source triangle into world space, so attachment follows moving objects without guessing a world height.

Terrain chunks use their `ProceduralWorld` identity as the adjacency namespace. Edges match at 0.1 mm in that namespace. Merely nearby colliders never share edges. Unsupported, degenerate or non-manifold edges stop traversal. Primitive boxes and curved sphere/capsule colliders have procedural topology adapters. Imported unreadable meshes can receive `SurfaceMeshProxy` data through the migration tool.

Tracing advances inside a triangle in barycentric coordinates. At the first crossed edge, rotate the tangent around that edge by the signed angle between face normals, then continue in the adjacent triangle. A tiny in-face epsilon avoids repeatedly hitting the same edge. The walk is bounded to 256 crossings per request. It returns failure at an unsupported boundary rather than snapping across space.

Goo uses a bounded connected-face flood. Its distance estimate is the length of valid routes through face entry points: an upper bound on shortest surface distance, not an exact geodesic solution. It can conservatively underfill a highly folded surface. Border triangles subdivide to about 13 cm; interior source triangles remain coarse. A continuous noise contour clips the outline, and the resulting surface triangles sit 6 mm above support. Steep surfaces also receive tapered gravity-following drips, clipped face by face so the streak sides do not bridge corners. Rendering and coverage share that mesh. A half-metre spatial index limits nearby grip queries; paint adds no solid box collider. The short spread animation is reflected in both visible and usable coverage.

`HandClimber` resolves the actual supporting collider and normal. Painted supports outside its usual layer mask can be acquired, while the nearest solid still blocks selection. Grip points follow their support transform. Coverage checks include facing, so a nearby opposite side of a thin wall does not inherit paint.

Terrain changes invalidate affected caches. Goo locally reacquires its impact support or disappears. Fungal nodes reacquire support and validate their connecting path; unsupported descendants retract. Flowers rebind locally before deciding whether to detach. Changes do not rebuild the whole terrain or every colony.

Research basis:

- [Geometry Central: tracing geodesic paths](https://geometry-central.net/surface/algorithms/geodesic_paths/) — face-based tracing, boundaries and bounded traversal. The project implements a small C# adaptation, not the native library.
- [Unity: mesh data access](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/mesh/acquirereadonlymeshdata) — readable mesh requirements and snapshot ownership. Runtime geometry is copied once per collider revision; editor baking handles imported unreadable meshes.

## Growth and animation

`RecycleNetworkGeometry` retains its seeded graph between meals. Local attraction points are sampled through surface walks. Candidate influences are checked against a real surface path before steering growth. Tips extend, branch and occasionally connect to another branch. Recent ancestors are excluded from reconnection, preventing repeated tiny loops along the same cord. Fine threads also follow surface paths and participate in contact checks.

Every segment records its birth time, duration and parent position. Render vertices interpolate from the segment's beginning toward their final coordinates; splitting paths at source-face crossings keeps this interpolation on the supporting face. Geometry updates run at 10 Hz while active; the shader interpolates between updates. Stable mature geometry stops rebuilding. Nutrient pulses and green nodules communicate activity without displacing the rooted body.

Flowers use parameterized curved petal surfaces with persistent topology. Closed-bud and open positions are stored per vertex. A shared bloom parameter opens stalk, outer leaves, inner petals and filaments with staggered delays. A transported stalk frame avoids sudden twisting, including for wall-mounted flowers. Veins, edge colour, remaining-life desaturation and restrained emission are procedural. Forward, shadow and depth passes use the same deformation.

Research adaptations:

- [Runions et al., leaf venation](https://algorithmicbotany.org/papers/venation.sig2005.html) — attraction-driven branching and open/closed connection graphs, adapted here to a terrain surface.
- [Prusinkiewicz et al., Animation of Plant Development](https://algorithmicbotany.org/papers/animdev.sig93.html) — separate discrete developmental events from continuously animated development.
- [Hanson and Ma, Parallel Transport Approach to Curve Framing](https://scholarworks.iu.edu/dspace/items/39fbe931-c2c8-43a1-9c9d-a207b92d551e) — stable frames for curved stalks and filaments.
- [SideFX, Blooming Flowers](https://www.sidefx.com/tutorials/blooming-flowers-part-one-procedural-petal-model-and-rig/) — parameterize one petal, then assemble and animate the complete flower. No Houdini dependency is required.

## Resource and lifecycle rules

- Every successful corpse gives `max(1, floor(Nutrition / 20))` points and proportional strand growth credit.
- Digestion takes five simulation seconds. The existing soft fullness threshold is 50 points; waiting corpses retain partial progress.
- Mature network tips provide one flower slot per 32 metres of established strands, capped at eight. Colonies start with 32 metres of strand growth and have a 256-metre budget. Each nutrition point also grants four metres of growth credit. Main strands use twice the original step spacing and ten times the original width, with fewer side threads and branches. Attraction samples reach farther ahead, keeping growth moving outward instead of curling toward nearby growth. Flowers retain their nominal size with deterministic +/-15% variation and mount outward from walls with clearance checks.
- Replacing a missing flower costs five points. A bud appears as soon as it is funded, then visibly unfolds over five seconds. Missing flowers have priority over new goo reservations.
- Each flower reserves ten points per normal Goo Creature and takes seven seconds to produce it. Blocked output waits without spending twice.
- Picked flowers start empty. Any unfinished attached production reservation returns to the colony. Detached meals fund only that portable flower.
- First detachment starts 120 seconds of remaining Age. Dropping, picking again and feeding do not reset it. Existing paused-simulation and stillness rules apply. Saturation and emission fall with remaining life; petals curl inward near death.
- A held flower's LMB selects one creature within five metres with solid occlusion. Dead creatures briefly lift before shrinking into recycling. Living creatures receive a brief visual nudge, with a small impulse for dynamic bodies; rooted networks pulse instead of moving. No damage or area consumption is applied. Held-use input and hints take priority over nearby bone boarding.
- `CompostAbsorption` is an exclusive claim shared by network and flower. Generic consumption, capture and corpse retirement respect it. Pickup cancels it and restores the corpse. Completion disables the body before deferred destruction and awards once, without replaying food effects.
- A dead flower touching upward-facing terrain germinates once. Contact callbacks remain active after Creature simulation stops, and death while already resting checks support immediately. The seed produces 16 metres of starting growth, no free points and no free flower. A consumed flower cannot also seed.

## Setup and focused verification

`Tools > Creatures > Recycle > Create Or Update Fungal Assets` updates procedural assets while preserving their identities. `Migrate Recycles In Open Scenes` disables old platform geometry, wires new components, and bakes any unreadable collision surfaces. Existing Compost setup entry points route to this migration.

`RecycleValidation.ValidateAssets()` checks wiring and shader compilation. `RecycleValidation.Run()` is a disposable, deterministic Play Mode fixture: concave/convex corners, disconnected surfaces, chunk seams, moving paint, growth, exclusive feeding, cancellation, portable output, accelerated ageing, single germination, and stale mesh revisions. It simulates a physics step where an impulse needs one. It does not wait two minutes, run many generations, or repeat full-scene builds.

Visual review uses a few live captures and a short traversal check. Long ecology runs are reserved for a demonstrated population/economy problem. Existing Wwise SoundBank errors are unrelated to this redesign.

## Verification recorded during implementation

- Scene_Goo cave concentration fix: both colonies had identical tuning, but marching-cubes triangles collapsed along the deeper colony's flat 10-by-10-metre floor perimeter. Registering those zero-area faces made otherwise valid shared edges appear non-manifold. The surface cache now excludes collapsed faces from edge connectivity. The focused seam fixture verifies that continuous surfaces remain traversable while genuinely ambiguous edges still stop traversal. At the same seed and growth allowance, the deeper colony's graph spread changed from about 9.8 by 10 metres to 31.5 by 28 metres; the near-spawn colony stayed at 33.8 by 33.3 metres. Eight-metre walks from the deeper seed now pass all four directions. Growth time was advanced directly, without a long ecology run.
- Latest sizing-only check: saved prefab settings and +/-15% flower variation passed. A 32-metre starter on a flat fixture produced 172 graph nodes over approximately 7.7 by 7.6 metres; its first main strand measured 24 cm across and its footprint stayed 3 mm above the support. Growth time was advanced directly; no ecology or traversal suite was repeated for this tuning change.
- Eleven asset/shader checks and 29 deterministic geometry/lifecycle checks passed. The final core fixture took 32 ms; its lifetime checks advance time directly.
- The actual player crossed the authored painted gate with zero personal goo. The traversal portion has an eight-second limit and waits separately for the two physical drops to settle before moving the player.
- Before the sizing changes, a ninety-frame sample with eight developed geometry instances (6,431 graph nodes) and 24 total paint patches measured approximately 0.42 ms p95 for steady surface work. Construction is excluded. The largest sampled queued upload was 10.82 ms: the queue spreads jobs across frames, but a single native mesh upload is atomic and can exceed its soft two-millisecond budget. This historical sample is not a full-game frame-time guarantee.
- Live flower, network and goo review captures are in `Temp/GooSceneValidation/`. Flower normals, including petal tips, are checked explicitly to prevent black shading from zero-length normals.
