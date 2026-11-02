# Art Formation

Open `Assets/Scenes/Art Formation.unity`.

Each row is a distinct non-color surface treatment from Scene_ArtReference. Each column supplies one named color. Every cell contains the source blob, crystal cluster, smaller bone dragon, and leaf, normalized to a comparable size.

In Play mode: mouse wheel zooms; right/middle drag or WASD/arrows pans at a fixed height and viewing angle; Shift moves faster; Home fits the whole board; End returns to the opening detail; C jumps to the custom color column; Page Up/Down moves one style row. Click the Game view to give it keyboard focus. The standard Scene view also works.

Select the Art Formation root in the Hierarchy. In the Inspector, click Custom Color to open Unity's built-in color picker (hue, saturation, value, RGB or hex). It updates the Custom column across every row in Edit mode and Play mode. It uses per-renderer overrides and never changes source materials. Color edits made in Play mode reset when Play mode stops; tune in Edit mode and save the scene to keep a color.

Source scene, source materials, and source shader code are retained. Generated board-only shader copies remove source RGB hue at the final fragment output, preserving tonal patterns, alpha, animation and HDR intensity. Material color properties are neutralized first. Flat unlit is the color baseline. The shell-dot heat-haze vertex program is adapted to dots on each specimen surface, because its original billboard expansion would destroy the example models. Specimen UVs are standardized; original vertex tint is removed. Glass and lens rows require the camera's opaque color texture, enabled on the scene camera. Transparent/additive styles are shown against divided neutral supports.

Styles differing only in source RGB, random seed, or the generic Speed/SwaySpeed properties share a row. Other numeric settings, textures, shader keywords and render queues remain distinct. The list below preserves every source material mapping. Text-label font material is not a surface treatment.

Rebuild from Tools > Capstone > Art Direction > Build Art Formation. This replaces the generated board and catalog.

## Rows

1. **Flat / no pattern** — Universal Render Pipeline/Unlit; sources: Assets/Materials/Landmarks/MAT_Landmark_Ink.mat
2. **Stone / hatch and dots** — Capstone/World/Decor; sources: Assets/Materials/World/MAT_Decor_Stone.mat
3. **Stone / inner shell** — Capstone/World/Decor; sources: Assets/Materials/World/MAT_Decor_InnerShell.mat
4. **Terrain / cave contours** — Capstone/World/TerrainLinesLit; sources: Assets/Materials/HabitatBlock/HB_Inner.mat
5. **Terrain / contour lines** — Capstone/World/TerrainLinesLit; sources: Assets/Materials/HabitatBlock/HB_Ground.mat
6. **Water / flow lines** — Capstone/World/ErosiveWater; sources: Assets/Materials/Fluid/MAT_ErosiveWater.mat
7. **Poster / dots** — Capstone/ArtDirection/PosterPattern; sources: Assets/Materials/HabitatBlock/HB_Poster_BoneDots.mat
8. **Poster / spots** — Capstone/ArtDirection/PosterPattern; sources: Assets/Materials/HabitatBlock/HB_Poster_HeatSpots.mat
9. **Poster / stars** — Capstone/ArtDirection/PosterPattern; sources: Assets/Materials/HabitatBlock/HB_Poster_LightStars.mat
10. **Poster / stripes** — Capstone/ArtDirection/PosterPattern; sources: Assets/Materials/HabitatBlock/HB_Poster_GrowthStripes.mat, Assets/Materials/HabitatBlock/HB_Poster_HeatRing.mat
11. **Poster / terrazzo** — Capstone/ArtDirection/PosterPattern; sources: Assets/Materials/HabitatBlock/HB_Poster_GrowthTerrazzo.mat
12. **Poster / waves** — Capstone/ArtDirection/PosterPattern; sources: Assets/Materials/HabitatBlock/HB_Poster_HeatWaves.mat
13. **Bone / marbled bands** — Capstone/Landmarks/BoneMarble; sources: Assets/Materials/Landmarks/MAT_Landmark_BoneMarble.mat
14. **Bone / translucent comb** — Capstone/Landmarks/BoneComb; sources: Assets/Materials/HabitatBlock/HB_BoneComb.mat
15. **Flora / vertex shading** — Capstone/Landmarks/Flora; sources: Assets/Materials/HabitatBlock/HB_Flora.mat
16. **Kelp / veins and beads** — Capstone/Realm/RealmKelp; sources: Assets/Materials/HabitatBlock/HB_GreenKelp.mat
17. **Membrane / bubble** — Capstone/Realm/RealmMembrane; sources: Assets/Materials/HabitatBlock/HB_Bubble.mat
18. **Membrane / ribbon** — Capstone/Realm/RealmMembrane; sources: Assets/Materials/HabitatBlock/HB_TimeRibbon.mat
19. **Root / grain** — Capstone/Realm/RealmRoot; sources: Assets/Materials/World/MAT_Realm_Anemone.mat
20. **Slime / luminous veins** — Capstone/Realm/RealmSlime; sources: Assets/Materials/HabitatBlock/HB_MoltenVein.mat, Assets/Materials/World/MAT_Realm_Vein.mat
21. **Slime / spots and stripes** — Capstone/Realm/RealmSlime; sources: Assets/Materials/World/MAT_Realm_Slime.mat
22. **Soft / mushroom gills** — Capstone/Realm/RealmSoft; sources: Assets/Materials/HabitatBlock/HB_GrowthMushroom.mat
23. **Thread / moving beads** — Capstone/Realm/RealmThread; sources: Assets/Materials/World/MAT_Realm_Thread.mat
24. **Crystal / Apatite** — Capstone/ArtDirection/Crystal; sources: Assets/Materials/HabitatBlock/HB_Crystal_Apatite.mat
25. **Crystal / Clear** — Capstone/ArtDirection/Crystal; sources: Assets/Materials/HabitatBlock/HB_Crystal_Clear.mat
26. **Crystal / Fluorite** — Capstone/ArtDirection/Crystal; sources: Assets/Materials/HabitatBlock/HB_Crystal_Fluorite.mat
27. **Crystal / Smoky** — Capstone/ArtDirection/Crystal; sources: Assets/Materials/HabitatBlock/HB_Crystal_Smoky.mat
28. **Crystal / Uranium** — Capstone/ArtDirection/Crystal; sources: Assets/Materials/HabitatBlock/HB_Crystal_Uranium.mat
29. **Glitch / signal bands** — Capstone/GlitchedPool; sources: Assets/Materials/GlitchedPool.mat
30. **Heat / tonal ramp** — Capstone/Effects/HeatMap; sources: Assets/Materials/Creatures/HeatMap.mat
31. **Static / grain flashes** — Capstone/Creature/FireBubble; sources: Assets/Materials/Creatures/FireBubble.mat
32. **Static / stretched waves** — Capstone/Creature/FireFlower; sources: Assets/Materials/Creatures/FireFlower.mat
33. **FX / broad halo** — Capstone/ArtDirection/HabitatFX; sources: Assets/Materials/HabitatBlock/HB_FX_HaloApatite.mat
34. **FX / fine halo** — Capstone/ArtDirection/HabitatFX; sources: Assets/Materials/HabitatBlock/HB_FX_HaloFluorite.mat, Assets/Materials/HabitatBlock/HB_FX_HaloUraniumGlass.mat
35. **FX / flowing veil** — Capstone/ArtDirection/HabitatFX; sources: Assets/Materials/HabitatBlock/HB_FX_Veil0.mat, Assets/Materials/HabitatBlock/HB_FX_Veil1.mat, Assets/Materials/HabitatBlock/HB_FX_Veil2.mat, Assets/Materials/HabitatBlock/HB_FX_Veil3.mat
36. **FX / growth ripples** — Capstone/ArtDirection/HabitatFX; sources: Assets/Materials/HabitatBlock/HB_FX_GrowthPool.mat
37. **FX / leaf membrane** — Capstone/ArtDirection/HabitatFX; sources: Assets/Materials/HabitatBlock/HB_FX_Leaf.mat
38. **FX / soft glow** — Capstone/ArtDirection/HabitatFX; sources: Assets/Materials/HabitatBlock/HB_FX_GlowApatite.mat, Assets/Materials/HabitatBlock/HB_FX_GlowFluorite.mat, Assets/Materials/HabitatBlock/HB_FX_GlowUraniumGlass.mat
39. **FX / spectral caustics** — Capstone/ArtDirection/HabitatFX; sources: Assets/Materials/HabitatBlock/HB_FX_CausticsApatite.mat, Assets/Materials/HabitatBlock/HB_FX_CausticsClearGem.mat, Assets/Materials/HabitatBlock/HB_FX_CausticsFluorite.mat, Assets/Materials/HabitatBlock/HB_FX_CausticsSmokyGem.mat, Assets/Materials/HabitatBlock/HB_FX_CausticsUraniumGlass.mat
40. **Haze / broad dot shells** — Capstone/Effects/HeatHaze; sources: Assets/Materials/HabitatBlock/HB_HeatHaze_CraterHaze.mat
41. **Haze / fine dot shells** — Capstone/Effects/HeatHaze; sources: Assets/Materials/HabitatBlock/HB_HeatHaze_FlowerHaze.mat
42. **Lens / refraction** — Capstone/ArtDirection/HeatLens; sources: Assets/Materials/HabitatBlock/HB_HeatLens.mat

## Color columns

- Gray (`#92969E`) — Neutral
- Red (`#C71F38`) — Red
- Orange (`#F58632`) — Orange
- Yellow (`#F4D348`) — Yellow
- Green (`#42D96E`) — Green
- Teal (`#2CB49F`) — Teal
- Cyan (`#36D5E7`) — Cyan
- Blue (`#1F5EFF`) — Blue
- Purple (`#A350DA`) — Purple
- Pink (`#E780AC`) — Pink
- Brown (`#A2774A`) — Brown
- White (`#F3F4F7`) — Neutral
- Black (`#10121A`) — Neutral
- Custom (`#C08BFF`) — Custom

## Specimen sources

- Blob: Habitat Block/Poster: Heat/Wave Dome
- Crystal: Habitat Block/Light (back right)/Crystals
- Bone Dragon: Habitat Block/Bone Mother, Time, Distortion (front left)/Bone Child
- Leaf: Habitat Block/Leaf Growth/Leaf Membrane
