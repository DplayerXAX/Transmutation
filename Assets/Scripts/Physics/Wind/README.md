# Wind

Shared wind physics that any creature, the player and loose props respond to. A `WindArea` is a box with a flow direction; everything inside gets velocity along it. A **wind wall** is the simplest use: a thin, strong area that works like an obstacle.

## The rule

- An area flows along its local **+Z** (the blue arrow / gizmo arrows) at `Speed` m/s, with `Grip` (1/s) setting how fast bodies take the wind on.
- **Things that move themselves** (player, creature motors) move at **own velocity + wind**. Only the part along the wind changes; the part across it is untouched. Moving against the wind, the result is `ownSpeedAgainst − windSpeed × (1 − resistance)`:
  - slower than the wind: pushed back (a wall stops you at its edge);
  - faster than the wind: you get through, slowed;
  - slow down partway through: you're pushed back out.
- **Passive things** (props, sleeping creatures, corpses, thrown creatures in flight) have their along-wind speed pulled up to the wind speed. Something already faster in the wind's direction is never braked.
- Overlapping areas add together. After leaving an area, the drift dies away at that area's grip, so a gust leaves a little momentum.
- `EdgeSoftness` fades the wind in over that many metres inside each face. **Sink an area's bottom face below the ground.** Otherwise things sitting on the floor fall inside the fade and get less wind.

### Choosing a wall speed

The player (PlayerObjects prefab) walks at **5.25 m/s** and sprints at **7.5 m/s** (walk 7 / sprint 10 × `moveSpeedScale` 0.75). The default wall speed is **6.5**: it holds back walking and lets sprinting through. Blasts, flings and throws that are fast enough also get through.

## Resistance (0 = carried fully, 1 = immune)

| Who | Where |
| --- | --- |
| Every creature | `Creature` → **Wind / Wind Resistance** (species may override the virtual `WindResistance`) |
| Player | `PlayerCreatureStats` → **Wind Resistance** (an effect can set `WindResistance` at runtime) |
| Loose props | Add `WindResistance` (no component = 0) |

All three implement `IWindResistant`.

## Who owns the movement

| State | Wind |
| --- | --- |
| Carried, bubble-captured, web-captured, cradled by a bone child, frozen in reflection | None (`MovementFrameMember.IsExternallyMoved` plus cradle/freeze checks) |
| Thrown flight | `CreatureThrower` bends the flight with the passive rule |
| Creature motor using `SetCreatureVelocity` / `SetLocomotionVelocity` / `SetCreatureHorizontalVelocity` / `MoveCreature` | The base `Creature` adds `WindOffset` to what the motor writes |
| No motor write in the last 0.15 s (sleeping, dead, props) | `WindBody` pulls the Rigidbody's velocity directly |
| Player | `SmoothFirstPersonController` strips the wind before its own movement code and adds it back afterwards. Steering, braking, speed limits and jumps only see the player's own motion. No wind while climbing, wall-running, restricted, frozen, chainsaw-riding, bone-riding or bubble-locked. |
| Unmaterialised sea angels, live kinematic tentacles, ridden skate geckos, boomerang/projectile flights | Immune for now |

## Scripts (`Assets/Scripts/Physics/Wind/`)

- `WindArea`: the box. It sits in a static `Active` list, finds rigidbodies with a non-allocating overlap each physics step and gives them a `WindBody`. It draws gizmos (box plus flow arrows) and builds the play-mode visuals.
- `WindField`: the shared rule (`Target`, `StepOffset`, `PullPassive`). New consumers should use these.
- `WindBody`: per-body state, added automatically. It switches itself off once the body is out of every area and its drift has died down, so there's no idle cost.
- `IWindResistant`, `WindResistance`: resistance contract and the prop component.
- `WindVisuals`: play-mode particles built from code.

## Look

`Assets/Shaders/Effects/Resources/WindStreak.shader` (`Capstone/Wind/Streak`):

- **Streaks**: stretched particles born on the upwind face. Pointed heads lead, with ink-outlined paper-cut bodies, wobbly tails that hook and break into crawling dots, and a faint rainbow fringe. Density scales with face area × speed, so a wall reads as a curtain and a breeze as a few wisps.
- **Motes**: hollow notched seeds tumbling slower than the wind.

Colours are on the area (`Streak Color`, `Ink Color`), or assign your own `Streak Material`. Turn off `Show Visuals` for an invisible wind.

## Setup

Drop `Assets/Prefabs/Wind/WindWall.prefab` into a scene (6 × 4 × 1.5 m, speed 6.5, grip 12). Rotate it so the blue arrow points where the wind blows. Resize with **Size**. Lower **Speed** and **Grip** for a soft breeze.

## Verification

- **Tools > Capstone > Wind > Run Wind Validation (Empty Scene)** runs 10 scripted-physics checks in an empty, unsaved scene, then reopens your scene. Results go to the console and `Logs/WindValidation.txt`. They cover: walking held at the wall, sprinting through, being pushed out from inside, full sideways speed while crossing, player full resistance, a passive box reaching breeze speed, a 50% resistant box getting half, and `WindBody` switching off when out of wind.
- Look: in Play mode, drop a WindWall in front of the camera and check that the streaks lead with their points.

## Known limits

- Box areas only.
- Species that write `Rigidbody.linearVelocity` directly every frame instead of using the `Creature` velocity helpers get only the passive pull, which their motor overwrites. Route them through `SetLocomotionVelocity` to feel the wind.
- Body-less creatures (rooted flowers) are never found by the overlap and are effectively immune.
- Creature-motor and thrown-flight wind isn't covered by the automated check yet; test it in a scene.
