# Glow Bee / 萤蜜蜂

`Scene_Ariia` contains a `Glow Bee Colonies` object. Enter Play Mode to generate the ecology from the scene's existing ground, rocks and trunks. Nothing is generated or saved into the scene in Edit Mode.

- Default population: three concealed nests, four bees per nest, five nectar flowers per nest. Placement uses seed 814, rejects overlaps and floor edges, ranks overhead cover and shade, and keeps nests at least nine metres apart.
- Bees fly to violet nectar flowers, sip for 2.5 seconds and bring one unit back. Flowers hold three units and replenish one unit every twelve seconds.
- A nest ferments three units of nectar for fourteen seconds to produce one physical drop of luminous honey. It holds at most four drops and twelve units of unprocessed nectar. A full nest waits until honey is harvested.
- Approach within five metres of a nest to see its current honey count/capacity, stored nectar and fermentation progress.
- Pick honey up using the existing player carry controls, or interact with the nest to harvest a drop. While held, left click drinks it and F applies it to the body. Right-click putdown/throw still works. One drop supports one use; merely picking it up does not consume it.
- Drinking grants 45 seconds of honey sight: green night illumination and exposure adjustment, plus the ability to see creatures tagged exactly `transparent`. Set this tag on a Creature root or a visual child; the colony's player effect discovers future spawns. Tagged visuals are normally hidden, appear as luminous silhouettes during honey sight, and hide again when it expires. Physics and AI continue normally. Original materials are restored after revelation.
- Applying honey grants 60 seconds of body light, glowing hand beads and rising sweet-scent motes. Scent expands bees' player-interest range from five to twelve metres. Drinking and coating have separate timers; another drop refreshes the selected effect without unlimited stacking. Honey is not a healing consumable.
- Bees occasionally circle a nearby player for three seconds, then resume foraging with an eight-second social cooldown. Returning with nectar and fleeing spiders take priority. Bees only visit players within 24 metres of their hive, so colonies keep producing honey.
- Both bee abdomens and honey use emissive materials and small point lights. Their glow pulses; the point light grows stronger away from direct sunlight. No global lighting changes or bloom setting are required for local illumination.
- One existing SkySpider prefab is spawned near a flower patch. Bees flee nearby living spiders. Their dynamic Rigidbody and WebCapture use the existing web capture and spider age-draining rules. No changes to other species or spider scripts are required.
- Bee deaths do not automatically respawn replacements. Corpse handling remains owned by Creature. Population and honey storage are bounded.

Main scripts: `GlowBeeColony` generates the population and visuals, `GlowBee` owns foraging and flight, `NectarFlower` owns nectar, `GlowBeeNest` owns storage and fermentation, `GlowHoney` integrates carrying/food, and `GlowBeeLight` owns local lighting.

The scene object's Inspector exposes population, spacing, seed, placement layers and the predator prefab. Runtime nests expose fermentation time, nectar cost and honey capacity (the generated shelf has four physical slots).

`GlowBeeValidation.RunBatch` validates a copied Ariia scene in `.utmp/GlowBeeChecks/UnityProject` (the older `Temp/GlowBeeChecks/UnityProject` location is also supported). The original scene is not opened or saved by that batch check. The 34 integration checks cover autonomous honey production, harvesting and dropped physics, replenishment, nectar conservation, spider capture/predation, drink/coating exclusivity and expiry, future tagged-creature revelation, the proximity storage readout, and player orbit behavior.
