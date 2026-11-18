# Creature slope footing and corpse friction

Status: source/prefab audit and implementation plan, 2026-11-17. No runtime behavior changed and no play-mode reproduction performed in this audit. Reported Asura sliding is consistent with the code; other creatures below are risk classifications, not measured failures.

## Intended behavior

- Living walkers hold position on walkable ground when idle, and move uphill, downhill, or across it at their intended surface speed.
- Walkers cannot propel themselves up slopes beyond their configured limit. They may slide or fall when support is too steep or lost.
- Dead bodies have no active footing. Ordinary contact friction lets block-shaped remains settle on gentle inclines, while steeper ground and impulses can still move them.
- Rolling, gliding, flying, climbing, and structural creatures retain their own movement rules. Shape alone does not select a locomotion policy.
- Carrying, capture, throws, knockback, jumps, and disabled simulation retain movement ownership.

## Audit findings

| Creature/system | Source evidence | Assessment and action |
| --- | --- | --- |
| Melee, Ranged, General Asura | `AsuraMotor.FixedUpdate` uses a short center ray returning only a grounded boolean. It applies bounded world-horizontal acceleration; there is no surface normal, gravity compensation, or maximum walk angle. All four Asura prefabs reference `Materials/Creatures/Asura/Locomotion.physicMaterial`, with zero static/dynamic friction and Minimum combine. | Highest-priority fix. Horizontal braking does not fully control velocity along an incline. The short center ray can also miss support beneath a box resting on its uphill edge. |
| Rolling Asura | Shares that motor/material; its sphere collider is authored by `AsuraFeelSetup`. Rolling animation comes from traveled distance, and the prefab freezes X/Z rotation. | Explicit policy required. Preserve its existing attack movement; do not accidentally introduce physical rolling or walker-style corpse locking. Test separately. |
| Squirrel | `SquirrelMotor.Awake` installs zero friction with Minimum combine. `FixedUpdate` steers X/Z while retaining Y. Ground/ahead checks do not impose a normal walking slope limit; the recovery search has a separate normal threshold. Original material is restored only on destruction. | Strong candidate for the same defect, including slippery remains. Share the footing calculations while retaining hops, navigation, and territory rules. |
| Fire Snail, Cistern Snail | `Crawl` calls `Creature.SetCreatureHorizontalVelocity`; the helper writes X/Z and retains Y. Behavior runs from the creature update loop. | Slope movement and idle-drift risk; reproduce before migration. Move ground control to the physics tick if adopting shared footing. |
| Fire Eater | Samples normals, accepts ground up to `maxGroundAngle` (55 degrees), projects motion, and writes surface-planar velocity. | Already partially slope-aware. Verify idle drift, real support distance, steep-ground fallback, and physics timing before changing it. |
| Chameleon and passive dynamic creatures | Chameleon has a dynamic box with no explicit physics material and no footing motor. | Test passive contact friction independently; do not assume every resting creature needs an autonomous motor. |
| Bounce Shroom | `SetGrip` switches between compact grip and platform materials. Death disables bouncing but does not explicitly choose a corpse material. | Existing grip precedent; check death while expanded versus compact. Preserve platform behavior. |
| Skate Gecko / ceiling crawlers | Gecko deliberately uses very low friction; `CeilingBatBugSurfaceMotor` controls adhesion and gravity. | Specialized exceptions; do not globally replace their living materials or gravity rules. |
| Fuzz, Furnace, Compost, Tentacle, Ore Colossus | Their living bodies use kinematic/custom movement. Ore Colossus already has foot/ramp constraints. | Not the same dynamic sliding mechanism while alive. Check dynamic death transitions where applicable, preserving structural exceptions. |

`Creature.ApplyCorpsePhysics` enables dynamics/gravity and releases frozen position, but does not select a corpse contact material. `AsuraMotor.OnDied` adds linear damping (1.2), which is air/body drag rather than static contact friction. Squirrel likewise retains its frictionless runtime material after death.

Project gravity is `(0, -30, 0)`. Increasing all collider friction is not a sufficient locomotion fix: `SquirrelMotor` explicitly documents that default friction previously canceled its ground acceleration. A box can still slide when its contact material is frictionless.

The player reference is `SmoothFirstPersonController`: default maximum slope 40 degrees, motion projected onto the slope, idle braking, and slope gravity handling. Reuse those behavior principles without importing player-specific jump, skate, or ride code.

## Implementation sequence

1. **Establish a measured baseline.** Add a dedicated slope validation scene/probe following existing creature validation conventions. Instantiate real prefabs, not simplified substitute bodies. Record support detection, tangential drift, travel speed, impulses, and death transitions. First cover the three walking Asuras, Rolling Asura, and Squirrel; then both snails and the exceptions above.

2. **Introduce shared ground-support calculations, initially used by Asura.** Return support collider, contact point, normal, distance, support velocity, and walkable/steep classification. Use contacts plus a short footprint-aware cast/probe fallback; filter self colliders, triggers, and unsupported bodies. Query the object's own physics scene. Avoid accepting a wall normal or distant floor as standing support. Use a configurable 40-degree walking default, with a small hysteresis near the limit. Retain per-species overrides.

3. **Control movement in the support plane.** Project and normalize requested movement onto valid ground, preserving requested speed. Brake relative tangential velocity toward zero when idle; accelerate toward intent when walking. Compensate gravity along the support plane only while valid living footing owns motion. Retain normal gravity/contact pressure, and do not cancel separating jump/launch velocity. Use bounded acceleration and deceleration rather than position locking. On steep ground, stop active footing and reject uphill propulsion; allow physical sliding. Add an ahead-support check so steering does not repeatedly request an impossible climb.

4. **Respect external movement and lifecycle.** Integrate with `CreatureImpulse` and Asura's existing `AcceptPush` grace period. Suspend footing during a deliberate impulse/throw and until valid landing after upward separation, even if a ground probe still reaches the floor. Extend equivalent yielding to any migrated motor. Skip active support for dead, carried, captured (including web capture), consumed, kinematic, or simulation-disabled creatures. Reacquire support after release. Do not let a second component also write the same body's velocity or gravity.

5. **Give remains explicit passive friction.** Add opt-in contact profiles for ordinary walkers/passive bodies and separate rolling/gliding exceptions. Suggested starting values for ordinary remains: static friction 0.5, dynamic friction 0.3, zero bounce; tune on actual terrain, not as a universal default. Validate combine behavior against both terrain and creature materials. Switch owned solid body colliders on death, including relevant child colliders; preserve triggers and special gameplay surfaces. Avoid mutating shared material assets at runtime. Handle material restoration, runtime material cleanup, and initially dead remains without replaying death events. Keep `KeepCorpseStationary` behavior intact. Do not use global damping or FreezePosition as slope friction.

6. **Expand after Asura validation.** Migrate Squirrel while preserving hops, recovery, and ledge avoidance; then reproduce and migrate snail behavior as needed. Give Fire Eater and passive creatures targeted changes only if their tests fail. Update prefab authoring/setup scripts alongside prefab settings so regeneration preserves the fix.

7. **Align terrain navigation with motor limits.** The current in-progress `AsuraTerritorySetup` accepts sample normals down to 0.72 and link samples down to 0.65 (approximately 44 and 49 degrees). Those exceed a 40-degree walking default. Use the same walkability policy for sampling, links, and runtime steering, and validate actual traversable segments. Coordinate this change with the existing uncommitted territory work; rebake maps only after the policy is chosen.

## Validation and acceptance

- Ramps at 0, 10, 20, 30, 39, 40, 41, 50, and 60 degrees; test idle, ascent, descent, and cross-slope travel. Include smooth ramps and real Basilt terrain, seams, convex crests, partial box support, and narrow ledges.
- Provisional living-footing target: less than 0.05 m tangential drift over 10 seconds after settling on clearly walkable ramps; steady surface speed within 10% of requested speed after acceleration. Test the threshold/hysteresis band separately.
- Above the walking limit, no sustained powered ascent or wall adhesion. Leaving an edge causes a fall. A downward probe reaching a lower floor must not produce hovering.
- Kill creatures while idle and moving on ramps: no active hold after death, gentle-slope settling with ordinary friction, and visible sliding on a sufficiently steep low-friction test ramp. Do not require every corpse to slide at exactly the living slope limit.
- Verify throws, combat knockback, explosions, hops, moving supports, carry/capture/release, death during capture, simulation toggles, and local/reflection physics scenes. No sudden impulse erasure, renewed adhesion in flight, or stale gravity/material state.
- Repeat at multiple render rates with the same fixed timestep; also exercise supported fixed-timestep settings. Check allocation stability and representative creature counts.
- Run existing Asura combat/feel, Squirrel physics, creature impact, control, and lifecycle validations after relevant implementation changes. Inspect a mixed group on Basilt hills visually before considering the issue resolved.

Deliver the first implementation as shared support calculations plus walking Asura integration and explicit corpse material behavior, with before/after slope measurements. Broaden to other confirmed cases afterward; avoid a blanket physics-material change across all creatures.
