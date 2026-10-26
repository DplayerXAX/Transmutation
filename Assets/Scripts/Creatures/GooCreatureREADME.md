# Goo creature

Goo is a passive, portable blob. Use the existing pickup/drop controls to carry it;
left-click either a world blob or the held blob to apply it. Pickup alone does not
consume it. Applying it removes the creature and coats both arms.

`PlayerGooCoating` on the player owns the session-local supply. Each creature adds
20 seconds, capped at 30 seconds. Extra supply above the cap is discarded. At the
cap, use preserves the creature. These values are tunable on the shared player
prefab through `secondsPerCreature` and `capacityMultiplier`.

Only `HandClimber` requires and consumes this supply. Time attached to a wall,
including resting and mantling, drains at one second per gameplay second. Leaving
the wall or pausing preserves the remaining supply. Depletion cancels the climb
or mantle, restores gravity, and prevents regrabbing. Advanced First Person
Traversal is independent of goo.

Both arm coatings retreat from the shoulders toward the fingertips below 20
seconds. Between 20 and 30 seconds, both arms remain covered and the layer grows
thicker. There is no separate HUD bar. The overlay follows each arm segment and
preserves the underlying limb materials and existing surface restrictions.

## Impacts and paint

All creatures expose `ReceiveImpact(CreatureImpact)`. Scripted throws and the
separate `CreatureImpactRelay` use the same minimum normal impact speed (15.6 m/s
by default: 60% of the 26 m/s maximum throw speed, tunable on each creature). Resting contacts, gentle placement,
triggers, and carried/captured receivers do not trigger it. Normals point from
the other surface toward the receiver; relative velocity is receiver minus other.

Goo handles this hook by coating an accepting Fuzz, or consuming itself to paint
a non-creature solid. A falling object hitting resting Goo paints both the striker
and the supporting ground, when present. Successful reactions log `goo explode`
once. Full Fuzz and other creatures preserve the blob.

Paint follows its supporting object. Its radius is approximately 1.98 metres,
twice the former 1.4-metre patch's area; visible paint, grip collider, and climb
coverage scale together. The oldest patches are removed beyond 24 active splats.
The maximum charged throw speed is 26 m/s; minimum speed remains 4 m/s.

## Setup

- `Tools > Creatures > Goo > Create Assets And Configure Player` creates missing
  goo assets and configures `PlayerObjects` without replacing existing tuning.
- `Tools > Creatures > Goo > Add Three To Scene Yoyo` also places a group of three
  blobs near an existing climbable wall. Repeating it preserves the placed group.

## Verification

- In Play Mode, choose `Tools > Creatures > Goo > Validate Shared Impacts (Play Mode)`
  to check filtering, Fuzz behavior, moving paint, and real falling-object contacts.

- In Edit Mode, run `GooCreatureValidation.ValidateAssets()` through the Unity
  CLI to check prefab wiring, collider setup, and shader compilation.
- In Play Mode, choose `Tools > Creatures > Goo > Validate Gameplay (Play Mode)`
  or run `GooCreatureValidation.Run()`. It creates and removes a temporary rig,
  checking direct/held use, carry cleanup, duplicate consumption, supply
  arithmetic, drain, climb/mantle depletion, regrab blocking, and both arm shells.
- Existing climbing and erosion fixtures explicitly supply goo. The long climbing
  geometry regression replenishes its fixture through the normal application API;
  finite resource behavior is covered by the dedicated goo validation.
