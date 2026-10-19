# Corrupt Seed

A passive, carryable seed with Plant food tags, no diet, and zero automatic aging.
It has no movement decisions, direct-use action, or autonomous reproduction. Its inherited
Nutrition supplies a meal budget (default 120); only the actual eater receives infection.

Run **Tools > Creatures > Create Corrupt Seed Assets** outside Play Mode, then place
`Assets/Prefabs/Creatures/CorruptSeed.prefab` wherever you want. Setup is repeatable:
existing art, physics tuning, and nonempty references are preserved.

## Infection and death

Eating through `TryConsumeFood` calls the seed's `Consume(consumer)` effect, which
adds or updates a `CorruptSeedInfection` component on the consumer. Its presence
represents infection; the parent has no parasite-specific flag. Additional seed meals
merge food flags into one infection, retaining its first offspring template.

TimeSpider's Age drain is separate from ordinary consumption and never calls this
effect: draining a seed does not infect the spider. A victim infected before being
drained still produces its normal brood when Age reaches zero.

`Creature.Died` is a synchronous C# event, raised exactly once from `base.Die()` after
the creature becomes dead, simulation stops, and `OnDeath()` cleans up abilities. Shared
Age expiry then invokes `OnAgeDeath()` for age-specific remains. Direct death and consumption
do not invoke that hook. Listeners must react independently, leave host removal to its lifecycle code,
and unsubscribe when their owner is removed. Listener exceptions are logged separately
so other listeners and normal death behavior can continue. No scene-wide event bus is used.

An infection listens without an `Update` method. On any host death, including being
eaten, it spawns 3-5 active seeds in the host's scene alongside normal remains.
Offspring food flags are:

```csharp
Creature.FoodTag.Plant | accumulatedSeedTags | deadHost.FoodTags
```

Flags accumulate across generations. Plant seeds from a Meat host become Plant + Meat;
a later Matter host adds Matter. Untagged hosts add nothing. Offspring use the original bird's
tags, not the Matter tags of its bones. An infected bird's age death leaves bones plus seeds;
other deaths follow the normal corpse or consumption behavior and still produce seeds.
Infection does not change the host's food, diet, age, movement, or heat. Eating an infected
host does not directly infect its predator. Seeds never eat, even after gaining Meat tags.

## Prefab reference lifetime

The seed references a shared `CorruptSeedDefinition` asset whose `Prefab` points back
to the seed prefab. This external asset prevents Unity from remapping a direct prefab
self-reference to a spawned seed instance. The infection therefore retains the real
prefab after the original meal is destroyed. The definition has no per-instance state.
Offspring inherit food flags before activation and registration in `CreatureRegistry`.

Missing offspring references log an error at death without interrupting normal death
processing. Disabling a host, removing its infection, destroying an object without
`Die`, and unloading a scene do not generate offspring. An inactive host explicitly
receiving `Die()` still generates its brood.

## Validation

Enter Play Mode, then run **Tools > Creatures > Validate Corrupt Seed (Play Mode)**.
Checks use a disposable scene and real physics, preserve Unity's random state, and cover
diet checks, repeated feeding, template lifetime after seed destruction, tag inheritance,
death notification timing and exceptions, aging, eaten hosts, component cleanup, normal
bird remains, food registration, and gravity. The exception-isolation and missing-template
checks deliberately emit two failure logs; successful validation identifies them as expected.
Results are also written to `Temp/CorruptSeedValidation/results.txt`.

Run the existing **Validate Creature Diet** and **Validate Bird Life and Food (Play Mode)**
checks as regressions. Scene placement, infection visuals, pooling, and general status-effect
infrastructure are not part of this first version.
