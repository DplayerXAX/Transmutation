# Creatures

Every living thing the player can meet, carry or interact with derives from `Creature`.
Shaders for them are in `Assets/Shaders/Creatures`.

## Creature (base class)

`Creature.cs` holds only what all creatures share:

- **Identity:** `creatureName` (shown in prompts and the meditation journal).
- **Lifecycle:** subclasses override `InitializeCreature()` (instead of Awake) and `TickCreature(dt)`
  (called each frame while `simulationEnabled`). `SetSimulationEnabled` pauses the logic only.
- **Heat:** a shared float. `AddHeat`, `SetHeat`, `SpendHeat` (returns the amount actually removed),
  and the `OnHeatChanged()` hook.
- **Interaction:** `ReceiveInteraction(CreatureInteraction)` is called on left click.
  `InteractionPrompt` (verb, e.g. "Ignite") and `CarriedPrompt` feed the prompt HUD. Null = no prompt.
- **Carry:** `TryBeginCarry` / `PullTowards` / `EndCarry` let `PlayerCreatureCarrier` move the creature with
  forces without stopping its behaviour. A Rigidbody is added if the creature has none.
  Subclasses must move through `MoveCreature`, `SetCreatureHorizontalVelocity`, `FaceCreature` and
  `SetMovementPhysics`, which do nothing while carried, so carrying always wins.
- Carry Physics settings: `carryStrength`, `carryDamping`, `maximumCarryAcceleration`, `maximumCarryDistance`.

To add a creature: subclass `Creature`, implement `TickCreature`, and use the protected movement helpers.

## The fire cycle

```text
FireSource --heat--> FireFlower --blooms--> FireBubble --falls, lies still--> new FireFlower
                                                 \
                                                  FireEater eats a fallen bubble --digests--> FireFlower
```

| Script | What it does |
| --- | --- |
| `FireSource` | Stores Heat and radiates it each physics step to every creature inside its trigger sphere, less with distance. The trigger radius follows the remaining Heat. |
| `FireFlower` | States: Closed, Heating, Blooming, Recovering. Heat spreads its linked `DensityShapeOverlay`s outward and grows its body cylinder. At `bloomHeatThreshold` (70) it blooms, spawns one `FireBubble` per shape (sharing leftover Heat), then pulls the shapes back in. |
| `FireBubble` | Floats on one heading with sway, then falls (lifetime or `fallStartWorldY`). After lying still for a delay it turns into a `fireFlowerPrefab`. Its shader flow speed follows Heat. |
| `FireEater` | Wanders on the ground (aligns to the surface normal, slides along walls, unsticks itself). Seeks fallen bubbles within `detectionRadius`, flees the player inside `playerAwarenessRadius` unless eating, digests, then plants a Fire Flower. |

The flower's shape comes from `MarchingCubes/DensityShapeOverlay` objects injecting density into a
`MarchingCubesVolume` (see `Scripts/MarchingCubes/README.md`).

## Other creatures

**FuzzCreature** — a sticky furry blob. Inside `noticeRadius` (6 m) it joins a single-file chain behind the
player (static `Chain` list); each one follows the one in front, joined by a sagging goo strand. Leaves the
chain past `loseRadius`. Body is a procedural lumpy mesh; fur is stacked shells drawn by the `StickyFur`
shader (`furShells`).

**TentacleCreature** — an abstract octopus: primitive bones wrapped in a translucent membrane rebuilt every
frame. Tentacles reach for and stick to nearby surfaces and step when stretched. It wanders and floats on
them. **While carried**, if at least `minGripsToClimb` tentacles hold a surface, the player can climb:
**Space** up, **Ctrl** down (shown through `CarriedPrompt`).

**CaveHarvestItem** (in `Scripts/World`) — fruit and flowers. Not alive; uses `Creature` only so pickup,
prompts and carry work.

## Links to other systems

- **Player:** carried by `PlayerCreatureCarrier`, clicked through `PlayerCreatureInteractor`, named by
  `InteractionPromptHUD`.
- **Meditation:** `CreatureJournal` records each creature type the first time the player is near it and
  saves a cast of its meshes.
- **Audio:** the `AudioEventBank`'s creature channels map a creature class name (e.g. `FuzzCreature`) to a
  music layer that fades in near it.

## Misc

`Scripts/Animation/SkinnedMeshAnimator` ping-pongs blend shapes on SkinnedMeshRenderers (for modelled creatures).
