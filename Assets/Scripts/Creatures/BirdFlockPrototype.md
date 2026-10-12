# Cube bird flock

`BirdCreature` is an ordinary `Creature`: instantiate the prefab, carry it through the
existing player carrier, pause its simulation, or disable/destroy it independently.
Its Rigidbody stays dynamic with gravity off. Flight never writes a carried bird's
velocity or rotation. After release it flies from its current position and velocity;
a stationary release uses its previous heading.

Birds include `WebCapture` and can be caught by Sky Spider silk through the normal
web contact scan. While trapped, both paused simulation and explicit captured-state
checks exclude them from flock movement. Picking up a trapped bird releases its silk
joint through the shared Creature carry system. Dropping it resumes flight, and the
web's normal recapture cooldown still applies.

## Shared coordination

`BirdFlockManager` is a scene-local service, not a creature or permanent flock identity.
Birds register themselves on enable. If none exists, the first bird creates one centered
on its spawn position. A placed manager supplies editable world-space sky bounds.
All nearby birds in that scene participate in separation, alignment, and cohesion,
regardless of which spawn area created them. Initial groups can merge naturally.
Carried and simulation-disabled birds are excluded from the flight snapshot and octree.

Each physics step snapshots every flying bird, rebuilds a pooled octree, computes all
new velocities, and only then applies movement. The root encloses actual bird positions,
including releases outside the sky volume. Exact spherical queries filter octree cells;
leaf capacity is 8 and subdivision stops at depth 6. Coincident positions are supported.
Rendering uses individual cube GameObjects with a shared instanced URP Lit material.

## Setup and tuning

- `Tools > Creatures > Create Bird Flock Assets` creates the prefab and material while
  preserving existing assets and artist edits.
- `Tools > Creatures > Add Bird Flock To Scene Yoyo` places one manager and three spawn
  tools, each creating 24 birds in Play Mode. Repeated setup does not duplicate placement.
- Save `Scene_Yoyo` after running the placement menu.
- Drag `Assets/Prefabs/Creatures/BirdCreature.prefab` into any scene, or instantiate it
  from another script. No explicit manager reference or spawn area is required.
- `BirdSpawnArea` is only a development placement tool. Its count, radius, and seed
  determine a one-time runtime spawn. Spawned birds are independent scene roots;
  removing the area after spawning does not remove the birds.
- Tune flight speeds, steering, neighbor radius, separation distance, and weights on
  the bird prefab. Tune sky bounds, edge margin, and boundary weight on the manager.
  Bounds use world coordinates; moving the manager does not move the volume.
- On each spawn area, enable **Override Flight Speed** and set **Flight Speed Range**
  (minimum/maximum metres per second) to customize its birds. Leave the override off
  to inherit the prefab's **Speed Range**. This is applied to each instance before
  flight starts and does not change the prefab. Since areas spawn only once, Play Mode
  edits to an area affect the next spawn/run; adjust an existing bird's Speed Range
  to tune that bird immediately.
- Selected-object gizmos show sky bounds and spawn areas. Enable `Show Octree` on the
  manager to see occupied leaf cells in Play Mode.

## Validation

`Tools > Creatures > Validate Bird Octree` compares exact searches with exhaustive
searches, including coincident points, empty inputs, radius boundaries, and distant
outliers. `Validate Bird Runtime (Play Mode)` uses a temporary local physics scene to
check automatic registration, lifecycle, pause, real carrier handoffs, release recovery,
scene transfers, and 72/200-bird populations. Reports are written under
`Temp/BirdFlockValidation` and never overwrite authored scenes.

`Validate Bird Web Capture (Play Mode)` verifies actual silk-contact capture, excludes
trapped birds even if their simulation is manually reenabled, and exercises pickup,
release, and resumed flight with both spring and firm grips. The runtime checks also
verify spawn speed overrides, initial velocities, and inheritance from the prefab.

Profiler markers: `BirdFlock.Simulation` and `BirdFlock.NeighborQueries`.
`CandidateChecks` reports actual point distance tests versus an exhaustive N² search.
Pooled storage grows when a larger population or tree is first encountered; warmed
simulation reuses storage. Dense flocks can still approach N² neighbor work when
every bird is inside the search radius. The octree is an optimization, not a promise
that 200 tightly packed birds cost less than 72 widely spaced ones.

Measured in the Unity Editor on this machine: warmed flight updates averaged about
0.47 ms for 72 birds and 2.21 ms for 200 birds, with zero managed allocations over
500 steps. These timings exclude the subsequent physics simulation and rendering.
For the compact initial clusters, octree rebuild plus searches measured 0.14/0.88 ms
versus 0.10/0.84 ms for exhaustive searches at 72/200 birds. At these small counts,
tree overhead can exceed the saved comparisons; the octree remains the shared spatial
index requested for this prototype. Reports include both timings rather than assuming
spatial partitioning is always faster.

The project includes `com.unity.pipeline` for live Editor automation and verification.
Bird behavior itself uses Unity physics and ordinary GameObjects.

This prototype has no bird models, wing animation, dedicated audio, feeding rules,
or waypoint routes. Pausing creature simulation stops steering, while its dynamic
body can still move under physics, consistent with the shared Creature lifecycle.
