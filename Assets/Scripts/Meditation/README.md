# Meditation and the Reflection Space

Press **M** to sit down. The world (sky, then terrain and creatures) is slowly erased into blank paper, and
the player wakes in the **reflection space**: a pale floor in a paper void with a ring of black stone steles
and tablets to draw on, and casts of every creature met so far. Press **M** again to return.

## Setup

**Tools > Capstone > Meditation > Add Meditation To Open Scene** creates the materials in
`Assets/Materials/Meditation/` and a `Meditation` object with all five components below.

Other menu items:

- **Open Saved Drawings Folder**
- **Clear Saved Drawings And Journal**

## Components (all on the `Meditation` object)

| Script | Job |
| --- | --- |
| `MeditationController` | Runs the whole sequence: enter, inside, leave. |
| `ReflectionSpace` | Builds the space once at runtime and moves it to where the player meditates. |
| `CreatureJournal` | Remembers each creature type the first time the player is near it. |
| `ReflectionVisitor` | Player control inside the space: walk, look, draw, move casts. |
| `MeditationHUD` | Hints, "Remembered: ..." toasts, and the full-screen paper blank. |

Helpers (no component): `MeditationVeil` (the erasing sphere), `InkSurface` (a drawable face),
`CreatureCast` (frozen copy of a creature's meshes), `ReflectionCast` (a cast standing in the space),
`MeditationShapes` (procedural meshes), `ReflectionStorage` (files).

## The sequence

1. **Enter** (only on the ground, otherwise a "Find still ground" toast): the player's controller, body,
   carrier, interactor, climber and prompt HUD are paused and creatures are frozen. The view sinks
   (`sinkDepth`) and look slows (`settleLook`).
2. **Erase:** the sky fades, then `MeditationVeil` (an inside-out paper sphere) shrinks around the player,
   hiding everything beyond it.
3. **Jump:** behind the paper blank, the space is placed `spaceHeight` (3000 m) above the spot and revealed.
4. **Inside:** `ReflectionVisitor` drives the player and camera.
5. **Leave:** drawings are saved, the space is erased, the player returns to the exact spot and the world
   is revealed from near to far. Control is handed back.

Timing: `settleTime`, `skyEraseTime`, `worldEraseTime`, `spaceRevealTime`, `spaceEraseTime`,
`worldRevealTime`, all scaled by `transitionSpeed` (2 = twice as fast).

## Inside the space

| Input | Action |
| --- | --- |
| WASD + mouse | Slow walk (`walkSpeed` 2.4) and look |
| LMB on a stele / tablet | Walk the camera up to the face and start drawing |
| LMB / RMB while drawing | Draw / erase (`brushPixels`, `eraserPixels`) |
| E or Esc while drawing | Step back |
| E on a cast | Pick it up / set it down |
| M | Return to the world |

Layout (`ReflectionSpace`): `seed`, `floorRadius`, `walkRadius`, `steleCount`, `tabletCount`,
`steleDistance`, `tabletDistance`. **Changing the seed changes every stele shape, so saved drawings no
longer fit.**

## Creature journal

Every `scanInterval` it checks creatures within `encounterDistance` (6 m) that are on screen
(`requireInView`). The first of each type (by class; all Fuzz Creatures share one entry) is recorded and a
`CreatureCast` is copied from its renderers at real size, capped at `castMaxSize`. Nothing is recorded while
meditating. New casts appear in the space next time; where the player put them is saved too.

## Saved files

Under `Application.persistentDataPath/Reflection/`:

- `journal.json` — met creatures and cast positions
- `drawings/<id>.png` — one ink texture per face
- `casts/<key>.mesh` — cast meshes (materials are found again by name)
- `landmarks.json` — seeds and glyphs, written by the Landmarks system

## Links to other systems

- **Player:** pauses and restores the movement scripts in `Scripts/Movement`.
- **Creatures:** the journal reads any `Creature`; casts are frozen while meditating.
- **Landmarks:** `SteleInscriptions` (on the same object) carves learned glyphs high on the steles.
- Shaders: `Assets/Shaders/Meditation` (`MeditationVeil`, `MeditationInk`).
