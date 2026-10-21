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

## Food, lifespan, and bones

**Hunt Radius** (25 metres) finds the nearest compatible active creature in the same
scene. **Hunt Desire** is the maximum food attraction weight (existing prefab tuning is
preserved), in the same steering sum as separation,
alignment, cohesion, wandering, and bounds return. It is not a separate movement mode:
all weights remain active and the combined acceleration/speed limits still apply.
Food outside the sky bounds remains eligible and competes with inward steering. The
registry follows creature enable/disable/unload and snapshots food positions once per
physics step; no per-bird scene searches or allocating overlap queries are used.

Set **Food Tags > Plant** on a food creature, including a harvested item if desired.
These are the shared Creature enum flags, not Unity GameObject tags. The bird prefab's
**Diet Tags** accept Plant. Multiple birds can share food. On physical or trigger contact,
a freely flying bird with sufficient Hunger and positive Hunt Desire consumes compatible food. Desire zero
disables seeking and eating. Carrying, either capture type, simulation pause, or death
prevents autonomous eating. A food collider is needed for contact consumption.

Every creature inherits **Age** (remaining lifetime, default 40) and **Age Rate**
(spent per second, default 0). Simulation pause also pauses aging. Carrying alone
does not pause Age. At zero `Die()` runs once. Base death disables simulation, retains
the original body, and enables passive corpse physics; a release cannot revive it.
After shared death and its notifications, lifespan expiry alone calls `OnAgeDeath()`.
Birds use that hook to leave bones. Calling `Die()` directly does not invoke age-specific effects.

`Consume(consumer)` handles optional nutrition/special effects and does nothing by
default. It does not call death. The feeding caller uses `TryConsumeFood(food)` to claim
the target, run Consume, separately run Die, and remove the remaining original.
Deferred destruction cannot repeat feeding. Nutrition does not restore Age.

### Hunger and shared meals

Each bird starts with **Hunger 50** on a 0–100 scale: 0 is full and 100 is very hungry.
**Hunger Rate** defaults to 0.25 per second while simulation runs. Carrying still uses
nutrition; web/bubble capture, simulation pause, disabling and death stop hunger growth.
Maximum hunger does not cause death or alter Age.

At Hunger below **Appetite Threshold** (20), birds ignore food and cannot eat it. At or
above the threshold, **EffectiveHuntDesire = Hunt Desire × Hunger / 100**. Both food
selection and the weighted flight steering use this effective value; manually tuned
Hunt Desire remains the maximum. Full birds stay in the flock without competing for food.

Every `Creature` exposes **Nutrition**, default 120, independently of food/diet tags.
Edit this on each food prefab to tune its meal value. One successful bird meal shares
this fixed budget among the eater and compatible hungry flying birds in the same scene
within **Nutrition Sharing Radius** (10 metres) of the food. Recipients may have different
targets or spawn areas and may be below the appetite threshold. Carried, captured, paused,
disabled, dead, consumed, and full birds do not receive shares. The actual eater is included
even when a large food's origin is outside the peer radius.

Shares are equal, capped at each recipient's Hunger. Unused portions are redistributed
until the remaining birds receive equal portions or all are full; leftover nutrition is
discarded. A 120-point meal reduces Hunger by 20 each for 6 birds or 5 each for 24 birds
when they need that much. At 0.25 Hunger/second, 24 birds need roughly three such meals
per minute to maintain their collective hunger, ignoring wasted surplus.

Meal nutrition, position, tags, type and scene are cached before food callbacks. Nutrition
is awarded only after the shared feeding transaction succeeds; `Consume()` overrides
need not call base. Only nutrition is shared: a Corrupt Seed infects the actual eater,
while its nearby flock mates receive nutrition without infection. Recipient selection
uses the manager's existing bird list and current Rigidbody positions, not scene searches
or the previous flight octree snapshot. Allocation buffers are reused.

Bird `Die()` removes flock membership, stops flight, and releases hand, web, and bubble
attachments through the shared lifecycle. Direct death (including Inspector Kill) retains the
original active, visible, collidable body with passive gravity, food tags, and Nutrition.
Consumption runs shared death then removes that original, without leaving extra bones.
Only Age expiry replaces the bird with one **BirdBones** after death notifications, preserving
its current scene, pose, linear velocity, and angular velocity. Configure **Bones Prefab** on
the bird; an empty reference creates automatic placeholder bones. Original colliders and food
registration retire immediately. Repeated death calls do not create extra remains.
Independently placed **BirdBones** initialize as dead Matter remains (Age 0, simulation off),
without firing another death event. They remain portable and edible by Matter eaters.
Web release gives the retained corpse the normal half-second recapture grace period.

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
- Tune Hunt Radius/Hunt Desire alongside other weights on the bird prefab. The selected
  bird gizmo shows its food detection radius and current target. Create Bird Flock Assets
  also creates `Assets/Prefabs/Creatures/BirdBones.prefab` and its shared URP material;
  bones can be spawned independently.

## Validation

`Tools > Creatures > Validate Bird Octree` compares exact searches with exhaustive
searches, including coincident points, empty inputs, radius boundaries, and distant
outliers. `Validate Bird Runtime (Play Mode)` uses a temporary local physics scene to
check automatic registration, lifecycle, pause, real carrier handoffs, release recovery,
scene transfers, and 72/200-bird populations. Reports are written under
`Temp/BirdFlockValidation` and never overwrite authored scenes.

`Validate Bird Web Capture (Play Mode)` verifies actual silk-contact capture, excludes
trapped birds even if their simulation is manually reenabled, and exercises pickup,
release, and resumed flight with firm carrying. The runtime checks also
verify spawn speed overrides, initial velocities, and inheritance from the prefab.

`Validate Bird Life and Food (Play Mode)` checks countdown/pause/death, separate Consume
effects, weighted food steering, actual contact feeding, simultaneous eaters, retained
bird corpse physics, age-only bones with prefab/fallback, notification order, dead bones,
and free/held/web/bubble/consumed death paths.
The trapping bubble's elapsed timer is separate from inherited remaining Age.

`Validate Bird Hunger and Sharing (Play Mode)` checks hunger clocks and appetite,
solo/6/24-bird meals, caps and redistribution, exact sharing boundaries, state/diet/scene
exclusions, rejected or duplicate meals, callback mutation/removal, and seed infection.
This check only runs when explicitly invoked and does not edit prefab or scene assets.

Profiler markers: `BirdFlock.Simulation` and `BirdFlock.NeighborQueries`.
`CandidateChecks` reports actual point distance tests versus an exhaustive N² search.
Pooled storage grows when a larger population or tree is first encountered; warmed
simulation reuses storage. Dense flocks can still approach N² neighbor work when
every bird is inside the search radius. The octree is an optimization, not a promise
that 200 tightly packed birds cost less than 72 widely spaced ones.

Runtime reports include 72/200-bird timings and managed allocations both without food
and with 12 Plant targets. Timings exclude physics and rendering; the food microbenchmark
holds positions fixed. Octree validation separately compares searches with exhaustive
scans. At these small counts tree overhead can exceed the saved comparisons; reports
include both timings rather than assuming spatial partitioning is always faster.

The project includes `com.unity.pipeline` for live Editor automation and verification.
Bird behavior itself uses Unity physics and ordinary GameObjects.

This prototype has no bird models, wing animation, dedicated audio, starvation death,
or waypoint routes. Pausing creature simulation stops steering, while its dynamic
body can still move under physics, consistent with the shared Creature lifecycle.
