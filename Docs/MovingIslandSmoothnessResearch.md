# Moving-island smoothness: architecture review

2026-11-23. Research and source inspection before modifying Rigidbody motors. This review is the next layer in the implementation checkpoint, ahead of population scheduling optimization.

## Reference implementations and lessons

| Primary source | Documented pattern | Application here |
| --- | --- | --- |
| [Unity Character Controller moving-platform sample](https://github.com/Unity-Technologies/CharacterControllerSamples/blob/master/_Documentation/Tutorial/tutorial-movingplatforms.md) | A kinematic platform moves before character physics using a calculated velocity; smoothing should be consistent between platform and character. Tracked platform transforms supply relative movement. | Separate motion planning, passenger simulation, and rendered pose. This sample uses Unity's ECS controller, not our PhysX MonoBehaviours; apply its timing principles rather than copying its API. |
| [Unity 6.3 Rigidbody interpolation](https://docs.unity.com/en-us/engine/6000.3/manual/physics-section/physics-overview/rigidbody/rigidbody-interpolation) | Interpolation reconstructs poses from physics history and delays rendering by one physics update. Direct Transform writes require care when physics owns the pose. | Interpolated island plus unsmoothed/directly teleported passengers can look unstable even with little net physical drift. More damping does not correct different pose times. |
| [Unity 6.3 MovePosition](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/rigidbody/moveposition) | Kinematic movement uses fixed-step targets and participates in interpolation; teleportation is a separate operation. | Ordinary transport must not use the same direct pose writes as reset/teleport. Use simulation Rigidbody poses to compute the next target, not interpolated Transform poses. |
| [Catlike Coding: Moving the Ground](https://catlikecoding.com/unity/tutorials/movement/moving-the-ground/) | Tracks connected support and its local contact point, then derives support movement and accelerates relative to that connection. | Support velocity belongs to a motor's relative calculations. Our global subtract/restore bridge is a prototype compatibility mechanism; it should not become a hidden contract every new motor must guess. |
| [Epic: SaveBaseLocation](https://dev.epicgames.com/documentation/unreal-engine/API/Runtime/Engine/GameFramework/UCharacterMovementComponent/SaveBaseLocation?application_version=5.5) | Stores previous base pose and relative location/rotation for based movement. | Model base/frame identity, pose history, and transitions explicitly. This does not establish Sea of Thieves' exact implementation. |
| [Glenn Fiedler: Fix Your Timestep](https://gafferongames.com/post/fix_your_timestep/) | Fixed simulation and display rates differ; rendering an interpolated pair of completed simulation states removes timing stutter. | Compare physics-local and rendered-local motion separately. Changing the physics rate to match one monitor is not a general solution. |

## What the current code actually does

- The island plans a kinematic target early in FixedUpdate, then uses MovePosition/MoveRotation with interpolation enabled.
- Dynamic passengers have their previously added frame velocity removed before their motors run and a new point velocity added afterwards. Existing player support is a separate path. This bridge must be reviewed before generalizing motor behavior; do not simply add a second platform displacement.
- Kinematic passengers currently receive direct Transform and Rigidbody position assignments each fixed step, using the Transform as their input pose. Fish have interpolation disabled in their prefab. That mixes teleport-style writes with an interpolated base, and can mix rendered and physics poses.
- Several creatures also write transforms or cached world-space procedural geometry from Update/LateUpdate. Fixing a passive kinematic passenger does not prove those other pose writers are synchronized.
- AsuraPresentation computes stride from all world displacement and treats every awake, uncarried Asura as moving. That is a confirmed presentation contract bug: island transport advances its gait. It is separate from physical motor stability.
- The player's camera follows from Update, while procedural body animation runs in LateUpdate. This is another timing relationship to measure rather than hide with smoothing coefficients.

## Chosen approach and boundaries

1. Establish an opt-in baseline with an idle dynamic Rigidbody and an idle kinematic Rigidbody on the same island. Measure frame-local rendered displacement and physical displacement separately, after settling. Request multiple render-rate caps while retaining the same physics timestep; report actual sample counts rather than assuming the Editor hits those caps.
2. For a confirmed kinematic pose mismatch, use Rigidbody physics pose as the transport source and fixed-step MovePosition/MoveRotation for ordinary motion. Keep direct writes for explicit reset/teleport only. Use interpolation for that body to match the platform's history, restoring its prior setting when frame ownership ends.
3. Fix Asura gait through motor-owned locomotion permission/intent and relative motion, not raw world displacement. Preserve combat recoil, hurt response, rolling animation, and passive impulses.
4. Re-measure the same fixtures and exercise carry/throw/exit/reset. Do not change acceleration, friction, forces, solver iterations, physics rate, or collision thresholds as a general anti-jitter patch.
5. Next review active Transform movers (fish), procedural limbs (tentacle), and cached ecology separately. Where a creature's own Update still writes its root pose, migrate that owner deliberately to a simulation/presentation contract; do not claim interpolation alone solves it.

Longer-term motor API: consume a support/frame snapshot and relative intent, retain world Rigidbody velocity as authoritative state, and compose the output once. Migrate the compatibility bridge by motor family only after representative measurements justify it. Separate physics-frame conversion from render-frame conversion. Goo break tuning is recorded separately below.

## Measurement interpretation

An idle fixture's frame-local per-render displacement should be near zero. Its RMS and maximum step expose short-period wobble that net drift hides. Physical local steps identify contact oscillation or transport error; much larger rendered steps suggest sampling/interpolation disagreement. These metrics alone do not measure the subjective smoothness of articulated limbs, camera behavior, or active flight. Sea angels fly through air; this project has no swimming mechanic. Test fixtures are temporary and are not saved into the gameplay scene.

## First measured change

The baseline confirmed a rendering mismatch for the passive kinematic fixture: physical local movement was negligible while render-local movement oscillated. The first change uses physics poses plus MovePosition/MoveRotation for ordinary kinematic transport, with interpolation matching the island. Prior interpolation is restored on frame exit; explicit reset still uses teleport semantics. No acceleration, forces, friction, gravity, timestep, or solver settings were changed. The dynamic velocity bridge remains in place for later family-specific review.

Measurements below are per-render local position steps in metres (RMS / maximum). Each cap ran for three simulated seconds after five seconds of settling. These are requested application caps; actual render sample counts are reported rather than a claim of exact hardware frame rates.

| Requested cap | Kinematic before | Kinematic after | Dynamic before | Dynamic after | Samples before / after |
| --- | --- | --- | --- | --- | --- |
| 30 FPS | .02382 / .04345 | .00001 / .00001 | .00044 / .00066 | .00044 / .00064 | 89 / 90 |
| 60 FPS | .01935 / .05264 | .00000 / .00001 | .00019 / .00032 | .00019 / .00031 | 180 / 181 |
| 120 FPS | .02514 / .04425 | .00000 / .00001 | .00010 / .00018 | .00010 / .00020 | 355 / 349 |

Kinematic physical steps remained at approximately .00001 m maximum. Dynamic physical maximum steps stayed approximately .00028–.00070 m. The zeroes in the table are rounded values, not claims of exact zero error. The passive kinematic timing problem is strongly supported by this isolated comparison; it does not establish that every observed creature shake is solved.

AsuraPresentation now consumes explicit AsuraMotor locomotion intent/grounded/ownership state and support-relative speed for stride distance. Its focused runtime test passed idle transport without gait advancement, deliberate walking with advancement, and stopped gait. Combat recoil and rolling presentation remain separate.

Remaining architecture work: move active root-pose writers into consistent simulation/presentation timing, check procedural limb endpoints in the same rendered frame as their body/support, and only then revisit dynamic motor families or the global compatibility bridge when measurements show a physical defect. Do not replace small residual physical motion with arbitrary visual damping.

The representative transport/fishing regression passed after this change, including carry/throw, jump, mesh support, frame exit/reentry, and reset. The isolated Asura test also passed. One throw test initially hit the pole dropped immediately beforehand; removing that test obstacle resolved the assertion without changing collision behavior. Compilation and the successful runtime check reported no current errors. This validation does not cover every active procedural mover or the queued goo threshold issue.

## Active flight and procedural limbs

Intangible sea angels have no enabled physical colliders and author autonomous flight during Update. Their transport now uses the same rendered frame pose, rather than a simultaneous fixed-step owner. Dormant sea angels retain this cheap frame transport without running flight behavior. Physical catch/retrieval remains a separate owner. At requested 30/60/120 FPS caps, baseline maximum local flight speeds were 1.230/3.180/3.780 m/s despite a 0.5 m/s movement setting. After unifying the pose time, measured maxima were 0.500/0.500/0.433 m/s, with zero positive step excess above a 0.65 m/s envelope in this sample.

Free kinematic tentacles also own root movement and world-space procedural geometry in Update. Their frame transport and cached holds now run on that same rendered timeline, with Rigidbody interpolation disabled only while this render-driven owner is active (the prior setting is restored on exit). Externally controlled or suspended tentacles retain their external owner. Visible tentacles run their procedural pose every frame; offscreen idle behavior can still be throttled. This deliberately prioritizes visible continuity; scheduler performance must be measured instead of assuming that displaying old membranes is a safe optimization.

The tentacle probe disables intentional wandering and bobbing, pins an arm to the deck, and samples local body movement and pin alignment. Its successful run measured maximum body steps and pin error of approximately 0.00001 m. This verifies one free/pinned pattern, not all tentacle interactions. Direct physical contact queries and nested supports remain representative regression subjects.

Goo tuning is separate. An actual full-force scripted throw reproduced a normal closing speed of 7.434 m/s on a level floor: its high horizontal launch speed did not guarantee a hard hit into the surface normal. The shared dispatch path delivered the hit, but the threshold rejected it. The final goo-specific effective threshold is 6 m/s, down from the shared 15.6 m/s default. An intermediate 8 m/s tuning still rejected that reproduced hit and was replaced. Other species retain their thresholds.

The updated 32-check shared impact suite passed. `GooThrowBreakProbe` also passed ordinary drops and actual full-force `CreatureThrower.Throw(1)` hits on both static ground and the moving island. Gentle half-metre drops remain below the threshold; two-metre falls are intentionally no longer considered harmless. This changes practical break sensitivity without bypassing collision filtering or carry/capture ownership.
