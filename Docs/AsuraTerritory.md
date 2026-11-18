# Asura homes and surface territory — first playable version

Scene: `Assets/Scenes/Scene Basilt.unity`. The `Asura territory battleground` root owns the encounter. Enter Play to populate it. No manual activation is required.

## Organisms and behavior

`AsuraHome` is a permanent rooted creature and shared population budget. `AsuraLinkage` is a smaller rooted creature of the same polarity. Neither can be carried, captured, consumed, or removed by incidental combat damage. Linkages disappear under sustained enemy presence; roots remain permanent.

Each linkage owns a patch of connected walkable surfaces. The baked six-metre graph distinguishes ground, basalt shelves and vine tops, rejects steep transitions, checks intermediate support and walls, and has explicit Basilt bounds. Influence is measured along that graph, not through air. Territory area is the union of sampled surface patches, counting overlap once and separate elevations separately. It is an approximation, not exact mesh area.

The revised, wider layout uses a 65-metre influence radius (about 5.8 times the old 27-metre footprint), minimum new-node spacing of 50 metres, and maximum surface-link distance of 100 metres. Starting nodes are about 80 metres apart along routes. Each home is limited to 16 total nodes, including its root. The starting networks extend from opposing corners to the main shelf, with the Light network routed through a vine approach.

Three friendly fighters within 26 metres of surface travel must outnumber enemies for six seconds to establish a linkage. Expansion is attempted every four seconds, with one forming linkage per home. Formation times out if sustained support never arrives. Pressure uses a separate 36-metre local radius: 12 seconds of enemy superiority collapses a linkage; friendly recovery reduces pressure. Disconnected branches stop spawning immediately, reconnect through any available overlapping route, or collapse after a 10-second grace period.

Each home starts with up to 24 fighters, shares a rate of 0.65 replacements per second, and caps living owned fighters at 42. Failed attempts retain at most one spawn credit; the total burst budget is capped at two. New units use the existing Melee and Ranged Asura prefabs. Their pursuit, attacks and damage are unchanged; colony units opt into Asura-only enemy targeting. Other encounters retain their prior target rules.

Spawns sample the whole connected patch, including vines, with runtime surface and body-clearance checks. They stay at least five metres from nodes, 3.2 metres from other fighters, and seven metres from enemies. Local crowding is capped at five friendly fighters in an 11-metre radius at spawn time. Thirty-five percent of replacement attempts now favor a shared reachable edge site so a supporting group can assemble. Remaining attempts retain the broad engagement-band preference and twenty-percent quiet-area choice. Link candidates favor newly covered surface area. Edge sites expire after sixty seconds if no linkage establishes. These are spawn rules, not restrictions on movement once fighting begins.

Gold/purple claw-like organisms and surface-following tendrils show ownership. Forming, pressured and disconnected links dim; arrival rings mark new fighters. Selecting the manager in Play shows sampled coverage and map bounds. Selecting a linkage shows its nominal range (the actual patch follows surface connectivity).

## Bounded cleanup

Colony corpses expire after 25 seconds, and blood created by colony deaths expires after 120 seconds. Quiet fighters recycle after 180 seconds without a target. Carried, bubble-captured and pending-consumption creatures are protected from this cleanup. Living captured units still count toward the home cap. Existing creatures and blood outside this encounter are unchanged.

## Authoring and limits

`Tools > Creatures > Asura > Build Basilt Territory` creates the assets and starting networks and refuses to replace an existing encounter root. `AsuraTerritorySetup.Bake()` refreshes terrain caches and the static surface graph; it ignores dynamic creature occupancy. Rebake after terrain changes. Runtime spawn checks reject missing ground, but the territory graph is not rebuilt continuously.

For future open-world use, author a different surface map/boundary per combat region. Explicit encounter bounds and a node cap are already enforced; dynamic regional expansion policy remains future work. This does not add strategic orders or full pathfinding to individual Asuras. Surface connectivity is not proof that every existing motor will navigate every vine bend.

## Review and light verification

The first live run produced combat immediately: at about 53 seconds, each home had spawned 58 fighters, 12 new linkages had formed, and the living populations were 35 and 29. Code review subsequently tightened wall-crossing checks, moved starting sites onto the connected walkable component, made baking resilient to missing editor chunk colliders, and preserved the colony-only targeting setting through reloads. Influence and spacing were then enlarged in response to the review. Final verification is intentionally a short connectivity/combat check, not a long balance soak or exhaustive traversal test.

Final wider-layout check: 11 authored nodes. At approximately 47 seconds, two new nodes had formed and one had collapsed; all 12 remaining nodes were connected. Each home had spawned 54 fighters, with 30 Light and 36 Dark living. Six successful spawns were on vine surfaces. There were 15 current combat targets and 275 attack releases among remaining fighters; no new runtime errors were reported. This was a short smoke check, not a balance claim.


## Coverage overlay

Off by default. In Play mode, press **F8**, use the top-right **Territory: OFF/ON** button (when the cursor is available), or toggle **Show Coverage** on the `AsuraTerritoryOverlay` component. Gold is Light coverage, purple Dark, ivory overlapping claims, and grey unclaimed sampled ground. The overlay is depth-tested and follows separate ground/vine surface samples; it does not render a solid spherical claim through empty air. Assign **Focus Linkage** in the Inspector to inspect a single connected linkage. Forming and disconnected nodes do not claim coverage.

The overlay includes baked unclaimed surfaces that may be unreachable from either current network; grey does not promise a location can currently be colonized. Side growth still requires a reachable link, spacing, sustained local friendly support, and available population/node budgets. No territory is granted solely by showing the overlay.

A roughly 33-second smoke check verified default-off, on/off toggling, Game-view controls, surface rendering, and two newly formed linkages with no fresh runtime errors. Side-area distribution remains a tuning pass rather than a long-run balance guarantee.
