# Glow Bee / 萤蜜蜂

`Scene_Ariia` contains a `Glow Bee Colonies` object. Enter Play Mode to generate the ecology from the scene's existing ground, rocks and trunks. Nothing is generated or saved into the scene in Edit Mode.

- Default population: three concealed nests, four bees per nest, five nectar flowers per nest. Placement uses seed 814, rejects overlaps and floor edges, ranks overhead cover and shade, and keeps nests at least nine metres apart.
- Bees fly to violet nectar flowers, sip for 2.5 seconds and bring one unit back. Flowers hold three units and replenish one unit every twelve seconds.
- A nest ferments three units of nectar for fourteen seconds to produce one physical drop of luminous honey. It holds at most four drops and twelve units of unprocessed nectar. A full nest waits until honey is harvested.
- Pick honey up using the existing player carry controls, or interact with the nest to harvest a drop. After release it falls normally and remains a luminous, edible Plant object. Honey is not a healing consumable.
- Both bee abdomens and honey use emissive materials and small point lights. Their glow pulses; the point light grows stronger away from direct sunlight. No global lighting changes or bloom setting are required for local illumination.
- One existing SkySpider prefab is spawned near a flower patch. Bees flee nearby living spiders. Their dynamic Rigidbody and WebCapture use the existing web capture and spider age-draining rules. No changes to other species or spider scripts are required.
- Bee deaths do not automatically respawn replacements. Corpse handling remains owned by Creature. Population and honey storage are bounded.

Main scripts: `GlowBeeColony` generates the population and visuals, `GlowBee` owns foraging and flight, `NectarFlower` owns nectar, `GlowBeeNest` owns storage and fermentation, `GlowHoney` integrates carrying/food, and `GlowBeeLight` owns local lighting.

The scene object's Inspector exposes population, spacing, seed, placement layers and the predator prefab. Runtime nests expose fermentation time, nectar cost and honey capacity (the generated shelf has four physical slots).

`GlowBeeValidation.RunBatch` validates the copied Ariia scene in `Temp/GlowBeeChecks/UnityProject`. The original scene is not opened or saved by that batch check. It checks actual autonomous honey production, harvesting and dropped physics, replenishment, nectar conservation, and compatibility with spider capture/predation.
