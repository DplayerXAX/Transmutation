# Bubble Flower prototype

Use **Tools > Creatures > Add Bubble Flower To Scene Yoyo** outside Play Mode. This creates the two
prefabs and materials if missing and places one flower near the player without duplicating an existing
root flower. Existing prefab settings are preserved. Geometry uses Unity cubes and spheres.

Pick up Bubble Flower with the existing pickup control. **Left-click shoots** while held, including
when looking at another interactive creature. The same priority lets a held BounceShroom expand and
place itself. Creatures that do not opt into `CanUseWhileCarried` keep normal cursor interactions.

On the ground, flowers emit every **3–6 seconds**. Bubbles rise along Unity's **Y axis** at **0.5 m/s**
for **12–20 seconds**, then hover. They expire **45–60 seconds after spawning**. Manual shots start
at **8 m/s**, with exponential launch decay of **0.35 seconds**. Horizontal sway is small.

An empty bubble captures one eligible creature or player on contact. Bounds are measured from enabled
model renderers, excluding effects and nested creatures, with solid colliders as the fallback. Player
size uses its capsule. Models larger than **4 m on their largest dimension** are rejected. The fitted
sphere encloses the bounds plus **0.1 m** padding; that sphere can be wider than 4 m. No size property
is added to `Creature`. The occupant is not scaled or parented under the bubble.

`BubbleCapture` owns the occupant's movement, pauses creature simulation, and records/restores physics
and collider states. It releases silk before taking ownership. Captured occupants cannot be carried or
recaptured. Carried creatures, trapping bubbles, silk, the firing flower, and a briefly protected shooter
are excluded. The player's held creature stays usable while the player is trapped.

While trapped, **Space escapes** and launches the player approximately **4 m upward**. Camera look,
pickup/drop, and interaction remain available; walking, ordinary jumping, crouching and traversal are
blocked. Automatic and external popping release without a launch. Release gives **0.75 seconds** of
capture immunity. Bubble lifetime is not reset by capture.

Every bubble has **Require Explicit Interaction To Pop**, disabled by default:

| Event | Disabled | Enabled |
|---|---|---|
| Another creature touches an occupied bubble | Pop | Remain trapped |
| Solid scenery contact | Pop | Physics stops against scenery |
| Left-click occupied bubble | Pop | Pop |
| Pickup attempt, empty or occupied | Pop | Pop |
| Space escape or timeout | Pop | Pop |

A deliberate `ReceiveInteraction` or `MeetCreature` pops an occupied bubble in either setting.
Expansion-generated contacts are ignored until they separate, preventing immediate popping after
capture. Unity's normal solid sphere/Rigidbody contact solver handles blocking, with continuous
collision detection for shots. Disable/destruction also releases the occupant and restores state.

Tune motion, lifetime, size limit, escape height, immunity and the toggle on
`Assets/Prefabs/Creatures/TrappingBubble.prefab`. Tune firing intervals, cooldown, aim distance, launch
speed and muzzle on `Assets/Prefabs/Creatures/BubbleFlower.prefab`.

## Validation

Run **Tools > Creatures > Validate Bubble Flower Assets** in Edit Mode, then
**Validate Bubble Flower Runtime (Play Mode)** in Play Mode. Runtime checks create an unsaved local
physics scene and write a report under `Temp/BubbleFlowerValidation`, without changing authored scenes.
Check the playable scene for firing, popping, the trapped HUD, Space escape, and both toggle settings.
