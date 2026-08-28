# FullMovement-faithful controller — setup

This version ports FullMovement's six active movement scripts into two Capstone components:

- `SmoothFirstPersonController`: PlayerMovementAdvanced + PlayerCam + Sliding
- `AdvancedFirstPersonTraversal`: WallRunningAdvanced + Climbing + LedgeGrabbing

The forces, drag, state order, speed limits, slope handling, jump timing, scale-based crouch/slide,
wall checks, timers and default tuning values come from FullMovement.

## Hierarchy

```text
PlayerWhole                    Empty common parent
├── Player                    Rigidbody, CapsuleCollider, SmoothFirstPersonController
│   ├── Orientation           Empty Transform
│   └── CameraPos             Empty Transform at eye position
└── CameraHolder              Empty Transform; no Rigidbody
    └── PlayerCamera          Camera and AudioListener
```

Do not add a separate look script. Camera look is now merged into `SmoothFirstPersonController`.
If the old `SmoothFirstPersonLook` component is still on PlayerCamera, remove that missing/old
component. The merged controller also performs the original `MoveCamera` behavior by copying
CameraPos's position to CameraHolder every Update.

## Core controller references

On `SmoothFirstPersonController`, assign:

- `Orientation` → the empty Orientation object
- `Camera Position` → the CameraPos child on Player
- `Camera Holder` → the CameraHolder sibling of Player
- `Player Camera` → the Camera component on PlayerCamera
- `What Is Ground` → the layers that count as ground

Put Player and its children on the Player layer. Do not include Player in `What Is Ground`.

Core controls match FullMovement:

- WASD: move
- Left Shift: sprint
- Space: jump
- Left Ctrl: stationary crouch or moving slide, when their toggles are enabled

The camera uses raw new-Input-System mouse delta with no smoothing.

## Feature toggles

On `SmoothFirstPersonController`:

- `Allow Crouch`
- `Allow Double Jump`
- `Allow Sliding`
- `Enable Camera Fov And Tilt`

On `AdvancedFirstPersonTraversal`:

- `Allow Wall Running`
- `Allow Wall Climbing`
- `Allow Ledge Grabbing`

## Advanced controller setup

Add `AdvancedFirstPersonTraversal` to Player only if an advanced feature is needed. Assign:

- `Orientation` → Orientation
- `Camera Transform` → the actual PlayerCamera Transform
- `What Is Wall` → wall-running/climbing layers
- `What Is Ground` → ground layers
- `What Is Ledge` → ledge-grabbable layers

Wall-run controls match FullMovement: hold W beside a wall while above the ground, Left Shift moves
up the wall, Left Ctrl moves down, and Space wall-jumps. Wall climbing activates while airborne,
facing a wall and holding W. Ledge grabbing uses the original forward sphere cast; movement input
releases the ledge after its minimum hold time, and Space performs the ledge jump.

## Required differences from FullMovement

1. Capstone uses the new Input System. Configurable `Key` fields and `Keyboard.current` replace
   `KeyCode` and `Input.GetKey`; `Mouse.current.delta` replaces legacy mouse axes.
2. New Input System mouse delta is already per-frame, so it is not multiplied by `deltaTime`.
   Sensitivity therefore defaults to `0.12`, not FullMovement's legacy value of `400`.
3. Unity 6 names Rigidbody's old `velocity` and `drag` properties `linearVelocity` and
   `linearDamping`; their purpose is unchanged.
4. Six components were merged into two. The merged advanced routines run internally in the same
   order as the source prefab's components: climbing, wall running, then ledge grabbing.
5. FullMovement's `StopAllCoroutines` could be local to PlayerMovementAdvanced. After merging,
   the speed coroutine is stopped by its own handle so it cannot cancel DOTween-related behavior.
6. Feature toggles add only enable/disable gates and cleanup for a feature that is turned off.
7. Dash, vault, attack, debug UI, jump VFX and the duplicate `Scripts/Ignore` implementations are
   intentionally excluded as requested.
