# Leaf Garden prototype and showcase plan

Status: prototype implemented. See `Docs/LeafGardenPrototype.md` for the actual controls, implementation, validation, and current limits. The design below records the intended milestones. The first implementation uses a custom compute shader and indirect mesh draw instead of installing VFX Graph, and one bounded persistent GPU population instead of streamed tiles.

## Intended experience

Create a dedicated `Assets/Scenes/Scene_LeafGarden.unity` showcase: a broad garden carpeted in golden leaves. Walking disturbs nearby leaves; moving quickly opens a visible wake. At intervals, a local section gathers and spirals upward into a large tornado. The tornado's entire visible body consists of leaves. Most of the garden remains resting throughout the event. Afterward, the airborne leaves settle and the cycle can repeat elsewhere.

The tornado has one gameplay rule: eligible movable bodies inside its active volume are launched upward. Keep the first prototype to this rule. Its presentation consists only of leaves, without a rendered wind funnel, smoke, dust, trails, glow, or additional spell effects.

## Project findings

- The project is on Unity 6000.3.6f1 with URP 17.3.0. VFX Graph is not listed in the current package manifest.
- `Creature` provides the existing creature identity and lifecycle. The leaf garden should have one collective creature controller; individual leaves are presentation data, without individual creature scripts, rigidbodies, or colliders.
- `CreatureImpulse.Apply` already routes player impulses through `SmoothFirstPersonController.AddBlastImpulse`, makes Ashura motors yield, and respects carry/capture ownership. Reuse this behavior for tornado launches instead of introducing another player movement mode.
- `AsuraShowcase` demonstrates the existing pattern of authored encounters, replay/reset, observer views, and switching to the project player.
- The existing GPU grass system demonstrates compute-driven rendering and distance culling, but its placement is Terrain-specific. Treat it as a reference; do not modify it or assume it works unchanged on the project's procedural meshes.

## 1. Build a small visual proof first

Use a flat, bounded patch and a single persistent GPU leaf system in VFX Graph. Install the matching VFX Graph package as an implementation step. Check the project's color space and target graphics API before setup.

Each leaf stores a stable home position, current position/velocity, orientation, and variation seed. At rest it lies on the ground. Interaction adds outward and upward movement; drag and a gentle return behavior restore the carpet. No leaf-to-leaf collision or airflow simulation is needed.

Feed a small bounded list of nearby moving actors to the effect. Derive strength from actual movement speed, with gentle walking and stronger fast movement. Measure proximity against the segment from the previous actor position to the current one, rather than testing only a point, so fast movement still creates a continuous wake. Reset that segment on teleport/reset.

Start with a few simple leaf silhouettes, varied golden tones, double-sided rendering, and tumbling that slows as a leaf settles. Avoid camera-facing billboards that look like upright cards when resting on the ground. Prefer cutout rendering or small opaque leaf meshes where appropriate; keep materials simple and start without leaf shadow casting.

Acceptance: at the actual player camera height, a moving actor visibly clears leaves from its path, leaves lift and tumble, and the patch becomes a convincing carpet again. A stationary actor does not continuously explode the patch.

## 2. Expand into a broad carpet

Initial showcase footprint: approximately 96 x 96 meters, divided into roughly 8 x 8 meter tiles. These are starting dimensions for evaluation, not fixed product requirements.

- Keep full leaf detail near the player and the active tornado source; reduce detail at distance and cull invisible rendering.
- Pool particle resources and bound active tile counts. Do not allocate new systems every frame or give each tile its own creature AI.
- Keep each tile's random placement stable. Moving the camera must not shuffle the carpet, refill a cleared path instantly, or respawn a tornado's source leaves.
- Begin with GPU particles representing the actual resting leaves in active tiles. Prototype the ground-to-air transition before adding a second representation.
- If profiling shows distant coverage is too expensive, use a cheaper distant leaf surface with a local removal mask synchronized to disturbances and tornado gathering. Do not leave a permanent golden carpet underneath leaves that supposedly lifted away. This is a later optimization with extra authoring cost.
- Author the first scene on flat ground. Add gentle variation using baked placement heights/normals if needed; do not perform a CPU raycast per leaf per frame. Arbitrary cliffs and moving surfaces are outside the first prototype.

Acceptance: the carpet reads as a substantial area from both player and overview cameras. Tile boundaries, LOD changes, and local clearing remain unobtrusive.

## 3. Form a localized leaf tornado

Use one state sequence: resting -> gathering -> rising/active -> settling -> resting. Run one tornado at a time in the initial prototype. Select a source region inside the garden, far enough from its edge to fit the gathering footprint.

Only leaves in that source region join the tornado. Leaves outside it retain normal carpet behavior. Cap the source region to a small fraction of the garden, initially below approximately 15% of its ground area. Most of the garden must remain covered even at the largest tornado setting.

During gathering, nearby leaves slide into curved streams and begin lifting. During the active phase, steer them around a shared vertical axis, with radius varying by height, upward motion, flutter, and some irregularity in the axis. Use layered leaf streams so the silhouette reads as a tornado rather than a uniformly filled cylinder or a few rigid rings. The axis may sway slightly; keep the source location fixed for the first prototype.

The effect controller owns shared center, growth, height, radius profile, and strength parameters. Visuals and gameplay consume these same values. Leaves can be guided along designed paths; physical wind simulation is unnecessary.

Starting presets, all subject to visual review:

| Preset | Height | Source radius | Approximate upper radius | Purpose |
| --- | --- | --- | --- | --- |
| Small | 15 m | 4 m | 5 m | Read flutter, gathering, and settling |
| Large | 40 m | 8 m | 12 m | Main garden event |
| Huge | 80 m | 12 m | 20 m | Landmark spectacle and stress case |

Scale leaf population/density deliberately as volume increases. Merely stretching a small effect to 80 meters will leave it visibly sparse. Retain visible leaf scale and use overlapping streams to communicate depth without making the entire volume opaque.

Starting cycle: 25-45 seconds resting, 3-5 seconds gathering, 8-12 seconds active including growth, and 6-10 seconds settling. Choose a new local source for the next event. Expose timings and allow immediate manual activation for review.

Track or deliberately approximate redistribution so source depletion, airborne density, and settling remain visually connected. Exact leaf identity conservation is optional; an undiminished source carpet plus unrelated airborne leaves is not an acceptable illusion.

Explicitly size effect bounds for the largest tornado and its settling spread. An observer turning away or a GPU effect being culled must not cancel the event, reset its state, or disable gameplay. Keep an active tornado's presentation resources alive independently of ordinary ground-tile streaming.

Acceptance: the huge preset reads as a towering body made entirely of golden leaves. Only one local portion of the carpet rises, and the transition back to ground coverage is coherent.

## 4. Add the upward launch rule

Use one coarse broadphase query for the active tornado volume, followed by a simple height-and-radius test against its current shape. Use project-compatible physics queries, a reusable results buffer with explicit overflow handling, and deduplication by attached Rigidbody. No query or collider belongs to an individual leaf.

While the tornado is visibly active, a newly entering movable body receives an upward impulse through `CreatureImpulse.Apply`. Bodies already inside when it activates also receive a launch. Base strength on a tunable upward launch speed; preserve existing horizontal momentum. Start around 18-25 m/s upward and tune against the player controller and showcase scale. Avoid repeatedly stacking a launch every physics step: track successful launches per entry, require a real exit before rearming, and add a short re-entry cooldown. For an already faster ascending body, add no further upward speed.

The volume follows current growth and height; it must not use the final huge dimensions while only a small spiral is visible. Keep it slightly inside the readable leaf envelope so being launched has a visible cause. Clear tracked bodies when an event ends or the showcase resets.

Apply to the player, creatures, and loose dynamic props. Terrain, static scenery, and intentionally anchored/kinematic bodies remain fixed. Preserve existing carry/capture ownership; a held object follows its carrier rather than being forcibly detached. Inspect special locomotion types that may overwrite velocity and adapt only those used in this showcase if necessary.

The first prototype applies no damage, suction, horizontal orbiting to bodies, debris destruction, or prolonged trapping. Leaves spiral; affected bodies shoot upward and resume their existing movement/gravity behavior.

Acceptance: a player, a creature, and a loose prop each launch when entering the visible active tornado; compound colliders do not multiply the impulse; entering after the event ends has no effect.

## 5. Compose the Leaf Garden showcase

Build one garden scene with a broad golden carpet, restrained ground/stone boundaries, an open sky/backdrop that makes the leaves readable, and a safe return route after launches. Keep the layout open enough to see the full huge tornado from a distant viewpoint. Use normal lighting to reveal the leaf colors and silhouettes.

Provide three views of the same event:

1. Player view: walk/run through the carpet and enter the tornado.
2. Ground-level observer: watch a test creature cross the leaves and get launched, with loose props nearby.
3. Distant overview: see that only part of the garden rises and appreciate the 80-meter tornado against the resting carpet.

Reuse the existing player prefab and input bindings. Add development controls consistent with the project's showcase pattern: trigger tornado, choose small/large/huge, pause automatic cycling, switch camera, change density preset, and reset with a repeatable seed. Expose the same actions to Editor automation for repeatable captures. Development diagnostics can be toggled off for clean presentation.

Planned implementation assets:

| Asset | Responsibility |
| --- | --- |
| `Assets/Scripts/Creatures/LeafGarden.cs` | One collective creature; event state, source selection, shared parameters |
| `Assets/Scripts/Creatures/LeafGardenPresentation.cs` | Tile resources, actor inputs, particle properties and bounds |
| `Assets/VFX/LeafGarden/` | Leaf graph/subgraphs, leaf geometry/textures and materials |
| `Assets/Scripts/Creatures/LeafTornadoLaunchVolume.cs` | Active-volume queries, entry tracking and upward impulses |
| `Assets/Scripts/Dev/LeafGardenShowcase.cs` | Replay controls, views, density/scale presets and reset |
| `Assets/Scripts/Editor/LeafGardenShowcaseSetup.cs` | Repeatable scene/prefab authoring using existing project assets |
| `Assets/Scenes/Scene_LeafGarden.unity` | Dedicated review scene |

Names and component boundaries may be simplified during implementation. Configure the collective creature as an anchored region with no ordinary carry interaction or automatically added combat behavior.

## 6. Find the practical limit and capture the result

Use low/medium/high quality settings with independent ground-leaf and airborne-leaf budgets. Initial stress points are 25k, 50k, 100k, and 200k total live leaf particles, stopping earlier if the frame budget is exceeded. These counts are experiments, not promised supported densities. Record separately how many leaves are simulated and how many are rendered after culling.

Profile a standalone development build on the actual test machine, at a recorded resolution and quality level. Compare the same camera path and seed with the leaf feature disabled, carpet only, active large tornado, and active huge tornado. Include interaction with multiple moving actors and test bodies. Target 60 fps / 16.7 ms per frame on the chosen showcase machine; initially aim for at most about 2-3 ms additional GPU time from the leaves. Report measured results and adjust budgets rather than claiming a universal maximum.

Measure steady state, gathering, peak airborne density, and settling. Check CPU, GPU, allocations, memory, and high-percentile frame times over repeated cycles; prewarm resources so first-use compilation/allocation spikes are visible and addressed. Watch transparent overdraw, material/shader cost, bounds-related culling, and churn when tiles activate. Keep body-launch cost independent of particle count.

Verification should cover fast path continuity, local carpet depletion, simultaneous actors, compound colliders, bodies already inside at activation, re-entry behavior, high-speed volume crossing, carried/captured objects, camera switching, full-height visibility, and several reset/cycle repetitions. For high-speed volume crossings, add a swept-body check if broadphase snapshots miss the intended launch.

Deliver clean captures at normal playback speed: a close carpet/wake shot, a full localized gathering-to-settling cycle, a huge-tornado overview, and a player/creature/prop launch demonstration. Save selected captures under `ArtCaptures/LeafGarden/` and accompany them with the tested density, resolution, hardware, and frame-time summary. An Editor screenshot alone does not establish runtime movement or performance.

## Delivery order

1. Small visual proof: resting leaves, actor wake, one local spiral, coherent settling.
2. Large garden: stable tiled coverage, quality presets, nearby-actor input budget.
3. Full spectacle: localized gathering and 15/40/80-meter tornado presets, complete bounds and overview camera.
4. Gameplay: simple upward launches through the shared impulse helper.
5. Showcase verification: repeated cycles, performance sweep, clean captures and recorded limits.

The first milestone gates further work: establish that the carpet and transition look convincing at player height before scaling particle counts or building the full scene.

## References

- Interactive fallen-leaf reference: https://github.com/DJ-Huang/UnityVFXLibrary#interactive-leaves
- Unity leaf VFX workshop: https://learn.unity.com/tutorial/creating-visual-effects-with-unity-s-vfx-graph
- Matching VFX Graph compatibility: https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@17.3/manual/System-Requirements.html

References inform the approach. External example assets have not been imported or tested for this project.
