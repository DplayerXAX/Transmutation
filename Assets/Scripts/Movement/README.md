# Player: Movement, Camera, Climbing, Carry

Everything the player does with their body. All scripts sit on the **Player** object (the one with the
Rigidbody), except `InteractionPromptHUD` (in `Scripts/UI`), which can live anywhere once per scene.

For the exact hierarchy and reference wiring, see [QUICK_SETUP.md](QUICK_SETUP.md).

## Components

| Script | Job |
| --- | --- |
| `SmoothFirstPersonController` | Walk, sprint, jump, crouch, slide, mouse look. Owns the camera. |
| `AdvancedFirstPersonTraversal` | Optional wall run, simple wall climb, ledge grab. |
| `HandClimber` | Hand-over-hand climbing on any steep surface, including pull-up over the top. |
| `FirstPersonBody` | Procedural arms, hands and legs (primitives + two-bone IK). |
| `CameraWallGuard` | Keeps the camera out of walls and ceilings. |
| `PlayerCreatureCarrier` | Picks up and carries a `Creature`. |
| `PlayerCreatureInteractor` | Left-click interaction with a `Creature`. |
| `UI/InteractionPromptHUD` | Control hints under the crosshair. |

## How they connect

```text
SmoothFirstPersonController  <- state flags set by ->  AdvancedFirstPersonTraversal, HandClimber
        |                                              (SetClimbing / SetRestricted / SetFreeze)
        v
FirstPersonBody  reads controller (walk), carrier (held creature), HandClimber (hand/foot grips)
        |
        v
Audio/Footsteps  reads FirstPersonBody.WalkPhase

InteractionPromptHUD  reads carrier, interactor, traversal, HandClimber to pick one hint
```

- Most references are found automatically (`FindFirstObjectByType`) when left empty.
- `MeditationController` pauses all of these while the player meditates.

## SmoothFirstPersonController

A port of the FullMovement asset (PlayerMovementAdvanced + PlayerCam + MoveCamera + Sliding) to the new
Input System.

- **Feature toggles:** Allow Crouch, Allow Double Jump, Allow Ungrounded Jump, Allow Sliding,
  Enable Camera Fov And Tilt.
- **Speed:** `walkSpeed` 7, `sprintSpeed` 10, `moveSpeedScale` 0.75 scales walk/sprint/crouch/air together.
- **Ground check:** a thin BoxCast down from the feet against `whatIsGround`. Keep the Player layer out of it.
- **Slopes:** `maxSlopeAngle` 40. Steeper ground is not walkable (and becomes climbable, see below).
- **Camera:** raw mouse delta, `sensitivity` 0.12. Look limits: down 60, up 85, down 25 while climbing.
- **References (required):** Orientation, CameraPos, CameraHolder, Player Camera.
- **Public API used by others:** `Grounded`, `Climbing`, `SetClimbing`, `SetRestricted`, `SetFreeze`,
  `Orientation`.

Controls: **WASD** move, **Shift** sprint, **Space** jump, **Ctrl** crouch / slide (if enabled).

## AdvancedFirstPersonTraversal (optional)

FullMovement's WallRunningAdvanced + Climbing + LedgeGrabbing merged into one component.
Each part has its own toggle: Allow Wall Running, Allow Wall Climbing, Allow Ledge Grabbing.

- `minWallSteepness` tells walls from floors when both share one layer (the generated terrain does).
- Wall run: hold **W** beside a wall in the air, **Shift/Ctrl** run up/down, **Space** wall jump.
- Ledge: **Space** jumps off, **WASD** lets go.

## HandClimber

Physical-feeling climbing. Each hand grabs a real point on the surface; the body is pulled to hang below
the hands, feet step on real footholds. Blocked hands reach around bumps and overhangs. At the top both
hands plant on the edge and the player is pulled over.

- **Starts** when facing a surface steeper than `minWallSteepness` (55) on `climbLayers` (Ground + layer 8)
  and pressing **W**. From the ground it only starts on walls taller than a step.
- **Ends** on a ledge flatter than `maxStandSlope` (36, keep it below the controller's Max Slope Angle),
  when the hands lose the wall, or on **Space** (push off).
- Tuning groups: Detection, Body (`wallOffset`, `pullSpeed`), Reaching (`reachLength`, `reachTime`,
  `climbSpeed`), Over The Top (`pullUpTime`), Feel (camera lag, tilt, look-at-hands).
- `lastEvent` (read-only) shows why the climb last changed. `drawDebug` draws the stand-spot search.
- Exposes hand/foot grips and a climb frame (`GetHand`, `GetFoot`, `GetClimbFrame`) for `FirstPersonBody`.

Controls while climbing: **W/S** up/down, **A/D** sideways, **Shift** faster, **Space** push off.

`Dev/ClimbTestDriver` can climb cliffs and the Hanging Spire automatically (editor started with `-climbTest`).

## FirstPersonBody

Builds arms (with fingers) and legs from primitives at runtime and poses them with analytic two-bone IK.

- Arms hang from the camera; while climbing they switch to the body frame from `HandClimber`.
- Hands: idle sway, right hand holds a carried creature, quick reach on interact, finger-by-finger grips.
- Legs hang from the body and walk with a stride cycle. `WalkPhase` is public; footsteps read it.
- Settings: limb/joint materials, thicknesses, `showLegs`, arm and leg lengths, `handFollow`.

## CameraWallGuard

Each frame, sphere-casts from a safe point inside the capsule to the camera and pulls the camera in front
of any hit, then eases back out (`releaseSpeed`). Also sets a short near clip (0.05).
Creatures, carried things and loose bodies are ignored. Added by
**Tools > Capstone > Apply World Look And Camera Guard To Open Scene**.

## Carry and interaction

**PlayerCreatureCarrier**

- `carryInput`: **Hold Right Mouse** (default) or **Toggle Key** (`E` by default).
- Finds a `Creature` by a ray from the camera within `pickupRange` (5 m). Creatures larger than
  `maxCarrySize` (2.2 m) can't be picked up.
- The creature keeps simulating while held. It is pulled to a carry point by a spring
  (`Creature.PullTowards`), or locked to it with `firmGrip`. Big creatures are kept below and right of the
  crosshair so they don't block the view.
- Exposes `PlayerBody` and `PlayerController` so a held creature can move the player
  (the `TentacleCreature` climb assist uses this).

**PlayerCreatureInteractor**

Left click raycasts from screen centre (`interactionRange` 5 m) and calls `Creature.ReceiveInteraction`.

**InteractionPromptHUD** (`Scripts/UI`)

Builds its own overlay canvas. Shows one prompt, in this priority order: carried creature (plus its
`CarriedPrompt`), hand climbing, ledge / wall run / climb jump, creature in view (carry and/or its
`InteractionPrompt`), then "can climb" / "can wall run". All hint text is editable in the inspector.
