# Scene Goo

Open `Assets/Scenes/Scene_Goo.unity`. Its saved Marching Cubes terrain appears in
the editor automatically and remains editable with the existing Sculpt And Place
window. The creation menu refuses to overwrite an existing scene or terrain asset.

## Spaces

- **Open canyon:** a descending oval crack, with low curling rock-shell sides,
  broken edges, unequal stone shards and a narrow skylight. A flat encounter terrace supports the first
  Recycle creature, flowers, canopy spider and 24 initial birds.
- **Underground chamber:** a bent approach opens into a broad irregular room.
  Small, low side nooks remain subordinate to the large upward exit arch. That
  arch opens toward the mountain shaft so daylight provides a direction to follow.
- **Mountain shaft:** a broad mountain approximately 32 metres above the plain,
  with a much narrower opening. Its interior rises from approximately -9 metres.
  Uneven, staggered shelves replace complete horizontal ledge rings.

The surrounding plain is an open rocky tundra with 96 instances of the existing
procedural stone prefabs, varied in scale and orientation. Low sunlight produces
ground shadows. The mountain can also be explored from outside: broad eastern and
northern approaches remain below the walking slope limit, while the compressed
western and southeastern faces become cliffs. The interior sequence encourages
discovery rather than enforcing a single route through the environment.

The curved gate between chamber and shaft begins below the player's 60-degree
walking limit and steepens toward vertical. Two authored Goo blobs fall after
terrain collision is ready and coat its upper curve. No personal Goo is needed
for those painted grips. The taller shaft is intended for a prepared player with
Goo on their arms; its rest shelves provide places to pause, and a walkable eroded
saddle finishes the ascent near the top.

## Ecology and current bug milestone

Both Recycles retain their existing `CompostCreature` identities, redesigned as immovable terrain-conforming fungal networks. Corpses must contact grown strands. Meals extend the network and award recycle points; missing flowers are funded before new goo batches. Flowers are independent child creatures, detach on pickup with an empty bank, and can be manually fed while held. Their 120-second detached lifetime ends in a dead flower that can seed a new network on terrain. See [Recycle surface and growth design](../RecycleSurfaceDesign.md). Birds seek the flowers, the spider drains trapped prey's Age, and
bones fall past the web. Every dead creature is excluded from web capture.
There are no scripted kills, guided corpses, or automatic feeding.

The second milestone adds 24 ceiling bat bugs over the underground Recycle
creature and 12 on the shaft walls. They use local surface probes for floor,
wall and ceiling crawling, retain normal carry behavior without a held use,
and die through the shared Age lifecycle. Initial remaining lifetimes are
staggered from 20 to 60 seconds; newborn lifetimes are 55–75 seconds. Their corpses
fall normally and can feed Recycle creatures.

Bugs now share persistent cavern/shaft homes with their offspring and roam much
farther (18/28 metres), with spread-out starting positions outside the Goo gate.
Each lays one egg after 15–30 seconds of elapsed life. Incubation takes 13–17
seconds; ready eggs wait for local population space, with nearby deaths waking
the oldest eligible egg. Eggs can be carried intact or cracked with LMB; solid
impacts and a held egg's carrier landing use an 11 m/s closing-speed threshold.
Unclaimed bug corpses retire after 180 seconds; carrying or Compost absorption
protects them. Destroying every bug and egg can still eliminate a colony.

Hold **LMB** on a reachable supported bug to grip it and be dragged along as it
crawls. Looking at another reachable bug while holding transfers the grip.
Release LMB to let go, Space to jump off, or W to reach a ledge or Goo wall.
RMB retains ordinary pickup, drop and throw for bugs. Bugs have 1.5× larger
visuals, a 2.8-metre grip reach and more forgiving grip targeting. Bug shells
and legs fade toward white as their remaining lifespan falls.
The upright player capsule sweeps against terrain while following one moving
anchor; support loss, death, pickup or capture releases the grip.

## Review and validation

`Tools > Capstone > Goo` contains the two milestone builders, geometry validation,
review captures and the disposable bug/web gameplay check. Reports and images go
to `Temp/GooSceneValidation`. The traversal helper tests the real player's grips
and the actual physical Goo splats rather than bypassing them with a shortcut.

`Goo Level Content` holds terrain and encounters. `Standalone Setup` holds the
player, sunlight and audio bootstrap and can be omitted when merging. ENTRY and
EXIT markers identify the connection points. Bird sky bounds are world-space;
update them if the level is repositioned for a future merge.

Audio currently depends on the project's generated Wwise SoundBanks being present.
