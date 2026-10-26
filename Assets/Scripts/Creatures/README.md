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
- **Invulnerable:** override to `true` for structural or unique creatures. `ApplyDamage` then refuses all
  damage before the `Damaged` event, and erosive water does not age them. Direct `Die()`, `SetRemainingAge`
  and consumption still work.
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
| `FireSource` | Stores Heat and radiates it each physics step to every creature inside its trigger sphere, less with distance. The trigger radius follows the remaining Heat. Carried, left click shoots a fire bubble along the view (`shotHeat` 20 from its own Heat, `shotSpeed` 4, it keeps that heading). |
| `FireFlower` | States: Closed, Heating, Blooming, Recovering. Heat spreads its linked `DensityShapeOverlay`s outward and grows its body cylinder. At `bloomHeatThreshold` (70) it blooms, spawns one `FireBubble` per shape (sharing leftover Heat), then pulls the shapes back in. |
| `FireBubble` | Floats on one heading with sway, then falls (lifetime or `fallStartWorldY`). After lying still for a delay it turns into a `fireFlowerPrefab`. Its shader flow speed follows Heat. |
| `FireEater` | Wanders on the ground (aligns to the surface normal, slides along walls, unsticks itself). Seeks fallen bubbles within `detectionRadius`, flees the player inside `playerAwarenessRadius` unless eating, digests, then plants a Fire Flower. |

The flower's shape comes from `MarchingCubes/DensityShapeOverlay` objects injecting density into a
`MarchingCubesVolume` (see `Scripts/MarchingCubes/README.md`).

## Other creatures

**Ore Colossus** is a huge, peaceful creature on four long legs that roams the procedural map with a geode
shrine on its back, after Elden Ring's Walking Mausoleum. Its stomps fling the player. Only erosive water
wears away the crystal heart hanging under its belly (an `ErosionTarget`). Once the heart breaks, it kneels
for good: its tail becomes a ramp, its body becomes walkable and climbable static remains, and the geode
releases one **Geode Larva**. Carried, the larva's left click gives its stored life (Age) to the creature
under the crosshair, and it recharges near any colossus. See [OreColossusREADME.md](OreColossusREADME.md).

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

## Squirrels: food, real nest inventory and construction

Drag `Assets/Prefabs/Systems/SquirrelSystemManager.prefab` into a scene to add the complete system.
`Scene_Ariia` already contains an instance. Its single `SquirrelSystemManager` component owns population,
nests, food spawning/refresh and food fights, with all three creature prefab references assigned.
At startup it discovers scene-local `SquirrelTree` anchors and authored squirrel/food/nest components.
Trees named `Tree`, `Tree (...)` or `Environment Trunk ...` are also recognized automatically.
Disable Auto Discover Scene and assign the Trees list to use explicit anchors instead.
The source is split into main, Food and Discovery partial files, all belonging to the same component.
The previous `FoodManager`, `SquirrelManager` and Ariia-only bootstrap have been removed.
Food and Nest prefabs now include Creature-derived components and rigidbodies for the player's existing
carry system. Their combat polarity and diet tags are None; nests cannot be consumed.

### Player controls

- Right mouse picks up world food or a nest using the player's configured carry mode.
- Empty-handed left click on a nest withdraws **one original stored food instance** into the hand.
  This interaction pickup stays held until right click drops it.
- With food in hand, left click at a nest deposits that same item if there is room.
- The nest prompt displays current stock/capacity. Empty nests cannot create food; full nests reject
  deposits without deleting the held item. Stock stays with the nest while the player moves it.

### Food and inventory

Food grows near trees every **15 seconds**, **3 per cycle**, up to **24 unstored items** including held
and squirrel-carried food. Stored items remain inside their nest and are **never recycled by food growth**.
Each nest holds **12 real items** by default (`foodCapacity` on Nest.prefab); its displayed count comes
from the actual inventory. Withdrawing and redepositing does not duplicate items or preserve a historical count.

### Multiple homes and building

A full home selects another known non-full owned nest, or starts gathering **3 food items** to build one.
Construction consumes those exact collected items after reaching and validating a site, then takes **2 seconds**.
The full nest and its stock remain intact. New overflow nests are at most **10 m** from that home,
at least **3.5 m** from another owned nest, within **6 m** of a registered tree, and at least **6 m**
from other squirrels' nests. The site must have clear, supported Ground-layer terrain.
No materials or valid site means waiting and retrying, rather than creating a free nest.

The owner navigates to remembered home coordinates. An unseen player pickup does not send it the nest's new position.
Only after reaching the former site does it discover an unseen loss and prioritize gathering materials for a replacement.
Replacement sites can be searched within **45 m**. On later journeys, a missing old nest can be rediscovered
within **6 m** with line of sight. An owner still rebuilding can reuse a non-full recovered home;
a stocked old home found later is worth carrying back beside the present home when a valid site exists.
Otherwise the recovered home is retained as known spare storage.

### Shortage migration and validation

After **30 seconds** finding no local food, a squirrel searches **45 m** for new food and carries its existing
nest there at **70% speed**, retaining owner and inventory. Migration has a **90 second** timeout.
Construction and migration do not start food fights and pause with creature control. Death/disable releases
carried items and detaches a squirrel-carried nest.

**Tools > Creatures > Validate Squirrel System Prefab (Play Mode)** checks the prefab in an isolated
scene and runs ecology, inventory/building and physics regressions.
Other Play Mode checks under **Tools > Creatures**: **Validate Squirrel Physics**, **Validate Squirrel Ecology**,
**Validate Squirrel Squeeze**, and **Validate Squirrel Nest Inventory And Building**. The latter verifies real
player transfer, empty/full behavior, no remote knowledge, paid rebuilding, nearby overflow storage and old-home recovery.
**Configure Squirrel Food And Nest Prefabs** is an edit-mode utility for upgrading model-only prefab assets.

### Witnessed player actions

`Squirrel.prefab` now contains the squirrel and motor components. Its **green +Z face is the front**;
the **blue -Z face is the back**. The nest owner reacts only when it sees the player at the moment
of a successful action: within **18 m**, inside its **120 degree forward cone**, and with unobstructed
line of sight. Back-facing or occluded actions retain the normal remembered-home behavior.

- A real deposit starts one physical orbit around the player (radius **2.5 m**). Dropped/thrown food
  colliding with a nest can also enter storage within **5 seconds** of being held; full nests reject it.
- A witnessed withdrawal starts a jumping pursuit of that exact held item. The player must still hold
  the item and the squirrel must reach it while rising; success releases the hand and returns the item home.
- A witnessed nest pickup starts following. Behind cover, it searches the last seen player position.
  After the nest is dropped, it retrieves the same nest and carries it back to its remembered position.
- At placement, a player within **6 m** who maintains mutual facing triggers a warning. **Warning Clip**
  on the squirrel prefab is an optional spatial `AudioClip` slot, currently empty. The red marker above
  the squirrel flashes and Console logs **Squirrel warning triggered**, so the branch works without audio.
- Looking away or leaving allows placement. Continuing to face each other for the **2 second warning**
  plus **2 further seconds** triggers escape. Default escape speed is **80 m/s**, raised above measured
  player speed when needed. The same nest and contents move to clear Ground near a registered tree,
  at least **12 m** from the player, with spacing from other nests. If no valid site exists, it retains
  the nest and continues searching instead of placing it at an invalid point.

Reaction settings and the empty audio slot are on **Squirrel.prefab > Squirrel**. Social reactions take
priority over foraging and food fights; creature carrying/capture still pauses autonomous movement.
**Tools > Creatures > Validate Squirrel Player Reactions (Play Mode)** exercises facing, occlusion,
real inventory, full orbit, jumping retrieval, following, return, silent warning, broken eye contact,
escalation and physics-based escape placement in isolated scenes.
**Configure Squirrel Reaction Prefab** can reapply the prefab components and visual markers in edit mode.

### Museum display prefab

Drag **Assets/Prefabs/Systems/SquirrelMuseumDisplay.prefab** into a museum/display level with an
existing player. This is a **variant of SquirrelSystemManager.prefab**, retaining the same creature,
food, nest and player-reaction behavior. The original system prefab is unchanged.

The exhibit includes a **32 x 22 m Ground-layer platform** and **two simple tree anchors**. Keep the
root upright at scale one; place its origin at the desired floor height. It starts with **two equal-size
squirrels and two owned nests**, each with an **8 m territory**. Only its explicitly assigned child
trees are used; scene-wide discovery is disabled, so other trees and squirrels are not added to the exhibit.
Population is fixed at two initial squirrels; food replenishment never creates more squirrels.

There are **six initial food items**, then **one new item every 12 seconds**, capped at **eight unstored
items**. Normal gathering, nest storage/expansion, missing-home rebuilding, migration and witnessed
player reactions remain available. Forage and building begin around the included trees, but witnessed
player pursuit can leave the platform; this is an interactive habitat, not a sealed enclosure.

Adjust **Population**, **Size Range**, **Territory Radius** and food settings on the variant's manager
if a different exhibit size is needed. Keep **Auto Discover Scene** off and the **Trees** list bound to
this exhibit's child anchors. Add suitable ground around it if players should be able to lead squirrels
farther away. A player/camera is supplied by the museum level, not by this prefab.

**Tools > Creatures > Validate Squirrel Museum Display (Play Mode)** verifies variant inheritance,
relocated placement, the two-squirrel population, local anchor isolation and capped periodic food.
**Create Squirrel Museum Display Prefab** rebuilds a missing asset without overwriting an existing one.
