# Systems Index

One README per game system. Start here.

For optional project-wide orientation, see [project context](../../Docs/PROJECT_CONTEXT.md). AI working rules are in [AGENTS.md](../../AGENTS.md). Use only the entries relevant to the task; this index is not a required reading list.

**Maintenance:** when adding or changing a creature/system, update its local README. If none covers it, create one and link it from this index or the [creature index](Creatures/README.md). A related creature family can share a guide; individual helper scripts do not each need a README.

| System | README | Covers |
| --- | --- | --- |
| Player | [Movement/README.md](Movement/README.md) | Movement, camera, wall guard, hand climbing, procedural body, carrying, four-slot hotbar inventory (mouse wheel, `BodyMounts`), interaction, prompt HUD, conductor baton (`ConductorBaton`), mouse relock (`CursorRelock`) |
| Creatures | [Creatures/README.md](Creatures/README.md) | `Creature` base, fire cycle (Source, Flower, Bubble, Eater), Fuzz (and the grown pack beast), Tentacle, Rewind Coil (time save point), Ore Colossus and Geode Larva, Droplet Choir (water beads that write glyphs; test scene `Scene_Droplets`), cave glow creatures (Glim Lantern, Lure Maw, Bell Snail, Tone Crate) |
| World generation | [MarchingCubes/README.md](MarchingCubes/README.md) | `ProceduralWorld` (surface, caves, inner world), `MarchingCubesVolume`, density shapes |
| World look and sky | [World/README.md](World/README.md) | `WorldDecorator`, `CaveHarvest`, `TerrainLinesLit` and decor shaders, skyboxes, dark caves under the crack and strange water (`DarkTunnels`, `StrangeWater`) |
| Meditation | [Meditation/README.md](Meditation/README.md) | M key meditation, reflection space, drawing on steles, creature journal and casts |
| Landmarks | [Landmarks/README.md](Landmarks/README.md) | Hanging Spire, seeds, inscriptions, glyphs on steles |
| Basin test level | [BasinLevel/README.md](BasinLevel/README.md) | Small basin, two towers, three creature routes, creature regrowth |
| Main menu | [MainMenu/README.md](MainMenu/README.md) | `Scene_MainMenu`, UI buttons for Map, Test Level and Exhibition, Build Settings order |
| Exhibition | [Exhibition/README.md](Exhibition/README.md) | `Scene_Exhibition`, one stall and sign per creature and feature |
| Habitat block | (section below) | `Scene_ArtReference`, small floating diorama with the game's elements together, art bible reference |
| Audio | [Audio/README.md](Audio/README.md) | `AudioManager`, adaptive music layers, Wwise and Unity backends, footsteps |
| Erosive fluid | [Fluid/README.md](Fluid/README.md) | Fluid sources/droplets, procedural terrain erosion, creature contact and setup |
| Wind | [Physics/Wind/README.md](Physics/Wind/README.md) | `WindArea` (wind walls, breezes), shared wind rule, per-creature/player wind resistance, streak particles and shader, `WindWall` prefab |
| Grass | [../Grass/README.md](../Grass/README.md) | GPU compute grass on Unity Terrain |

## Tools > Capstone menu

| Menu | Does |
| --- | --- |
| Apply World Look And Camera Guard To Open Scene | World and decor materials, decorator, harvest, calm sky, camera guard |
| Apply Fractal Sky to Current Scene | Sets `FractalSky.mat` as the skybox |
| Apply Rising FBM to Scene Terrain | FBM shader on Unity Terrains (older scenes) |
| Meditation > Add Meditation To Open Scene | Meditation object and materials |
| Meditation > Open Saved Drawings Folder / Clear Saved Drawings And Journal | Saved reflection data |
| Landmarks > Add Hanging Spire To Open Scene | Spire, its materials, glyphs on steles |
| Landmarks > Reset Landmark Progress (Seeds And Glyphs) | Clears collected seeds and glyphs |
| Basin Level > Create Basin Test Scene / Set Up Open Scene As Basin Level | Scene_Basin: basin world, two towers, creatures (Scene_Daniel unchanged) |
| Basin Level > Add Basin Creatures Round The Spire (Open Scene) | Goo pool + compost, bubble flowers, spider, birds round the spire's stage towers |
| Main Menu > Create Main Menu Scene | Scene_MainMenu with Map / Test Level / Exhibition buttons, menu first in Build Settings |
| Main Menu > Add Menu And Game Scenes To Build Settings | Menu, Scene_Daniel, Scene_Basin, Scene_Exhibition (if present) first in Build Settings |
| Exhibition > Create Exhibition Scene (Copy Of Scene_Daniel) | Scene_Exhibition: flat hall, a stall per creature / feature (Scene_Daniel unchanged) |
| Art Direction > Create Habitat Block Scene | Scene_ArtReference: a floating chunk in four regions (bone mother / time, heat, light, green growth) with an inner world cave in the front cut |
| Art Direction > Capture Habitat Block Views | Renders the block's views (and a hero view with callouts) to `ArtCaptures/HabitatBlock/` |
| Audio > Set Up Adaptive Music In Open Scene | Event bank, AudioManager, landmark / cave channels, footsteps |
| Audio > Add Music Zone At Scene View | Box zone with a `MusicChannel` |
| Audio > Relink Clips By File Name | Fills Unity clips in the bank from `Assets/Audio` |
| Terrain > Bake Open Scene World To Sculpted Terrain | Freezes the ProceduralWorld layout into `Assets/Terrain/Sculpted/<Scene>_Terrain.asset` and assigns it |
| Terrain > Sculpt And Place Window | Sculpt brushes, hand placing prefabs on the ground, runtime spawner switches |
| Terrain > Show Terrain In Scene View | Builds the world in Edit Mode (to sculpt or place on it) |
| Terrain > Make Decor Prefabs (From World Decorator) | Saves the decorator's shapes as prefabs in `Assets/Prefabs/WorldDecor/` |
| Terrain > Make Scene Editable (Terrain, Landmarks, Decor) | Saved terrain, landmark handles on their current spots (Scene view preview), decor baked to prefabs |
| Terrain > Bake Decor Into Scene As Prefabs / Remove Baked Decor | Decorator pieces as prefab instances under `Placed World Objects` (decorator off), or back |
| Landmarks > Show Landmarks In Scene View | Toggle: builds the spire and kiln in Edit Mode as an unsaved preview |

All menu code is in `Editor/`.

## A typical scene (`Scene_Daniel`)

```text
ProceduralWorld  + WorldDecorator + CaveHarvest + CaveMusicChannel
PlayerObjects    (prefab: SmoothFirstPersonController, AdvancedFirstPersonTraversal,
                   PlayerCreatureCarrier, PlayerCreatureInteractor)
                 + scene additions: HandClimber, FirstPersonBody, CameraWallGuard, Footsteps
Meditation       + MeditationController, ReflectionSpace, CreatureJournal, ReflectionVisitor,
                   MeditationHUD, SteleInscriptions
HangingSpire     + MusicChannel
AudioManager     (+ Wwise AkInitializer)
InteractionPromptHUD
Creatures
```

## Other folders

- `UI/` — `InteractionPromptHUD` (described in the Player README).
  `LoadingVeil` boots itself in any scene with a ProceduralWorld: a wordless black veil (ring of 24 dots + a filling
  line) that covers the world while terrain chunks, Hanging Spire / Bone Kiln builds, FloraPatch tiles and
  SettleWhenGroundReady objects finish, then fades out (max 30 s; logs when it lifts). It cannot cover the editor freeze before the first
  Play frame (script reload + scene reload), which happens before any game code can draw.
- `Animation/` — `SkinnedMeshAnimator`, blend-shape ping-pong for modelled creatures.
- `Dev/` — `ClimbTestDriver`, automated climbing test (editor started with `-climbTest`, see `Editor/ClimbTestLauncher`).
  `HitchProbe` runs by itself in editor play: every frame over 33 ms goes to `Logs/HitchLog.txt` with the player position and the slow engine phases (scripts, physics, GC, instantiate, render).
  `Editor/PerfCapture` (no menu): write `run` to `Temp/Perf.request` with the scene open; the editor presses Play, times
  the enter-Play freeze, profiles startup / idle / walking / walking with the Game view maximised, exits Play and writes
  `Logs/PerfReport.txt` (EditorLoop = editor-only cost, PlayerLoop = the game). Creature.Update is split per species
  in the Profiler as "Creature tick: <type>".
  `Editor/StallCapture` (no menu): write `x y z seconds` to `Temp/Stall.request`; the editor plays with the player at that point, notes what is awake every 20 s, profiles when frames stay slow and writes `Logs/StallReport.txt`.
  `StartupTimeline` runs by itself in editor play: `Logs/StartupTimeline.txt` lists every log message and milestone
  (scene objects awake, first frame, terrain complete, loading veil gone) with its time since the scene began loading.
- `RenderingTests/` — experiments: dither renderer feature, photo capture, sphere post-process volume.
- `Utilities/` — `VisualPrimitive` (display-only primitive with no collider; use it instead of CreatePrimitive plus destroying the collider, which costs about 5 ms each in Scene_World), `DestroyAfterSeconds`, `Emitter` (spawn and launch a prefab), `XRotator`.

## Habitat block (art bible reference)

`Editor/HabitatBlockSetup.cs` builds `Assets/Scenes/Scene_ArtReference.unity`: one floating 16 x 12 m chunk of a
black-and-white world with colourful life bursting out of it, split into four regions (seen from the top, front
at the bottom). Made from the game's own materials, shape builders and prefabs.

| Region | Shows |
| --- | --- |
| Front left: bone mother, time, distortion | Long marble skeleton with arching ribs, cyan time ribbons and threads through it, a glitched puddle and a glitch tear |
| Front right: heat | Crater with molten cracks, blooming fire flowers, fire bubbles rising, orange light |
| Back right: light | Pale crystals, frost and film crystals, lamp stalks, iridescent bubbles, beams, halos |
| Back left: green growth | Spring, green blades, glowing moss, mushrooms, coral, anemones, bubble flower, goo, compost, squirrel |

Each region also has its own poster dressing (after Daniel's flat graphic poster reference): stones, pillars,
steps, arches, rings and discs drawn with `Shaders/HabitatBlock/PosterPattern.shader` (flat two-step shading, ink
outline, one bold object-space pattern: stripes, dots, mosaic, waves, leopard / terrazzo spots or stars) in the
region's palette: bone = cream / black / salmon / teal, heat = crimson / mustard / black, light = teal / cream / gold
and a starfield, growth = olive / forest / mustard / coral. The bone mother is built in the Bone Kiln's language
(marble vertebrae, comb-glass ribs, kiln flora, curled children) and the heat region shows the HeatHaze dotted shells.

Animated effects (`Shaders/HabitatBlock/HabitatFX.shader`, additive; they move in Play mode or a Scene view
with Always Refresh): heat = hot-air bands climbing two columns out of the crater plus rising embers; light =
soft shafts with drifting dust falling on the ridge plus twinkling four-point flares on the crystals; time =
two clear tide sheets hovering over the bone mother, their waves flowing towards the front (only crests and
glancing edges show) plus slow cyan motes. On the ground each element has its own language: heat = jittering
sub-atomic foam whose cell walls split into colour fringes, light = diffraction rings fanning into spectra with
six spikes, growth = soft green ripples that overlaps its neighbours,
and `Weave()` links the pieces: kiln flora on every poster stone, moss creeping from the growth region over the
mother's tail and nearby stones, molten cracks running from the crater to the stream and under the light ridge.
The scattered grey decor prefabs are gone.

Optics (`Optics()`), one optical language for the three elements: heat = molten lens blobs over the crater
(`Shaders/HabitatBlock/HeatLens.shader`) that refract the opaque texture behind them with per-channel dispersion,
heat shimmer, melting vertices, highlight bands and a red rim (the habitat camera turns the opaque texture on);
time = translucent veils flowing out of the mother into the other regions, brightness from the thin-film path
length 1/|N.V| so the folds glow (HabitatFX mode 8); light = diffraction starbursts on lamps and crystals with
each colour spread by wavelength (HabitatFX mode 9).

Crystal optics (`RefineOptics()`, also **Tools > Capstone > Art Direction > Habitat Block: Refine Optics In Place**,
which edits the saved scene without rebuilding it so hand edits stay): no sparkles; `Shaders/HabitatBlock/Crystal.shader`
gives clear specular gems (flat facets, Schlick Fresnel with high F0, GGX sun highlight, sky reflection, refraction
with dispersion, total-internal-reflection "fire" from the sky, Beer-Lambert tint) and fluorescent minerals
(uranium green, fluorite violet, apatite cyan) that glow from the light they receive, with a small light of their
own, a soft halo shell (HabitatFX mode 11) and a glow pool on the ground. Every crystal casts caustics (mode 10:
focused light filaments with per-channel dispersion) on the ground away from the sun. Growth = backlit chlorophyll
leaf membranes over the spring (mode 12, alpha blended: path-length absorption, clear lime face-on, dark green
edge-on, veins) on a lime light pool. Note: **Create Habitat Block Scene** rebuilds from scratch and would undo hand edits in the scene.

A cyan stream runs from the spring through the bones with glowing veins beside it and falls past a cut in the
front face that shows the inner world cave (violet black, cyan lines, inner glow decor). Grey inked decor marks
the borders between regions. The ground uses calmer copies of the world line materials (no hatching, wider
lines); those and the other tuned copies are in `Assets/Materials/HabitatBlock/` and are rewritten on rebuild.

Creatures are static mesh copies (no scripts), so the scene holds still in Edit and Play mode. The `Callouts`
object (off by default) labels each region. `Views` holds the camera positions the capture menu renders (hero,
front cut, back high, one per region, inner world, top). Scene_Daniel and Scene_Lam are not touched;
rerunning the menu rebuilds the scene from scratch, so tune it in the setup script rather than by hand.

## Saved data

Everything the game saves is under `Application.persistentDataPath/Reflection/` (journal, drawings, casts,
landmark progress). See the Meditation README.
