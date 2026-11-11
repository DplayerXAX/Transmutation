# Leaf Garden prototype

Open `Assets/Scenes/Scene_LeafGarden.unity` and enter Play Mode. The first tornado begins after five seconds. The showcase starts in the distant overview so the entire 80-meter leaf column is visible.

## Controls

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

Player view uses the project's existing movement bindings. Walk/run through the leaves or enter the active tornado to be launched. The three teal demonstration creatures cross the carpet at different speeds; loose brown seedpods demonstrate upward launches.

## Behavior

The carpet covers 96 x 96 meters. One anchored `LeafGarden` creature controls the entire organism. After a 25-45 second rest, one local section gathers for five seconds, stays active for twelve, and settles over eight. The automatic source can change between events. The default source radius is twelve meters: approximately 5% of the carpet area. The visible tornado consists only of leaves.

The same persistent leaves lie on the ground, scatter, join the spiral, and return to their home positions. No separate permanent gold ground texture remains underneath a gathered section. Exact redistribution is intentionally guided back to the stored positions; this is designed animation rather than a physics pile simulation.

The launch volume follows current growth and a tapered radius profile. Launch speed is computed from the actual world gravity and an intended apex at 115% of the full tornado height, with at least 40 meters of rise above the entry point. The original 24 m/s setting remains only a minimum speed. For the 80-meter tornado and this project's -30 m/s² gravity, a ground-level player starts at approximately 74 m/s and aims for an apex around 92 meters. Horizontal momentum is preserved; compound colliders cannot multiply the impulse. Remaining inside does not add another impulse every frame. A real exit and cooldown permit another launch. The existing creature impulse receiver still applies species knockback resistance; the height target is directly applicable to the player and ordinary undamped dynamic bodies. Carried/captured objects, static scenery, and kinematic bodies retain existing ownership/anchoring behavior.

## Place it in another scene

Drag `Assets/Prefabs/Creatures/LeafGarden.prefab` into the scene. Place its root at the surface height of a flat floor, keep rotation and scale at their defaults, and edit `LeafGarden.Area` to set the footprint. `LeafGardenPresentation.LeafCount` sets visual density. It uses the enabled Main Camera automatically; assign `ViewCamera` explicitly if the scene uses a different camera setup. The scene needs its own ground collider, lighting, and player. The garden prefab does not include the showcase's floor, cameras, demonstration creatures, props, or keyboard controls.

The prefab can also be created with `Tools > Creatures > Leaf Garden > Create Reusable Prefab`; `Add Garden to Current Scene` adds an instance at the origin without saving or replacing the current scene. The first prefab event waits 25-45 seconds. Only the showcase forces the first event after five seconds. Walking scatters leaves and does not trigger the tornado. Disable `Automatic` to keep a carpet without timed tornadoes, or call `TriggerTornado()` / `TriggerAt(position)` from an authored event.

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

The first implementation uses one bounded GPU population rather than streamed tiles. This keeps the ground-to-tornado transition coherent; it still simulates the full selected population when most leaves are outside the camera. Distant density currently changes at the detail-distance boundary. Streaming, smooth LOD transitions, terrain-height placement, and world-wide integration remain follow-up work if needed.

## Authoring and verification

`Tools > Creatures > Leaf Garden > Create and Open Showcase` regenerates this dedicated scene and its local assets. It does not alter the other game scenes. Save unrelated scene edits before using the command.

`LeafGardenVerification` accepts explicit JSON requests in `Temp/LeafGardenRequest.json`, and writes a matching result to `Temp/LeafGardenResult.json`. Supported actions include setup, play/stop, status, trigger, size, density, view, reset, sample, capture, validate, benchmark, and build. It is an Editor-only development tool; it executes only its fixed action list. Play-mode ticking is enabled only by an explicit play request and stopped by stop.

`LeafGardenRuntimeChecks` verifies actual GPU state, creature movement/scattering, localized airborne density, active height, launch deduplication, momentum preservation, re-entry, anchored-body exclusion, actual player motion, and settling. Results are saved under `ArtCaptures/LeafGarden/`.

`LeafGardenBenchmark` runs an opt-in fixed-overview sweep with a baseline, carpet and huge tornado cases at 25k/50k/100k/200k, plus a 400k huge tornado stress case. Each case warms for two seconds and samples for four. A development player started with `-leafGardenBenchmark` writes CSV/hardware metadata and exits. Zero GPU timings mean the platform did not supply that counter. Screenshot capture frames must not be used for performance measurements.

## Limits of this prototype

This is a flat-ground desktop showcase requiring compute-shader and instanced-rendering support. It does not place leaves on arbitrary cliffs or moving geometry. The garden is anchored, invulnerable, and cannot be carried/captured. Tornadoes launch bodies without damage, suction, or a separate wind simulation. Fast bodies that cross the entire launch volume between physics steps may need a future swept-volume test; ordinary player traversal is the current target. Measured performance applies to the recorded hardware, view, and resolution, not every scene or device.

Selected clean captures, the full-cycle animation, runtime-check output, and benchmark data belong in `ArtCaptures/LeafGarden/`. Raw capture frames and development builds are temporary files under `Temp/`.

## Recorded validation

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
