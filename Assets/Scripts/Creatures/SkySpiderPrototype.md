# Sky Spider / TimeSpider prototype

Run **Tools > Creatures > Create Sky Spider Assets** once, then place
`Assets/Prefabs/Creatures/SkySpider.prefab` at the desired airborne web center.
The setup command creates assets only; it never opens or saves a scene.
Repeated setup runs preserve existing prefab and material edits.

## Behavior and controls

- `SkySpider`: Web Scale (1), return range (12 m), movement speed, turning speed,
  and patrol pauses. Set Web Scale on the spider to control the webs it spawns:
  0.5 is half size, 2 is double size. Each new web receives that value once,
  before creating its meshes. Changing the spider's value affects future webs;
  returning to an existing web retains that web's original size.
  Pickup uses the existing creature carrier. A nearby drop returns to one of that
  spider's webs; a distant drop starts a new one there.
- `SkyWeb`: maximum radius (6 m), growth duration (45 s), random seed, cell count
  (7), strand budget (256), patch budget (6), thicknesses, and trapping layer mask.
  Cluster Spacing sets the distance between groups; Cluster Spread sets spacing
  within each group. The default layout has two small pairs and three individual
  cages on separate branches. Cell Size controls cage size relative to the web.
  Loose Strands Per Cell adds long individual threads within the existing budget.
  The web's own Web Scale is the default for standalone webs. A spawning spider
  overrides it with its Inspector value without modifying the shared prefab.
  Web Scale uniformly scales newly generated geometry: 0.5 is half size, 1 is
  normal, and 2 is double size. It scales positions, thicknesses, patches, patrol
  and trapping together without changing the seed, growth time, or geometry count.
  Maximum Radius remains the base construction radius; actual radius is
  Maximum Radius times Web Scale. Set these on the prefab before generating a web.
  Edit these on the web prefab referenced by the spider.
- Web geometry stays at its starting world position and uses two combined meshes:
  open D12 cages (12 pentagonal faces, 20 corners, 30 edges), joined by strands,
  and sparse transparent pentagonal patches.
  With **Walkable** on (default), every grown strand also gets a capsule collider (at least
  **Walk Radius**, 0.1 m) on the Ground layer, so the player can walk on and hand-climb the web.
  The building spider ignores these colliders. The thin strands use six-sided tubes
  for thickness; the cells themselves are dodecahedrons. No rope or cloth
  physics is simulated. Meshes update when a strand is added and stay static when
  complete. The first cell exists immediately so the spider has a patrol volume.
- Only the current web grows. Carrying, returning, or pausing the spider pauses its
  construction. Abandoned webs remain in the play session and continue trapping.

## Colors

Select `Assets/Materials/Creatures/SkyWebStrands.mat` and change **Silk Color**
(fine strands) and **Support Color** (cage edges and connections) in the Inspector.
Both default to black. **Tools > Creatures > Set Sky Web Strands Black** resets
both colors explicitly. Patches use the separate `SkyWebPatches.mat` material.
Player movement is unchanged; slow falling is deferred.

## Trapping

Silk captures dynamic Rigidbody objects with solid colliders. The player,
spiders, carried creatures, static/kinematic bodies, and trigger-only colliders
are excluded. Transparent patches do not trap.

Contact uses a conservative sphere around collider bounds and the swept body
center against completed strand segments. Wide or elongated objects can stick
slightly before their actual surface touches a strand. This is an intentional
prototype approximation; there is no collider or GameObject for each strand.
Bodies beyond the web's broad-phase region in both samples can miss detection
at extreme speed. The overlap buffer grows on unusually crowded frames.

`WebCapture` creates a world-connected FixedJoint and temporarily pauses an
attached Creature's simulation. Pickup releases the attachment before carry
physics takes ownership. Disable/remove the capture or web to release it;
the prior creature simulation setting is restored. A short capture cooldown
prevents pickup or cleanup from immediately sticking the object again.

`SkyWeb.Captured` supplies the `WebCapture` with `Body`, `ContactPoint`, and
`OwningWeb` for prey responses. `SkyWeb.Captures` is a cached read-only view of its
live captures. `SkyWeb` itself derives from `Creature` and cannot be carried.
Escape, terrain-aware placement, and following individual strands are deferred.

## Hunting remaining Age

`SkySpider` implements the TimeSpider behavior; existing script, prefab, and scene
references keep their names. Initialization sets Food Tag to **Meat** and Diet to
**Everything**, without introducing another food category or editing the prefab.

The spider chooses the nearest eligible creature captured in its **current web**,
then keeps that target until it feeds or the target becomes invalid. It completes
an existing return-to-web movement before hunting. Hunting interrupts patrol pauses
and uses the existing movement speed and turn speed. Abandoned webs keep trapping,
but the spider does not hunt their prey until it returns to that web.

**Bite Range** defaults to 0.75 metres, measured from the spider's Rigidbody position
to the nearest enabled solid collider surface attached to the prey's captured body.
Compound and offset colliders work without requiring the spider to reach the body origin.
On arrival, `TryDrainCapturedAge(capture)` revalidates range, ownership, and both creatures.
It adds the prey's full remaining Age to its own, releases the silk, and sets the victim's
remaining Age to zero through the shared age-death path. There is no additional Age cap,
and the spider's existing Age Rate is preserved (default zero).

Prey must be active, alive, not already pending consumption, in the same scene, and
have positive Age. Their capture-paused simulation and Age Rate zero do not protect
them from draining. Plain Rigidbody objects and dead remains are ignored. Released,
carried, bubble-captured, disabled, or removed prey cannot be drained. Carrying,
bubble capture, pausing, disabling, or death prevents the spider from hunting or biting.
Invalid targets are discarded; the spider resumes hunting or patrol. Dead, disabled,
destroyed, or foreign-scene webs cannot be selected as its current web.

This attack does not call `Consume`, use Nutrition, share Hunger gains, or destroy
the victim separately. Ordinary victims remain dead bodies; birds produce their
age-death bones. A Corrupt Seed does not infect a spider that drains it, but an
already-infected victim still produces seeds through its normal death notification.
Repeated attacks cannot transfer Age or produce death effects twice. The base
`Creature.SetRemainingAge` helper only controls lifetime and age-death dispatch;
all Age stealing and hunting logic belongs to `SkySpider`.

## Validation

- **Tools > Creatures > Validate Sky Spider Geometry** checks deterministic seeds,
  geometry limits, 3D depth, stopped rebuilding, and segment contact math.
- In unpaused Play Mode, **Tools > Creatures > Validate Sky Spider Runtime**
  checks growth, roaming, nearby/distant carry release, compound and swept-body
  trapping, open gaps, exclusions, simulation restoration, and web persistence.
  It also measures trapping scans for one and three webs after warmup and renders
  three camera views. Test objects use a temporary unsaved scene; scene files are
  not changed. Reports and images are written to `Temp/SkySpiderValidation`.
- **Tools > Creatures > Validate TimeSpider Age Hunting (Play Mode)** checks target
  selection and retention, surface range, fractional and paused Age transfer, interruptions,
  return priority, foreign captures, ordinary consumption races, bird bones, infection
  effects, dead-web recovery, and no revival. It uses a disposable local physics scene.

Each web is bounded individually. Repeated distant drops intentionally preserve
old webs, so total scene cost grows with their number until a later web-lifetime
system is added. Transparent faces in a combined mesh have approximate sorting;
the deliberately sparse patches keep this artifact small.
