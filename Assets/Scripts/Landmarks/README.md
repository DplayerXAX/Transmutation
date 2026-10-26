# Landmarks

Large built places in the generated world that reward exploring with **seeds** and **inscriptions**
(glyphs). Landmarks are architecture, not creatures. There is one so far: the **Hanging Spire**.

## Setup

**Tools > Capstone > Landmarks > Add Hanging Spire To Open Scene** (`Editor/LandmarkSetup.cs`) creates the
materials in `Assets/Materials/Landmarks/`, adds a `HangingSpire` linked to the `ProceduralWorld`, and adds
`SteleInscriptions` to the Meditation object (if there is one) so learned glyphs show on the steles.

**Tools > Capstone > Landmarks > Reset Landmark Progress (Seeds And Glyphs)** clears the saved progress.

## Editing landmarks in the Scene view

The landmarks are still built by code, but they also show while editing (`Editor/LandmarkScenePreview.cs`,
toggle **Tools > Capstone > Landmarks > Show Landmarks In Scene View**, on by default). The `HangingSpire` and
`BoneKiln` objects in the scene are handles: each is copied onto an unsaved preview object named
`[Preview] ... (not saved)` and built there with the same code as in Play mode, without the creatures and the
kiln's children (they roam anyway). Clicking the preview selects the handle. Previews are removed before Play mode
and never saved.

- **Placement > Place At This Object**: the landmark uses the sinkhole nearest to its object (the kiln skips the
  spire's) instead of `preferredDistance`. Move the object and the preview follows; it is rebuilt when the mouse
  is let go. **Tools > Capstone > Terrain > Make Scene Editable** turns this on and puts both handles where the
  landmarks stand now, so nothing moves until you move it.
- **Move Its Sinkhole Under This Object** (inspector button, saved terrain only): moves that sinkhole in the
  saved layout to the handle, rebuilds the world and the landmark. Sculpt edits are kept. Ctrl+Z does not undo it.
- Inspector changes (height, seed, ledges...) rebuild the preview too. Only move the handles; turning them does
  nothing (the door still faces the spawn).
- `BuildPreview()` on both components builds in edit mode; `LandmarkShapes.Discard` destroys safely in both modes.

## Hanging Spire

`HangingSpire` waits until `ProceduralWorld.LayoutReady`, picks a sinkhole and builds around it:

- **Outside:** a huge hollow spire with a door, slit windows, a jagged crown and ledges spiralling up. The
  whole wall is steep enough for `HandClimber`.
- **Top:** a beam spans the crown with a **seed** above its middle.
- **Inside:** the hollow core is the sinkhole shaft. A `ShaftDraft` trigger slows the fall with an updraft.
- **Below:** an upside-down twin hangs from the inner world's ceiling under the same hole. The player drops
  out of its mouth onto an **inscription**. The same glyph stands over the door.

Settings:

- Placement: `preferredDistance` (picks the sinkhole whose distance from spawn is closest, default 85 m)
  or `sinkholeOverride`.
- Shape: `seed`, `height` (72), `ledgeCount`, `windows`, `hangingLength` (share of the inner world's
  height), `halo`.
- Rewards: `seedId`, `glyphId`.
- Materials: spire, inner spire, glyph, seed (shader `Assets/Shaders/Landmarks/SpireStone`).

## Scripts

| Script | Job |
| --- | --- |
| `HangingSpire` | Builds the landmark from the world's sinkhole and column heights. |
| `LandmarkShapes` | Mesh builders: hollow spire (`SpireShape` radius functions), ledges, beams, halos, glyph strokes, seed pods. |
| `LandmarkPickup` | A seed or inscription taken by touching it. Hovers, turns, and does not come back once taken. |
| `LandmarkProgress` | Static save of collected seed and glyph ids (`Reflection/landmarks.json`). Raises `Changed`. |
| `LandmarkGlyphs` (in `LandmarkProgress.cs`) | Stroke data for each glyph id; makes up a simple glyph for unknown ids. |
| `LandmarkToast` | Short fading text low on screen ("A seed.", "The sign settles somewhere in your mind."). |
| `ShaftDraft` | Trigger with a gentle updraft so the player falls slowly. |
| `SteleInscriptions` | On the Meditation object: carves each learned glyph high on a reflection-space stele. |

## Links to other systems

- **World:** needs a `ProceduralWorld` with at least one sinkhole (`SinkholeCentre`, `ColumnHeights`).
- **Climbing:** designed to be climbed with `HandClimber`.
- **Meditation:** learned glyphs appear on the steles.
- **Audio:** the audio setup menu adds a `MusicChannel` (`BGM_landmark`, 15–70 m) to the spire.
