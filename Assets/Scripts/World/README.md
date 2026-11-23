# World Look: Decorations, Harvest, Terrain Shading, Sky

How the generated world looks: the lit line shading on the ground, procedural decorations, pickable cave
fruit and flowers, and the skybox. The terrain shape itself is in `Scripts/MarchingCubes`.

## One-click setup

**Tools > Capstone > Apply World Look And Camera Guard To Open Scene** (`Editor/WorldLookSetup.cs`):

1. Puts `MAT_World_Surface` / `MAT_World_Inner` on the `ProceduralWorld`.
2. Adds `WorldDecorator` with the four decor materials and resets its kinds to the defaults.
3. Adds `CaveHarvest` with the fruit / flower / stalk materials.
4. Sets the skybox to `MAT_Sky_FractalCalm`.
5. Adds `CameraWallGuard` to the player.

All materials are in `Assets/Materials/World/`. Save the scene afterwards.

## WorldDecorator

Listens to `ProceduralWorld.ChunkCreated` and grows decorations on each chunk as it is built. All pieces of
one chunk are merged into one mesh per material and parented to the chunk.

- **Kinds** (`kinds` list): each kind has a shape, a material group, clusters per 1000 m², members per
  cluster, scale range, the ground normal range it grows on (1 = floor, -1 = ceiling), how much it aligns to
  the normal, and patch size / coverage so the land has bare and crowded areas.
  - Surface stone: shards, arches, cairns, cages, totems, halos, pebbles, obelisks.
  - Surface organic: polyps, bladders, creepers, needles, ribbons, mushrooms, fronds.
  - Inner world: shell tentacle colonies and carpets, needle beards, hanging veils, glowing crystals and sacs.
- **Groups / materials:** Stone, Organic, Inner, InnerGlow.
- **Placement:** `seed`, `variantsPerShape` (meshes are made once per variant and reused),
  `spawnClearRadius`.
- **Physics:** `stoneColliders` on, `innerColliders` off (many small colliders slow loading),
  `decorLayer` 7 (Ground, so the player can stand on stones).
- The setup menu calls `ResetKinds()` to restore the default kinds list.

**DecorShapes** builds the raw meshes: bent tubes and lumpy blobs with random harmonics.
Mesh convention: stands on y = 0, grows along +y, `uv.x` = 0 at the base to 1 at the tip.

## Hand-placed decor and creatures

Decor and creatures can also be placed by hand instead of (or as well as) by the runtime spawners.

- **Tools > Capstone > Terrain > Make Decor Prefabs (From World Decorator)** saves every decorator kind as
  one prefab per shape variant (`variantsPerShape`, 8) in `Assets/Prefabs/WorldDecor/` (meshes in `Meshes/`), with the same shapes, materials, layer
  and colliders as the procedural pieces. Running it again overwrites them in place (scenes keep their links).
- **Sculpt And Place Window > Place**: build a palette (buttons add all decor or all creature prefabs),
  click in the Scene view to place, Shift + click to remove. Options: several per click with a scatter radius,
  scale range, random turn, align to slope (1 = along the ground normal, e.g. for ceilings), sink.
  Placed objects go under the root object `Placed World Objects`. Prefabs with a Rigidbody get
  `SettleWhenGroundReady`, so at runtime they wait for their chunk instead of falling through.
- **Drop Selected Objects To The Ground** snaps any selection onto the terrain below it.
- With **Keep Placed Objects On Ground** on, sculpting moves placed objects up or down with the ground.
- **Spawners tab**: switches the runtime spawners on or off (disables the component): `WorldDecorator`,
  `CaveHarvest`, `RealmFlora`, `RealmDressing`, `SpireOvergrowth`, `TowerGroves`, `SpireCreatures`, `BasinLevel`.
  All stay on unless you switch them off.

Hand placing needs the terrain visible in the Scene view: the window builds it (Show Terrain In Scene) and, with
**Auto Show Terrain** on, rebuilds it after opening the scene, leaving Play Mode or reloading scripts.

## Baked decor (the decorator's pieces as editable prefabs)

**Tools > Capstone > Terrain > Bake Decor Into Scene As Prefabs** (`Editor/SceneEditableSetup.cs`) asks the
`WorldDecorator` where it would put every piece (`PlanPieces`, the same placement as at runtime, including the
tower districts and the clearing round the kiln) and puts one prefab instance per piece under
`Placed World Objects`, named `Decor_<Kind>_<variant> (baked)`. Then it switches the decorator off, so nothing is
placed twice. The pieces are ordinary scene objects: move, copy or delete them, or Shift + click them away in the
Place tab. Baking again replaces only the `(baked)` ones; hand-placed objects stay. **Remove Baked Decor** deletes
them and switches the decorator back on. Bake again after moving a landmark or a sinkhole if decor is in the way.

Landmarks show in the Scene view too (see `Landmarks/README.md`). Spawned flora and creatures still appear only in
Play Mode.

## RegionTitles (region names in the planet's script)

`RegionTitles` (needs `PlanetGlyphSource` on the same object) watches the player. Regions are upright cylinders
(centre, radius, height range); after `settleTime` (1.2 s) inside a new region, its title appears like an area
title fixed to the screen, upper middle (`DropletChoir.SummonTitle`): the choir is a child of the camera,
`distanceAhead` 4 m ahead and `raise` 0.95 m up in camera space, glyphs `glyphHeight` 0.75 m tall, so it stays
put on screen however the view turns, yet is made of real 3D creatures. Written in ~2.5 s, held `holdTime` 6 s,
gone after ~16 s. Each region at most once per `cooldown` (90 s). Things between the eyes and the title are
not avoided.
**Scene_World is not edited:** on loading a scene named `Scene_World`, RegionTitles makes a
"Region Titles (runtime)" object with `WorldMapRegions()` (from `WorldMapTerrain`: Flower Field r 50, Cave = the
Goo graft at heights -64..-6 only so the forest above does not count, Mushroom Basin r 72, Tower = spire ruins
r 55 at heights 8..170), unless the scene already has a RegionTitles, so there is never a second copy. The
material is loaded from `Resources/GlyphCreatures/MAT_GlyphGold` when none is assigned. Any other scene can use
it by adding the component and filling in its regions. Check: `Editor/WorldRegionTitlesSetup.PlayAndTest` with
`-regionTitleTest` (opens Scene_World without saving, teleports the player into each region).

## Flower meadow (FloraPatch, MeadowMotes)

Scene_World's flower field is overlaid with a wildflower meadow: `FloraPatch` tiles (32 m squares under
"Flower meadow (instanced wildflower tiles)") draw daisies with GPU instancing, no GameObjects or colliders.
Drifts, colour bands and the ragged meadow rim come from world-space noise, so tiles join without seams; colours
shift towards ice near the bone mother. Flowers grow round the `LilyField` lilies (real, pickable bubble flowers).
Tiles grow one per frame when the terrain is ready (about 0.5 s for all 34), heights come from ray casts against
the terrain chunk colliders. Far tiles draw a half, then a quarter, of their flowers. Meshes are in `FloraShapes`
(petals are two-sided sheets so their tops are lit). Materials: `Assets/Generated/WorldMap/Flora`.
`MeadowMotes` is the ambient pollen and light-mote particle system that follows the camera inside the meadow.
`Editor/FloraSampleApply` is a request-file helper (Temp/Flora.request: `apply` lays the tiles, `capture` renders
review shots to ArtCaptures/Flora).

## Distance wake-up for far decoration (`WorldStreaming`, `Editor/WorldStreamingScenePrep`)

Scene_World's decoration groups (region decor, placeholder forest and ruins, Cistern decor) are switched off just before Play copies the open scene (and back on when Play ends), and in built scenes, so they cost nothing while it loads. The scene file is unchanged, though the open scene shows as modified after Play. `WorldStreaming` wakes each object when the player comes within its group's radius (150 to 220 m), nearest first, inside a per-frame time budget; `LoadingVeil` waits for the objects around the start. Add only groups that gameplay code does not look up: an object still asleep has not run Awake and is not found by Find calls. To check it, write `run` to `Temp/StreamCheck.request` with Scene_World open (`Editor/WorldStreamingCheck`): it visits surface spots in Play, reports near objects still off in `Temp/StreamCheck.status`, and saves shots to `ArtCaptures/Streaming`.

Water follows the player too: `FluidNearPlayer` stops springs and rain and clears droplets while the player is in the caves (below y -30, back on above -22), and pauses water for 30 s if frames stay above 120 ms while it flows.

## Dark caves under the crack (`DarkTunnels`, `MarchingCubes/DarkTunnelShape`, `StrangeWater`)

Underground content of Scene_World (moved 2026-11-16 from behind the spawn chamber to under the crack, Daniel's choice).
The world's inner layer (open from about -33 to -65 under the whole map) is the open cavern. Round the west part of the
crack `DarkTunnelShape` adds, in the world's own density (`Carve`, called at the end of `WorldMapTerrain.ShapeDensity`):

- **Stair tower** at (-140, 115): a hollow rock column from the cavern floor to its ceiling with a spiral tunnel
  inside, 3.6 turns; it starts as a pit in the ground south of the crack rim (`Mouth`) and opens north onto the cavern floor.
- **Pillars**: six rock columns in the open cavern round the hub (-110, 160).
- **Tunnels** dug into the cavern floor (a loop east of the hub, a branch to a grotto, dead ends, the descent to the
  skeleton chamber at (-72, -75, 180)), the Tone Crate's cross-shaped trench at (-92, 132), and two tunnels in the
  ceiling (only Glims and water reach them).

**Layers (2026-11-18):** deep galleries under the floor; the cavern floor; eight rock shelves round the pillars and walls at
different heights (climb their sides, drop through shafts in them); two massifs with tunnels and rooms inside (north one:
walk in at its foot, up through a room to its flat top, then a steep climb through its neck into the ceiling galleries;
west one: a chimney from the grotto to its top). Nothing bridges the gaps. Ten Tone Mesas: on the floor, on shelves
(slide one off and it falls and stays as a step), some heavy.

**Note Seeds** (`NoteSeed`, what the player gathers here): twelve shut pods, three per note, in the hard places. Playing a
pod's note within 14 m opens it; walking up takes the seed, which stays as a coloured bead on the baton's shaft (no menu
or text). Heavy mesas move only for a note holding enough seeds. Kept for the play session only for now.

**After changing the layout, bake the chunk meshes again** (`TerrainChunks.bytes`): write `bake` to
`Temp/CaveBaton.request` with Scene_World open (`Editor/CaveBatonCheck`), otherwise the old saved meshes are used and the
caves are missing (DarkTunnels then logs a warning and places nothing).

`DarkTunnels` adds itself when Scene_World loads (like `SceneAudioBoot`). It puts the conductor baton in the player's hand
at the start (`startWithBaton`, on), and when the terrain is built places 8 Bell Snails, 7 Glim Lanterns, 1 Lure Maw,
three Tone Mesas over the holes into the floor tunnels (notes E, B, G), the strange water and the skeleton (with the baton in its fist when `startWithBaton` is off).
Testing: `spawnUnderground` (off since 2026-11-19; Play starts in the tutorial cave) moves the player to the cavern floor by the stair tower's door as soon as the caves are
built (the dark snaps in at once). Turn it off on the runtime Dark Tunnels object (or in code) for the normal spawn.

Revealed ground is a dim light in the dark (`CaveReveal.SoftEdge`, reach `SoftReach` 2): strong only near the source, a long continuous tail, and rock turned away from the light sinks into black. Off = the clean stencil edge below. The baton adds no light while held. Glow creatures' point lights skip the terrain layer, because `LineWorldLight` steps light into flat levels and would draw a hard ring.
Light round the glow creatures: `CaveGlowCreature.LightStyle` = `Reveal` (default), like a stencil: within a light's
radius (3x the old halo width, Glim about 7 m, snail 8 m) the terrain is drawn with its own normal look, cut cleanly out
of the black fog, so a light far away shows the shape of the ground round it while everything else stays black. No extra
brightness or pattern, a colour filter of the light's colour (`CaveReveal.TintAmount`, 0.55). `World/CaveReveal.cs`
sends the nearest 16 lights each frame to `Shaders/World/CaveReveal.hlsl`, which `TerrainLinesLit` uses at its fog step
(no lights = no change). Only terrain is revealed, not other objects. The baton tip reveals 2.5 m round it.
`CaveGlowCreature.RevealShafts` (off) adds faint light shafts in the air.
`Volume` draws `Capstone/Cave/LightVolume`: a
sphere of light (twice the old halo width) whose air glows in hard banded shells clipped by the scene depth (volume), and
whose surfaces inside are lit in hard bands with a thin rim where the light ends (mask). Close to the camera (a carried
Glim) the volume fades and only the mask stays, so it never fogs the view. `LegacyHalo` brings back the old soft
billboard (`Capstone/Cave/GlowHalo`).

Darkness: below about -30 (the whole inner cavern) and down the spiral the fog turns black and close (`sight` 1.9 m,
`deepSight` 1.2 m below the cavern floor, + `lanternSight` 2.5 m while carrying a Glim), ambient and reflections down to
8%, the sun off. Everything goes back on the way out. Glows and the water's caustics are drawn without fog.

**Mirror pools** (`MirrorPool`, shader `Capstone/Cave/MirrorPool`, Daniel's reference: a cave lake so still the lit
rock above hangs upside down in it), replacing the hanging lenses (`StrangeWater.HangingLenses` brings those back):
a lake lapping at a pillar's foot (-124, 164), a pool by the stair tower (-151, 140) and one in the grotto under
stalactites; stalagmites round each (terrain material, colliders), and a reveal light of the pool's own colour (3x its
radius) so the rock round it and its mirror image show. A planar reflection camera renders the mirrored view at half
resolution for the nearest pool in view within 45 m only (one extra render a frame); the others show a dark tint.

**Hanging lakes** (`HangingLake`, shader `Capstone/Cave/HangingLake`): sheets of water draped under shelves and the ceiling,
following the rock to an irregular rim. Ripple bands flow in to pendants where the water sags; a drop swells, necks,
falls and leaves a ring on the rock. They replace the floating balls (`StrangeWater.FloatingBlobs` brings those back).

`StrangeWater` (shader `Capstone/Cave/StrangeWater`, after Daniel's toon water in ShaderLab_1 week 8: refracted background,
banded depth colour, hard-edged caustics, banded light shafts marched through the volume, two-tone surface, fresnel
opacity, hard highlight). Gravity is a property, so: **hanging pools** are water lenses stuck to the cavern ceiling with
gravity reversed (surface underneath, shafts rising into the rock), dripping down to the floor; **floating blobs** wobble
in the air, each with its own "down" (some drip sideways or upwards); drops leave a widening glowing ring where they hit
rock. The caustics glow by themselves (the caves have no sun). Needs URP Opaque Texture (on in PC_RPAsset).

Test: write `test` to `Temp/CaveBaton.request` (or run `CaveBatonCheck.Batch` with `-caveTest`, add `-caveBake` to bake
first, in a batch copy): walks the spiral pit, tower door, hub and skeleton chamber, shoots the water, plays the Tone
Crate and a Glim, and saves `Logs/CaveBatonTest.txt` and screenshots in `ArtCaptures/DarkTunnels/`.

## CaveHarvest

Also listens to `ChunkCreated`, Play Mode only. In the inner world it hangs fruit bunches on thin stalks
from ceilings and stands flowers on floors and walls.

- Each fruit / flower is a `CaveHarvestItem` (a `Creature`, so normal pickup, prompt and carry work).
  Once picked it becomes a normal falling object. Stalks stay and are merged per chunk.
- Settings: clusters per 1000 m² for fruit and flowers, items per cluster, `stalkLength`,
  `clusterRadius`, `seed`, `variants`.

## Shaders (`Assets/Shaders/World`)

| Shader | Used by | Notes |
| --- | --- | --- |
| `Capstone/World/TerrainLinesLit` | `MAT_World_Surface`, `MAT_World_Inner` | Polar ray / ring line pattern, lit in a few flat steps with shadows, SSAO and hatching in dark areas. The inner material uses no sun, a fake fill light (`_FakeLightDir`), line sheen and distance haze. |
| `Capstone/World/Decor` | decor and harvest materials | Same line lighting. Organic pieces sway and breathe in the vertex stage; stone stays still. |
| `LineWorldLighting.hlsl` | both above | Shared stepped light + hatching. |

Key TerrainLinesLit properties: base / shadow / line colours, `_ShadeLevels`, Hatching group,
`_SheenAmount`, `_HazeColor` (alpha = strength) with `_HazeStart` / `_HazeEnd`.

## Sky

The sky is just a skybox material (`RenderSettings.skybox`).

| Material | Shader | Look |
| --- | --- | --- |
| `Materials/World/MAT_Sky_FractalCalm` | `Capstone/Skybox/Morphing Fractal` | Calm copy of the white morphing fractal (current default). |
| `Shaders/FractalSky.mat` | `Capstone/Skybox/Morphing Fractal` | Original, busier version. Tune scale, speed, warp, twist, contours, contrast. |
| `Materials/World/MAT_Sky_QuietLines` | `Capstone/Skybox/Quiet Lines` | Dark gradient with a few thin drifting contour lines that fade near the horizon. |

Menus: **Tools > Capstone > Apply Fractal Sky to Current Scene** sets `FractalSky.mat`.
Meditation erases the sky together with the world (see `Scripts/Meditation/README.md`).

## Older terrain look

`Shaders/Terrain/FBMRisingTerrain` is for Unity Terrain scenes (e.g. `Scene_Lam`).
**Tools > Capstone > Apply Rising FBM to Scene Terrain** assigns it.

## AerialPerspective (haze)

On the Procedural World object. There is no haze near the player, then a long gentle linear ramp (Unity fog mode Linear) to fully hidden. Settings: **Clear Distance** (no haze nearer than this, default 300 m) and **Haze Depth** (from there to fully hidden, default 420 m),
**Scale Height** (the air thins as the eye climbs, so the far land clears from up the spire), a sun tint (brighter
and warmer looking into the sun, cooler away; Mie-style phase), and a separate violet haze in the inner world.
Line detail fades at given haze strengths. While chunks load, the haze is thickened to hide the unloaded edge.

## Creature song (`CreatureSong`, `ChordDoor`)

Replaces the conductor baton in Scene_World, for every living creature. `DarkTunnels.UseBaton = true` brings the baton back; its code is all still here.

- `Resources/CreatureNotes.asset` gives each species a `role` (melody, accompaniment, drums; each has a planet glyph in `SongGlyphs`) and a `motion`: Walk, Fly, Rooted, or Silent. Silent is for objects, food, eggs and shots, and is the default for a species not in the table.
- Every species has a fixed phrase of 3-4 inputs (each Tone Mesa its own). Drums use WASD, jump and Shift, accompaniment uses the two mouse buttons, melody uses all of them.
- Face a creature within 16 m with clear sight and its phrase appears above it, drawn over rock, in its role's colour. Press the inputs in order, with no timing. A wrong input starts the phrase again. Only presses made while facing a creature count.
- With the song on, each control always plays its own note of E natural minor, in place of the old click, pick-up, drop and footstep sounds (a footstep sounds the held movement key's note, on the music grid) (`inputNotes` in the same asset; Wwise events `Play_Song_*`): S low E, A F#, D G, W A, Shift B, jump C, LMB D, RMB high E. The sounds are Daniel's E2 to E3 samples (the events keep their first names, `Play_Song_E3` to `Play_Song_E4`).
- Fuzz, Fire Eater, Time Spider, Bird, Tentacle, Glim, Bell Snail and Lure Maw play a four-note motif from their own music (`phrase` in the asset). Glim, snail and Maw come from their MIDI; the others were transcribed from audio, so check them by ear.
- The mouse keeps its normal work within 6 m and while carrying, so pick up, pet and throw still work. Mouse notes are played from further off, or to finish a phrase already begun.
- A finished phrase makes the creature follow for good, one for each kind of note: playing to another creature of the same kind (melody, accompaniment or drums) makes it take the place of the last one. melody leads ahead, accompaniment circles, drums come behind. Its music layer plays while it follows.
- Cave glow creatures use their own routing, fuzz uses its chain (with the song on it joins only when played to), Tone Mesas slide. Everything else is moved by `CreatureSongDrive` after its own logic: physics bodies by velocity, others by position with terrain checks. Walkers stop at ledges and cliffs. Rooted species stay and send up notes, and count while the player is within 20 m.
- Nothing is shown while no creature follows. The glyph of each kind that has a follower appears at the bottom left of the view, with collected seeds as dots under it.
- All three roles following is the full chord. `ChordDoor` in the deep descent to the skeleton sinks open when you bring it within 18 m, and stays open.
- A Note Seed pod opens when a following creature of its role comes within 9 m. Heavy mesas need that many drum seeds.

## Hanging water

The cave water is one kind: clear lakes lying on the rock (`HangingLake`, shader `Capstone/Cave/HangingWater`). The rock shows through sharply, thin webs of light move inside, and the water fades out towards its rim and near rock, so it has no visible edge. The nearest lake within 45 m also mirrors the floor below (`MirrorPool.Attach`, one extra half-size render). Drops come from `WaterDrips` and send rings across the surface. `StrangeWater.CeilingMirrors` off brings back the floor pools, `HangingLake.ToonRipples` the older ripple bands. Earlier versions are in `Backups/ceiling-water-2026-11-18`.
