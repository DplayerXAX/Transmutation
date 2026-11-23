# Shared simulation implementation and measured limits

2026-11-23. Implements the next shared scheduling phase after the representative smoothness checks. Related: [optimization plan](CreatureSimulationOptimizationPlan.md), [checkpoint](MovingIslandImplementationProgress.md), [smoothness research](MovingIslandSmoothnessResearch.md).

## Runtime responsibilities

- `CreatureSimulationScheduler` provides one scene-local relevance clock for policies. It runs after rendered-frame transport and before ordinary creature behavior. It does not write physics poses or animate creatures.
- `CreatureSleep` is a distance policy, not an independent Update governor. Creature registry lifecycle events register/remove its candidates immediately. The old five-second rediscovery and half-second all-creature batch are removed.
- Distance candidates have individual half-second deadlines distributed across frames. At most 32 candidate slots are visited per render frame by default. Relevance distance still has separate sleep/wake radii and uses the current live position, including transported dormant inhabitants.
- `DimensionalSchool` is a source-specific policy on the same clock. Poles, stones, honey, reservations, spatial cells, visibility, and catch behavior remain domain rules. Player-centered wake sources are combined into one maximum-radius query; stone-centered queries remain separate.
- `CreatureSimulation` is the effective optimization gate. Owned `ActivityRequest` handles can require immediate activity without waiting for a polling tick. Carrying and fish reservations use these handles. Releasing one handle does not release another owner's request; disposal is idempotent, and destroyed owners do not keep creatures active forever.
- `Creature.SimulationPermitted` remains the semantic permission. `SimulationEnabled` combines that permission, death state, and optimization eligibility. An activity request cannot override a capture, throw pause, or another explicit semantic pause.

Fishing no longer refreshes a second reservation wake deadline every quarter-second: the reservation owns an activity request until release. Existing source wake grace continues to apply for ordinary nearby visibility/interaction sources. Renderer visibility rules are not replaced by scheduler distance.

## Safe scope of dormancy

The first scheduler migration pauses behavior and age using the existing contract. It does not disable whole GameObjects or universally freeze physics. Falling, thrown, carried/captured, pinned/suspended, petted, and actively engaged combat creatures are protected by distance policy where applicable. Flying types not already given a safe dormant representation retain their existing active-physics policy. Sea angels use their existing source-driven sleep with independent render-frame transport.

This intentionally preserves the distinction between AI work, required physical motion, presentation, and frame membership. Arbitrary dynamic body suspension, streaming, and state-only records require additional ownership handoffs and are not silently introduced as a performance shortcut.

## Measured scheduler check

An opt-in runtime probe added 512 passive creature fixtures through normal lifecycle registration, waited for distance dormancy, moved a fixture near the player, and exercised policy disable/re-enable and multiple activity owners. It passed:

- Registration without waiting for a discovery scan; destruction removes candidates.
- Far-away optimization dormancy while semantic permission remains intact.
- Immediate owned activity, independent release, duplicate disposal, and destroyed-owner handling.
- Explicit semantic pause taking priority over activity requests.
- Waking from current position when approaching the player.
- Policy shutdown releasing only that policy's dormancy, and re-enable preserving semantic pauses.
- Additive-scene isolation and scene-unload cleanup, including safely disposing a request after its entity has been destroyed.

In the final lifecycle run, 228 sampled frames produced a peak of 16 distance checks per frame under a budget of 32. The complete relevance scheduler averaged 34.6 microseconds and peaked at 194.8 microseconds. An earlier run measured 37.3 / 372.0 microseconds; these are run-dependent Editor timings. They include policy execution, not all creature AI, physics, rendering, or allocations. There is no claim of an overall game frame-rate speedup or an apples-to-apples CPU-time comparison with the previous implementation. The demonstrated change is lifecycle discovery plus bounded/distributed relevance work instead of the previous concentrated sweep.

At very large populations, wake latency includes both the configured half-second deadline and the time needed to revisit the candidate list at the frame budget. This phase does not promise fixed latency for arbitrary populations. Activity requests are immediate; further spatial relevance partitioning should be introduced only against a measured target population.

## Reproducing the focused checks

Use a fresh Play session in Scene-Fishing: the pole and other live fixtures can die of age if the Editor is left running during analysis. Add `CreatureSchedulerProbe` temporarily; inspect its static `Result` after completion. It creates 512 passive fixtures to measure the scheduler contract, not 512 species or a representative AI workload. The final probe tracks fixture unregister events directly, because combat/ecology can legitimately change the rest of the live population during the test.

`CreatureSimulationProfileProbe` captures the populated island without disabling its manual test group. It settles for three real seconds, then samples for ten. Avoid evaluating other C# or recompiling during the sample: diagnostic evaluation itself can add a large allocation/time spike. Read the static `Result` after completion. It records population changes instead of assuming combat and ecology leave a fixed count.

The probe uses Unity's [ProfilerRecorder.StartNew](https://docs.unity.com/en-us/engine/6000.3/script-reference/unity/profiling/profilerrecorder/startnew) with [default options](https://docs.unity.com/en-us/engine/6000.3/script-reference/unity/profiling/profilerrecorderoptions/default), which sum marker samples per frame. Missing counters are reported as unavailable. Main-thread and allocation counters in the Editor include Editor/tooling overhead; scheduler time comes from its own instrumented policy loop. This establishes a current Editor baseline, not a target-device budget or causal attribution of allocations to creatures.

## Populated-island Editor baseline

A fresh Play session, three-second settle, and ten real seconds of uninterrupted sampling produced 1,384 render frames. The saved test population remained enabled, including its bone dragon; no diagnostic evaluation ran during sampling. Registered creatures changed from 30 to 32 through normal live ecology, with eight sea angels in both counts. This small active population complements the 512-passive-fixture contract check; neither substitutes for a target-device large-population benchmark.

| Counter | Mean | p50 | p95 | Maximum |
| --- | --- | --- | --- | --- |
| Relevance policy loop | .004 ms | .002 ms | .009 ms | .031 ms |
| Main Thread (Editor) | 7.214 ms | 7.029 ms | 8.935 ms | 10.648 ms |
| Physics.Simulate | .085 ms | .000 ms | .262 ms | .497 ms |
| GC Allocated In Frame | 77,520 bytes | 64,167 bytes | 103,271 bytes | 810,295 bytes |

Peak distance evaluations were three per frame. Physics has zero-cost render frames when no fixed step runs. The main-thread marker can include waiting and Editor work, so it is not a direct AI CPU-time metric. GC is the entire recorded Editor frame, not a scheduler allocation measurement. The next performance investigation should attribute allocations and active creature costs in a development Player with a repeatable population/camera route; the current scheduler timings do not justify jobification or a separate physics world. There is no matched pre-change sample, so no numerical overall speedup is claimed.

## Remaining conditional work

The current representative checks do not justify a separate physics world, jobification, or state-only unloading. Those remain optional later phases. Behavior components still own their movement-specific ticks; the scheduler owns relevance and activity requirements. Centralizing every species' Update callback is a separate migration requiring evidence that callback overhead is material and a review of external/manual TickCreature callers.

Next larger-scale work should extend this live Editor baseline to matched larger mixed populations on target hardware, attribute render/physics/GC costs, and measure wake latency. Keep visible procedural presentation continuous even if perception and decision-making are later reduced in frequency. Do not reintroduce jitter by simply skipping rendered poses.
