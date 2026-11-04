# Ceiling Bat Bug: crawling, climbing holds, and local reproduction

Status: design and implementation plan. Creature behavior has not been changed as part of preparing this document.

## Intended experience

Small rocky bugs crawl across connected floors, walls, and ceilings. The player can pick one up to relocate it, or grab a surface-attached bug as a moving climbing hold. A wall with several bugs becomes a bouldering route: reach a bug, hang while it moves, transfer to another reachable hold, and reach a ledge.

The bugs have short lives. On death they lose adhesion and become falling physical corpses that Compost Creatures can absorb. Each bug normally leaves exactly one egg. Eggs hatch in response to nearby vacancies, using local checks and direct communication rather than a population manager.

This expands the ceiling bat bug phase of the Scene Goo plan. It supersedes that plan's finite-population restriction and deferral of reproduction and player attachment. The canyon, cave, crater, Goo teaching obstacle, bird/web ecology, and terrain requirements remain part of the separate scene plan.

## Decisions and starting values

These are first-pass tuning values, not mathematical guarantees for every physical encounter.

| Setting | Starting design |
|---|---|
| Appearance | Small, squat, dark rocky body, short legs, restrained amber markings |
| Normal lifetime |  range of 55 - 75 seconds of active creature simulation |
| Initial population | 24 in Area 2; 12 in Area 3 |
| Initial remaining life | Staggered across 20–60 seconds |
| Reproduction | One guaranteed successful egg placement per lifetime under normal supported conditions |
| Egg laying |  random from 15 to 30 seconds into life, with a small timing variation |
| Incubation | Around 15 seconds, with a small timing variation |
| Ready egg lifetime | Indefinite while waiting; no automatic old-age death |
| Egg handling | Pickup carries intact; deliberate use cracks it |
| Egg fragility | Break on a hard direct impact or a hard player landing while carried |
| Population control | Soft local living-bug threshold, checked at the egg's location |
| Roaming | Bounded around a persistent home position; descendants inherit that home |
| Player attachment | One active bug support at a time, with transfers to reachable valid holds |

Choose density and sensing distances after measuring actual habitat dimensions. A local density threshold is not the total population of 24 or 12 unless its radius actually covers that habitat. Keep home, roam radius, sensing radius, and density threshold visible in tuning data.

## 1. Creature lifecycle and surface crawling

- Use the shared Creature age, death, carrying, capture, and compost-consumption behavior.
- Separate locomotion from lifecycle and player support. Suggested responsibilities: `CeilingBatBug`, `CeilingBatBugSurfaceMotor`, `CeilingBatBugEgg`, and a small player-grip integration component.
- Crawl autonomously using local surface probes and physics movement. Follow surface normals and preserve heading across connected concave and convex transitions. Cover floor → wall → ceiling and their reverse transitions.
- Steer back toward the inherited home without teleporting or crossing unsupported gaps. Descendant homes must not reset to their hatch positions, which would let the habitat drift generation by generation.
- Turn away from unsupported edges when possible. Actual support loss enables gravity; nearby valid surfaces can be reacquired after landing or release.
- Pause autonomous crawling during pickup, capture, throwing, disabled simulation, and unavailable terrain. Hanging from the bug does not count as picking it up and does not pause crawling or ageing.
- On any death, stop locomotion and adhesion, invalidate player support, and enable normal corpse physics. Use existing corpse nutrition and consumption rules.
- Reproduction and incubation use the same active-simulation clock policy as ageing. Suspended or unloaded habitat must not silently advance lifecycle timers.

## 2. One egg per lifetime

- Each bug stores whether it has successfully laid its egg. Never reset that flag on pickup, release, capture, or disable/re-enable.
- Once old enough, lay one egg on a valid nearby habitat surface. Place it clear of terrain and other bodies; it adheres to floor, wall, or ceiling.
- Only mark laying complete after placement succeeds. If there is no valid surface, retry on support acquisition or a bounded delayed retry while eligible. Do not run an unrestricted placement search every frame.
- Defer laying while carried, captured, thrown, or otherwise unsupported. This means forced disruption can prevent replacement; the design promises normal replacement, not invulnerability to intervention.
- Eggs inherit the parent's species, habitat identity/reference, persistent home, roaming bounds, and population tuning. Habitat identity is passive data, not a counting service or manager.
- Make eggs creatures with a clear developing/ready appearance. Pickup carries an egg intact; a separate deliberate use/interaction cracks it and permanently cancels incubation and hatching. A dead egg cannot act as a climbing hold or produce a bug.
- An egg performs one terminal action: hatch once or die. Its parent dying does not kill it.
- New hatchlings start at age zero, with a fresh lifetime and one future egg allowance. Never reroll an adult's lifetime or add multiple hatchlings from one egg.

The one-child rule prevents exponential reproduction without a global egg cap. In a closed population, each founding bug has at most one continuing descendant chain. Living generations can overlap, so the local hatch check still matters. Imported bugs add chains; destroyed eggs and deaths before laying remove chains. There is no automatic recovery after all bugs and eggs are destroyed.

For staggered initial adults, initialize reproductive progress consistently with their elapsed life. If an adult is already past its laying age, ensure it has its one seed egg or lays it promptly on valid support; never do both. Persist this state through normal saves/reloads.

### Egg carrying and breaking

- Use normal Creature pickup, carry, drop, and throw behavior. Pickup detaches the egg from its laid surface without breaking it. Distinguish pickup from deliberate use through the existing interaction conventions and clear prompts.
- Deliberate use cracks the egg, whether targeted in the world or held. It must not also pick up or hatch the egg in the same interaction.
- Reuse the shared Creature impact detection used by Goo, with an egg-specific configurable impact threshold and response. Do not inherit Goo's coating, paint creation, or target-specific effects.
- Break from sufficiently hard solid impacts, including drops, throws, and strikes. Gentle contact and small tumbles survive. Measure closing speed into the surface rather than accumulating distance fallen; begin tuning around an impact equivalent to a two-metre fall under the scene's gravity.
- The shared impact eligibility currently excludes carried creatures. For a held egg, forward the player's landing strength from the existing player movement/landing path to the egg. A hard landing can crack it even though it did not collide independently. Do not add a separate per-egg fall-distance tracker or enable normal collision reactions against its carrier.
- Direct impacts and carrier landings use the same configurable fragility setting where their measurements are comparable; document any conversion needed for the player's landing measurement. Forward the landing only to the egg actually held at that moment.
- Route deliberate use, direct impact, and carrier landing through one guarded `BreakEgg` operation. Claim the terminal state before callbacks or effects, cancel incubation and pending hatch attempts, release it safely from the carrier, invoke shared creature death cleanup, and play the shell-breaking effect once. Shell remnants are inert and cannot hatch or produce another egg.
- A break that has already claimed the terminal state prevents hatching. An egg that has already completed hatching cannot also break and remove or duplicate its child. Repeated interaction, impact, landing, and death callbacks must be harmless.
- A carried, captured, or airborne egg cannot hatch. Incubation follows the active-simulation clock policy; if it becomes ready while held, it waits until released and safely settled. On release, use a nearby valid habitat surface for adhesion where available; otherwise allow normal falling and impact breakage. Recheck local hatch eligibility only after safe settling, with inherited habitat/home data preserved.

## 3. Managerless, event-driven hatching

### Egg eligibility

`TryHatch` rejects eggs that are dead/broken, already breaking/hatching/hatched, still incubating, suspended, carried, captured, airborne, or missing a safe spawn location. It then counts live bugs of the matching habitat within its sensing radius. Exclude corpses and other species; count carried living bugs if physically inside the radius.

If the count is below the local threshold, reserve the egg's own hatch state, create one fully registered live bug, and retire the egg. If spawning fails, restore a valid waiting state without duplicating a child.

Hatch candidates cannot bypass the density check just because a death notified them.

### Wake-up events

1. Incubation completes: the egg attempts to hatch once.
2. A bug dies: after its living status is cleared, it queries eligible nearby eggs and asks them one at a time to try hatching. Stop after the first successful hatch.
3. An egg resumes after suspension or is relocated and settles: recheck readiness and local space.
4. A hatch was blocked only by temporary terrain or spawn clearance: recheck on readiness notification or a bounded, low-frequency retry. Density-blocked eggs simply wait for a relevant event.

Use one death-notification dispatch per actual death. Deduplicate eggs found through multiple colliders. Choose a fair candidate order, such as oldest-ready first, with a tie-breaker, so one location is not always preferred.

Nearby deaths should reach every egg whose density neighborhood included that death position: use compatible notification/sensing radii and filter by habitat. Newly spawned bugs must become visible to subsequent local physics queries before another hatch is accepted; do not rely on the next physics update to expose their colliders. Include this synchronization in the hatch operation.

Keep discovery local through physics queries. Do not add a population manager, central reproduction service, global death broadcast, or per-egg per-frame population scan.

### Deliberate limits

- Different eggs have overlapping but different neighborhoods. This is a soft density system, not an exact cave-wide quota.
- A bug simply walking out of an egg's radius does not trigger an immediate hatch. Waiting until a later death or other wake-up is acceptable for this first version.
- A cliff or thin wall can put two locations close in straight-line distance. Habitat filtering prevents unrelated areas interacting; validate whether the scene needs a cheap obstruction check for remaining false neighbors.
- One nearby death produces at most one replacement through that death dispatch. Eggs reaching maturity independently can also hatch where local density permits.
- Do not add meeting/mating requirements, probabilistic failure to lay, emergency spawning, or bonus eggs in the initial implementation.

## 4. Player interaction: pickup versus climbing support

### Pick up and relocate

- Preserve normal creature pickup, drop, and throw behavior. Pickup removes surface adhesion and pauses the motor.
- On release, reacquire only genuinely nearby surfaces; otherwise fall normally.
- Keep a relocated bug's inherited home for this version. Deliberately creating a new habitat by relocation is a later design decision.
- Pickup and climbing attachment must be distinct, clearly prompted actions using the project's existing interaction conventions. Implement them so one input cannot activate both.

### Grab as a moving hold

- A living, supported, freely crawling bug is a valid handhold without Goo. Eggs, corpses, carried bugs, captured bugs, and airborne bugs are not.
- Validate hand reach and an unobstructed path to the target. Store the hand anchor relative to the bug, so it follows the bug's translation and rotation.
- While held, the bug keeps crawling normally. The player follows its anchor through the existing player movement/collision system; the player does not steer the bug.
- Keep the player upright with natural hanging posture on ceilings. Do not rotate the whole player with the bug's surface normal or directly parent the player transform to it.
- Sweep/check the player's movement against terrain. If the player cannot follow safely or the grip stretches beyond its allowed reach, release cleanly instead of pulling the player through rock or destabilizing the bug.
- A bug can support the player's weight as a gameplay rule; avoid a coupled physical simulation that lets the hanging player tear it off the wall accidentally.
- Holding a bug does not grant grip on surrounding bare rock. Transfers to terrain still obey normal rough-surface/Goo rules.
- Allow deliberate release, jump-off where supported by existing controls, transfer to a reachable bug or valid terrain grip, and mantle onto a reachable clear ledge. Validate reach and body clearance before committing a transfer.
- Keep the old grip until the new grip is accepted, then switch once. Use one active support for the initial implementation rather than a separate two-hand constraint simulation.
- Death, lost surface support, capture, pickup, target disable/destruction, scene unload, or player incapacitation immediately clears the grip and restores the player's appropriate movement/gravity state.
- Preserve sensible movement velocity on release, with a cap to prevent launch spikes. Provide ordinary ledges/fall recovery rather than pausing the bug's age or granting hidden immortality while held.

Show a clear grip prompt only when the bug can actually support the player. Differentiate developing and ready eggs visually, and make imminent bug death readable if playtesting finds unexpected falls frustrating.

## 5. Scene Goo integration

- Leave the other chat's terrain and initial scene work isolated. Coordinate shared-file edits before execution; implement creature behavior in focused files and integrate into the saved scene last.
- Area 2: place the 24 bugs primarily on connected overhead habitat above the Compost Creature. Natural age deaths supply falling corpses. Preserve the probabilistic deposition and existing Goo costs.
- Area 3: distribute the 12 bugs across connected climbing surfaces, with visible reachable sequences, broad rest ledges, and a recovery route. Tune speed and spacing so transfers are possible despite moving holds.
- Retain the first Goo teaching obstacle: its initial arrangement must not be bypassable using nearby bugs. Keep that teaching face outside bug roaming/reproduction reach and verify it remains so over multiple generations.
- Preserve the planned one-application Goo crater route. The bug route is an additional bouldering interaction, not a reason to remove the Goo route or its safety margin.
- Confirm sustainable bug deaths do not flood the scene with corpses or make Goo production trivial. Tune scene placement, population, or lifetime deliberately after observation; preserve shared corpse values and production costs.
- Keep the normal saved terrain workflow, scene organization, connection markers, and unrelated creature tuning from the original plan.

## 6. Execution order

1. **Surface movement and mortality:** verify the bug prefab, transitions, roaming, carry/release behavior, and corpse handling in a small test habitat.
2. **Egg replacement and fragility:** add single-egg state, placement, incubation, local eligibility, death dispatch, safe one-time hatching, and staggered initial-adult handling. Add intact carrying, deliberate cracking, shared direct-impact detection, carrier-landing forwarding, and one-time break cleanup.
3. **Player support:** integrate moving bug anchors, separate pickup from hanging, implement release and safe hold transfers, and preserve existing Goo/terrain climbing.
4. **Scene integration:** populate Areas 2 and 3, author the bouldering opportunities and recovery ledges, and protect the Goo teaching obstacle.
5. **Observation and tuning:** run multi-generation lifecycle and actual player traversals, then tune readable movement, recovery cadence, and corpse supply.

## 7. Acceptance checks

### Lifecycle and reproduction

- A supported bug lays exactly one egg and dies on its normal age schedule, including while a player hangs from it.
- Pickup, capture, suspension, re-enable, and save/reload neither duplicate eggs nor reset age or reproductive progress.
- Initial adults with only 20 seconds remaining still have a valid replacement opportunity.
- A ready egg waits at sufficient local density. A death can wake it; an immature egg missed by a death can still hatch on maturation.
- One death with many ready eggs dispatches at most one successful hatch. Two deaths in the same update cannot ignore an already spawned replacement.
- Dead bugs and eggs are excluded correctly; multiple colliders do not multiply counts or notifications.
- Destroyed eggs never hatch. An egg cannot hatch twice during overlapping wake-up events or failed spawn retries.
- Pick up an egg intact, carry it, and set it down gently without breaking it. Deliberate use cracks both world-targeted and held eggs without also triggering pickup.
- Small drops survive; drops/throws/strikes above the egg's impact threshold break it. Contact with the carrier during ordinary movement does not crack it.
- While carrying an egg, test soft and hard player landings. Only a sufficiently hard landing breaks the currently held egg; releasing it before landing does not forward that landing to the former egg.
- Interaction, direct impact, landing, and hatch readiness arriving together cause only one terminal outcome. Breaking cancels all pending hatching and plays its effect once, including while held.
- An egg that becomes ready while carried waits. After safe release and settling it checks local density and can hatch; one that breaks on landing never hatches. Check suspension/resume and save/reload preserve these states.
- Full removal of bugs and eggs leaves the habitat empty. Imported bugs and interrupted reproduction have the documented effects on lineage count.
- Observe at least ten normal generations in a simple habitat: no reproduction-driven runaway growth, no accumulating duplicate eggs, and no extinction caused by a missed maturation event. Record live/egg counts; do not assert an exact cave-wide quota from local density rules.

### Locomotion and player

- Traverse floor → wall → ceiling in both directions over concave and convex geometry. Lose support and fall without hovering or teleporting.
- Pick up, throw, drop, and reacquire surfaces without confusing pickup with climbing attachment.
- Hang from a moving bug on walls and ceilings, transfer between reachable bugs, release/jump, and mantle onto a clear ledge.
- Reject unreachable, obstructed, dead, captured, carried, and unsupported targets.
- Test grip release during death, support loss, capture, disable, destruction, and scene unload. No stuck movement mode, residual anchor, or launch impulse remains.
- Test a bug crawling around a tight corner while carrying the player's grip: body collision must remain valid or force a clean release.
- Verify ordinary Goo/terrain climbing, carrying other creatures, and their prompts still work.

### Scene and ecology

- Complete the original scene's terrain, web/dead-body, Goo obstacle, and crater-route checks.
- Traverse a readable bug-assisted bouldering sequence with actual controls and recover after a bug dies mid-grip.
- Observe five three-minute ecology runs; record bug/egg populations, corpse buildup, compost deposits, and Goo output. Extend observation if those runs do not cover enough complete generations.
- Verify multiple generations stay inside their intended habitat and never seed a bypass around the first Goo obstacle.

## Deliverables

- Bug and egg prefabs with scene-specific tuning and clear lifecycle visuals.
- Focused surface-motor, reproduction, and player-grip integration changes.
- Updated Scene Goo placements and bouldering opportunities, preserving the original scene requirements.
- Targeted lifecycle/interaction validations and a short record of observed tuning and remaining limitations.
