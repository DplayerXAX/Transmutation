# Sea-angel island prototype

Implementation started 2026-11-23. Related: [technical review](MovingSeaAngelIslandTechnicalReview.md), [shared simulation plan](../CreatureSimulationOptimizationPlan.md).

## Play and author

### Measured smoothness pass

The smoothness passes are documented in [the architecture research and measurements](../MovingIslandSmoothnessResearch.md). Ordinary kinematic Rigidbody transport now uses physics poses and MovePosition/MoveRotation with interpolation matching the island; teleport/reset remains separate. The passive test fixture's render-local wobble fell from centimetres to approximately .00001 m maximum. Collider-free sea angels now transport on the same rendered timeline as their autonomous flight. Free kinematic tentacles transport their body and support-owned procedural caches on that timeline; one pinned-arm fixture also measured approximately .00001 m maximum body step and pin error. Dynamic motor forces and acceleration were not retuned. These representative results do not establish every ability or nested rider is smooth.

Asura gait now requires its motor's locomotion intent, grounding, and movement ownership. Transport alone no longer counts as stride distance; relative speed controls stride only when locomotion is permitted. Combat recoil and rolling presentation retain their own behavior.

Goo's impact threshold is now 6 m/s, separate from other species. An actual shallow-angle full-force throw produced a 7.434 m/s normal impact; the earlier threshold rejected the delivered hit. Ordinary drops and full-force scripted throws now pass on static ground and the island, alongside the 32-check shared impact suite.

### Rider timing at higher speed (2026-11-24)

Ground inhabitants visibly sawed back and forth on the deck while it moved. The island Rigidbody interpolates, so it is drawn one physics step (0.02 s) late; several riders were drawn on a different clock. Dynamic bodies whose prefabs use interpolation None (squirrel, chainsaw, recycle flower, bubble flower, bat-bug egg, fishing pole) were drawn at their newest physics pose, up to `speed × 0.02 s` ahead of the deck: about 6 cm at 3 m/s and 18 cm at 9 m/s, changing every frame. Rigidbody-free inhabitants (fire flowers and other rooted creatures before they are first carried) were teleported once per physics step and drew the same sawtooth.

`MovementFrameMember` now puts every passenger on the island's rendered timeline. Joining sets Rigidbody interpolation to Interpolate for dynamic as well as kinematic passengers, and leaving restores the original value unless carry or capture changed it in the meantime. The player already interpolates, and sea angels and tentacles keep their own render-driven owners. Rigidbody-free members now move in `Update` from the frame's interpolated pose; their logic caches still move on the physics step. Physics transport, velocities, forces and the timestep are unchanged.

To measure, play Scene-Fishing and use **Tools > Creatures > Fishing > Measure Island Rider Jitter (Play Mode)**. The temporary probe samples every member's position in the island's rendered frame for 6 seconds. It then returns the affected bodies to their original interpolation for 6 seconds, restores the fix, and logs both results (per-frame second difference, RMS and maximum). The probe is never saved into the scene.

A later regression pass fixed dead tentacles jittering, flying off or losing their skeleton, Recycle colonies and terrain shaking, and edge shake from the route's stepped heading. See the research doc's regression-pass table and the `IslandJitterSurvey` usage there.

### Shared relevance optimization

After the representative smoothness gate, distance and fish-source policies moved onto one scene-local relevance scheduler. Distance candidates register through creature lifecycle events and use staggered half-second deadlines, with at most 32 visits per render frame by default. Carrying and reservations acquire immediate owned activity handles; those cannot override semantic capture/freeze permissions. Fish source and visibility rules remain distinct. See [implementation and measured limits](../CreatureSimulationImplementation.md).

The 512-fixture check passed bounded work, lifecycle discovery, independent activity ownership, semantic pause priority, position wake, and policy restart. This is relevance scheduling, not universal freezing or state-only unloading. The full representative moving-island regression also passed after integration. Use fresh Play sessions for the probes so age-limited creatures have not expired.

### Visual, terrain, and population refresh

The giant now preserves the small sea angel's authored child proportions under one uniform visual scale. The earlier oversized opaque sphere was caused by replacing the authored Head scale with an absolute scale of 100 and applying the deck material to every body part. The giant uses the same Dimensional Angel rim shader with a normal transparent forward pass; the original window/honey passes retain their indices.

Walking animation is gated by the controller's walking/sprinting/crouching states **and movement input**. Standing still on moving ground does not advance the gait. Relative velocity controls stride rate only after that state/input gate permits walking.

There is a 20 × 32 metre baked mesh patch on the left side of the back, with a smooth 3.5 metre hill. Its MeshCollider is part of the kinematic island, is recognized by player support, and is marked as terrain for surface-attached ecology. It does not run a live procedural terrain generator. The recycle colony sits on this patch and successfully grows strands there.

The saved **Island Test Creatures** group contains Light and Dark melee/ranged Asuras, a chainsaw, three tentacles, five birds, bubble/fire flowers, a recycle colony and portable flower, Light/Dark goo, a bounce shroom, an attachment egg, and a dragon fuel bubble. A native procedural bone child (the small bone dragon) spawns at runtime near the rear of the deck. These are active gameplay creatures: Asuras may fight, flowers act normally, and play-session changes reset on leaving Play Mode.

Use **Tools > Creatures > Fishing > Refresh Island Visuals Terrain And Test Creatures** to apply the visual/terrain refresh. Existing test population placement is preserved on repeated calls. Pinned tentacle holds, recycle caches, and the bone child's cached movement points now have explicit frame transport; uncommon interactions still need incremental manual testing.

The focused gait/terrain probe passed idle gait suppression while travelling, gait advancement with walking input, stopping animation on input release, hill collision and player grounding, terrain ecology eligibility, and population/bone-dragon spawning. The first populated run exposed a bird startup registration race; registration now defers until its scene has loaded.

Open `Assets/Scenes/Scene-Fishing.unity` and enter Play Mode. The player starts on a giant sea angel's 48 × 72 metre back. Six 24 metre sections share one continuous deck collider. The body and animated fins have simple collision; the back is the reliable walkable surface.

The independent `Island Flight Route` object contains numbered child waypoints. Select the route to see handles, numbering, and direction arrows. Move the waypoints in Edit Mode with standard Undo. Four non-null points are required. The closed route uses an arc-length lookup, approximately constant cruise speed, and level yaw-only orientation. Positions and headings come from the curve itself, not the lines between samples (that made the deck edges whip). The route's `Vertical Amplitude` (default 12 m) and `Vertical Cycles` (default 1 per lap) make the island rise and sink like the sun over a day, without tilting. Set the amplitude to 0 for a flat route. Default speed is 9 m/s, acceleration 1.8 m/s² (tripled on 2026-11-24 from 3 m/s and 0.6 m/s² to stress-test rider smoothness; it still reaches cruise in about five seconds). Set both on the `SeaAngelIsland` prefab's Moving Frame.

Select `Giant Sea Angel Island` to change cruise speed, pause the flight, or adjust habitat dimensions. Pausing decelerates. The component context menu's **Reset Flight To First Waypoint** resets passengers together without assigning teleport velocity. The habitat extends 12 metres past each deck edge, 30 metres above the back and 8 below, with a 2 metre retention margin. Tick **Show Habitat** on the Moving Frame (or use its **Toggle Habitat Display** context menu) to draw the volume in the Scene and Game views, including during Play Mode: green is the entry box, where anything (walkers, sleepers and flyers alike) joins and moves with the island; yellow is the larger release box it must leave before it stops following. While inside, the player's view yaw turns with the island's heading (`Turn With Moving Frame` on the player controller, on by default). A disabled overview camera is available for a fixed external view; disable the player camera before enabling it.

`Tools > Creatures > Fishing > Build Moving Sea Angel Island` installs the prefab, route, and reference scenery when no island exists in the scene. Repeating it selects the existing island and does not rebuild or overwrite edits. To experiment with a fresh setup, use a copy of the scene rather than deleting personal scene work.

The small sea-angel prefab and meshes remain separate from the giant creature's gameplay. A saved island prefab supplies the body/deck; the route and inhabitants remain scene content. Existing fishing controls remain applicable.

## Implemented foundation

- `CreatureSimulation` resolves optimization gates independently from semantic pause permission. `CreatureSleep` supplies distance policy; `DimensionalSchool` supplies dimensional-source policy. Their decisions converge through `Creature.SimulationEnabled`; distance wake cannot undo a capture/throw pause.
- Shared registry lifecycle events replace repeated distance discovery. Per-creature distance deadlines retain the half-second cadence while work is distributed across frames; fish source relevance retains its quarter-second cadence and source wake grace. This is a first migration, not the full population/streaming optimization project.
- `MovingFrame` plans fixed-step platform motion and implements existing `IRideSurface` support. `MovementFrameMember` transports kinematic inhabitants even when their AI is asleep.
- Dynamic creature motors run between removal of the prior transport velocity and addition of the next point velocity. The player uses its own existing support-relative controller instead of receiving a second transport pass.
- Fish spatial cells are partitioned by frame; dormant fish do not need world-cell reindexing for every island move. Sea-angel flight homes and fishing cast endpoints follow frame motion.
- A shared habitat discovers imported creatures. Carrying, capture, and scripted throws retain authority. Throws from the island inherit its point velocity once.
- Bird flock boundary steering uses the habitat when a bird belongs to an island, while ordinary birds retain the existing world sky bounds.

## Creature code review and targeted validation

The user's revised scope is representative testing, not exhaustive per-creature verification. Transport capability does not prove every cached waypoint or ability is frame-safe.

| Pattern | Reviewed examples | Prototype coverage / follow-up |
| --- | --- | --- |
| Intangible Transform flight, source dormancy | SeaAngelCreature, DimensionalSchool | Rendered-frame home transport and frame-local cells. Tested sleeping transport, active flight, reservation, and moving catch. |
| Dynamic ground velocity motor | SquirrelMotor, AsuraMotor, FireEater | Common velocity transport applies around FixedUpdate motors. Test squirrel walking and stopping; combat/territory systems remain review follow-ups. |
| Coordinated dynamic flight | BirdCreature, BirdFlockManager | Habitat-relative boundary steering added. Test representative bird flight. Mixed-frame flock velocity interpretation needs later attention. |
| Destination-driven flight | GlowBee, SkySpider | Velocity/kinematic transport is available, but cached destinations and world-configured managers require future focused debugging. |
| Rooted or passive inhabitants | FireSource, RecycleFlower, BounceShroom, FireFlower | Kinematic/static transport applies independently of AI. Tested a stationary FireSource. Detached RecycleFlower and hopping shrooms use dynamic behavior and are not equivalent to rooted inhabitants. |
| Scripted movement owners | CreatureThrower, ProjectileCreature, Boomerang | Explicit ownership exclusions are required. Test representative throw transitions; projectiles must remain world ballistic. |
| Capture / carrier movement | Creature carry, BubbleCapture, WebCapture | Frame transport yields to these owners. Deeply nested owner chains require later representative tests. |
| Surface attachment and pinned limbs | CeilingBatBugSurfaceMotor, CeilingBatBugEgg, TentacleCreature | A deck-adhered egg and one free/pinned tentacle passed targeted checks. Some other code keeps world anchors. No claim that all limbs, suspended rigs, or webs follow correctly. |
| Nested riders | CompostRideSurface, OreColossus, SkateGecko | Existing ride surfaces remain in place. Composition with the island needs a focused follow-up rather than broad assumptions. |
| Time/history abilities | RewindCoil, boomerang return paths, squirrel remembered territories | Cached world history is not automatically converted by common transport. Explicitly deferred for incremental debugging. |

No automatic reflection-based rewrite of species fields is used. Future adapters should identify which cached points belong to a moving support and which intentionally remain in the world. New explicit locomotion helpers preserve the applied transport term for Update-based velocity writers; the existing world-velocity helpers retain their original semantics.

## Verification results

Unity compilation and deterministic route/simulation-gate checks pass. The focused Play Mode probe passed idle riding, dormant fish movement, wake/reservation, squirrel motor walking, stationary FireSource transport, bird flight, an adhered egg, a full moving catch through retrieval, throwing inside the habitat, jumping, boundary exit/reentry, baked concave MeshCollider collision, and flight reset.

The longer idle check measured 0.0156 m player drift and 0.0109 m sleeping-fish drift during 89.73 m of island travel over 30 simulated seconds. A later focused run measured 0.0085 m and 0.0059 m respectively over 5 seconds. These are transport checks, not a population performance benchmark.

The tests spawn their representative creatures temporarily and do not leave test components or extra animals in the saved scene. `MovingIslandValidation.Run()` runs deterministic editor checks; in Play Mode, `MovingIslandValidation.StartProbe()` starts the representative probe. Results are exposed in `MovingIslandPlayModeProbe.Result` and `.Measurements`.

## Terrain and limits

The frame is collider-based and does not depend on Unity Terrain or ProceduralWorld. Baked generated meshes can be children of the kinematic root. Keep concave MeshColliders kinematic, and ensure occupants use supported convex collision. The live procedural terrain API still contains world-height/bounds assumptions; this prototype does not claim moving it wholesale is safe.

The authored route is not stopped by obstacles. Keep the entire body/wing sweep clear of scenery. Collision on fins is approximate; it is not an animated walkable mesh simulation.

Dormancy currently saves AI work, not all physics or renderer cost. Dynamic passengers retain physics and apply grounded braking when behavior is dormant; there is no state-only unloading yet. Distant world projectiles and unresolved falls must not be frozen merely to reduce cost.

The prototype is intended for iterative play and debugging. Strong banking, arbitrary gravity, perfect cross-frame constraints, and exhaustive ability compatibility are not completion gates under the revised scope.
