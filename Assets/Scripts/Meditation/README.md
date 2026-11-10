# Mind Palace

On the ground, aim at a resting creature and left-click to sit. A second left click enters the
Reflection Space. Right-click or Space stands. Pick up and move a resting creature with the usual
right-click carry controls. Left click has no effect while carrying one.

The central resting place in the theater works the same way and sends you back to your world seat.
The giant slab opens drawing on left click. Draw with left click, erase with right click, and
step back with Space. Drawings and learned glyphs are visible on separate parts of the slab.

A world seat records eligible families within 10 metres when you enter. To adjust or preview this
range in the editor, select a resting creature in Play Mode and use its Discovery Radius and Show
Discovery Range fields. The theater places exhibits in the order they were discovered. Click a
frozen member or the light switch to release the group. Use the fixture to turn the beam back on.
Only its own members freeze when they enter a lit beam. The room resets every visit.

## Authoring exhibits

Edit the **ReflectionCatalog** asset under **Assets/Resources**. For a typical new creature:

1. Add an exhibit with a permanent, unique ID and the creature's exact C# type name.
2. Select the **Prefab** recipe and assign the creature prefab. Set its count.
3. Set the radius and height to fit the entire starting composition. Allow space for the natural
   size, movement, and any supporting pieces. Placement and beam dimensions follow these values.
4. Test a clean visit and a release. If the creature needs a habitat or a special group, add a
   recipe in ReflectionSpace. Keep products such as eggs and webs out of the discovery catalog
   unless they are intended to get their own exhibits.

Each visit has its own scene and physics. Runtime queries that previously used the global physics
scene should use the owner's PhysicsScene or ReflectionPhysics. Spawn supports and descendants
in the owner's scene so the whole visit is cleaned up together.

Discoveries use Reflection/exhibits-v1.json under Unity's persistent data directory. The slab
uses Reflection/drawings/amphitheater-slab-v1.png. Old journals, casts, and drawings stay on disk.

Outside Play Mode, use **Tools > Capstone > Meditation > Unlock All Exhibits** or
**Clear Exhibit Unlocks** to change discovery progress without affecting drawings or glyphs.
