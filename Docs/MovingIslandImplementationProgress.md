# Moving island implementation checkpoint

Baseline revision: `56577cd29007af906268e66bebc640d14d0dae72`. Existing planning documents were untracked before implementation. Scene baseline is preserved at `Backups/MovingIslandBaseline/Scene-Fishing.unity`.

## Current work

Latest user-directed refresh: restored authored sea-angel proportions and its translucent rim shader; walking animation now requires locomotion state plus movement input; added rolling baked mesh terrain and a saved manual test population (Asuras of both polarities, chainsaw, tentacles, birds, flowers, recycle, goo, shroom, egg, fuel bubble, and a runtime bone dragon). See the prototype guide's refresh section. Gait/terrain/population checks passed; bird scene-start registration was fixed after exposing an early-OnEnable race. Recycle growth on the mesh patch was observed at 34.6 m while its root stayed at local (-12, 3.6, 14).

Final refresh verification: compilation passed; the gait/terrain probe passed again after the bird startup fix; all five birds registered and one bone dragon spawned. Current runtime console error count was zero. Left the saved Scene-Fishing stopped with the manual test group present and no probe saved into the scene.

Implemented first shared optimization gates, fish source integration, scene registry discovery, moving frame/member transport, editable closed spline route, player support, inherited throw velocity, moving fishing endpoints, bird habitat bounds, giant creature prefab, and Scene-Fishing installation.

Scope update: the user explicitly replaced exhaustive creature testing with a code review of movement patterns and focused representative tests. Remaining creature-specific issues are recorded in `Docs/LevelDesign/SeaAngelIslandPrototype.md` and do not imply the prototype is perfect.

## Verification so far

- Running Unity 6000.3.6f1 connected through the project's existing Pipeline package. Sandbox access required elevated local CLI connection; the connection works.
- Baseline compilation was healthy. Existing console history included a Burst JIT library load error (Windows code 4551) and a previously attempted unsupported Pipeline command. No baseline code changes were made to address these unrelated entries.
- New scripts compiled successfully in the running Editor.
- `MovingIslandValidation.Run()` passed periodic position and heading continuity, approximately uniform arc-length steps, independent distance dormancy, and preservation of semantic pauses.
- First live sample: island travelled 44.19 m; idle player remained grounded near local `(0, 1, -14.99)` after 17.2 simulated seconds.
- The 30-second transport check passed: 0.0156 m player local drift, 0.0109 m sleeping fish drift, 89.73 m island travel.
- The complete focused Play Mode probe passed: fish sleep/wake/reserve, squirrel walking, FireSource rooted transport, bird flight, adhered egg, full catch/retrieval, throw, jump, baked mesh collision, habitat exit/reentry, and reset. Its repeat 5-second idle portion measured 0.0085 m / 0.0059 m player/fish drift.
- Corrected test setup errors: disable DimensionStone as well as pole for source sleep; use a genuinely stationary FireSource rather than a detached RecycleFlower; seed frame velocity for deliberately placed test creatures. These corrections are retained in the opt-in probe.
- The successful runtime check reported zero current Console errors. Earlier failing probe entries remain in historical console output; they are not new unresolved gameplay exceptions.
- Final regression after removing the fish-specific duplicate wake deadline also passed the entire representative probe (5-second drift: player 0.0129 m, sleeping fish 0.0101 m). Final compilation succeeded. Editor is stopped in the saved, clean Scene-Fishing with one island and zero test probes.

## Next checkpoint

**Next priority: creature motion smoothness and presentation review, before the remaining full simulation scheduler migration and optimization.** The first playable prototype is functional, but the user observes visible shaking across inhabitants. Earlier drift and functional tests do not establish smooth rendering. Follow the new work layer below before expanding optimization. Do not restart exhaustive creature testing: the user explicitly chose code review plus representative behavior checks.

## User-reported issues — recorded, not fixed

Reported 2026-11-23 after manual play. These observations are not yet independently reproduced or diagnosed; this checkpoint update changes documentation only.

| Issue | Observation / requested outcome | Investigation boundary |
| --- | --- | --- |
| Asura idle movement animation | Asuras play locomotion animation while stationary relative to the island. Idle inhabitants should not appear to walk merely because the island transports them. | Review Asura motion/animation state and movement intent, then how stride speed is supplied. Use the same principle accepted for the player: intentional locomotion permits gait; transport alone does not. Do not assume the cause is proven. |
| Goo full-force throw / break sensitivity | User corrected the initial report: goo **does break normally** on impacts. Full-force throws appear less reliable, and the user would prefer goo to break more easily. | Compare ordinary drop, gentle throw, and full-force throw; inspect scripted throw handoff, impact dispatch, closing speed and threshold. Determine whether the strong-throw case misses an impact or fails a threshold before tuning sensitivity. Preserve the request to lower the practical break threshold; do not classify this as universally broken collision. |
| General visible shaking / jitter | Inhabitants appear to shake while riding. Gameplay is otherwise working fairly well. Improve visible stability across movement patterns. | Distinguish physical contact oscillation, frame-transport error, procedural animation jitter, and a mismatch between camera/render/physics sampling. Low net drift alone does not prove this is fixed. |

The user explicitly requested **no fixes yet** for these reports. The work below is the next proposed implementation layer, not work already completed or authorization to silently begin fixes during this documentation task.

## New work layer: smooth creature motion before optimization

1. **Reproduce and capture a baseline.** Use the current populated Scene-Fishing with the route stationary, then cruising straight, ascending/descending, and turning. Compare the view from the player with an external camera. Record which bodies, limbs, visual meshes, and attachments shake; separate persistent jitter from occasional frame-time hitches. Include normal mainland/static-ground behavior as a regression reference.
2. **Review movement ownership and timing by pattern.** Inspect grounded Rigidbody motors, kinematic/Transform movers, coordinated flight, procedural world-space limbs, rooted ecology, and attached/ridden actors. Map their writes across Update, FixedUpdate, and LateUpdate. Review the island's planned physics poses, passenger velocity removal/restoration, direct kinematic pose writes, Rigidbody interpolation, cached world points, and camera following. Treat these as candidate mechanisms, not a confirmed diagnosis. Ensure each actor has one physical movement authority and a deliberate presentation path.
3. **Define consistent simulation and presentation sampling.** Choose authoritative physics poses for collision and support calculations, and one consistent rendered pose/time for the visible island, passengers, camera, and procedural limbs. Preserve collision/throw/capture ownership. Avoid hiding physical instability with arbitrary smoothing or enabling interpolation everywhere without checking its timing. Keep intentional wingbeats, steps, and body motion intact.
4. **Address motion-state presentation in that pass.** Reproduce idle Asura gait and review shared locomotion signals. Gate walking on actual locomotion state/intent; use support-relative speed only for permitted stride timing/amplitude. Include stopping, blocked movement, passive transport, and knockback. Keep creature-specific animation styles; do not force all species into a player-only state machine.
5. **Debug goo impact behavior as a separate small change.** Compare identical drops/throws on stationary ground and the island's flat and mesh surfaces. Preserve valid impact ownership through scripted flight. Tune the break sensitivity after establishing which impacts are delivered, and record the resulting tuning and examples. Goo impact tuning should not be bundled into the transport timing change.
6. **Verify a few representative behaviors, then resume the scheduler plan.** Use an idle/walking Asura, one ordinary ground mover, a sleeping/awake sea angel, a bird, a tentacle, and an attached/rooted example. Focus tests on patterns affected by each actual change. Check carry/throw transitions and the player's camera/gait where relevant. Do not require every creature or every ability to be perfect before proceeding.

Acceptance for this layer: visibly stable idle inhabitants relative to the back during cruise and turns; no transport-induced idle gait; intentional walking and flight remain smooth; attachments do not visibly vibrate because their endpoints use different pose times; ordinary ground movement and fishing remain functional. Establish a short-term local-pose variation/render-continuity measure in addition to net drift, and report before/after behavior at multiple render rates with a fixed physics timestep. Agree numerical tolerances from the reproduced baseline rather than claiming an unmeasured threshold. No generalized scheduler, streaming, or population-scale rewrite is required to diagnose this layer.

After the smoothness work and representative regression checks, return to the remaining shared scheduling migration and measured optimization. Keep unusual abilities, cached territories, and nested rider cases as incremental debugging tasks unless they explain a reproduced jitter pattern.

The shared simulation migration provides one effective activity gate and separate semantic pause permission; it does not yet implement state-only unloading, generalized physics suspension, central ticking of all species, or population-scale performance work. These remain later phases of the accepted optimization plan.

## Commands

The installed Pipeline uses legacy command names: `unity command recompile`, `recompile_status`, `eval`, `editor_play`, `editor_stop`. Pass `--caller plugin --skill unity-pipeline --format json`, run from this project, and use an elevated local connection if sandbox networking cannot reach the Editor. Use the new Editor port discovered after each domain reload rather than hard-coding it.

The opt-in `MovingIslandPlayModeProbe` is never saved into gameplay scenes. Call `MovingIslandValidation.StartProbe(30)` in Play Mode; its helper supplies squirrel, bird, FireSource, and egg prefab references. Recompile before trusting results after source changes.
