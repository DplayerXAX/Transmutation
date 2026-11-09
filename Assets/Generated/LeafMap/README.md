# Leaf map — playable layout study

Open `Assets/Scenes/Scene_LeafMap.unity` as the only gameplay scene and press Play. `Scene_Daniel`, the existing tutorials and `Scene_ArtReference` are preserved.

The leaf is approximately 250 m wide and 390 m long. South is the tutorial stem; north is the pointed light grove. The tower can be seen from the tutorial exit. The central route remains broad, while both side routes return toward the tower.

| Region | Landscape / palette | Interaction |
| --- | --- | --- |
| 01 Tutorial stem | Saved underground cave and ascending tunnel from Scene_YoYoTutorialFall; exit overlooks the flower basin | Original underground spawn and three practice BounceShrooms |
| 02 Bubble meadow | Dense original purple four-petal flowers, low moss, gentle rolls and shallow dips | Ride / tether bubbles; gather and plant seeds; observe squirrels |
| 03 Western cliffs | Touches the northwest of the meadow; stepped cliff connected to a broad 29 m high plateau | Tentacle climbing, goo rest stops and a short floating-stone chain |
| 04 Central tower | Existing three-stage hanging spire | Existing climbing, riding and tower rewards |
| 05 Eastern sanctuary | Connects directly to the northeast flower drifts; quiet ground, ivory ribs and cyan time | Bone mother, three young, nearby tentacles; mother still requires 15 fire bubbles |
| 06 Fire rift | Runs east–west above the bone sanctuary; irregular overhangs, projecting strata, talus and cool shafts | Catch rising fire bubbles; cross on floating ground; bring heat to the bones |
| 07 Leaf tip | Fluorite and green growth | Scenic return loop and resources leading back toward the tower |

Planting: pick up a flower seed with RMB, aim at open ground within 6 m and press LMB. Aim at a nest to store it instead. Invalid ground, a blocked ray or another flower keeps the seed in your hand. Seeds retain their planting component through squirrel carrying and nest storage. The scene starts with 49 interactive mature flowers and allows up to 72; 13 of the 49 flowers emit automatically every 12–22 seconds, with staggered timing and a 24-bubble ambient budget. Every interactive flower, including quiet flowers, shoots with LMB while carried; this stays available when the ambient budget is full. Newly planted flowers follow the same one-in-four emitter pattern. The smaller flowers are batched scenery with no movement-blocking colliders. This does not add a lifetime to player bubbles.

The art pass removes the large palm-like leaves and uses approximately 5,000 flowers with the original purple four-petal silhouette and materials. Interactive mature flowers use the original prefab meshes and materials, including flowers grown from seed; distant scenery uses a batched LOD of the same shape. Six collidable floating stones form two short traversal chains. Terrain, cave walls and floating rocks use the exact black HB_Ground material from ArtReference; the sky uses Scene_Daniel’s original MAT_Sky_FractalCalm. This supports the existing palette, with green/purple growth, cyan time and a restrained red heat accent. The ArtReference assets are unchanged. `Tools > Capstone > Leaf Map > 5 Dense Meadow And Geological Rift` reapplies this authored layout and art pass; preserve any later hand placement before running it. The rift and floating route have `09_RiftInterior` and `10_FloatingTraversal` review cameras.

Hierarchy groups are numbered by region. Select `Leaf Map — authored habitats and traversal` to see route gizmos. Terrain uses the normal ProceduralWorld sculpt workflow with a dedicated `LeafTerrain.asset`; hand edits stay local to this scene. Original materials are referenced, or cloned into this folder for scene-specific adjustments.

Review cameras are saved disabled under group 08. Use `Tools > Capstone > Leaf Map > 3 Capture Review Views` to export actual Unity renders into `ArtCaptures/LeafMap`. `4 Validate Planting And Ecology In Play Mode` exercises real ownership and scene behaviour; runtime test changes are discarded when Play Mode ends.

This is a first playable layout and visual pass. The tutorial cave is resampled from the saved Scene_YoYoTutorialFall terrain, preserving its sculpted chamber and continuous ascending route. Its practice actors and player spawn are translated by (112, 8, -61). The source tutorial, ArtReference, Daniel scene and build starting scene are unchanged. Route timing and difficulty still need a human playthrough.

Traversal revision: connected flower drifts across the basin, with a narrow walking route and three optional bubble-lift terraces. West cliff rest ledges connect to the highland. Three flight landings guide warmed bone riding toward the rift. Five canyon recovery ledges create a route between the vent floor and upper bank. A bounce launch leads to four northern canopy stones. Native ArtReference frost flora links the northeast flower edge to the mother. Three children are visible in editor preview and start on that same flower-facing side in Play Mode.

## Grown map, one flower field and the time palette (2026-11-02)

`Tools > Capstone > Leaf Map > 6 Grow Map, Flower Field And Time Decor` (`Editor/LeafMapTimePass.cs`) does three things and then captures the review views:

1. **Grows the leaf 1.4x.** `LeafMapTerrain` now works in "design" coordinates (all the numbers in the code) and scales them horizontally around the tutorial exit (`LeafMapTerrain.Pivot`, design (0, -97)); heights are unchanged. The scale is saved in `LeafTerrain.asset` as `leafScale` and read by `ProceduralWorld` when it loads the layout. The grid is now 376 x 568 m. Every placed object and merged decoration mesh was moved with the scale (merged meshes were copied to `TimePass/Migrated`), the tutorial cave was grafted again in the same place, and the player still starts in it. All public `LeafMapTerrain` methods take world positions; use `LeafMapTerrain.ToWorld(design)` for positions written in the old numbers.
2. **Moves the bone mother** to the east rim of the flower basin (`LeafMapTerrain.MotherDesign`), with her sinkhole, rest clearing and the three children's rest spots (`LeafMapTerrain.ChildRest`).
3. **Dresses the basin** under `10 Time — flower field, bones and sky`:
   - one connected field of the original four-petal flowers (about 14 000, batched, no colliders) that streaks along a flow towards the mother and cools from purple through violet to ice blue round her; the old separate drifts are switched off, not deleted;
   - six tall glass hook ribs on the mother's flower side with webs between them, and half-buried bone relics in the field (a walk-under ribcage arch that can be climbed, a skull, bubbled vertebrae, a vertebra trail into the mother, hook ribs below the exit) plus a few further relics on the west shelf, north saddle, rift lip and leaf tip; sticky film pads and strands wherever bone or silk meets the ground;
   - ArtReference time veils (HabitatFX Veil materials) low over the flowers, arching from the exit, spiralling up out of the mother, and high across the leaf; jelly time ribbons and bright threads through the ribs;
   - iridescent bubble columns and dew, blue glowing mushroom clusters, a time-ripple tide on the ground round the mother and the arch, two bird spawn areas and a sky spider in the arch (settles once its ground has loaded), cold point lights.

The terrain uses the leaf's own copy of the ArtReference ground (`Leaf_InkStone`) with its line centre moved out with the map; `HB_Ground` and the ArtReference scene are unchanged. Re-running the command only rebuilds the decoration (the map is grown once). Review views 13–17 show the field, the mother, the arch and the sky.

**Going back:** `ArtCaptures/LeafMap/BeforeTimePass` holds the scene and terrain asset from before; copy both back (with Unity closed) to return to the first layout. `5 Dense Meadow And Geological Rift` is written in the first layout's numbers and refuses to run on the grown map.

### Second pass: highland cave, merged rift, findable seeds (2026-11-02)

- **Highland:** on the grown map the tutorial cave and its stem are lifted 18 m (`LeafMapTerrain.CaveLift`, also used by `LeafMapTutorialGraft.Offset`). The cave opens high on the stem and looks down a cliff onto the flower basin; the main route ramps down the cliff edge. The player still starts inside the cave (now at y -1.8).
- **One rift:** the fire rift now opens out of the bone mother's crack (her sinkhole is its narrow head) and runs north-east, about 32 m wide at the floating crossing. The pass rebuilds its strata, talus, molten vein, fluorite, five floating stepping stones and six recovery ledges along it, and moves the vent, fire flower and first rising bubbles to the new floor. The old rift dressing is switched off.
- **Findable flowers and seeds:** the decorative carpet leaves a clearing round every interactive bubble flower and seed, and the seed prefab carries an iridescent shell.
- After reshaping, creatures resting on changed ground and earlier decoration pieces are set back on the new ground.

### Third pass: rift across the leaf, calmer composition and ground (2026-11-02)

- The rift now runs east across the leaf from the mother's crack and is cut right through the crust into the inner world (like the sinkholes of Scene_Daniel): floor at the inner ground, strata only in the crust walls, fallen rock and the molten vein on the inner floor, a 12-ledge stair from the inner floor back up one wall, five floating stones across the middle. Rising fire bubbles start above whatever floor is under the rift.
- The highland cliff edge wanders in bays and spurs instead of a straight line.
- Composition: one leading veil from the cave overlook into the mother, her spiral, one high ribbon to the leaf tip, one jelly ribbon through the arch; three relics in the basin and two far away; bubble columns only at the arch and the mother; fungi only at bone feet; no sky threads. The older meadow runnel and pearl reeds are switched off.
- Ground: `Leaf_InkStone` has no hatch grid and fewer, smoother contour lines (HatchStrength 0, RingDistort 0.22, FloorFreq 2.2, RingFreq 8). Rocks from the earlier passes use it too; `HB_Ground` is unchanged.

### Fourth pass: smooth lines, bigger field, bubble hops, mood (2026-11-03)

- Ground lines: `_RingDistort` 0 removes the small-scale warp that made contour lines zig-zag; lines are now plain smooth curves.
- Flower field reaches further up the banks and west (about 18 000 blooms).
- **Bubble hops** (`12 Bubble hops`): solid iridescent bubbles (convex mesh colliders, layer Ground) about 4 m apart, each a little higher than the last. One route climbs from the west of the field up the highland cliff to the cave; one spirals up round the mother to a large lookout bubble. A glowing core marks them as solid. The old decorative bubble columns are gone.
- **Mood:** exponential-squared blue-grey depth fog, a cool fill light against the sun, and a scene-only global Volume (`TimePass/LeafMood.asset`: bloom, vignette, slight grade); post-processing is switched on for the player camera in this scene only.
- **Sculpting:** `Tools > Capstone > Leaf Map > Toggle Decoration In Scene View (Sculpting)` hides the flowers, veils and bones in the Scene view only, so brush strokes redraw quickly. The cave graft now stores fewer edits (samples far from any surface keep the base value).

### Fifth pass: inner world look and scattered bubbles (2026-11-03)

- The leaf world's inner material is Scene_Daniel's `MAT_World_Inner` again, and below the crust the rift counts as inner world, so its floor has the same underground look as Scene_Daniel.
- (Replaced in the sixth pass: the solid model bubbles were removed.)

### Sixth pass: real bubbles for hopping, calmer polar lines (2026-11-03)

- The bubbles to hop through are the real `TrappingBubble`s that bubble flowers shoot, as in Scene_Daniel. `12 Bubble groves` adds about 20 interactive flowers (same `MeadowFlower` prefab, registered with the ecology) that all emit every 5–9 s: west of the field, on the three bubble-terrace crowns and round the flower side of the mother's rim. `MeadowBubble` now rises at 0.45 m/s for 3–17 s and then hovers until it pops after 40–60 s, so bubbles hang 1.5–8 m above ground of different heights. Ride one, escape with Space and drop into the next. The ecology's ambient budget is 64 bubbles near the player. Validation keeps the one-in-four rule for all other flowers.
- Ground: fewer polar rays and rings (`_RayPhaseScale` 5, no extra rays near the camera, `_RingFreq` 5, `_FloorFreq` 1.5).

### Seventh pass: the bubble cascade (2026-11-03)

- Five steep mesas (`LeafMapTerrain.BubbleTierDesign`) step up from the west of the flower basin past the west plateau: tops at 10, 17, 24, 31 and 38 m, too far apart to walk between.
- `12 Bubble cascade` puts 19 interactive bubble flowers on the mesas and at their foot (their own timers off). `World/BubbleCascade.cs` fires them in turn, each once every 7 s at a golden-ratio offset, so neighbours never fire together and the air above the mesas always holds bubbles at many heights. Bubbles rise for 3–17 s and hover, so they hang 1.5–8 m over their mesa, overlapping the next mesa up: ride, steer, escape with Space, drop into the next. Flowers fire only within the ecology's bubble budget and near the player. `BubbleFlower.EmitGroundBubble()` is the small hook the cascade uses.
- `Tools > Capstone > Leaf Map > Capture Bubble Cascade In Play Mode` plays for 40 s at the foot of the cascade and saves `ArtCaptures/LeafMap/Play_BubbleHops.png` (65 bubbles alive in the last run).

### Performance, awakening and the shell (2026-11-03)

- `Tools > Capstone > Leaf Map > Measure Performance In Play Mode` (request `perf`; `perfquick` = baseline only) switches content and every per-frame script type off in turn and writes `ArtCaptures/LeafMap/Perf.txt`. Rendering was not the bottleneck; scripts were, mainly the 14 tentacles and the bone mother's ribs.
- `World/CreatureSleep.cs` (added under the time-pass group) pauses tentacle creatures more than 75 m from the player and wakes them within 60 m. `BoneKiln` updates a sleeping mother's ribs every second frame beyond 40 m and every fourth beyond 90 m. Baseline in the editor went from about 26–42 ms per frame (scripts 11–13 ms) to 13.7 ms (scripts 4.5 ms).
- Awakening: the spine is kept together after collision (`BoneKiln.KeepSpineTogether`, longest link 1.12× its rest length in the test), the skull stays on the neck, she rises out of the hole sooner, the cutscene camera is collided again after smoothing, and the crack's head is a wider nest round her. Test: `Tools > Capstone > Leaf Map > Test Bone Mother Awakening In Play Mode` (request `awaken`) writes `ArtCaptures/LeafMap/Awakening.txt` and frames.
- Shell: the grown leaf has a 7 m solid wall along its outline below the surface, so the inner world is closed in at the sides.

### Bubble lily look (2026-11-03)

- Interactive bubble flowers in the leaf map look different from the decorative carpet: a 1.25 m curved teal stem, a cup of six pearl-blue glowing petals and a small iridescent bud in the cup. The mesh is generated once (`TimePass/BubbleLily_v2.asset`, three submeshes: stem `BubbleLily_Stem`, petals `BubbleLily_Petal`, bud `HB_Bubble`) and drawn by one static renderer per flower; the original primitive renderers stay in the prefab but are switched off. Applied to `MeadowFlower.prefab` (planted and cascade flowers) and to the scene's instances of the shared `BubbleFlower` prefab (as scene overrides); the shared prefab itself is unchanged. The muzzle moved to the cup and the capsule collider grew to the new height, so pick-up and shooting still pass validation. View `20_BubbleLily`.

### Lily field: every flower can be picked up (2026-11-04)

- The bubble lily has two petal tiers: six outer petals (purple / violet / ice by the field's flow) and five pale inner petals standing between them, plus the bubble in the cup.
- The decorative field is now about 14 000 bubble lilies drawn by `World/LilyField.cs` with GPU instancing from `TimePass/LilyField.bytes` (position, yaw, scale, tone per lily). The field mesh `BubbleLily_Field` is one submesh coloured from baked 4 x 1 strips (`LilyFieldColour_*`, `LilyFieldGlow_*`), so each 24 m tile draws once per tone; tiles beyond 70 m draw every third lily. No shadows.
- Every lily can be picked up: lilies near the camera's aim within reach (1.4 m either side of the view ray, up to about 6 m) or within 2 m of the player are swapped (checked ten times a second) for real `MeadowFlower` bubble flowers from a pool of 32 (collide with the ground only, so the player walks through). Carry one off and it stays a real flower and leaves a gap; the rest turn back into instances as the player moves on.
- Bubbles by area: `13 Field bubble zones` has two real emitters per 28 m area (42) fired in turn every 9 s by a `BubbleCascade`. Each cascade stops firing while 45 bubbles are in the scene; bubbles live 28–42 s; the ecology lets flowers within 80 m emit.
- `Tools > Capstone > Leaf Map > Test Lily Field In Play Mode` (request `lilytest`) checks the swap, a pick-up and a shot, counts bubbles and frame time -> `ArtCaptures/LeafMap/LilyTest.txt`. `perfcontent` measures the content groups only.
- The Scene-view decoration toggle also hides the instanced lilies.

- Ground lines: a height-only contour mode was tried on 2026-11-04 and reverted at Daniel's request; `TerrainLinesLit` is back to its committed version and `Leaf_InkStone` to the previous tuning.

## Skeletons, knolls and glowing groves (2026-11-04)

- The scattered relics, rib flanks, fungi and webs are gone. `03 Sleeping skeletons` holds two whole skeletons drawn in the Bone Kiln language: an arched vertebra spine, glass comb ribs (front ribs planted, rear ribs rising and curling back), a half-sunk skull. One lies in the flower field with its head toward the mother, one on the north-east knoll.
- The flower field has no paths any more: lilies grow in organic drifts (`Clump`, warped Perlin), dense clumps with bigger flowers and thin gaps between them.
- `LeafMapTerrain.KnollDesign` adds six soft hills round the leaf (grown map only). `06 Glowing groves on the knolls` puts one night-forest vignette on each: dark trunks with violet glowing cap shelves, luminous seed puffs, mint glowing blades, blue mushrooms and a violet lamp. View `21_GlowGrove`.
- The old leaf-vein path ribbons and reed rows are switched off (kept in the scene, inactive).

## Mother's shaft, rolling ground, short stems (2026-11-04, later)

- The two extra skeletons are gone; the bone mother is the only skeleton.
- The long east-west rift is retired (`LeafMapTerrain.GrownRift = false`, `HasRift`): the mother now dives into her own sinkhole shaft exactly as in Scene_Daniel (neck down the shaft, skull under the inner-world ceiling). `Shaft()` dresses it lightly; the fire vent, its flower and the rising fire bubbles moved to `LeafMapTerrain.VentSpot`, east of her. Set `GrownRift` to true to bring the rift back.
- `LeafMapTerrain.Rolling` adds warped swells and long soft dune ridges over the grown leaf (gentler in the flower basin, none on the cave highland); routes and clearings still flatten on top.
- Lily stems are 40 % of their old height (`LilyHeight` 0.5; meshes `BubbleLily_v4`, `BubbleLily_Field_v2`).

## Mother's ribs and Play start (2026-11-04, later)

- The ribs jumped because the earlier optimisation updated a sleeping mother's ribs only every 2nd/4th frame when the camera was 40 m+ away (from the start point she is 118 m away, so every 4th frame). Now every rib moves every frame; only its physics checks (ground, fuzzy gap) run every 2nd/4th frame (`BoneRib.Drive(target, dt, checks)`).
- The rolling ground fades out within ~24-42 design m of the mother so her body and ribs lie on even ground as in Scene_Daniel.
- Play start: the leaf map builds the whole terrain before the first frame (`blockingRadius` 500) and that build used only the 2-worker budget (~17 s). The blocking phase now uses every core (`ProceduralWorld.Generate`, shared), ~3 s. Entering Play Mode itself (Unity's domain reload + scene load, 20-35 s in tests) is editor-side and unchanged.
- Test: request `mothertest` (`Editor/LeafMapMotherTest.cs`) -> `ArtCaptures/LeafMap/MotherTest.txt`: Play Mode step timings, long frames, rib motion per frame, the mother's warmth. `motherprobe` lists solid colliders round her.
