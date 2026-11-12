# Mind Palace

On the ground, aim at a resting creature and left-click to sit. A second left click enters the
Reflection Space. Right-click or Space stands. Pick up and move a resting creature with the usual
right-click carry controls. Left click has no effect while carrying one.

The central resting place in the theater works the same way and sends you back to your world seat.
Left-click the giant slab to open the field journal. Use its physical left and right arrows to
turn pages while standing nearby or while editing. The right arrow adds a page at the end.
Use **T** for writing, the **pen** to draw with left click, or the **eraser** to erase with
left click. Click the selected tool again to return to neutral mode, where clicks can move
stickers without typing or drawing. Right click also erases while using the pen. Esc steps back
from the slab.

The black satchel on the right shows in-game creature cutouts for unlocked families in two columns.
Scroll to browse, click a sticker
to pick up a copy, and click the page to place it. Click a placed sticker to move it in any mode.
Right-click or click the satchel to discard a held sticker. Press Esc to cancel a move and
put the sticker back.

To add a sticker, add its transparent texture and an entry to **SlabStickerCatalog** in
**Assets/Resources**. Use a unique sticker ID, the parent's exhibit ID as **Family Id**, and
a short display name. Several sticker entries can share the same unlock.

A world seat records eligible families within 10 metres when you enter. To adjust or preview this
range in the editor, select a resting creature in Play Mode and use its Discovery Radius and Show
Discovery Range fields. The theater places exhibits in the order they were discovered. Click a
frozen member or the light switch to release the group. Use the fixture to turn the beam back on.
Only its own members freeze when they enter a lit beam. The room resets every visit.

## Implemented roster and grouping

The current catalogs contain **29 unlock families and 37 stickers**. Each row is one unlock
and one exhibit beam. The discovery type must be inside the world resting creature's scan range
when entering. Every sticker in that row becomes available with the same unlock.

| Unlock ID | Discovery type | Initial exhibit | Stickers sharing this unlock |
| --- | --- | --- | --- |
| `birds` | `BirdCreature` | 6 birds | Bird, Bird Bones |
| `bat-bugs` | `CeilingBatBug` | 5 adults, a perch, and a habitat that supports eggs | Ceiling Bat Bug, Bat Bug Egg |
| `sky-spider` | `SkySpider` | 1 spider and its web | Sky Spider, Sky Web |
| `glow-bees` | `GlowBee` | 1 colony with 4 bees, a nest, and nectar flowers | Glow Bee, Glow Bee Nest, Glow Honey |
| `squirrel` | `Squirrel` | 1 squirrel, a nest, 3 starting food items, tree anchors, and a manager | Squirrel |
| `mushroom-tree` | `MushroomTree` | 1 young tree and 1 grown bouncy-cap tree | Grown Mushroom Tree, Young Mushroom Tree |
| `bounce-shroom` | `BounceShroom` | 1 compact specimen that expands through its normal interaction | Bounce Shroom, Expanded Bounce Shroom |
| `water` | `ErosiveFluidDroplet` | 30 droplets | Water |
| `general-asura` | `GeneralAsura` | 1 General that can produce its Blast Core | General Asura, Blast Core |
| `melee-asura` | `MeleeAsura` | 1 specimen | Melee Asura |
| `ranged-asura` | `RangedAsura` | 1 specimen | Ranged Asura |
| `rolling-asura` | `RollingAsura` | 1 specimen | Rolling Asura |
| `fuzz` | `FuzzCreature` | 1 specimen | Fuzz Creature |
| `bellows` | `BellowsCreature` | 1 specimen | Bellows Creature |
| `chameleon` | `ChameleonCreature` | 1 specimen | Chameleon |
| `compost` | `CompostCreature` | 1 specimen | Compost Creature |
| `cistern-snail` | `CisternSnail` | 1 specimen | Cistern Snail |
| `geode-larva` | `GeodeLarva` | 1 specimen | Geode Larva |
| `ore-colossus` | `OreColossus` | 1 natural-size Colossus with its ore and Geode Larva lifecycle | Ore Colossus |
| `goo` | `GooCreature` | 1 specimen | Goo Creature |
| `tentacle` | `TentacleCreature` | 1 specimen | Tentacle Creature |
| `fire-eater` | `FireEater` | 1 specimen | Fire Eater |
| `recycle-flower` | `RecycleFlower` | 1 specimen | Recycle Flower |
| `light-moth` | `LightMoth` | 1 specimen | Light Moth |
| `flash-bloom` | `FlashBloom` | 1 specimen | Flash Bloom |
| `afterglow-pod` | `AfterglowPod` | 1 specimen | Afterglow Pod |
| `corrupt-seed` | `CorruptSeed` | 1 specimen | Corrupt Seed |
| `fire-flower` | `FireFlower` | 1 specimen | Fire Flower |
| `bubble-flower` | `BubbleFlower` | 1 specimen | Bubble Flower |

Supporting objects can appear through the parent creature's behavior without their own unlock.
Webs, bat eggs, bird bones, bee nests and honey, squirrel nests and food, Mushroom Essence,
Fire Bubbles, Trapping Bubbles, Blast Cores, and projectiles have no independent exhibit entry.
Their presence alone does not unlock a parent family. Some have stickers, as listed above.

Geode Larva, Bounce Shroom, Recycle Flower, and Corrupt Seed each have their own unlock even
though another creature can produce them. Detect that creature itself to unlock its exhibit.
The central resting creature is always present. Fire sources and cave harvest items are
outside the current exhibit roster.

Use [ReflectionCatalog](../../Resources/ReflectionCatalog.asset) and
[SlabStickerCatalog](../../Resources/SlabStickerCatalog.asset) as the source of truth when changing
the roster. Update this table when adding a family, changing an exhibit group, or adding a variant.

## Adding a creature exhibit

Most creatures need one prefab and one entry in [ReflectionCatalog](../../Resources/ReflectionCatalog.asset).
The theater places the exhibit automatically.

1. **Start with a working prefab.** The root needs the creature script and its interaction
   collider. Assign the same materials, shader assets, and required references as the world
   creature. If the creature currently exists only in a scene, make a prefab from a configured
   specimen. The Fuzz Creature and Tentacle Creature are examples.
2. **Add an entry in the catalog Inspector.** Use a unique, permanent **Id** such as
   `light-moth`, and set **Creature Type** to the exact C# class name, such as `LightMoth`.
   The type controls discovery and the ID is what the save stores. Keep the ID stable when
   renaming the prefab or script. If the class name changes, update the catalog type.
3. **Set up the exhibit.** Select **Prefab**, assign the prefab, and set **Count** (usually 1).
   **Radius** and **Height** are in metres and define the beam volume. The radius also reserves
   space in the spiral. Allow room for the full group, including its supports, at natural size.
4. **Decide what grants the unlock.** Give the main creature a catalog entry. Supporting
   objects like eggs, webs, bones, and honey can appear or spawn as part of an exhibit without
   a separate entry.

### Creatures with special setup

Use [ReflectionSpace.cs](ReflectionSpace.cs) for exhibits that need a habitat, multiple forms,
or a manager. Existing examples include the five Bat Bugs and their perch, the young and grown
Mushroom Trees, and the Sky Spider and its web. Give new recipe values explicit numbers so
existing catalog assets keep their recipe assignments.

- Set up required materials and dependencies before the creature builds its visuals. Adding
  the script to an empty object skips the prefab's Inspector assignments. That produced pink
  fur and a missing membrane in the original Fuzz and Tentacle exhibits.
- Build any first-frame geometry before registering the creature with the spotlight. The
  spotlight pauses simulation immediately, so visuals that normally appear on the first update
  need an initial setup step. Register each starting group member and any supporting creatures
  that should freeze with it.
- Spawn supports, managers, and descendants in the exhibit's scene so exiting removes them.
  Use the owner's `PhysicsScene` for physics queries. Existing reflection-aware queries can
  also use `ReflectionPhysics`.

### Verify in Play Mode

1. Outside Play Mode, choose **Tools > Capstone > Meditation > Unlock All Exhibits** to preview
   the new exhibit. Check its materials, pose, footprint, and frozen state when you enter.
2. Click its fixture or a frozen member. Check normal movement and interactions. Turn the beam
   back on and check that only its own members freeze inside it.
3. Leave and re-enter. Check that the exhibit resets under its beam and any spawned objects from
   the previous visit are gone.
4. To verify discovery, choose **Clear Exhibit Unlocks** outside Play Mode. Enter from a seat
   with the eligible creature within 10 metres. Check that its exhibit appears and persists
   on a later visit. For grouped exhibits, also check that a supporting object alone does
   not grant the unlock.

Discoveries use Reflection/exhibits-v1.json under Unity's persistent data directory. The slab
journal uses Reflection/slab-journal-v1.json with a PNG per page in Reflection/drawings.
An existing amphitheater-slab-v1.png is copied into the first page when the journal is first opened.

Outside Play Mode, use **Tools > Capstone > Meditation > Unlock All Exhibits** or
**Clear Exhibit Unlocks** to change discovery progress without affecting drawings or glyphs.


## Creature unlock sphere

A shattered black sphere above the tablet follows the same creature-family unlocks as the exhibits.
Tune it on ReflectionSpace > Unlock Sphere. The inspector has save-free previews for shattered,
partial, whole, and animated restoration states, plus an isolated editor preview window.
See Docs/ReflectionUnlockSphere.md for geometry, lifecycle, tuning, and verification details.

Resting creatures now ensure the shared Resources/ReflectionRig prefab exists at runtime when
no MeditationController is present in their scene. Adding a RestSpot is sufficient; the normal
player/camera and floor are still required for interaction. Existing authored rigs are reused.
World seats stay invulnerable and can be carried; only the reflection return seat is fixed.
The seat is a simple slab/block without the old decorative ring, frame, or hood.
Creature > Ignore Carry Size Limit lets other creatures opt out of the pickup size cap.
Resting creatures, Bellows, and Tentacle creatures use this override by default.

The unlock sphere restores bottom-to-top. Each valid discovery restores the next lowest group
of chunks, leaving a broken-egg rim and a missing upper cap. Existing saves reconstruct the
same lower portion immediately. Dense and one-fragment modes both follow this order.
