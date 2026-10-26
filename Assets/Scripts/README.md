# Systems Index

One README per game system. Start here.

| System | README | Covers |
| --- | --- | --- |
| Player | [Movement/README.md](Movement/README.md) | Movement, camera, wall guard, hand climbing, procedural body, carrying, interaction, prompt HUD |
| Creatures | [Creatures/README.md](Creatures/README.md) | `Creature` base, fire cycle (Source, Flower, Bubble, Eater), Fuzz, Tentacle |
| World generation | [MarchingCubes/README.md](MarchingCubes/README.md) | `ProceduralWorld` (surface, caves, inner world), `MarchingCubesVolume`, density shapes |
| World look and sky | [World/README.md](World/README.md) | `WorldDecorator`, `CaveHarvest`, `TerrainLinesLit` and decor shaders, skyboxes |
| Meditation | [Meditation/README.md](Meditation/README.md) | M key meditation, reflection space, drawing on steles, creature journal and casts |
| Landmarks | [Landmarks/README.md](Landmarks/README.md) | Hanging Spire, seeds, inscriptions, glyphs on steles |
| Basin test level | [BasinLevel/README.md](BasinLevel/README.md) | Small basin, two towers, three creature routes, creature regrowth |
| Main menu | [MainMenu/README.md](MainMenu/README.md) | `Scene_MainMenu`, 3D buttons for Map, Test Level and Museum, Build Settings order |
| Audio | [Audio/README.md](Audio/README.md) | `AudioManager`, adaptive music layers, Wwise and Unity backends, footsteps |
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
| Main Menu > Create Main Menu Scene | Scene_MainMenu with Map / Test Level / Museum buttons, menu first in Build Settings |
| Main Menu > Add Menu And Game Scenes To Build Settings | Menu, Scene_Daniel, Scene_Basin, Scene_Museum (if present) first in Build Settings |
| Audio > Set Up Adaptive Music In Open Scene | Event bank, AudioManager, landmark / cave channels, footsteps |
| Audio > Add Music Zone At Scene View | Box zone with a `MusicChannel` |
| Audio > Relink Clips By File Name | Fills Unity clips in the bank from `Assets/Audio` |

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
- `Animation/` — `SkinnedMeshAnimator`, blend-shape ping-pong for modelled creatures.
- `Dev/` — `ClimbTestDriver`, automated climbing test (editor started with `-climbTest`, see `Editor/ClimbTestLauncher`).
- `RenderingTests/` — experiments: dither renderer feature, photo capture, sphere post-process volume.
- `Utilities/` — `DestroyAfterSeconds`, `Emitter` (spawn and launch a prefab), `XRotator`.

## Saved data

Everything the game saves is under `Application.persistentDataPath/Reflection/` (journal, drawings, casts,
landmark progress). See the Meditation README.
