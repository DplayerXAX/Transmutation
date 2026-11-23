# Sea-angel island prototype

Implementation started 2026-11-23. Related: [technical review](MovingSeaAngelIslandTechnicalReview.md), [shared simulation plan](../CreatureSimulationOptimizationPlan.md).

## Play and author

### Visual, terrain, and population refresh

The giant now preserves the small sea angel's authored child proportions under one uniform visual scale. The earlier oversized opaque sphere was caused by replacing the authored Head scale with an absolute scale of 100 and applying the deck material to every body part. The giant uses the same Dimensional Angel rim shader with a normal transparent forward pass; the original window/honey passes retain their indices.

Walking animation is gated by the controller's walking/sprinting/crouching states **and movement input**. Standing still on moving ground does not advance the gait. Relative velocity controls stride rate only after that state/input gate permits walking.

There is a 20 × 32 metre baked mesh patch on the left side of the back, with a smooth 3.5 metre hill. Its MeshCollider is part of the kinematic island, is recognized by player support, and is marked as terrain for surface-attached ecology. It does not run a live procedural terrain generator. The recycle colony sits on this patch and successfully grows strands there.

The saved **Island Test Creatures** group contains Light and Dark melee/ranged Asuras, a chainsaw, three tentacles, five birds, bubble/fire flowers, a recycle colony and portable flower, Light/Dark goo, a bounce shroom, an attachment egg, and a dragon fuel bubble. A native procedural bone child (the small bone dragon) spawns at runtime near the rear of the deck. These are active gameplay creatures: Asuras may fight, flowers act normally, and play-session changes reset on leaving Play Mode.

Use **Tools > Creatures > Fishing > Refresh Island Visuals Terrain And Test Creatures** to apply the visual/terrain refresh. Existing test population placement is preserved on repeated calls. Pinned tentacle holds, recycle caches, and the bone child's cached movement points now have explicit frame transport; uncommon interactions still need incremental manual testing.

The focused gait/terrain probe passed idle gait suppression while travelling, gait advancement with walking input, stopping animation on input release, hill collision and player grounding, terrain ecology eligibility, and population/bone-dragon spawning. The first populated run exposed a bird startup registration race; registration now defers until its scene has loaded.

Open `Assets/Scenes/Scene-Fishing.unity` and enter Play Mode. The player starts on a giant sea angel's 48 × 72 metre back. Six 24 metre sections share one continuous deck collider. The body and animated fins have simple collision; the back is the reliable walkable surface.

The independent `Island Flight Route` object contains numbered child waypoints. Select the route to see handles, numbering, and direction arrows. Move the waypoints in Edit Mode with standard Undo. Four non-null points are required. The closed route uses an arc-length lookup, approximately constant cruise speed, and level yaw-only orientation. Default speed is 3 m/s, acceleration 0.6 m/s².

Select `Giant Sea Angel Island` to change cruise speed, pause the flight, or adjust habitat dimensions. Pausing decelerates. The component context menu's **Reset Flight To First Waypoint** resets passengers together without assigning teleport velocity. The habitat extends 12 metres past each deck edge, 30 metres above the back and 8 below, with a 2 metre retention margin. A disabled overview camera is available for a fixed external view; disable the player camera before enabling it.

`Tools > Creatures > Fishing > Build Moving Sea Angel Island` installs the prefab, route, and reference scenery when no island exists in the scene. Repeating it selects the existing island and does not rebuild or overwrite edits. To experiment with a fresh setup, use a copy of the scene rather than deleting personal scene work.

The small sea-angel prefab and meshes remain separate from the giant creature's gameplay. A saved island prefab supplies the body/deck; the route and inhabitants remain scene content. Existing fishing controls remain applicable.

## Implemented foundation

- `CreatureSimulation` resolves optimization gates independently from semantic pause permission. `CreatureSleep` supplies distance policy; `DimensionalSchool` supplies dimensional-source policy. Their decisions converge through `Creature.SimulationEnabled`; distance wake cannot undo a capture/throw pause.
- Shared registry iteration replaces the default distance system's repeated global creature discovery. Existing polling intervals and fish wake grace remain intact. This is a first migration, not the full population/streaming optimization project.
- `MovingFrame` plans fixed-step platform motion and implements existing `IRideSurface` support. `MovementFrameMember` transports kinematic inhabitants even when their AI is asleep.
- Dynamic creature motors run between removal of the prior transport velocity and addition of the next point velocity. The player uses its own existing support-relative controller instead of receiving a second transport pass.
- Fish spatial cells are partitioned by frame; dormant fish do not need world-cell reindexing for every island move. Fish swimming homes and fishing cast endpoints follow frame motion.
- A shared habitat discovers imported creatures. Carrying, capture, and scripted throws retain authority. Throws from the island inherit its point velocity once.
- Bird flock boundary steering uses the habitat when a bird belongs to an island, while ordinary birds retain the existing world sky bounds.

## Creature code review and targeted validation

The user's revised scope is representative testing, not exhaustive per-creature verification. Transport capability does not prove every cached waypoint or ability is frame-safe.

| Pattern | Reviewed examples | Prototype coverage / follow-up |
| --- | --- | --- |
| Intangible Transform swimming, source dormancy | SeaAngelCreature, DimensionalSchool | Explicit home transport and frame-local cells. Test sleeping transport, reservation, and moving catch. |
| Dynamic ground velocity motor | SquirrelMotor, AsuraMotor, FireEater | Common velocity transport applies around FixedUpdate motors. Test squirrel walking and stopping; combat/territory systems remain review follow-ups. |
| Coordinated dynamic flight | BirdCreature, BirdFlockManager | Habitat-relative boundary steering added. Test representative bird flight. Mixed-frame flock velocity interpretation needs later attention. |
| Destination-driven flight | GlowBee, SkySpider | Velocity/kinematic transport is available, but cached destinations and world-configured managers require future focused debugging. |
| Rooted or passive inhabitants | FireSource, RecycleFlower, BounceShroom, FireFlower | Kinematic/static transport applies independently of AI. Tested a stationary FireSource. Detached RecycleFlower and hopping shrooms use dynamic behavior and are not equivalent to rooted inhabitants. |
| Scripted movement owners | CreatureThrower, ProjectileCreature, Boomerang | Explicit ownership exclusions are required. Test representative throw transitions; projectiles must remain world ballistic. |
| Capture / carrier movement | Creature carry, BubbleCapture, WebCapture | Frame transport yields to these owners. Deeply nested owner chains require later representative tests. |
| Surface attachment and pinned limbs | CeilingBatBugSurfaceMotor, CeilingBatBugEgg, TentacleCreature | A deck-adhered egg passed the local attachment check. Some other code keeps world anchors. No claim that all limbs and webs follow correctly. Pinned tentacles are the next attachment-focused debug case. |
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
