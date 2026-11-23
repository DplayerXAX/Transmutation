# Moving sea-angel island: technical review and prototype plan

Date: 2026-11-23. Status: proposal for discussion; no gameplay implementation or scene changes.

Implementation update: a first playable prototype now exists in Scene-Fishing. See [prototype guide and representative results](SeaAngelIslandPrototype.md) and [continuation checkpoint](../MovingIslandImplementationProgress.md). The original text below records research and design intent, not a claim that every advanced phase is implemented. The user replaced exhaustive creature validation with a code review and focused tests, leaving unusual abilities for incremental debugging.

## Recommendation

Build the island as a **kinematic moving platform with its own coordinate frame**, a flat physical deck, and a separate animated sea-angel visual. Keep ordinary world gravity and keep the deck level initially. Animate wings, tail, and body details to sell flight; move the whole island along a smooth authored route that rises, circles the world, descends, and returns periodically.

Use the existing `IRideSurface` player integration. Generalize resident transport into shared movement-frame membership, selected by an island habitat volume and interaction events, rather than implementing it inside fish AI. The island maintains its member registry; each member stores its frame-relative state. Keep simulation activity independent from membership, so imported and dormant creatures travel correctly. Keep loose physical objects under world physics, with explicit handoffs when catching, carrying, dropping, or throwing them. Do not make proximity alone move every object around the island.

**Follow-up revision:** the final section, “General habitat membership and dormant creatures,” extends and supersedes the fish-specific scope of the original prototype. Shared frame membership and dormancy transitions should now be established before the fish integration.

**Shared optimization workstream:** [Creature simulation and optimization plan](../CreatureSimulationOptimizationPlan.md) defines a phased migration of fish and ordinary creatures to one scheduling authority. Keep fishing-specific activation and visibility rules; share execution scheduling, safe dormancy, and frame integration. The moving-deck prototype can proceed before the full optimization migration. Common contracts, fish plus one ground-species migration, and moving-frame dormancy are the habitat integration prerequisites; broader migration and large-population optimization are separate later work.

Start in `Scene-Fishing`, using its existing 24 x 24 metre test platform. Prove transport and fishing before creating final terrain or placing the island in `Scene-World`.

This is feasible without replacing the game's movement or adding a pathfinding package. The meaningful work is coordinate ownership, support velocity, and transitions between movement systems. Generating an attractive island is a separate problem.

## Direct answers

| Question | Answer |
| --- | --- |
| Should this use procedural terrain? | Eventually it can reuse generated/sculpted meshes. Start with a simple deck. Do not initially move a complete live `ProceduralWorld` and assume all its queries follow correctly. |
| Will the player automatically move with the terrain? | Collision alone is insufficient for reliable transport. The player already supports moving surfaces, but vertical movement, jumping, and turning require validation and some changes. |
| How do airborne sea angels follow? | Their homes and swimming positions belong to the island's coordinate frame, even though they never touch its ground. Sleeping stops swimming, not transport. |
| Should everything within a radius follow? | Use a habitat volume to automatically assign eligible creatures to a shared movement frame, including creatures brought aboard. Preserve explicit exceptions for held objects, throws, projectiles, and externally anchored creatures. Frame membership persists through dormancy. |
| Will current navigation break? | Cached world positions and motors that overwrite absolute velocity will break assumptions. Local goals plus support-relative motion fix these without requiring A*. |
| Does the island need planetary gravity? | No, while the deck remains mostly horizontal. Walking around its underside or strongly banking it would expand the project into arbitrary-gravity locomotion. |

## Existing project findings

Inspected Unity version: **6000.3.6f1**. AI Navigation **2.0.9** is declared in the project. Searching `Assets/Scripts` found no `NavMeshAgent`, `NavMeshSurface`, `NavMeshPath`, or `CalculatePath` references; package installation does not establish that gameplay uses it. This was a source review, not a live Editor or performance test.

| Existing code | Relevant behavior and consequence |
| --- | --- |
| `Assets/Scripts/Editor/FishingSceneSetup.cs`, `CreateScene()` | Builds a cube named Fishing Platform on layer 7, scaled 24 x 1 x 24. The current scene also contains that named object. A useful isolated prototype starting point. |
| `Assets/Scripts/Movement/IRideSurface.cs` | Already exposes `Supports(Collider)` and `VelocityAt(Vector3 pointWS)`. Preserve this integration contract. |
| `Assets/Scripts/Movement/PoseRideSurface.cs` | Computes point displacement from successive poses, including rotation. Useful reference, but its contract samples an already-applied Transform move; do not blindly call `Step()` immediately after scheduling `Rigidbody.MovePosition()`. |
| `Assets/Scripts/Movement/SmoothFirstPersonController.cs`, `UpdateRideSupport()` | Finds supporting `IRideSurface` using a raised ground cast. It uses `whatIsGround`, so island deck layers must be included even when general grounding accepts other layers. |
| Same controller, `SteerVelocity()` and `SpeedControl()` | Subtracts platform velocity, controls relative walking velocity, then adds platform velocity back. This is the right existing foundation. |
| Same controller, `Jump()` | Explicitly sets world Y velocity to zero. That removes ascending/descending platform velocity instead of preserving a jump relative to the surface. |
| Same controller, `FixedUpdate()` fishing branch | Fishing locks input while preserving physics and support-relative horizontal braking. Retain that behavior on the island. |
| Same controller, flat-ground steering | Subtracting and then restoring the same Y support velocity leaves world vertical velocity unchanged. This is not an explicit vertical sticking mechanism; descent/acceleration needs a support test. |
| `Assets/Scripts/Creatures/SeaAngelCreature.cs` | Stores `home` in world space, swims toward world-space sinusoidal goals, and skips updates when asleep. Converting only the Transform hierarchy would leave stale homes. |
| `Assets/Scripts/Creatures/DimensionalSchool.cs` | One registry per Unity scene; cells use world position. Only awake fish are routinely reindexed. Transported sleeping fish could remain in their old cells and fail wake/reservation queries. |
| `Assets/Scripts/Creatures/FishingPoleCreature.cs` | Cast endpoints, lure position, reel direction, and retrieval history use world coordinates. Catch materialization changes fish movement authority. Audit each stage rather than only free swimming. |
| `Assets/Scripts/Creatures/FishingStruggle.cs`, `FishingLoad` | Already excludes body translation and externally repositioning fish from earned load. Preserve that intent; test rotation and frame transitions before changing its algorithm. |
| `Assets/Scripts/Creatures/SquirrelMotor.cs`, `AsuraMotor.cs` | Write world Rigidbody velocity. Squirrel also keeps navigation/bypass state. These are concrete examples requiring adaptation before such creatures become island residents. |
| `Assets/Scripts/MarchingCubes/ProceduralWorld.Runtime.cs` | Chunk meshes are children with local offsets, but `GridMinimum`, `VolumeBounds`, `ColumnHeights`, chunk bounds, and falling-player handling include translation/world-height assumptions. A rotated hierarchy does not make these APIs rotation-safe. |

## What comparable implementations establish

### Outer Wilds

Mobius's 2015 engineering post describes moving planets with rotating terrain colliders and a walkable ship cabin. It documents a Unity upgrade problem: non-convex colliders could no longer accompany dynamic, non-kinematic rigidbodies. The team discussed kinematic bodies with custom motion simulation as one possible response. **The post records a development problem and alternatives, not confirmation of the final shipped architecture.** [Mobius Digital: Breaking the Laws of Physics](https://www.mobiusdigitalgames.com/news/breaking-the-laws-of-physics).

The applicable lesson is that moving level geometry needs deliberate collision and reference-frame architecture. Reproducing a solar-system simulation is unnecessary for one island on an authored route. Floating-origin precision and passenger transport are also separate problems; moving the universe around the player would not remove the need to handle relative motion.

The developer interview documentary is useful additional context, but this review does not infer undocumented implementation details from its description. [Noclip: The Making of Outer Wilds](https://www.youtube.com/watch?v=LbY0mBXKKT0).

### Pirate ships and Sea of Thieves

Rare's developer update explicitly discusses PhysX, ship buoyancy, stable players on networked floating platforms, and erroneous collision ejection velocities. That supports treating boarding and contact transitions as first-class failure cases. It does **not** establish that Sea of Thieves uses an invisible stationary ship or a particular parenting algorithm. The official post was available in indexed search text; opening the forum directly returned HTTP 403. [Rare: Ladder Launching developer update](https://www.seaofthieves.com/community/forums/topic/177285/ladder-launching-developer-update/31).

Epic separately documents a movement-base API that supplies inherited velocity, usually when jumping from a moving base. That is a directly documented engine pattern we can adopt conceptually without claiming it describes Rare's exact code. [Epic: GetImpartedMovementBaseVelocity](https://dev.epicgames.com/documentation/unreal-engine/API/Runtime/Engine/GameFramework/UCharacterMovementComponent/GetImpartedMovem-?application_version=5.5).

### Unity implementation references

Catlike Coding's original working example covers synchronized platform motion, relative velocity, and moving/rotating ground. It is a useful implementation reference, with APIs to be checked against our Unity version. [Moving the Ground](https://catlikecoding.com/unity/tutorials/movement/moving-the-ground/).

Unity 6.3 documents moving kinematic rigidbodies with `MovePosition` in fixed steps and interpolation support. It also documents that concave MeshColliders can be static or kinematic, cannot collide with other concave colliders, and are costly to reshape at runtime. These constraints favor a simple physical deck with decorative animated geometry. [MovePosition](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/rigidbody/moveposition), [Mesh collider limitations](https://docs.unity.com/en-us/engine/6000.3/manual/physics-section/physics-overview/collision-section/collider-shapes/mesh-colliders/introduction).

## Architecture choices

| Approach | Advantages | Costs / decision |
| --- | --- | --- |
| One moving kinematic island in the current physics world | Existing collision, fishing, pickups, and player remain connected to the world. Reuses ride support. | Recommended. Requires local resident state and explicit velocity handoffs. |
| Fully dynamic creature/vehicle | Can react physically to impacts and forces. | More difficult route control, contact torque, stability, and convex collider restrictions. Unnecessary for this flight cycle. |
| Stationary duplicate island or separate local physics scene | Local simulation can stay near origin and stationary. | Boundary transfer, collision queries, visibility, fishing lines, and interactions with the world become substantially harder. Reconsider only if measured speed/scale problems justify it. |
| Parent every nearby object | Fast visual demonstration. | Conflicts with dynamic physics, stale world targets, and existing movement owners; proximity may capture unrelated objects. Unsuitable as the general architecture. |

## Proposed components and ownership

Names below are proposed, not existing classes.

| Component | Responsibility |
| --- | --- |
| `MovingIslandRoot` | Kinematic Rigidbody, fixed-step pose history, world/local conversion, and point velocity. Implements `IRideSurface` for approved deck colliders. Root scale stays one. |
| `IslandFlightPath` | Evaluates smooth route position and yaw with bounded speed, acceleration, and turn rate. Does not manipulate residents. |
| `SeaAngelIslandVisuals` | Fins, breathing, translucent body, and optional visual banking. Does not animate the deck collider or own root motion. Reuse sea-angel artwork, not the small catchable creature's gameplay component. |
| `IslandResident` / frame binding | Explicit island ownership and local pose/home for eligible nonphysical residents. Maintains attachment while their AI sleeps. |
| Frame-aware school index | Extends the scene's `DimensionalSchool` to maintain world and island-local partitions. Converts query centers into each relevant island frame. |
| `IslandLifecycle` | Arrival, boarding opportunity, flight, departure, hidden state, and reappearance. Prevents unsafe despawn/reset while occupied or tethered. |

Suggested hierarchy:

```text
MovingIslandRoot [kinematic Rigidbody, frame, ride surface, route]
  Deck [BoxColliders / simple collision mesh, Ground layer]
  Scenery [fixed rocks, plants, decorations]
  SeaAngelVisual [body, animated fins; no deforming walkable collider]
  ResidentAnchors [homes and authored local waypoints]
  HabitatVolume [membership candidate detection only]

Player and loose dynamic bodies remain world-physics actors.
Resident fish can use local Transform parenting only while their current
movement mode explicitly permits it; parenting is not their ownership model.
```

For the intangible fish, use local coordinates as authoritative state and a single presentation/transport owner. If their kinematic Rigidbody remains enabled, coordinate its pose updates with physics; do not let both hierarchy animation and another motor independently write its pose. The prototype should settle this detail before generalizing the binding component.

## Coordinate and velocity contract

For an island pose with translation `P` and rotation `R`:

```text
worldPoint = P + R * localPoint
localPoint = inverse(R) * (worldPoint - P)
pointVelocity = linearVelocity + angularVelocity cross (worldPoint - pivot)
worldVelocity = pointVelocity + R * relativeLocalVelocity
```

Angular velocity is radians per second and must correspond to the same pivot as the reported linear velocity. A finite-step alternative is `(nextPose(localPoint) - currentPose(localPoint)) / fixedDeltaTime`. This matches authored path motion and accounts for the greater movement near the edge of a rotating deck.

Compute the upcoming frame motion once, before dependent motors, from explicit physics poses. Schedule `MovePosition`/`MoveRotation`, supply the same step's point velocities to support controllers, then let PhysX simulate. Do not derive physics velocities from interpolated render Transforms. Initialize/reset pose history on spawn, teleport, or reappearance so a reset does not become an enormous launch velocity.

Keep one motion owner per object. A grounded player already receives base velocity through locomotion: adding a second full platform displacement or parenting the same body would transport it twice. Render interpolation and camera following should use consistent presentation timing; interpolation smooths display, not incorrect physical transport. [Unity: Rigidbody interpolation](https://docs.unity.com/en-us/engine/6000.3/manual/physics-section/physics-overview/rigidbody/rigidbody-interpolation).

### Player and grounded creatures

Retain support-relative steering and speed limits. Track support enter/stay/exit, the local contact point, and the support velocity used in the current physics step. On an ascending or descending level deck, test whether contact plus existing gravity maintains support; if not, add bounded support adhesion/correction only while valid support exists. Never glue jumping, knocked-back, or falling actors to the island.

At jump, compose the takeoff velocity from platform point velocity plus player-relative motion and the jump impulse. Preserve platform vertical velocity; do not add platform horizontal velocity a second time when it is already present. Subsequent air control and speed caps must not immediately erase inherited transport. Test coyote time and walking off an edge as well as an explicit jump.

The default proposal uses world gravity and a level deck with yaw. Let the player's view remain independently controlled initially; automatic view yaw is a later comfort choice. Strong deck roll/pitch needs a separate decision about gravity, slopes, and camera orientation.

Grounded creature motors should similarly brake and accelerate relative to support. This is a per-motor integration, not a blanket Transform offset. Port one creature to establish the contract before promising every creature can ride.

### Fish that are not on the ground

Resident fish have an island-local home, swimming pose, and velocity. Their orbit remains around that home as the frame moves. A sleeping fish retains the same local position while the island carries it; its individual AI need not wake.

For indexing, keep cells in the same local coordinates as the resident fish. Transform a world-space player/lure/stone query into island space before looking up those cells. With rigid transforms and unit scale, the query radius stays unchanged. Keep a world partition for ordinary fish and query each relevant partition without duplicates. A coarse world bound per island can reject distant queries without reindexing every sleeper.

On transfer, unregister from the old partition, preserve world pose, compute the new local pose, reset cached motion history, and register exactly once. Existing scene-handle registry ownership also needs explicit handling if content ever moves between loaded scenes. Moving around inside one scene does not itself change that ownership.

### Physics objects, catches, and throws

| Object/state | Recommended behavior |
| --- | --- |
| Fixed scenery | Child of island; follows its pose. |
| Intangible resident fish | Local swimming state; transported whether awake or asleep. |
| Player / walking creature | Ground support and relative locomotion. |
| Dropped physical prop or fish corpse | Dynamic world body; contact and friction first. If gameplay requires guaranteed retention, add an explicit resting-object transport policy with release thresholds. |
| Held object | Existing carrier owns movement. Do not also transport it as an island resident. |
| Jumping or thrown body | Retains takeoff point velocity, then follows world physics. No continued attraction merely because it remains inside the island volume. |
| Environmental particles | Choose local simulation for an attached aura; world simulation for a trail left behind. |

When a fish materializes, remove resident transport before dynamic retrieval takes ownership, preserve world pose, and seed world velocity from frame motion plus its intended relative velocity. `BeginCaughtRetrieval()` currently clears velocity; evaluate replacing that reset for moving-frame catches. Retrieval already follows a moving target, but its absolute maximum-speed clamps can become insufficient at higher island speeds.

The fishing system also needs an explicit cast policy. Proposed first version: a cast made aboard the island stores endpoints in that island frame, giving a stable cast relative to the habitat. A world cast stays world-based. Cross-frame fishing must either have fully defined world-space tether behavior or cancel cleanly on departure. Carrying a pole does not by itself imply frame ownership.

Check `castStart`, `castDestination`, `LurePosition`, `reelForward`, retrieval history, and `FishingLoad` together. Rotation of the island must not earn artificial struggle progress. Keep natural swimming displacement separate from frame transport. On discontinuous frame changes, reset sampling history.

## Terrain decision

Procedural generation answers “what shape is the island?” The frame system answers “how does it move?” Generate once in local coordinates and move the result; translation or rotation alone should not regenerate/cook the mesh.

Recommended progression:

1. Existing box deck and a sea-angel visual.
2. Authored deck outline with several simple colliders and generated decorative geometry.
3. If sculpting is useful, export/bake a bounded island from the existing terrain pipeline into local meshes and attach those assets to the moving root.
4. Retain a live `ProceduralWorld` only if runtime editing is actually required; audit coordinate-sensitive queries, bounds, spawn/respawn, decorators, shaders, and cached placement state first.

Bake/export requires a deliberate tool: current procedural chunks are marked `DontSave`, so simply parenting generated scene objects is not a persistent asset workflow. Preserve sculpt data separately if regeneration must remain editable.

A kinematic root can support concave mesh collision, but begin with primitives for a mostly flat deck. Never turn that root into a dynamic Rigidbody while retaining concave colliders. Also, an authored kinematic route is not automatically stopped by scenery, and concave island/world colliders do not provide a dependable island-against-terrain blocking solution. Validate route clearance with an approximate hull and world geometry before integration.

Audit world-space terrain materials so textures do not appear to swim over the moving island. Disable inappropriate static/baked assumptions for moving renderers, and keep large decorative wing animation separate from collision. These are presentation checks, not reasons to rebuild the world renderer.

## Navigation implications

There are three distinct pieces: deciding a destination, steering around obstacles, and physically moving the creature. All three need consistent frames even without A*.

An island waypoint should be stored as `(frame, localPoint)`. A creature pursuing another object can keep that object reference and evaluate its current world position. Cached last-seen positions, bypass points, home locations, and territory boundaries need frame ownership. Casts still run against world colliders using transformed positions/directions. Relative speed should drive stuck detection and walking animation, so island travel does not count as the creature walking.

A rigidly moving island does not change its internal route connectivity. Local waypoints or a small local graph can remain unchanged while its world pose changes. Connections between island and mainland are time-dependent: boarding links should exist only during an actual boarding opportunity, and be invalidated when it ends.

Do not add NavMesh as part of this first prototype. If later needed, evaluate a separate local-navigation representation whose path output feeds the existing motor, or validate movable NavMesh data and agents in a dedicated spike. Moving NavMesh data alone is not a passenger controller. Avoid letting a NavMeshAgent and dynamic Rigidbody both author movement; Unity explicitly warns about that conflict. Reviewed installed package documentation: `Documentation~/NavMeshSurface.md` and `Documentation~/MixingComponents.md`. [Unity: Mixing navigation and other components](https://docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/manual/MixingComponents.html).

## Arrival, departure, and disappearance

Proposed states: `Hidden -> Arriving -> BoardingWindow -> Cruising -> Departing -> Hidden`.

Use continuous position and velocity at route joins, with acceleration/turn-rate limits. A slow boarding window makes entry testable. The island must not teleport to the route start at the end of a visible lap.

Recommended initial policy: if a player is aboard or an active fishing interaction spans the island boundary, defer disappearance and continue a safe holding route. Disable/reset it only after resolving passengers, caught bodies, carried objects, and tethers. Do not silently destroy player-owned objects along with resident scenery. Before reopening the boarding window, reset motion history and ensure colliders, resident poses, and school indices are ready.

If the intended experience is riding it down into a hidden realm, that is a different lifecycle: keep the island alive or make an explicit scene transition. It should be decided as gameplay, not emerge accidentally from deactivating a GameObject.

## Prototype plan and acceptance gates

The following work is proposed for after discussion. Each stage should remain independently reviewable.

| Stage | Work | Acceptance gate |
| --- | --- | --- |
| 1. Physical transport | In a preserved copy/setup of Scene-Fishing, move the existing deck horizontally, vertically, then in yaw; integrate the frame with `IRideSurface`. | Standing, walking, stopping, jumping, and stepping off work at deck center and edges. No double carry or launch on enable/reset. |
| 2. Resident fish | Convert homes/swimming state and school partitions; retain sleeping optimization. | Move more than two 12 m cells while fish sleep, then approach: wake, visibility, and lure reservation still find the correct fish. |
| 3. Fishing lifecycle | Frame-aware cast, reeling, materialization, retrieval, carry/drop/throw transitions. | Complete a catch while rising and turning; no artificial load, frozen lure, lost corpse, or conflicting motor ownership. |
| 4. Other inhabitants | One ground creature and loose props; support-relative motor and explicit release. | Idle/walk/pursue behaviors remain on deck; throwing exits transport; passing the island near outsiders does not capture them. |
| 5. Appearance and route | Giant sea-angel visual, flat deck dressing, route and lifecycle in the test scene. | Wing motion never moves walkable collision; departure with passengers follows the selected policy. |
| 6. World integration | Reusable prefab, route-clearance checks, arrival area, and world performance pass. | Full route clears land/structures; boarding and return work; physics and school costs fit the measured frame budget. |

Suggested initial motion envelope, to measure rather than assume safe: 24 m deck, 0.5–3 m/s translation, up to 10 degrees/s yaw, and gentle acceleration. At a 12 m radius, 10 degrees/s already adds about 2.1 m/s tangential speed. Record the validated envelope before scaling the visual, route speed, or deck size.

Test at multiple render rates with the same fixed timestep, and during several fixed steps per rendered frame. Include fishing input lock, slopes/deck edges, rapid direction changes, ascending/descending jumps, returning to the mainland, two candidate habitats, disabled residents, and occupied departure. Compare existing compost/colossus riding and stationary-ground movement after controller changes.

For an initial idle-transport check, target less than 0.1 m net local drift over 30 seconds of steady cruise, with no visible foot/deck jitter. Treat that as a proposed acceptance threshold, not a measured result. Profile fixed-step time, contact counts, allocations, resident activation queries, and render cost against the stationary baseline. Do not claim performance from collider or fish counts alone.

## Discussion decisions

The architecture can start with conservative defaults, but these choices change scope:

1. **Jump behavior:** ordinary inherited momentum, or an island influence that keeps airborne players with it through turns? Recommend inherited momentum first.
2. **Departure while occupied:** wait/loop, carry the player into another destination, or force disembarkation? Recommend wait/loop for the first prototype.
3. **Deck attitude:** level deck with visual banking, or physically pitching/rolling ground? Recommend level deck initially.
4. **Terrain editing:** authored/baked island or live sculptable terrain? Recommend authored/baked.
5. **Loose objects:** allowed to slide/fall naturally, or guaranteed to remain aboard until thrown? Recommend natural physics first, then a narrow resting-object policy if needed.

The highest-confidence implementation path is a small moving deck plus local resident fish, followed by the full fishing loop. Strong banking, arbitrary gravity, universal creature support, seamless cross-frame tethers, and live terrain deformation should each earn their complexity through a demonstrated gameplay need.

## General habitat membership and dormant creatures

Follow-up research, 2026-11-23. This section changes the recommended foundation from fish-oriented attachment to a shared habitat/frame system. It does not propose implementing a separate physics engine.

### Verdict on a moving influence volume

**A platform-owned habitat volume is a good way to select a creature's movement frame. A volume that blindly parents or translates every overlapping collider is not a good general movement implementation.** The distinction is how membership is selected versus how movement is executed.

The island should maintain a registry of members, and a common creature movement service should implement frame transitions, relative locomotion, and dormant transport. Fish should express swimming intent and habitat preferences; they should not implement their own version of island transport. A creature brought from elsewhere can join through the same volume and release events as an authored resident.

The volume does not eliminate ownership: it is the mechanism that assigns it. Membership must be stored because a dormant or unloaded creature might have no enabled collider to trigger an event. The authoritative registration must survive disabling the creature's presentation.

A gravity field and a transport frame are different mechanics. Cancelling gravity only stops falling; it does not carry a creature sideways or around a turn. A force that makes bodies match the island's velocity is a physical current, with lag and acceleration limits. A habitat frame can instead make a hovering creature remain exactly relative to the island. That is an intentional gameplay behavior and suits these dimensional fish, but should not silently change every projectile's trajectory.

### What the additional research supports

Michael Barclay's first-person account of working as CIG's lead level designer describes Star Citizen stations and capital ships with their own physics grids, including levels moving within other levels. This supports the general concept of an environment supplying the local simulation frame. It does not supply a ready-made Unity implementation or establish the exact boundary or sleeping rules. [Michael Barclay: Space To Ground](https://www.gamedeveloper.com/design/space-to-ground-matters-of-scale-in-level-design). CIG also published an engineering feature specifically about multicrew ships and physics grids; the accessible landing page introduces scanned article pages, so this review does not infer further implementation details from that landing page. [CIG: Multicrew & Physics Grids](https://robertsspaceindustries.com/en/comm-link/engineering/15222-BTS-Multicrew-Physics-Grids).

Minecraft's useful precedent is separating simulation distance from rendering distance. Microsoft documents Bedrock entities ceasing movement outside ticking regions. “Not in view” is not the same as “not simulated,” and pausing an entity does not necessarily mean deleting its state. This is a scheduling precedent, not evidence for moving-island coordinates. [Microsoft: Simulation Distance, Render Distance, and Ticking Areas](https://learn.microsoft.com/en-us/minecraft/creator/documents/simulationrenderdistanceguide?view=minecraft-bedrock-stable).

Unity's CullingGroup supports distance bands and visibility information that can drive cheaper AI and animation. It supplies relevance signals, not frame ownership, physics suspension, or persistence. Use distance and interaction relevance for gameplay; visibility may reduce animation cost but should not stop a thrown creature merely because the camera looks away. [Unity 6.3: CullingGroup](https://docs.unity.com/en-us/engine/6000.3/manual/cameras/occlusion-culling/culling-group-api/api).

PhysX sleeping is another distinct mechanism: sleeping dynamic bodies have zero velocity, and movement/contact can wake them. A creature stationary relative to a moving deck is still moving in world space. Calling `Rigidbody.Sleep()` is therefore not a solution for retaining a dormant creature at an island-local position. The cited PhysX documentation explains the underlying concept; it is not a claim about Unity's exact bundled PhysX revision. [NVIDIA: Rigid Body Dynamics, Sleeping](https://nvidia-omniverse.github.io/PhysX/physx/5.4.1/docs/RigidBodyDynamics.html#sleeping).

### Existing optimization is already relevant

`Assets/Scripts/World/CreatureSleep.cs` already checks distance every 0.5 seconds and rediscovers creatures every 5 seconds. It has separate sleep/wake distances, a useful existing hysteresis pattern. Its `Sleepable()` excludes sea angels, birds, bees, bubbles, projectiles, and several other kinds. It pauses eligible creatures with `SetSimulationEnabled(false)` and remembers which creatures it paused.

In `Creature.cs`, that setter changes a boolean and invokes `OnSimulationChanged()`. The base `Update()` stops behavior and age advancement. It does not universally suspend Rigidbody simulation, all auxiliary components, or all subclass callbacks. Individual subclasses have their own responses. The class comment in `CreatureSleep` describing quiet creatures is not a physical rest test for every eligible body.

`Creature.OnDisable()` unregisters the creature from shared queries and can release carried/captured state. Consequently, `SetActive(false)` is not presently a safe universal distance optimization either. `CreatureRegistry` queries currently read actual Transforms/Rigidbodies, so a future dormant representation needs updated query contracts rather than leaving stale GameObjects in place.

Shared movement helpers exist in `Creature.cs`, but not all motors use them. `SetCreatureVelocity` is explicitly a world-velocity API, and `SquirrelMotor`/`AsuraMotor` directly write Rigidbody velocity. Changing one base helper would not automatically fix all creatures. Introduce explicit relative-motion methods and migrate callers; do not silently reinterpret existing world-velocity arguments.

### Three independent pieces of state

| State | Examples | Meaning |
| --- | --- | --- |
| Movement frame | World, island A, island B | Where local pose, local navigation, and dormant location are expressed. |
| Motion authority | Locomotion, carry, capture, scripted throw, free physics | Which system is permitted to move the actor now. |
| Simulation tier | Full, reduced behavior, dormant local state, unloaded record | How much work runs, independently of where the actor belongs. |

A fish can be `island A + flight locomotion + dormant`, then become `island A + flight locomotion + full` without changing position. A carried fish gets its actual pose from the carrier even if its previous habitat is remembered. Throwing it makes the throw controller authoritative; the habitat volume must not immediately seize it again.

Keep support contact separate as well. A grounded actor may have an island as its spatial frame while its world Rigidbody uses contact-based support velocity. A flying habitat member uses frame-relative flight. **The same frame movement must never be applied through both paths.** Dormancy switches representation; it must not add a second transport pass to an already-moving body.

### Shared component boundaries

The earlier `IslandResident` proposal becomes a general `MovementFrameMember` used through the base creature lifecycle. Proposed responsibilities:

| Component/service | Responsibility |
| --- | --- |
| `MovingFrame` | Pose, point velocity, frame ID, and registered members. Independent of creature AI. The island is one implementation; static world is the identity frame. |
| `HabitatVolume` | Offers membership to eligible actors using spatial containment, priorities, and enter/exit margins. No direct Transform movement. |
| `MovementFrameMember` | Current frame, local pose, transport policy, and transition state. Registration outlives AI suspension. |
| Shared creature motion service | Arbitrates existing carry/capture/throw owners; converts relative motor intent to world motion; manages support and release. |
| Simulation scheduler | Selects behavior/physics/presentation tiers using distance, interaction pins, and safe suspension conditions. |
| Frame-aware queries | Resolve positions and local spatial indices consistently for awake, dormant, and eventually unloaded entities. |

Keep `Creature` responsible for integration hooks and a reference to these services. Do not grow the base class into the island route controller, spatial database, and simulation scheduler simultaneously. Build the common contract now; migrate species in measured increments.

### How imported flying creatures join

Recommended default policy: ordinary free flight/hover locomotion is eligible for habitat-relative movement. This is a shared locomotion capability, not a test for `SeaAngelCreature` or a list of prefabs that originally spawned on the island.

1. While held, the carrier owns position. Membership evaluation may remember a candidate but does not move the object.
2. When gently released inside the habitat, resolve the volume immediately. Preserve world pose, store local pose, and convert velocity into the new frame. No need to wait for a trigger-enter event that may have happened while the object was held.
3. The autonomous flying motor works relative to that frame. For a release intended to leave the animal hovering with the island, a bounded settling response brings relative speed toward its hover intent. This response is an actual acceleration choice, not a consequence of changing coordinates.
4. When thrown, inherit the carrier's appropriate point velocity exactly once and keep throw authority. Suppress habitat transport until the explicit throw state ends. A timeout alone is insufficient if the creature is still ballistic.
5. On finishing the throw, decide whether the creature becomes free flight, grounded, or free physics, then reevaluate membership under that policy.

For entry from another frame, preserve world pose and world velocity:

```text
newLocalPosition = inverse(newFramePose) * worldPosition
newRelativeVelocity = inverse(newFrameRotation) *
                      (worldVelocity - newFrame.PointVelocity(worldPosition))

on exit:
worldVelocity = oldFrame.PointVelocity(worldPosition) +
                oldFrameRotation * relativeVelocity
```

Changing the representation alone must not change world velocity. Choosing to entrain a passing creature into the island's motion requires a defined bounded acceleration or an intentional snap. Recommend bounded entrainment for active flying creatures and immediate local attachment only for explicit placement/rest or already-dormant established members.

Use an inner acquisition boundary and a larger retention boundary so creatures do not switch frames every tick at the edge. Deduplicate multiple colliders by actor identity. When volumes overlap, retain the current valid frame; otherwise use explicit priority and a stable tie-break. Ground support should identify the actual deck rather than a neighboring volume. Handle spawn, teleport, release, disable/destroy, and load through registry events and reconciliation, not only `OnTriggerEnter/Exit`. Fast entrants need swept checks or appropriately frequent candidate evaluation.

An island flying past a sleeping world creature must not automatically adopt it just because the creature has no motion that frame. Wake relevant candidates or resolve entry against their persistent world/frame state. Default exclusion should cover world-rooted or externally tethered creatures, active projectiles, throws, and other motion owners. If the desired fiction is that a magic field captures literally everything, implement that as a separate explicit gameplay ability with those consequences.

### Dormancy must be relative to the current frame

The basic rule is: **pause autonomous motion, retain local position and frame membership**. An animal sleeping 3 metres above the deck stays 3 metres above it as the island moves. That requires no fish behavior tick.

For a grounded creature, establish valid support before suspending physics. For an airborne creature, require a frame-relative hovering/flight policy that permits freezing. A ballistic falling creature should not become suspended midair simply because its distance crossed a threshold. Keep it physically active, or use a separately validated coarse trajectory simulation until it reaches a safe state.

Use these tiers progressively:

| Tier | Behavior | Physical/presentation representation |
| --- | --- | --- |
| Full | Normal perception, decisions, locomotion | Normal collision and visual updates. |
| Reduced | Decisions/perception run less frequently | Physics/contact and continuous motion still update as required; lowering AI frequency does not lower necessary collision frequency. |
| Dormant, represented | No individual AI, animation optionally reduced | Local pose retained. If collision remains enabled, a central manager moves a kinematic proxy in fixed steps; it still has a physics cost. Nonphysical visuals can follow a frame hierarchy. |
| Dormant, unrepresented | State record only | No active collider or renderer. Store frame ID and local pose; reconstruct world pose when queried/woken. Requires explicit lifecycle and query integration. |

Introduce represented dormancy first. Do not build unloading and persistence infrastructure just to ship the island. However, make the frame ID/local-pose contract usable by a later state-only representation.

When resuming, compute the current world pose from the latest frame pose, check overlap/clearance, restore the intended physics configuration, initialize world velocity from current frame motion plus the chosen saved relative velocity, reset motion histories, then resume motor/AI. Do not estimate velocity from the difference between old and new world positions over the last render frame. Choose whether local wandering velocity resumes or restarts at zero; dormant idle creatures should normally restart at rest relative to their frame. Apply the same principle to angular velocity where rotation is simulated.

If terrain/support changes while a creature is dormant, invalidate its resting attachment and wake or safely re-place it. If a frame disappears, transfer its records to a defined fallback or persist them with the island; never retain a dangling frame ID. Loading must resolve the island's current route pose before restoring members.

### What remains active when everything is far away

The island route or its time-based pose evaluator must remain available. Its frame transform and broad world bounds remain available. The scheduler uses those bounds to determine whether any member could be relevant. Only then does it inspect island-local groups or individual entities.

Do not test wake distance against a stale dormant Transform. Either update represented members or derive `worldPosition = framePose * localPosition` on demand. Keep member spatial partitions local so movement of the island does not require reindexing every sleeping creature. World queries first discover candidate frames, then query within each frame.

This can reduce far-away work to evaluating a few frames rather than thousands of creature brains. It is not automatically O(1): enabled child Transforms, renderers, colliders, and broadphase updates still cost work. Thousands of child rigidbodies do not become free when the parent moves. State-only records provide the larger saving when profiling eventually justifies them.

Wake ahead of possible interaction using a distance margin based on relative approach speed, scheduler delay, and activation latency. Pin physics/gameplay activity during carry, fishing, combat, throws, and cross-frame constraints. Turning away from a creature should not suspend an interaction. Use explicit suspension reasons or tokens so the distance scheduler cannot undo a pause owned by a capture, freeze effect, or another system.

Age/lifetime policy also belongs in this contract: the present base Update pauses age when simulation is disabled. Preserve that behavior initially and document it. Any later elapsed-time catch-up should be explicit; never run thousands of missed physics ticks at wake.

### Parenting versus a local physics world

Parenting is appropriate for fixed scenery and nonphysical presentation, and can be a convenient representation of dormant local poses. It is not the authoritative membership store, and should not fight an active dynamic Rigidbody or carrier. The hierarchy may change without changing semantic membership, or vice versa.

A separate stationary local physics scene becomes attractive if the requirement expands to thousands of independently colliding passengers that must all experience the island as stationary, including every airborne body during violent acceleration. That avoids some global transport work but introduces cross-scene collision, tether, raycast, and transfer problems. Unity will not automatically reproduce Star Citizen-style local physics grids by parenting a Transform.

For the current slow island and existing world interactions, choose the single-world shared frame system. Freeze far creatures in local state, simulate near creatures with explicit support/flight policies, and profile before introducing separate physics worlds.

### Revised implementation order and tests

Before original stage 2, follow phases A–D of the [shared simulation plan](../CreatureSimulationOptimizationPlan.md): baseline, common scheduling authority, fish plus one ground-species migration, then moving-frame dormancy. Include a shared-frame spike using one ground creature and one flying creature brought from outside the island. The common service should have no sea-angel type check for transport. Broader migration and advanced optimization remain separate phases E–F, rather than blocking the initial deck experiment.

Acceptance scenarios:

1. Carry a flyer from mainland, release it on the island, and watch it hover during translation and yaw.
2. Leave until both ground and flying members become dormant; move the island far from their original world positions; return and verify wake at the correct local positions without impulse spikes.
3. Let the moving island approach a stationary player: dormant members must wake based on their new world positions, even though the player never moved.
4. Throw the same creature across the volume; it retains ballistic motion and is not immediately recaptured. Repeat while turning and climbing.
5. Cross overlapping habitat boundaries and disable a volume while occupied: no duplicate movement, lost registrations, or flickering ownership.
6. Hide a creature behind the camera while it attacks, falls, or is fished: rendering relevance must not cancel required gameplay simulation.
7. Pass the habitat over dormant mainland creatures: world-rooted creatures remain there; eligible entrants follow the defined acquisition policy.
8. Suspend and resume during a separate capture/freeze effect: the distance scheduler never restores movement it does not own.
9. Profile dormant members with live colliders versus a state-only test representation, measuring transform/broadphase cost as well as AI time. Adopt unloading only if the saving warrants its lifecycle complexity.

The revised recommendation is **platform-selected membership, common creature movement integration, and frame-relative dormancy**. The useful part of the influence-field idea is making the habitat responsible for transport eligibility. The part to reject is treating every overlapping object as an unconditional child or applying a second platform displacement after its existing motor has already moved it.
