# Asura battlefield, blood coating, and pickup troubleshooting

Current implementation: November 18, 2026. Start here for current behavior. Older observations in [AsuraTerritory.md](AsuraTerritory.md), [AsuraBloodValidation.md](AsuraBloodValidation.md), and [AsuraPushPullPlans.md](AsuraPushPullPlans.md) include historical tuning and are not all current defaults.

## Playing and inspecting Basilt

### Fixed-site capture and marching revision

The saved Basilt encounter now enables **Fixed Capture Sites**. Its 82 authored linkage positions are permanent *locations*, while their living occupants can be destroyed and replaced by either faction. The two home roots are separate, permanent creatures. Old free-growth rules in historical notes apply only when fixed-site mode is disabled before Play.

- A destroyed linkage is removed from the network, made inactive immediately (including visuals and colliders), then destroyed at the end of the frame. There is no one-second corpse. Its site becomes neutral, represented by a small grey noncolliding ring.
- Three allied Asuras must remain within 26m of connected surface travel, with no opposing fighters in that area, for six uninterrupted seconds. Enemy presence or insufficient support resets progress. The site also needs a valid connection to a living home network and room under the node cap.
- The marker takes the claimant's color during capture. A replacement spawns with 300 HP and the new owner's polarity after the timer completes. Ground and body clearance must still be valid; a fighter standing directly on the center can delay completion. Capture never flips ownership directly on the killing blow.
- Capturing preserves the number and positions of sites. There is no procedural placement at new positions, and the old expansion-site spawn bias is disabled. Disconnected branches still expire after ten seconds and leave recapturable sites.
- The existing damage-kill victory buff is preserved. Administrative expiry does not grant it. Army caps, weighted composition, shared spawn budgets, vine preference and blood behavior remain as documented below.

Idle colony fighters now receive a reachable frontier destination: a neutral or enemy site adjacent to supplied friendly territory. Distance and the number of assigned fighters spread orders over the front. A friendly site is not an attack objective. Once a site is captured, its former attackers are reassigned toward the remaining front. If there is no eligible frontier, they have no marching order.

March orders refresh approximately every thirty seconds, staggered slightly between fighters; invalid ownership/supply is checked at the half-second manager update. Nearby combat interrupts marching through the normal enemy acquisition rules. Strategic knowledge of a site's location does not grant visibility through walls, longer weapon reach or remote damage. Rollers march too, using their existing movement speed and combat fuse.

Idle routes follow the baked surface graph rather than going directly across the map. Each destination's distance field is cached and reused; at most one new full destination field is built per manager tick. The set of eligible frontier sites is computed once per home per tick. Final approach samples are precomputed and distributed among fighters, generally 6–18m from the node, to leave room for the replacement body. Short final approach paths are cached per assignment. These paths inherit the bake's resolution and cannot guarantee traversal after terrain changes.

The movement motor also recognizes allied creatures as obstacles for colony fighters. After roughly 2.5 seconds of attempted movement with little progress, it tries a committed sidestep lasting 1.5 seconds, provided a terrain probe finds support. This does not disable collisions or teleport fighters. Long-term crowd jams and narrow-vine traversal still need human playtesting; this is deliberately a small recovery behavior, not a full crowd simulation.

**Inspector:** select **Asura territory battleground → Asura Territory → Fixed battlefield sites**. **Fixed Capture Sites** selects the mode before Play, **Capture Seconds** defaults to 6, and **March Order Seconds** defaults to 30. During Play, **Battlefield Sites** exposes occupants, claimants and capture progress. Existing **Minimum Support** and **Support Radius** control occupation requirements. The old formation/expansion settings are retained for the optional free-growth mode. Do not toggle modes halfway through a run; restart Play after changing the mode or authored site layout.

Implementation is split between `AsuraTerritory.cs` and `AsuraTerritory.Strategy.cs`; ownership and marching data live in `AsuraTerritoryMember.cs`. `CreatureCombat` exposes a new March state, while `RollingAsura` uses the same idle-order helper. `AsuraMotor` implements the local obstacle/stuck response. `AsuraSiteValidation.Run()` runs a disposable Play-mode fixture covering site count, march assignment, disabled free growth, immediate corpse removal, neutralization, breach reward, timed/contested capture, healthy connected replacement and objective release.

Open `Assets/Scenes/Scene Basilt.unity`. The encounter lives under **Asura territory battleground**. Enter Play; the manager waits for procedural terrain generation and then initializes both colonies. Light and Dark begin in opposite corners with a diagonal contested region. The map includes central basalt platforms, lower ground, vine routes and the off-center fossil.

Select the encounter root to inspect **Asura Territory** and **Asura Territory Overlay**. Select either home to inspect **Asura Home**, including living population, claimed area, population cap and spawn rate. Runtime numbers reset when leaving Play.

Coverage visualization is off by default. In Play, use **F8**, the top-right **Territory: OFF/ON** button, or the overlay component's **Show Coverage** checkbox. Gold is Light, purple Dark, ivory overlapping coverage, grey unclaimed sampled surfaces. **Focus Linkage** restricts the visualization to one connected node. A selected linkage's spherical gizmo is only a nominal range; actual claims follow the surface graph.

## Saved Basilt tuning

| Setting | Value / meaning |
|---|---|
| Initial network | 82 linkages plus two permanent homes |
| Initial population | Up to 140 successful spawns per home |
| Living population cap | 180 per home; captured owned creatures still count |
| Replenishment | 2.5 fighters/second per home, shared by every linkage |
| Burst credit | Maximum two; failed placement retains at most one |
| Fighter draw | Melee 62%, ranged 23%, roller 10%, general 5% |
| Linkage health | 300, with no regeneration |
| Claim radius | 65m measured along connected surface samples |
| New linkage spacing | At least 50m from existing nodes |
| Maximum link distance | 100m of surface travel |
| Capture | Three supporting friends within 26m, no enemies, for six uninterrupted seconds; requires home-network supply |
| Expansion | Fixed starting sites change ownership; procedural placement is disabled |
| Disconnected branch | Stops spawning immediately; expires after ten seconds unless reconnected |
| Territory detection | 60m acquisition, 75m target retention |
| Normal detection | Existing fighter defaults: 20m acquisition, 25m retention |
| Vine spawn preference | 35% of attempts prioritize safe vine samples |

Values on scene instances override script defaults. The older **Build Basilt Territory** command creates the original smaller network; it is not the full-map population operation. The later `AsuraMapPopulation.Populate()` authors broad initial coverage and population settings. Do not rerun authoring operations casually over a hand-edited encounter.

## How spawning works

The home owns a fixed budget, not each linkage. More territory distributes the same production across more locations. The cap blocks further spawning even if credits remain. Creature type is drawn first so larger generals receive appropriate clearance checks. If placement fails, another attempt may draw a different type; successful long-run proportions are therefore approximate rather than guaranteed quotas.

Each attempt samples connected friendly surface patches. Placement requires real supporting ground at runtime, adequate body clearance, at least five metres from nodes, 3.2m from other fighters, seven metres from opponents, and fewer than five nearby friends within 11m. Ground normals must be sufficiently upward-facing. These checks also apply to vines and generals.

For a vine-preferred attempt, the first 35 of 70 candidate trials sample cached vine patches directly. Valid vine candidates take priority over ground candidates. The remaining trials provide a ground fallback, so narrow, blocked or unclaimed vines do not stall the whole colony. **Vine Spawn Chance** on Asura Territory tunes this preference; it does not guarantee that 35% of the living population stays on vines.

Other scoring favors a broad engagement band and low crowding. Some attempts favor quiet space. The legacy expansion-site spawn bias is disabled in fixed-site mode. Initial population uses more quiet-space sampling to spread the start out. New fighters may pursue enemies away from their spawn location.

### Why vines were quiet

Vines were not disabled. A diagnostic run before the preference change recorded 20 vine spawns and 17 fighters near vine samples. Only 272 vine samples lay within the connected initial network, compared with thousands of ground samples, so uniform sampling strongly favored ground. Direct vine sampling addresses this imbalance without bypassing terrain safety.

The baked map has 720 vine samples in total. Some are outside the connected starting territory. The six-metre graph is an approximation and can miss narrow branches. Idle marching now follows shared routes on the baked surface graph; combat pursuit still uses local steering. The motor is not a general-purpose navigation planner for arbitrary vine networks. More vine spawning does not guarantee sustained fighting on every branch.

## Territory combat and push/pull

Linkages are rooted Creature instances and accept normal attack damage. Nearby enemy presence no longer destroys them automatically. They cannot be carried, consumed or bubble-captured. Permanent home roots remain invulnerable. Network disconnection is still a separate administrative removal mechanism.

After damage, a linkage has 0.2 candidate-selection weight for eight seconds, refreshed by subsequent hits. This is an 80% reduction in its sampling opportunity, not a guaranteed 80% reduction in final reinforcement count: accepted positions are subsequently scored, and overlapping patches can also supply a location.

An opposing Asura's damage kill on a linkage grants nearby allies a **12-second breach momentum buff**, within 65m of surface travel from the destroyed node:

| Effect | Multiplier |
|---|---|
| Damage | 1.15 |
| Attack/fire rate | 1.25; windup and cooldown are accelerated |
| Movement speed | 1.15 |
| Detection and retention | 1.5 |

Repeated breach victories refresh duration rather than stack multipliers. There is no healing. Disconnection expiry and other administrative removals do not award this buff. Rollers receive faster fuse timing as their attack-speed benefit. Asura damage also uses independent 0.75–1.25 random rolls.

This creates a temporary local advantage while successful expansion spreads a fixed army and replenishment budget more thinly. It is provisional balance, not a guarantee of evenly alternating victories.

## Detection and performance

Inside connected friendly coverage, a colony member acquires at 60m and retains at 75m; the breach buff increases these to 90m and 112.5m. Outside, its normal creature ranges apply. Detection does not increase weapon reach or allow attacks through walls. The colony opponent filter restricts acquisition to opposing Asura fighters and nonpermanent linkages; other encounters retain their own targeting rules.

The territory manager already locates fighters on the surface graph and computes coverage unions. It reuses those results to cache awareness range on members at the existing half-second tick and on connection refresh. Individual fighters do not scan the scene or physics world for linkages. Crossing a boundary can take up to one manager tick to update.

Targetless fighters in territory search every 0.5 seconds using the shared spatial index. Fighters with a valid target do not repeatedly search for another. Larger ranges still increase candidate and visibility-ray work; this is not free and has not received a full CPU profile. The existing nearest-surface lookup and network refresh also have their own costs. Avoid treating the awareness optimization as an optimization of the entire encounter.

Tune **Territory Awareness > Territory Detection Range** on Asura Territory. Avoid raising every creature prefab's base range to solve a single encounter's spacing.

## Blood controls and dependencies

Blood drops from Asura combat deaths. It is a coating interaction, not a carried item: aim at a drop within the usual five-metre interaction reach and use **LMB** with empty hands. When a held usable creature owns LMB, use **F** to coat with aimed blood. The interaction prompt displays the binding. Walking over blood does not consume it. RMB is the carry control and deliberately does not carry blood.

A first coat grants 50 health; matching blood adds 50 up to 100. Opposite blood replaces the current coat with 50 health of the new polarity. A full matching coat rejects another drop. At zero coat health, polarity returns to None. Blood does not replace ordinary player movement or change numerical luminosity.

### Fixed Basilt failure

Basilt's authored player lacked `PlayerCreatureStats`. `AsuraBlood.TryCoat` could find the pickup but had no player coat state to apply it to, so it returned false. This was a missing dependency, not a blood collider problem.

`SmoothFirstPersonController` now requires `PlayerCreatureStats` and ensures it during Awake for older saved instances, because adding RequireComponent alone does not repair existing scenes. The interactor and first-person body also resolve that same component idempotently. Basilt and Fishing have their player dependency saved. Existing stats are preserved; repair does not reset their luminosity or add duplicates.

`FirstPersonBody` uses its explicitly assigned blood material when present, otherwise loads `Assets/Resources/AsuraBloodCoat.mat`. This ensures an included runtime material/shader for older scenes. The existing blood HUD creates itself for a scene containing a player controller with stats. A manually authored HUD still takes precedence.

If coating fails, check the aimed prompt, distance, obstruction, whether hands own another action, the current coat's fullness, and interaction locks such as riding/cutscenes. Check that the player's stats component is enabled. Do not solve this by making blood carryable or bypassing the interaction ray.

## Caught fish pickup fix

In `Assets/Scenes/Scene-Fishing.unity`, uncaught Sea Angels are dimensional and cannot be picked up. Winning the catch makes the fish physical and deliberately dead, so it behaves as a caught body rather than continuing to swim away.

The shared carrier's `CanRetrieve` previously rejected all dead creatures, while `Creature.TryBeginCarry` and the carry/throw system supported corpses. The normal aimed pickup therefore rejected a valid caught fish. Removing that blanket death rejection restores consistent corpse pickup; individual `CanBeCarried`, size limits, capture/freeze state, consumption state and scene/hand rules still apply. No fish collider enlargement was needed.

Put down the pole, aim at the caught fish within reach, and use **RMB** to pick it up. The usual forgiving selection cast still respects solid occlusion. Uncaught dimensional fish remain unpickable because their own carry policy rejects them.

## Authoring and maintenance

The baked asset is `Assets/Generated/AsuraTerritory/BasiltSurfaces.asset`. Surface indices distinguish ground, raised basalt and vines. Influence measures graph travel rather than distance through air. Territory area is the union of sample areas, so overlapping nodes do not double-count the same surface.

After changing terrain or vines, run `AsuraTerritorySetup.Bake()` in Edit mode and review the network placement/coverage. Runtime spawn raycasts reject removed ground, but they do not discover newly created terrain absent from the bake. Keep encounter bounds explicit for open-world use. The node cap, map boundary, support requirements and population cap prevent unlimited expansion; broader regional strategy remains future work.

Generated terrain caches can disappear after a reload despite their completion flag. Regenerate the ProceduralWorld objects before evaluating coverage or spawn failures. Do not save temporary Play-mode test actors into the scene.

Colony corpses expire after 25 seconds; colony blood after 120 seconds. Quiet fighters recycle after 180 seconds without a target. Carried or bubble-captured fighters are protected from that cleanup and still consume population capacity. Cleanup does not award breach buffs.

## Main code locations

| File | Responsibility |
|---|---|
| `Assets/Scripts/Creatures/AsuraTerritory.cs` | Budget, placement, network, support, awareness cache, death rewards |
| `AsuraSurfaceMap.cs` | Baked surface connectivity and graph travel |
| `AsuraHome.cs`, `AsuraLinkage.cs` | Population owner and rooted damageable node |
| `AsuraTerritoryMember.cs` | Ownership, awareness values, bounded cleanup |
| `AsuraBreachMomentum.cs` | Nonstacking timed multipliers |
| `CreatureCombat.cs`, `RollingAsura.cs` | Acquisition and combat behavior |
| `AsuraBlood.cs`, `AsuraBloodDrop.cs` | Blood interaction and combat-death drops |
| `Assets/Scripts/Movement/PlayerCreatureStats.cs` | Player coat, polarity and blood rewards |
| `PlayerCreatureInteractor.cs`, `PlayerCreatureCarrier.cs` | LMB use, contextual F use and RMB pickup |
| `FirstPersonBody.cs` | First-person coat shell and resource fallback |
| `Assets/Scripts/Editor/AsuraTerritorySetup.cs` | Original encounter authoring and surface bake |
| `Assets/Scripts/Editor/AsuraMapPopulation.cs` | Full-map starting network population |

Paths without a directory in this table share the directory of the preceding full path in their group.

## Verification scope

Previous light checks verified 300-HP linkage destruction, spawn suppression, all breach multipliers, population caps, weighted composition, and 60m → 20m → 60m awareness on leaving/re-entering coverage. These were controlled tests, not long balance or performance soaks.

The caught-fish regression was reproduced using the real Sea Angel prefab and catch transition, then verified with the player's normal aimed `FindCreature`, `CanRetrieve`, and carry operation. All passed after the shared pickup fix. The blood validation's old exact melee-damage assertion was updated to the intended ±25% damage interval rather than removing random damage.

Final checks for this revision: Unity compilation succeeded; all 99 AsuraBloodValidation checks passed. In the actual Basilt scene, an aimed blood drop applied 50 coat health, with one blood HUD and the fallback material available. The live population recorded 222 vine spawns out of 654 total (33.9%). These are spawn counts, not evidence that every vine is navigable or stays occupied.

The console was not clean: Wwise reported an Unknown/Dead game object ID for Main Camera during the final run. That audio registration issue remains outside these pickup/spawn fixes. Older retained logs also contain physics and memory errors; they are not evidence of a passing long-term stability test.



Fixed-site revision validation: Unity compilation passed; AsuraSiteValidation.Run passed all 13 assertions after the route-cache optimization. A live Basilt sample retained 82 sites and 84 occupied nodes including homes, with 44 marching fighters, 43 moving faster than 0.25m/s horizontally, and both colonies below the 180 cap. The current Editor console reported zero errors and warnings. No natural site captures had occurred by that short sample; takeover timing was verified with the controlled fixture, not a long balance soak.



### Stable marching and slope walking revision

Asuras march toward frontier sites; there is no random roaming state. The saved march-order interval is now 30 seconds. A 40-metre score preference for the current destination prevents small changes in crowd allocation from repeatedly reversing direction. Invalid ownership/supply still causes reassignment without waiting for that timer. Each long-route waypoint persists until reached or until the fighter is displaced far off its route. Previously, choosing the nearest surface again every manager tick could turn a fighter back toward the cell it had just left. The final approach has a wider exit threshold to prevent switching modes repeatedly at its boundary.

Obstacle bypasses commit for up to three seconds and include forward progress. Each fighter keeps a consistent preferred passing side; the stuck response changes side if its support probe rejects that side, rather than alternating every attempt. These changes reduce dithering; they do not remove collisions or guarantee that every narrow passage can accommodate a crowd.

`AsuraMotor` now controls living grounded movement in the terrain's tangent plane. It uses a body-sized support cast, excludes creature bodies as ground, accepts slopes up to **45 degrees**, accelerates toward a slope-aligned walking velocity and cancels only the gravity component along the slope. This is active locomotion, not a friction/material adjustment. It leaves normal gravity/contact handling in place, releases control when airborne or dead, and respects the existing knockback grace interval. Steeper slopes remain physical and cannot be actively walked up. The wider general uses a box support cast because its uphill corner can contact a slope while a center ray or smaller sphere misses the ground.

Inspector: the existing **Asura Motor** component exposes **Maximum Walkable Slope** (45) and **Ground Probe Distance** (0.25m). **March Order Seconds** remains on the battlefield manager. The motor defaults apply to all Asura prefabs without scene-specific slope materials.

`AsuraWalkingValidation.Run()` uses a separate physics scene and real melee, ranged, general and roller prefabs on a frictionless 30-degree ramp. All 22 checks passed: support detection, three-second idle hold (drift rounded to 0.000m for each type), uphill travel, stopping, knockback preservation, rejection of a 65-degree slope and dead-body release. These are controlled locomotion checks; varied vine geometry and large crowds still need playtesting. The site validation also checks that a long-route waypoint survives a nearest-cell change without reversing direction.
