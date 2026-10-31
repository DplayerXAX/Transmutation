# Daniel color experiment

Scene: `Assets/Scenes/Scene_Daniel.unity`, current `ColorExpriments` branch.

The opening preserves the saved player start and faces the tower. Its arrival hall, wider mushroom chamber, enclosing rock, floor and raised exit are sculpted directly into the ProceduralWorld density and saved in Daniel's terrain asset. The temporary cube walls, roof, floor, ledge and sphere buttresses have been removed. Three expandable red mushrooms sit below an approximately 8.5 m ledge: the player's mass-one, 12 m/s jump reaches at most approximately 7.34 m, while the 22 m/s mushroom launch clears it. No new progression lock was introduced. The cave and its exit join a sculpted starting clearing and a path to the basin on the left while facing the tower.

The replacement uses `ProceduralWorld.SculptDensity` to combine an enclosing rock mound with carved hall/chamber volumes, a raised terrain shelf and an elevated exit passage. Density edits outside the bounded opening are preserved. **Tools > Capstone > Daniel > Replace Opening With Procedural Cave** applies this stage once; the main experiment setup includes it too.

The Asura basin contains 12 melee, 6 ranged, 3 generals and 3 spare ring launchers. Melee and ranged split evenly between Light and Dark; generals are two Light and one Dark. Creatures remain visible with simulation and physics held until approach. General combat deaths retain the existing single Blast Core drop. Projectiles retain gameplay polarity None while their visuals inherit emitter colors.

One Ore Colossus has an authored roaming anchor. Storms use existing gameplay water, with a 32 m bounded region, 12–25 seconds of rain and 30–60 seconds of clear weather. The starting habitat contains three squirrels, trees and food, and four bees with a hive and nectar plants. Habitat generators wait for local terrain colliders.

## Visual changes

| Family | Treatment |
| --- | --- |
| Organic | Red/coral bodies, pale stems and skeletal accents; new simple mushroom, goo, squirrel and fire-eater silhouettes |
| Time | Green skeletal birds, spider/web details and larva aura |
| Heat | Blue heat palette, heated fur tips, dotted heat waves and dragon warmth |
| Light | Pearl and iridescent bubbles, bees, honey and crystals |
| Asuras | Pale pink Light and black-violet Dark, combined ring sculptures and pupil-less spheres; distinct melee, ranged and general proportions |
| Launchers and rockets | Floating axial rings and central core; shooting flash; source-colored rockets |
| Bone dragon family | Geometry, animation, riding and behavior retained; elemental colors and flora rotated |

Shared material changes and runtime art adapters also affect creatures instantiated in other scenes. Gameplay statistics, AI targeting rules and collider shapes remain unchanged. Optional rain/colossus placement fields preserve previous behavior when unset.

## Verification

- Unity imported and compiled the new scripts and shader.
- Play Mode frame progression confirmed with `wait_for`.
- Existing isolated validations: 55 Asura combat checks, 131 Ore Colossus checks (including fluid), 44 goo checks passed.
- Live Daniel: 21 living Asuras and three spare cores remained dormant before approach; approach activated the encounter.
- Live General damage death produced exactly one usable launcher, even with a repeated death call. The launcher fired a projectile with polarity None.
- Expanded mushroom accepted a launch through the real `BouncySurface`.
- Live counts: three squirrels and four bees; colossus built and walked.
- Procedural opening revision: floor, ceiling, side wall, ledge and exit floor confirmed as world-chunk collisions; the arrival hall and elevated exit passage are clear. No direct primitive shell renderers remain. Play Mode confirmed the original player start and three mushrooms. Reapplying this stage leaves the terrain revision unchanged.
- Basin storm accepted; a storm requested at the starting cave was rejected. Active rain droplets observed.
- Bone ride and bee suites that require a different scene/batch mode were not run against Daniel.
- Existing Wwise missing SoundBank errors remain in this checkout; audio could not be verified.

The art is a basic procedural prototype, suitable for further visual tuning. Before/after inspection images are under ignored `Temp/Planning/`. Use **Tools > Capstone > Daniel > Build Color Experiment** to complete missing authored stages; version markers prevent duplicate placements or repeated palette rotation.
