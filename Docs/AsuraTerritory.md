Current detailed guide: [Asura battlefield README](AsuraBattlefield.README.md). This file also retains historical prototype notes.

## Combat and replenishment revision — November 18

This section supersedes the historical prototype settings below.

- Spawn mix: 62% melee, 23% ranged, 10% rollers, 5% generals. Each shared home attempt draws a type first, then searches for a valid position with enough clearance for that type. Location failures can affect the observed successful mix.
- Basilt: 140 initial fighters per side, fixed cap 180 per side, shared replenishment budget of 2.5 fighters/second per home. Extra linkages divide that budget; they do not multiply production. Two credits maximum; failed placement retains at most one.
- Linkages: 300 HP, no regeneration. Opposing fighters can target and damage them using normal melee, projectile and explosion attacks. Enemy presence alone no longer destroys them. Permanent home roots remain protected. Disconnected branches still stop spawning and expire after ten seconds without reconnection.
- Damage suppresses a linkage's candidate-selection weight to 0.2 for eight seconds after its latest hit. This reduces its opportunities to receive reinforcements; it is not a guaranteed 80% reduction in final spawn counts because candidates are also scored for space and combat proximity.
- Destroying an enemy linkage grants allied fighters within 65 metres of connected surface travel a 12-second breach buff: +15% damage, +25% attack/fire speed, +15% movement speed, and 1.5x detection/retention range. Repeated victories refresh the duration without stacking. No healing. Administrative/disconnection removal grants no buff.
- Damage retains independent 0.75–1.25 random rolls. Growing territory spreads a fixed capped army and shared production more thinly, while the temporary breach buff supports a local push. Balance is provisional.

Light verification: successful Unity compilation; advancing Play frames; controlled 300-HP linkage damage/destruction removed one node, set spawn weight to 0.2, and granted all four expected buff multipliers. Both homes stayed at the 180 cap. A 10,000-draw composition check yielded 61.52% melee, 22.81% ranged, 10.91% rollers and 4.76% generals. Reviewed death callbacks, projectile attribution, shared budget accounting, buff timing and roller cleanup. This is not a long balance soak.

Current Basilt population revision (November 18): see Docs/AsuraPushPullPlans.md. The saved scene now has 82 initial linkages plus two homes, targets 140 starting fighters per side, caps each side at 180, and replenishes at 2.5/second. All 5,429 reachable surface samples are initially covered. The earlier tuning below records the prior prototype settings.

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



## Territory awareness revision

Connected friendly coverage now grants colony members 60m detection and 75m retention, configured by Territory Detection Range on the AsuraTerritory component. The existing 1.5x breach multiplier applies afterward (90m/112.5m during a breach). Outside coverage the creature's normal 20m/25m ranges apply. Acquisition and attack line-of-sight checks and attack reach remain intact.

The manager caches coverage on members using its existing surface lookup and coverage union, twice per second and on network refresh. Fighters do not scan for linkages. Territory searches use the shared spatial index at a 0.5-second interval and only while targetless. A larger radius still increases candidate/raycast work; no profiling claim is made. Light runtime validation confirmed 60 -> 20 -> 60 on leaving/re-entering coverage, 0.5s cadence, and 35 targets farther than 25m. Compilation passed and the Editor console reported zero errors.


