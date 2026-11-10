# Mind Palace

On the ground, aim at a resting creature and left-click to sit. A second left click enters the
Reflection Space. Right-click or Space stands. Pick up and move a resting creature with the usual
right-click carry controls. Left click has no effect while carrying one.

The central resting place in the theater works the same way and sends you back to your world seat.
Left-click the giant slab to open the field journal. Use its physical left and right arrows to
turn pages while standing nearby or while editing. The right arrow adds a page at the end.
Use **T** for writing, the **pen** to draw with left click, or the **eraser** to erase with
left click. Right click also erases while using the pen. Esc steps back from the slab.

The black satchel on the right shows in-game creature cutouts for unlocked families in two columns.
Scroll to browse, click a sticker
to pick up a copy, and click the page to place it. In Type mode, click a placed sticker to move it.
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
