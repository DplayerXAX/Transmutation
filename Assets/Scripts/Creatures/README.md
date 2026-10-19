# Creatures

Interactive creatures and passive carryable items derive from `Creature`.
Shaders for them are in `Assets/Shaders/Creatures`.

The editable design reference is the **Creature Wiki** tab and its creature entries in
[Transmutation Design Docs](https://docs.google.com/document/d/17oOwAjg1onIAHYJW-9AEE0JaH0u00GqgwTtQK19nS0Y/edit?tab=t.2eknj3ryt44y).
This README describes the code contract; the wiki describes behavior and player-facing uses.

## Creature (base class)

`Creature.cs` holds only what all creatures share:

- **Identity:** `creatureName` (shown in prompts and the meditation journal).
- **Lifecycle:** subclasses override `InitializeCreature()` (instead of Awake) and `TickCreature(dt)`
  (called each frame while simulation runs). `SetSimulationEnabled` pauses logic and Age;
  it does not freeze the Rigidbody. Bird Hunger follows this simulation clock too.
- **Lifespan:** `Age` is remaining lifetime (default 40), spent at `Age Rate` per second
  (default 0) while simulation runs. At zero, `Die()` records death and disables logic.
  `IsDead` is the single serialized death state and prevents capture release from resuming a dead creature.
  `Die()` retains the original active, visible, collidable body, releases carry/web/bubble ownership,
  and gives mobile corpses passive gravity physics. Compost and Sky Web stay stationary.
  `Died` notifies independent components once, after ability cleanup; listener exceptions
  are isolated so normal death behavior continues. Only shared lifespan expiry then calls
  `OnAgeDeath()` for age-specific remains; birds use it to leave bones.
- **Consumption:** `Consume(consumer)` is an overridable effects hook with an empty base implementation.
  It does not call `Die()` or destroy anything. `TryConsumeFood(food)` checks diet and claims
  food once, calls its consumption hook, then separately calls death and removes the original.
- **Heat:** a shared float. `AddHeat`, `SetHeat`, `SpendHeat` (returns the amount actually removed),
  and the `OnHeatChanged()` hook.
- **Diet:** inherited food tags, accepted diet tags, and a same-type eating option.
  `CanEat(target)` checks shared dietary compatibility; individual creatures can add further conditions.
  See [CreatureDietREADME.md](CreatureDietREADME.md) for setup and examples.
- **Nutrition:** one food object's total meal value (default 120), independent of diet.
  Bird eaters split it among eligible nearby birds; their Hunger affects food attraction.
- **Interaction:** `ReceiveInteraction(CreatureInteraction)` is called on left click.
  `InteractionPrompt` (verb, e.g. "Ignite") and `CarriedPrompt` feed the prompt HUD. Null = no prompt.
  `CanUseWhileCarried` explicitly opts into held-use priority. That use consumes left-click even on cooldown.
  `PickupActionPrompt` / `ReceivePickupAttempt` allow a pickup action without beginning a carry.
- **Carry:** `TryBeginCarry` / `PullTowards` / `EndCarry` let `PlayerCreatureCarrier` move the creature with
  forces without stopping its behaviour. A Rigidbody is added if the creature has none.
  Subclasses use `MoveCreature`, `SetCreatureHorizontalVelocity`, full-3D `SetCreatureVelocity`,
  and `FaceCreature`; they yield movement to carrying or bubble capture. `SetMovementPhysics`
  updates the eventual release physics while a creature is held or bubble-captured.
- Carry Physics settings: `carryStrength`, `carryDamping`, `maximumCarryAcceleration`, `maximumCarryDistance`.

To add a creature: subclass `Creature`, implement `TickCreature`, and use the protected movement helpers.
If overriding `OnEnable` or `OnDisable`, call base so the active-creature food registry and
carry/capture cleanup still run. Hunger is currently bird-specific; Nutrition belongs to every creature.

## Food, nutrition, and death

| Setting | Default | Meaning |
| --- | --- | --- |
| Food Tags | None | What this creature provides as food: Meat, Plant, Matter, or a combination. |
| Diet Tags | None | Food categories this creature accepts. Everything also accepts untagged creatures. |
| Can Eat Same Type | Off | Allows compatible food with the same concrete script class; self-eating is always rejected. |
| Nutrition | 120 | Total value of this food object, independent of its tags. |
| Age | 40 | Remaining lifetime, rather than elapsed time. |
| Age Rate | 0 | Lifetime spent per simulated second; zero disables automatic ageing. |

`CanEat(target)` checks dietary compatibility only. It does not seek food, check contact,
or apply nutrition. A feeding behavior must check its own readiness and reach conditions.
`TryConsumeFood(food)` then claims the food once, runs `food.Consume(eater)`, separately
runs `food.Die()`, and destroys any surviving original. Failed and duplicate attempts do not
repeat the meal. Food's simulation may be paused or dead; the eater must be active and running.

Override `Consume(consumer)` for the food's special effects; the base hook is empty and does
not kill or destroy anything. Override `OnDeath()` to stop abilities and release owned occupants;
never destroy, deactivate, or replace the corpse there. Death sets Age to zero and disables simulation.
For remains specific to lifespan expiry, override `OnAgeDeath()`. The shared Age countdown
calls it once after `Die()` and all `Died` listeners; the default hook does nothing. Direct
`Die()` calls (including Inspector Kill) and consumption never run this hook, even though
all deaths set Age to zero. A listener consuming or removing the original skips this hook.
Birds leave the flock on every death. Age expiry replaces the bird with one Bird Bones object,
preserving pose and motion after attachment release; other deaths retain the original corpse.
Bird Bones initialize as dead remains without a death event. Consuming food removes its original
even when base death alone would leave that object in place.

`SetRemainingAge(value)` is an assembly-internal lifetime setter. It clamps at zero and
uses the same age-death path even during simulation pause; it cannot revive dead creatures
or mutate food already pending consumption. It contains no food selection or Age-transfer rules.

The inherited Inspector shows read-only **Is Dead** and **Simulation Running** values in Play Mode,
including mixed values for multiple selections. **Kill** calls normal `Die()` on active scene
creatures; it cannot revive them. The serialized flag survives script reloads and is not directly editable.
Simulation pause does not change `IsDead`, freeze physics, or determine pickup eligibility.
Eating, Goo application, composting, popping, and transformation own intentional removal separately.

Bird meals cache nutrition and food identity/position before callbacks, then share nutrition
only after accepted consumption. A food's `Consume()` override does not have to call base
to provide nutrition. Corrupt Seed infection applies only to its actual eater; nearby birds
receive nutrition without infection. Fire Eater retains its separate legacy feeding routine.

## Bird hunger and shared meals

| Bird setting | Default | Meaning |
| --- | --- | --- |
| Hunger | 50 | 0 is full; 100 is very hungry. |
| Hunger Rate | 0.25/s | Increases while simulation runs, including carrying. |
| Appetite Threshold | 20 | Below this, the bird does not seek or physically eat food. |
| Nutrition Sharing Radius | 10 m | Distance from the food within which eligible peers receive a share. |
| Hunt Desire | Existing tuning | Maximum food attraction weight; authoring and runtime overrides are preserved. |

At or above the appetite threshold, `EffectiveHuntDesire = HuntDesire * Hunger / 100`;
below it, the effective weight is zero. This scales only the food attraction contribution
alongside the other flock weights. Speed and acceleration remain capped. Hunger stops
at 100 without starvation death or any change to Age. Web/bubble capture, simulation pause,
disabling, and death stop hunger growth; carrying alone does not.

One accepted meal splits its fixed Nutrition budget among the eater and compatible hungry
flying birds within the sharing radius in the same scene. Peers may come from another
spawn area or be targeting another food. Birds below the appetite threshold may receive
shares while Hunger is positive. Full, carried, captured, paused, disabled, dead, or
already-consumed birds cannot receive shares. The actual eater is included even when a
large food's origin is outside the peer radius.

Shares are equal, capped at each bird's Hunger. Unused portions are redistributed among
birds still needing food; discard any remainder once all are full. A 120-point meal reduces
Hunger by 20 each for 6 hungry birds, or 5 each for 24. At the default rate, 24 birds need
roughly three such meals per minute to maintain collective hunger, excluding wasted surplus.
Sharing uses current Rigidbody positions and reusable buffers rather than the last octree snapshot.

Food attraction still needs to bring the bird into collider contact: Hunt Radius is detection
distance, not an automatic feeding radius. See [BirdFlockPrototype.md](BirdFlockPrototype.md)
for tuning and [CreatureDietREADME.md](CreatureDietREADME.md) for compatibility rules.

Explicit validation menus include **Validate Creature Diet**, **Validate Bird Life and Food
(Play Mode)**, **Validate Bird Hunger and Sharing (Play Mode)**, and the bird octree/runtime/web
checks. Play Mode checks create disposable test scenes only when invoked. Reports are written
under `Temp/BirdFlockValidation`.

**Validate Bird Life and Food (Play Mode)** also checks retained bodies, serialized death,
Inspector multi-selection, passive corpse physics, ability shutdown, occupant release, and
age-only bird bones (including automatic fallback, carried death, and notification order).
Goo, Compost, bubble, and infection validations cover intentional consumption/removal separately.

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

**Corrupt Seed** is passive Plant food that infects its consumer through a separate
component. Host death produces 3-5 seeds alongside normal remains, accumulating seed
and host food flags across generations. See [CorruptSeedREADME.md](CorruptSeedREADME.md).

**SkySpider (TimeSpider)** hunts living, positive-Age creatures captured by its current
`SkyWeb`. It keeps a target until invalidated, approaches using existing flight/patrol movement,
and drains all remaining Age at **Bite Range** (default 0.75 m from its Rigidbody position to
the prey's nearest solid collider surface). That Age is added to its own lifetime before the
victim's age-death callbacks. Its runtime Food Tag is Meat and Diet is Everything. This is
spider-specific Age feeding: no `Consume`, Nutrition, or Hunger sharing; ordinary corpses remain,
birds leave age-death bones, and existing infection death effects still run. Age Rate stays
unchanged (default zero). See [SkySpiderPrototype.md](SkySpiderPrototype.md) for eligibility,
interruption rules, and **Validate TimeSpider Age Hunting (Play Mode)**.

**BubbleFlower** — a stationary flower emitting trapping bubbles every 3–6 seconds. While held through
the existing carry system, left-click shoots toward the crosshair with a 0.5-second cooldown and stops
automatic emission. `TrappingBubble` is separate from `FireBubble`; neither changes the fire cycle.
See [BubbleFlowerPrototype.md](BubbleFlowerPrototype.md) for setup, controls, and tuning.

**BirdCreature** — carryable cube bird with 3D separation, alignment, cohesion, and wandering.
Birds register automatically with a scene-local `BirdFlockManager`, which creates a pooled
octree and snapshots all flight state before movement. Spawn areas only place initial
clusters; groups merge when they meet. See [BirdFlockPrototype.md](BirdFlockPrototype.md)
for setup, controls, and validation. Hunger scales food attraction; accepted meals share
nutrition with eligible nearby birds while special consumption effects stay with the eater.
Bird age expiry replaces it with falling `BirdBones`; other deaths retain the original falling
corpse. Shared death releases capture/carry attachments first. Bones are dead Matter remains;
both kinds can be carried or composted. Eating a living bird leaves no additional bones.

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
- **Petting:** holding left click on a creature within arm's reach makes the left hand stroke it
  (`PlayerCreaturePetter`). Override `OnPetStart`, `OnPet(deltaTime)` and `OnPetEnd` to react; read
  `IsPetted`, `PetPoint`/`PetNormal` (where the hand touches) and `Affection` (0..1, rises while petted,
  fades slowly). `TentacleCreature` is the example: it stops, turns to the hand and wraps two arms round it.
- **Meditation:** `CreatureJournal` records each creature type the first time the player is near it and
  saves a cast of its meshes.
- **Audio:** the `AudioEventBank`'s creature channels map a creature class name (e.g. `FuzzCreature`) to a
  music layer that fades in near it.

## Misc

`Scripts/Animation/SkinnedMeshAnimator` ping-pongs blend shapes on SkinnedMeshRenderers (for modelled creatures).
