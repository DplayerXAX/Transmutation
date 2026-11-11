# Leaf Garden prototype

Open `Assets/Scenes/Scene_LeafGarden.unity` and enter Play Mode. The scene starts on a bare cliff overlooking a rolling golden-leaf meadow. Three independent leaf creatures cover the meadow, with 100,000 GPU leaf slots each (300,000 total; organic gaps and cliff exclusion hide some slots). The first automatic tornado starts after 15-25 seconds so the prototype demonstrates the effect without waiting several minutes.

The 384 x 448-meter landscape uses the project's shared `ProceduralWorld` Marching Cubes generation and sculpting tools, with a real overhang. Its ground uses `MAT_World_Surface`. The sky is assigned `MAT_Sky_Black`, selected once from the current World scene. There is no ongoing synchronization with that scene.

## Controls

The meadow stays in normal player view. Tab, F1/F2/F3, and Escape do not switch cameras there. Camera shortcuts below apply only to the older flat showcase arena.

| Key | Action |
| --- | --- |
| Tab | Cycle player / ground observer / distant overview |
| F1 / F2 / F3 | Select those views directly |
| 1 / 2 / 3 | Reset and start a 15 / 40 / 80-meter tornado |
| G | Awaken a resting garden at the center |
| R | Reset leaves, bodies, and event state |
| P | Toggle automatic events |
| 4 / 5 / 6 | Select 50k / 100k / 200k live leaves |
| L | Toggle leaf drawing for a visual comparison |
| H | Hide/show development controls |
| Escape | Return from player view to overview |

Player view uses the project's existing movement bindings. Walk/run through the meadow leaves or enter an active tornado to be launched. The landscape has no artificial walking demonstration creatures or loose launch props.

## Behavior

Three anchored `LeafGarden` creatures each control a 100 x 280-meter strip. Each permits one tornado at a time, so up to three can overlap. After an event, each creature rests 540-900 seconds. Across all three, this produces a small tornado about every five minutes on average, with random gaps and possible overlaps. The middle colony's first delay is 15-25 seconds; the other two start after 90-600 seconds.

Automatic sizes are weighted: 82% small (8-18 meters), 13% medium (24-40 meters), and 5% large (55-80 meters). A large event averages roughly once every 80 minutes across the meadow after the introductory events; this is a probability, not a fixed schedule. Each event gathers for five seconds, stays active for twelve, and settles over eight. Sources move between events and avoid the bare overlook. The visible tornado consists only of leaves.

The same persistent leaves lie on the ground, scatter, join the spiral, and return to their home positions. No separate permanent gold ground texture remains underneath a gathered section. Exact redistribution is intentionally guided back to the stored positions; this is designed animation rather than a physics pile simulation.

The launch volume follows current growth and a tapered radius profile. Launch speed is computed from the actual world gravity and an intended apex at 115% of the full tornado height, with at least 40 meters of rise above the entry point. The original 24 m/s setting remains only a minimum speed. For the 80-meter tornado and this project's -30 m/s² gravity, a ground-level player starts at approximately 74 m/s and aims for an apex around 92 meters. Horizontal momentum is preserved; compound colliders cannot multiply the impulse. Remaining inside does not add another impulse every frame. A real exit and cooldown permit another launch. The existing creature impulse receiver still applies species knockback resistance; the height target is directly applicable to the player and ordinary undamped dynamic bodies. Carried/captured objects, static scenery, and kinematic bodies retain existing ownership/anchoring behavior.

## Place it in another scene

Drag `Assets/Prefabs/Creatures/LeafGarden.prefab` into the scene. Place its root at the surface height of a flat floor, keep rotation and scale at their defaults, and edit `LeafGarden.Area` to set the footprint. `LeafGardenPresentation.LeafCount` sets visual density. It uses the enabled Main Camera automatically; assign `ViewCamera` explicitly if the scene uses a different camera setup. The scene needs its own ground collider, lighting, and player. The garden prefab does not include the showcase's floor, cameras, demonstration creatures, props, or keyboard controls.

The prefab can also be created with `Tools > Creatures > Leaf Garden > Create Reusable Prefab`; `Add Garden to Current Scene` adds an instance at the origin without saving or replacing the current scene. The prefab's natural timing defaults are 60-300 seconds for the first event and 180-270 seconds between events; the three landscape instances override these to avoid tripling the meadow's event frequency. Walking scatters leaves and does not trigger the tornado. Disable `Automatic` to keep a carpet without timed tornadoes, or call `TriggerTornado()` / `TriggerAt(position)` from an authored event.

For this meadow, the shared `LeafGardenSurface` asset supplies baked collider heights, normals, and a leaf-coverage mask. The cliff and its approach have zero coverage while retaining valid terrain heights. After changing terrain geometry, rebake the support map with `LeafGardenLandscapeSetup.BakeSurface(world)` and rebuild leaf buffers. Flat scenes can leave `Surface` unset. Other uneven levels need their own support map and world bounds; do not reuse this meadow's map at unrelated coordinates.

| Part | Responsibility |
| --- | --- |
| `LeafGarden` | One real `Creature` identity for the entire patch, plus timers and tornado shape |
| `LeafGardenPresentation` | GPU leaf positions, leaf drawing, flutter, and visual movement interaction |
| `LeafTornadoLaunchVolume` | Queries actual nearby rigidbodies and applies upward impulses |

There is no Rigidbody or Collider on the garden root. Individual leaves have numerical GPU state, but no GameObjects, Creature identities, colliders, or rigidbodies. You can walk through them and see them scatter; you cannot pick up one leaf, block a projectile with it, or capture it in a bubble/web. The existing floor supports the player, while the tornado's mathematical volume launches bodies without needing a physical leaf funnel.

The collective garden explicitly disallows carrying, bubble/web capture, and consumption, is invulnerable to ordinary damage, and uses no combat polarity. Bubbles pass through the visual leaves; spider silk cannot attach to them. Nearby movable creatures still scatter leaves, and eligible bodies can be launched by the tornado. Existing capture/carry ownership prevents the tornado from stealing an occupied or held body. Species-specific feeding, fire, freezing, or other ecological behaviors are not automatically implemented merely because this component derives from `Creature`.

## Rendering implementation

The prototype uses a small compute shader and one indirect mesh draw instead of installing VFX Graph. This is the same general GPU-rendering approach already present in the project's grass implementation, with independent leaf data and shaders. There are no package changes.

- One 64-byte state record per leaf; 200k leaves use roughly 12.8 MB for state and 0.8 MB for visible indices, before graphics-driver overhead.
- A shared folded/serrated opaque leaf mesh avoids transparent sprite overdraw. Golden color variation and veins are procedural.
- All simulation stays on the GPU. Rendering uses GPU frustum culling and reduced distant density. Local tornado leaves retain full density.
- Actor discovery runs at 10 Hz, with up to sixteen distinct moving bodies feeding swept interaction segments each rendered frame. Teleports are excluded from those segments.
- Tornado shape and gameplay parameters share the creature controller. One broadphase query and a radius test handle launches independently of leaf population.
- Rendering bounds include the full huge tornado. Ground home positions remain stable when the camera moves.

Each creature uses one bounded GPU population rather than streamed tiles. This keeps its ground-to-tornado transition coherent; it still simulates the full selected population when most leaves are outside the camera. Distant density currently changes at the detail-distance boundary. Streaming, smooth LOD transitions, and world-wide integration remain follow-up work if needed.

## Authoring and verification

`Tools > Creatures > Leaf Garden > Create Golden Meadow Landscape` regenerates this dedicated landscape scene and its local assets. It does not alter the other game scenes or shared world materials. Save unrelated scene edits before using the command. `Create and Open Showcase` is the older flat benchmark arena and replaces the same scene, so use the meadow command for the current landscape.

`LeafGardenVerification` accepts explicit JSON requests in `Temp/LeafGardenRequest.json`, and writes a matching result to `Temp/LeafGardenResult.json`. Supported actions include setup, play/stop, status, trigger, size, density, view, reset, sample, capture, validate, benchmark, and build. It is an Editor-only development tool; it executes only its fixed action list. Play-mode ticking is enabled only by an explicit play request and stopped by stop.

`LeafGardenRuntimeChecks` verifies actual GPU state, creature movement/scattering, localized airborne density, active height, launch deduplication, momentum preservation, re-entry, anchored-body exclusion, actual player motion, and settling. Results are saved under `ArtCaptures/LeafGarden/`.

`LeafGardenBenchmark` runs an opt-in fixed-overview sweep with a baseline, carpet and huge tornado cases at 25k/50k/100k/200k, plus a 400k huge tornado stress case. Each case warms for two seconds and samples for four. A development player started with `-leafGardenBenchmark` writes CSV/hardware metadata and exits. Zero GPU timings mean the platform did not supply that counter. Screenshot capture frames must not be used for performance measurements.

## Limits of this prototype

This desktop prototype requires compute-shader and instanced-rendering support. Mapped ground support is baked, so terrain edits need a rebake; moving geometry is not supported. A top-down map supports one exposed surface at each horizontal coordinate. The garden is anchored, invulnerable, and cannot be carried/captured. Tornadoes launch bodies without damage, suction, or a separate wind simulation. Fast bodies that cross the entire launch volume between physics steps may need a future swept-volume test; ordinary player traversal is the current target. Measured performance applies to the recorded hardware, view, and resolution, not every scene or device.

Selected clean captures, the full-cycle animation, runtime-check output, and benchmark data belong in `ArtCaptures/LeafGarden/`. Raw capture frames and development builds are temporary files under `Temp/`.

## Recorded validation

The measurements below are from the earlier flat arena, not the new rolling landscape. Current landscape checks are recorded separately in `ArtCaptures/LeafGarden/landscape-checks.txt`.

The prototype passed 24 Play Mode checks, including moving-creature scattering, finite GPU positions, the selected tornado height, local source selection, compound-body launch deduplication, re-entry, momentum preservation, actual project-player launches, anchored-garden protection, and settling. The actual player reached a first apex 91.26 meters above the garden floor against an 80-meter tornado, targeting 92 meters. The golden-leaf shader reported no shader errors. A 200k-leaf GPU snapshot measured 9,833 source leaves, 9,148 above two meters during the tornado, a peak height of 80.21 meters, and zero invalid positions. Disturbances outside the tornado source are possible from the walking actors.

The fixed-overview Editor benchmark used an NVIDIA GeForce RTX 4090, AMD Ryzen 9 9900X, Unity 6000.3.6f1, and a 2560 x 1440 Game view, with VSync off. Each case warmed for two seconds and sampled for four. These are total Editor frame times, including Editor overhead, rather than an isolated GPU cost for the leaf effect.

| Case | Simulated leaves | Visible leaves at sample | Median frame | 95th percentile |
| --- | ---: | ---: | ---: | ---: |
| Baseline, leaves disabled | 0 | 0 | 2.885 ms | 3.648 ms |
| Resting carpet | 200,000 | 80,185 | 2.963 ms | 4.065 ms |
| Huge tornado | 200,000 | 85,894 | 2.909 ms | 3.531 ms |
| Huge tornado stress case | 400,000 | 171,520 | 2.902 ms | 3.529 ms |

The small differences are within ordinary run-to-run variation in this CPU/Editor-bound scene. The sweep did not establish a maximum supported population. Keep the default at 200k and profile again in the intended real level and on lower-spec hardware. Complete results, including the 25k/50k/100k cases and available timing counters, are in `ArtCaptures/LeafGarden/benchmark.csv`.

A standalone development build was attempted, but Windows Application Control blocked Unity's installed `monolinker.dll` with error 0x800711C7. Standalone performance is therefore unverified. The build did not fail because of a reported Leaf Garden C# or shader compile error. Build-generated changes to unrelated rendering settings and Wwise plugin metadata were removed from the working files.
