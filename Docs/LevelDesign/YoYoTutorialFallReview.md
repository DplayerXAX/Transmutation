# YoYo Tutorial Fall — encounter-flow review

The level introduces useful creatures in separate, readable spaces before opening into a mixed-creature basin. Terrain shapes the route and the view; broad slopes also allow climbing and exploration between the intended paths.

## Critique of the previous layout

- **The southern shortcut weakened the introduction.** A low, wide connection led toward the final basin before the individual encounters had a clear sequence. The revision replaces that flat shortcut with a climbable ridge and gives the encounter route a distinct entrance and destination.
- **Terrace edges read as walls.** Narrow height transitions around the plateaus produced abrupt faces. Broad slopes now connect the interior hills, with gentler approaches along the main route. The outer rim remains steeper and keeps the existing non-grippable world-edge protection.
- **The spider encounter lacked the cave shown in the design.** It was an exposed shelf. It is now a web grotto with an entrance visible from the approaching path and a second opening toward the upper route.
- **The cave exit combined a steep grade with steep joins.** Its original ascent rose about 28 metres over 41 horizontal metres, approximately 34 degrees. The longer, bending exit uses a continuous floor profile to remove the small steep sections created by overlapping tunnel ends. The BounceShroom obstacle remains at the entrance to this ascent.

## Revised route and reveals

**BounceShroom cave → compost hollow → bubble-flower pocket → spider grotto → upper route and optional tentacle cave → ridge lookout → mixed basin.**

The compost encounter includes nearby bones and food so its purpose can be tried locally. Hills hide the bubble flowers from that pocket, and the spider is hidden from the flower pocket until the player approaches the grotto. The mixed basin stays behind the ridge until the lookout. Climbing the surrounding slopes provides alternative approaches without adding progression locks.

The lower spire stays grounded. The taller spire floats with approximately 21 metres of clearance below its lowest rendered structure. Its platforms, seed, and collision geometry move together.

The sky uses Daniel's `MAT_Sky_FractalCalm` material and Morphing Fractal shader. Seven native bird spawn areas distribute five birds each across the entrance, compost, flower, spider, ridge, dragon-bone, and spire regions. Their shared flight bounds cover the basin.

## Checks

- Sampled cave-exit floor and head clearance: clear at all 45 test points.
- Highest sampled exit walking-surface angle: approximately **24 degrees**; the authored route segments are approximately **19 degrees** or less.
- All 21 main-route waypoints have generated ground and clear standing space.
- Seven terrain visibility checks passed: compost visible on approach; flowers hidden from compost; spider hidden from flowers; spider revealed at the grotto mouth; mixed basin hidden from flowers and the near side of the ridge; mixed basin visible from the lookout.
- Approximately 90% of sampled interior height-field slopes are below **38 degrees**. The steepest sampled interior surface is approximately **76 degrees**, below the current controller's 80-degree ground-slope setting. Cave walls and the outer rim remain distinct from the gentler route surfaces.

These geometric checks establish clearance and intended reveals. A human playthrough is still needed to judge pacing, recognition, and whether the route feels obvious enough without signage.

The level remains an ordinary saved `TerrainSculptData` asset, editable through the existing Sculpt and Place window. Existing sculpt differences were retained during the profile change. No creature reset or new progression-management system was added.
