# Compost Creature

A slow, unportable turtle-like moving platform with a level back and four animated
legs. One instance is placed near the player in Scene_Yoyo. Defaults are 0.6 m/s
walking, a 12 m home radius, five-second absorption, and seven-second goo production.

## Feeding and fullness

Only `Creature.IsDead == true` makes a body eligible. A living creature whose
simulation is paused does not qualify. Bodies must physically reach the back;
side contacts and bodies stacked above other bodies do not start absorption.

Bodies sink concurrently. Each successful body contributes
`max(1, floor(Nutrition / 20))` integer charges. Nutrition is snapshotted when sinking
starts. Charges are awarded only at completion, without repeating `Die()` or
calling the body's food-consumption effect.

50 charges is a soft fullness threshold, not a storage cap. Below 50, the next
completed body contributes its entire value: 45 + 15 becomes 60. At 50 or above,
other bodies remain partly sunk, usually with their upper 20% visible. Progress
does not rewind. Bodies resume when stored charges become strictly less than 50.
Simultaneous completions are processed in their recorded first-contact order.

Each production batch reserves 10 charges at its start. After seven seconds it
drops one normal, living Goo Creature behind the turtle. If the release point is
blocked or lacks safe ground, the completed batch waits without additional cost.
Roaming and absorption continue during production. Production spending can make
partly sunk bodies resume on the next update.

## Pickup and death

Existing corpse pickup rules remain in force. Picking up an eligible sinking body
cancels absorption and restores it above the back before carrying begins, without
awarding charges. Absorption temporarily owns physics and ignores collision with
the turtle, retaining collisions with other bodies. Cancellation restores the
captured physics and collision settings. Existing feeding or removal cancels a
claim without awarding compost.

The player follows the moving back without being parented to it and can walk or
jump off normally. The compost creature's death stops roaming and processing,
discards stored charges and unfinished production, and releases sinking bodies.
Its solid back remains as an unportable, stationary corpse.

## Setup and validation

- `Tools > Creatures > Compost > Create Assets` creates missing assets without
  replacing existing prefab tuning or materials.
- `Tools > Creatures > Compost > Add One To Scene Yoyo` places one creature;
  rerunning it selects the existing instance.
- `CompostCreatureValidation.ValidateAssets()` checks the prefab in Edit Mode.
- `Tools > Creatures > Compost > Validate Gameplay (Play Mode)` runs disposable
  state, physics, timing, pickup, production, navigation, and rider checks.
- `Tools > Creatures > Compost > Validate Live Riding (Play Mode)` checks eight
  seconds of actual translation and turning, jumping off, and dead-back support.

Death eligibility follows the shared `IsDead` state. Bird Bones initialize dead,
and dead birds retain their original bodies; both qualify without type-specific
exceptions. Simulation pause alone never qualifies a living body. Existing Nutrition
values and pickup restrictions remain in force. Advanced First Person Traversal is unaffected.
