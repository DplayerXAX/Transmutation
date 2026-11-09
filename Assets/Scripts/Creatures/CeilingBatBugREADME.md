# Ceiling BatBug

BatBugs crawl on floors, walls and ceilings. Living, attached bugs can carry the player as moving handholds. They age, whiten, die and fall as normal meat corpses, providing food for Recycle networks. Each bug can lay one replacement egg during its lifetime.

## Assets and components

- `Assets/Prefabs/Creatures/CeilingBatBug.prefab`: adult creature.
- `Assets/Prefabs/Creatures/CeilingBatBugEgg.prefab`: portable, breakable egg.
- `CeilingBatBug`: creature state, ageing appearance, leg animation and support eligibility.
- `CeilingBatBugSurfaceMotor`: local surface probes, heading transport, home steering and adhesion.
- `CeilingBatBugLife`: lifetime, one-egg allowance, nearby death notifications and corpse retirement.
- `CeilingBatBugEgg`: incubation, attachment, local hatch checks and guarded hatch/break outcomes.
- `CeilingBatBugHabitat`: passive habitat bounds and tuning; it is not a population manager.
- `Assets/Scripts/Movement/PlayerBugGrip.cs`: moving player anchor, reach/obstruction checks, safe release and held-egg landing impacts.

## Movement and player interaction

The motor follows nearby collision surfaces on layers 7 and 8, rotating its heading with the surface normal. It steers toward its inherited home when nearing the roaming limit. Unsupported bugs fall and can reacquire nearby surfaces; they do not teleport across gaps. Pickup, capture, throwing, suspended simulation and unavailable terrain pause crawling. Descendants retain the parent's home rather than shifting the habitat each generation.

Aim at a reachable attached bug and hold LMB to grip it. Release LMB to let go, or press Space while gripping to jump off. Aiming at another reachable bug can transfer the grip; W can request a valid terrain/ledge transfer through `HandClimber`. The player stays upright and follows through collision checks while the bug continues crawling and ageing. Carrying a creature prevents gripping. Dead, captured, carried and airborne bugs cannot support the player, and loss of support releases the grip.

Normal pickup/drop/throw remains separate from gripping. Eggs can be carried intact. Deliberate interaction or held LMB cracks an egg; sufficiently hard impacts and hard player landings while holding it also break it. Breaking is terminal and cannot produce a hatchling.

## Lifecycle and tuning

These are habitat component defaults; saved scene overrides take precedence.

| Setting | Default |
| --- | --- |
| Newborn lifetime | 55–75 seconds |
| Egg-laying age | 15–30 seconds elapsed |
| Eggs per adult lifetime | At most 1 successfully placed egg |
| Incubation | 13–17 seconds |
| Roaming radius | 18 metres |
| Local sensing radius | 10 metres |
| Local living threshold | 7 matching-habitat bugs |
| Unclaimed corpse retention | 180 seconds |

The installed adult prefab crawls at 1.2 m/s. The egg prefab's minimum impact speed is 11 m/s. Both settings are configurable.

An incubated egg attempts to hatch only when safely attached, active, inside its habitat and clear of obstructions. It counts living bugs of the same habitat within its sensing radius, including carried living bugs, and waits if the local threshold is met. This is a local density limit, not a cave-wide population cap.

Incubation completion, resuming/settling and a nearby adult death can trigger hatch attempts. Death notifications try oldest-ready eggs first and stop after one successful replacement. Clearance-blocked eggs use bounded retries; density-blocked eggs wait for a relevant event. Each egg hatches once or breaks once. Destroying every adult and egg ends that population; there is no emergency respawn. Carried corpses and corpses claimed for recycling are protected from timed retirement.

## Setup and focused checks

In Edit Mode, **Tools > Creatures > Ceiling Bat Bugs > Install Assets And Update Scene Goo** installs/wires the assets and, when Scene_Goo is open, updates its habitat placements. This changes assets and scene content; it is not a read-only check.

For relevant behavior changes, use **Tools > Capstone > Goo > Validate Bugs And Dead Web Filtering (Play Mode)** and the shared impact validation for egg-impact changes. Check only affected behavior; documentation and numeric tuning do not require a long lifecycle run.

See [the original design plan](../../../Docs/CeilingBatBugDesignPlan.md) for design intent and broader acceptance criteria, and [Recycle Network](RecycleNetworkREADME.md) for corpse recycling.
