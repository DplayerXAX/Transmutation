# Recycle Network

Recycle is an immovable fungal network that grows over connected terrain, consumes touching corpses and grows portable flowers. Flowers drop normal Goo Creatures. The existing `CompostCreature` class and prefab keep their original identities for compatibility; the former moving platform, ramp and riding behavior are retired.

## Assets and components

- `Assets/Prefabs/Creatures/CompostCreature.prefab`: rooted Recycle colony, retaining its legacy asset name.
- `Assets/Prefabs/Creatures/RecycleFlower.prefab`: attached flower and portable creature.
- `CompostCreature`: colony resources, corpse discovery, flower placement and production priority.
- `RecycleNetworkGeometry`: persistent procedural strand graph, growth animation, supported geometry and contact queries.
- `RecycleFlower` / `RecycleFlowerVisual`: flower interaction, local resources, lifetime and procedural blooming model.
- `RecycleGermination`: one-time germination of an eligible dead flower.
- `CompostAbsorption`: exclusive, reversible corpse-consumption claims shared by colony and flower.
- `CreatureRecycleJolt`: harmless visual response for living targets.
- `Assets/Scripts/World/SurfaceGeometry.cs`: shared surface anchors, triangle connectivity and terrain-following paths, also used by Goo.
- `Assets/Scripts/World/SurfaceWorkQueue.cs`: coalesced, staggered mesh updates.

## Surface growth and appearance

Strands follow actual collider triangles rather than a flat world-height projection. Surface anchors store a source face and barycentric coordinates; paths cross connected edges and transport direction around corners. Wider strand profiles also sample the surface. This supports uneven floors and walls while retaining the progressive growth animation.

The graph extends, branches and occasionally reconnects. Fewer fine threads and longer steps keep the network more open. Unsupported gaps and genuinely ambiguous connections stop a path. Zero-area marching-cubes triangles are excluded from connectivity: previously they falsely enclosed the deeper Scene_Goo colony inside a roughly 10-by-10-metre square and caused dense inward growth. Terrain changes invalidate affected anchors and reconnect locally where possible.

The visual reference is blue branching mycelium with green growth tips/nodules. Flowers have turquoise outer leaves, purple inner petals and orange/gold filaments. Their nominal size stays the same with deterministic ±15% variation. Flowers mount outward from the support normal, including sideways on walls; clearance checks reject obstructed placements.

## Current default tuning

| Setting | Default |
| --- | --- |
| Initial colony growth allowance | 32 metres of main strands |
| Germinated seed growth allowance | 16 metres |
| Maximum strand length per colony | 256 metres |
| Growth speed | 4 metres of total strand length per second |
| Main growth step | 0.32 metres |
| Strand width multiplier | 10× the original baseline, tapering along the graph |
| Growth credit per nutrition point | 4 metres |
| Flower capacity | 1 slot per 32 metres of established strands, capped at 8 attached flowers |
| Flower size | 85–115% of nominal size |

Strand metres measure the sum of branch lengths, not the radius or diameter of the covered area. Terrain shape and branching determine the footprint. A slot still needs a valid, sufficiently separated flower site.

## Feeding, flowers and Goo

Each successfully consumed corpse grants `max(1, floor(Nutrition / 20))` recycle points and proportional growth credit. For example, 120 Nutrition grants 6 points and 24 metres of growth credit, subject to the colony's length limit. Growth credit does not spend those stored points.

Digestion takes 5 simulation seconds. The colony has a soft fullness threshold of 50 stored points. Claims prevent duplicate consumption; interrupted consumption restores the corpse. Living creatures are not consumed.

An authored colony with `starterFlower` enabled grows one free starter after its starting strands establish. Additional/replacement flowers cost 5 points and bloom over 5 seconds. Missing flowers take priority over new Goo batches when a valid site is available. Each mature flower reserves 10 points per Goo Creature and takes 7 seconds to produce it; attached flowers share the colony's point bank. Blocked output waits without charging twice. Flower placement clearance can limit capacity below eight.

## Picking up a flower and seeding

Flowers are child creatures from creation. Picking up a mature flower detaches only that flower and leaves the rooted network and its stored points behind. The portable flower starts with an empty point bank; unfinished attached production is refunded to the colony.

Held LMB targets a creature within 5 metres with solid occlusion. Corpses briefly jolt and shrink into consumption. Living targets receive a harmless jolt and are not consumed. Detached feeding funds only that flower's Goo production.

First detachment starts a continuous 120-second lifetime, including time spent dropped. Colour fades as remaining life decreases. Feeding and picking it up again do not reset the timer. An eligible dead flower touching upward-facing terrain germinates once into a new colony with 16 metres of starting growth, no free flower and no stored points. A consumed or already-claimed flower cannot also seed.

## Setup and focused checks

**Tools > Creatures > Recycle** contains asset creation, migration and tuning commands. Migration disables the old platform geometry and preserves scene/prefab identities. Legacy Compost setup entry points route to the fungal implementation. Asset migration/tuning commands modify saved content.

- `RecycleValidation.ValidateAssets()`: asset wiring and shader checks.
- `RecycleValidation.ValidateCollapsedSurfaceSeams()`: small Edit Mode check for collapsed-triangle connections and genuine ambiguity.
- **Validate Focused Gameplay (Play Mode)** / `RecycleValidation.Run()`: disposable geometry, feeding, flower and lifecycle fixtures with accelerated time.

Choose checks matching the change. Size-only edits need saved-setting and surface-fit checks; documentation needs link/content checks. Long ecology runs are reserved for an observed population or production problem.

See [surface design and research](../../../Docs/RecycleSurfaceDesign.md) for the geometry algorithms, procedural animation, references and recorded verification, [BatBug](CeilingBatBugREADME.md) for the corpse supply, and [Goo](GooCreatureREADME.md) for paint behavior.
