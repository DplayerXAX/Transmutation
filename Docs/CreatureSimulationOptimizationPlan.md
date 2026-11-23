# Shared creature simulation and optimization plan

Date: 2026-11-23. Status: proposed work, not implemented or profiled.

Implementation update: the first shared activity gates, fish integration, registry-based distance discovery, and moving-frame transport are implemented in the sea-angel prototype. See [implementation checkpoint](MovingIslandImplementationProgress.md). Population performance and later phases remain unmeasured. The user revised validation to a movement-pattern code review and representative runtime tests; exhaustive per-creature testing is no longer a completion gate.

Related design: [Moving sea-angel island technical review](LevelDesign/MovingSeaAngelIslandTechnicalReview.md).

## Decision

Integrate fish into one shared simulation scheduling and dormancy system. Retain fishing-specific activation sources, reservation, dimensional visibility, and fishing behavior as domain logic. One scheduler should resolve the effective simulation state; species and interactions supply requirements rather than independently flipping competing sleep flags.

This is a correctness and extensibility improvement first. Performance benefits must be measured. It does not require every species to have the same activation radius, update frequency, physics representation, or reason for waking.

Implement the common contracts and migrate fish plus one ordinary creature before the moving-island habitat depends on dormancy. Complete the wider optimization in separate stages. Do not delay the initial moving-deck transport experiment for a full creature-system rewrite.

## Present duplication and limits

`CreatureSleep` scans for creatures every five seconds, evaluates distance every half second, and maintains its own sleeping set. It excludes flying kinds by type and calls `Creature.SetSimulationEnabled`. `DimensionalSchool` separately maintains fish cells, awake membership, wake deadlines, and source-driven activation every quarter second. Fish also have `SchoolAwake`, while base creature logic has `SimulationEnabled`.

The base simulation switch pauses normal creature Update logic and age advancement, not every auxiliary motor or Rigidbody. Existing registries do not yet represent unloaded creatures, and queries commonly read live world Transforms. These limitations matter before suspending arbitrary creatures on moving frames.

The two current systems have useful distinct policies: ordinary distance relevance versus dimensional sources such as poles, stones, honey, and lure reservation. Preserve those rules while removing duplicate authority over execution state. Do not replace the fish's source rules with “all nearby fish always awake.”

## Responsibility split

| Shared infrastructure | Domain-specific logic retained |
| --- | --- |
| Entity registration and lifecycle cleanup | Which sources activate dimensional fish |
| Simulation requests, suspension reasons, and effective tier | Fish reservation and catch lifecycle |
| Scheduling intervals, wake grace, and hysteresis | Swimming, flocking, walking, and combat decisions |
| Moving-frame membership and effective position | Dimensional visibility and rendering eligibility |
| Safe physics suspend/resume contract | Species-specific preparation for suspension |
| Optional common spatial-query backend | Fishing eligibility filters and target selection |
| Profiling and budget accounting | Individual tuning profiles |

`DimensionalSchool` may remain the fishing service name. It should stop being an independent simulation governor after migration. Its query implementation may initially remain an adapter to the existing fish index; merging every registry into one database is not a prerequisite.

## Shared state contract

Each registered creature has a simulation profile, movement frame, motion authority, effective activity state, and a suspension capability. Separate requests for behavior, physics, and presentation where their needs differ. A fish may need reservation queries without continuous swimming; a distant thrown creature may need physics without expensive perception.

Use owned request handles for temporary requirements, released on completion, cancellation, death, disable, and scene unload. Examples include FishingReservation, ActiveThrow, Carry, Combat, and proximity/source relevance. Name these by purpose without creating a large universal dependency framework.

Resolve policy explicitly:

1. Death/destruction and authoritative capture/freeze rules determine permitted operations.
2. Active interactions establish the minimum required activity within those permissions. A fishing wake request cannot override a freeze effect.
3. Distance/source relevance chooses the preferred tier for unconstrained activity.
4. Safe-suspension checks may retain physics even if behavior becomes dormant.
5. Presentation relevance selects animation/render detail without independently cancelling gameplay.

Keep environmental relevance, semantic pause/freeze, and distance optimization distinct. The scheduler must only remove its own restrictions. Introduce a compatibility boundary for legacy `SetSimulationEnabled` callers; audit their intent before migrating them. Do not let a scheduler wake undo a capture pause, or let migration reinterpret every existing false value as ordinary distance sleep.

Initially preserve current lifetime behavior for each migrated species: base simulation pause stops age; sea-angel school sleep currently bypasses base Update as well. Avoid accidentally aging sleeping fish when consolidating flags. Keep dimensional visibility separate from activation so an optimization change cannot reveal fish.

## Optimizing the scheduler itself

Start simple and instrument before replacing data structures:

- Reuse lifecycle registration rather than repeated global discovery. Existing `CreatureRegistry` and `Creature.Activated` are starting points, but cover unregister, scene unload, reload/bootstrap, and eventual dormant records explicitly.
- Stagger periodic relevance checks instead of making every creature reevaluate in one frame. Preserve immediate activation for reservation, pickup, and other player interactions.
- Keep source checks active even when their potential targets are dormant; an entirely sleeping school must still be discoverable.
- Partition spatial data by movement frame. Move a frame's broad bounds, then query local member cells. Do not reindex every dormant member merely because the island moved.
- Update local cells when actors actually change cells, while retaining explicit transfer/release handling. Check whether this improves on the current simple index before adding complexity.
- Query relevant source/region combinations and combine duplicate wake requests. Multiple poles near the same player should not force redundant full work if profiling shows it matters.
- Keep awake and dormant work lists so inactive creatures need not all execute their own Update just to return. Audit subclass Update/FixedUpdate and auxiliary components before switching to central ticking; never run both old and new ticks for the same creature.
- Reuse query buffers and measure allocations. Keep maximum wake latency bounded; spread optional work, not immediate collision or interaction correctness.

A central scheduler that loops over every creature and every source each frame can be worse than the current specialized systems. Track scheduler cost separately from the AI work saved. CullingGroup, jobification, state-only unloading, and shared indices are optional later tools, not initial requirements.

## Incremental work packages

**Priority update after prototype playtesting:** perform the creature motion smoothness/presentation review in the [implementation checkpoint](MovingIslandImplementationProgress.md) before resuming the remaining full scheduler migration and optimization. Investigate observed shaking and idle Asura gait by movement pattern, using representative tests. The goo strong-throw/break-sensitivity report is a separate queued change. These reports are recorded only; no fixes were requested in the checkpoint-update task. Existing functional and net-drift results are not proof of smooth visual motion.

| Phase | Deliverable | Exit condition |
| --- | --- | --- |
| A. Baseline and inventory | Profile current fish and general sleep; inventory all simulation switches, movement writers, and callbacks. Record activation/visibility/lifetime behavior. | Reproducible stationary and moving-frame test cases, counts, wake latency, CPU/physics cost, and allocation baseline. No performance claim without measurements. |
| B. Common authority | Registry integration, simulation profile, request/restriction resolution, motion-authority and frame-position interfaces; compatibility adapter for legacy callers. | A migrated entity has exactly one effective-state owner; freeze, carry, reservation, and distance requests cannot undo one another incorrectly. |
| C. Fish and one ground species | Move fish wake deadlines/activity decisions behind the common scheduler; preserve source queries, rendering, catch rules, and original timing. Migrate one ordinary creature alongside fish. | Stationary gameplay matches baseline, no missed dormant fish reservations, and no simultaneous old/new scheduler ownership. |
| D. Moving-frame dormancy | Shared frame membership and safe suspension/resumption for the migrated creatures. Add one additional airborne locomotion case when available. | Brought-aboard animals survive sleep, island travel, wake, release, and throw without stale positions or duplicated transport. This is the island's required integration gate. |
| E. Wider migration and scheduler efficiency | Migrate further species by capability; optimize discovery, work lists, query batching, and spatial partitions where measured. Retire legacy scheduling only when its final users have moved. | Regression cases pass and total cost improves or remains acceptable at the agreed population target. |
| F. Optional large populations | State-only dormant records, streaming/persistence, jobified queries, or separate local physics investigation. | Only proceed if profiling shows represented dormant entities still exceed budget. Correct queries and restore lifecycle are prerequisites. |

The deck/path/ride-surface prototype can proceed alongside phases A–C conceptually; no parallel agents are required. Fish and ordinary creature habitat dormancy should depend on phase D. Final art, world placement, and all-species migration need not be one change or one release.

During migration, assign scheduling ownership explicitly per entity/profile. Legacy `CreatureSleep` must skip migrated entities; `DimensionalSchool` must submit requests rather than also writing their effective awake flag. Temporary adapters should be removed once their consumers migrate, not left as a second permanent authority.

## Verification and measurement

Compare before/after with identical populations, routes, and camera/player paths. Measure median and tail frame/fixed-step time, scheduler time, physics cost, query count, allocations, number of active/dormant bodies, and worst-case activation latency. Population targets and timing budgets should come from the baseline and intended content, not invented universal thresholds.

Required regression cases:

1. Poles, stones, honey, and lure reservations wake the same eligible fish; loss of relevance retains the intended grace period.
2. Fishing reservation activates immediately between periodic scheduler checks. Materialization transfers authority and does not leave a stale wake request.
3. Lifetime, visibility, and capture behavior match the prior policy while dormant.
4. A pause owned by capture/freeze survives scheduler wake, source removal, and scheduler shutdown.
5. Dormant world and island creatures are found from their current effective positions, including when the island approaches a stationary player.
6. Falling/throwing creatures retain required physics; grounded and hovering creatures suspend only under their declared capability.
7. Spawn, disable, destroy, scene unload, and transfers leave no duplicate registry entries or leaked request handles.
8. Multiple wake sources and a large simultaneous approach do not cause unbounded frame spikes or missed interactions. Urgent wake takes precedence over routine budget spreading.
9. Non-migrated species retain their previous behavior, and migrated species are not double-ticked.

No tests or profiler runs have been performed for this proposal. The output of phase A will determine which performance changes are actually necessary; phases B–D establish the shared correctness needed by the moving habitat.
