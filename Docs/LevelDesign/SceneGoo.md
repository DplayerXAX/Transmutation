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

Both Recycle creatures are existing `CompostCreature` instances. Corpses must
actually reach their backs. Production retains ordinary nutrition, charge cost
and timing. Birds seek the flowers, the spider drains trapped prey's Age, and
bones fall past the web. Every dead creature is excluded from web capture.
There are no scripted kills, guided corpses, or automatic feeding.

The second milestone adds 24 ceiling bat bugs over the underground Recycle
creature and 12 on the shaft walls. They use local surface probes for floor,
wall and ceiling crawling, retain normal carry behavior without a held use,
and die through the shared Age lifecycle. Initial remaining lifetimes are
staggered from 20 to 60 seconds; the prefab lifetime is 60 seconds. Their corpses
fall normally and can feed Recycle creatures.

**Deferred:** eggs, population replacement, player gripping/riding and movement
controls. The current finite population can die out. The separate ceiling bat bug
design document concerns future phases, not the implemented milestone.

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
