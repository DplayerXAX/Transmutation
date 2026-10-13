# Creature food and diet tags

The parent `Creature` script provides one universal tag rule for dietary compatibility.
Every child creature inherits it. Configure defaults on a creature prefab to apply the
same category limits to all instances; scene instances can override those settings.

This is a shared limit, not a complete eating behavior. Individual creatures can require
additional conditions alongside it: a target must be fallen, an eater must be hungry,
or a cooldown must have finished. They combine those conditions with `CanEat(target)`.

## Inspector setup

Under **Food and Diet**, select any combination of these flags: `Meat`, `Plant`, `Matter`.
These are enum flags in the script, not Unity's GameObject tags.

| Setting | Meaning | Default |
| --- | --- | --- |
| Food Tags | What this creature counts as to another eater. | None |
| Diet Tags | What categories this creature accepts. | None |
| Can Eat Same Type | Allows other instances with the same concrete creature script class, if their tags match. | Off |

Example settings:

| Creature | Food Tags | Diet Tags |
| --- | --- | --- |
| Plant creature | Plant | None |
| Meat-eating creature | Meat | Meat |
| Mixed creature | Meat, Plant | Plant, Matter |
| Creature that eats everything | Matter | Everything |

Any matching tag is sufficient. A diet accepting `Plant` can eat a target tagged
`Meat, Plant`. Different creature classes sharing `Meat` are still different types.

`None` means no tags: an empty diet accepts nothing, and an untagged target matches no
ordinary diet. `Everything` in **Diet Tags** accepts every `Creature`, including
untagged creatures and future categories. In **Food Tags**, it simply provides all
categories; it does not grant an empty diet permission to eat it.

Same-type eating is blocked by default, even with an `Everything` diet. Turning on
**Can Eat Same Type** still requires matching tags or `Everything`. The eater itself
and null targets are always rejected. Type means the exact concrete script class,
not the creature's display name, food tags, or prefab identity.

## Using the rule

The read-only properties are `FoodTags`, `DietTags`, and `CanEatSameType`.
`CanEat(Creature target)` checks compatibility without changing either creature.

An eating behavior can check the shared rule first, then its own conditions:

```csharp
if (!CanEat(target))
{
    return;
}

// Check this creature's additional feeding conditions here.
// Search, approach, consume, and apply food effects in the eating behavior.
```

The shared method does not check distance, hunger, simulation state, target readiness,
or cooldowns, and does not destroy objects. `Everything` covers `Creature` targets,
not arbitrary scenery or objects without a `Creature` component.

Existing **FireEater** behavior does not yet call this system. It still seeks and eats
fallen FireBubbles using its existing rules. Setting these fields alone does not change
that behavior or make other creatures start eating.

## Consumption and death

`Consume(Creature consumer)` is a virtual hook on the food for nutrition or special
effects. Its base does nothing: calling it alone neither kills nor removes the food.
`Die()` is a separate virtual lifecycle method; its base records death, sets remaining
Age to zero, and disables simulation without changing physics or destroying the object.

An eating behavior calls `TryConsumeFood(food)` after checking its own contact or
readiness conditions. This shared handoff checks dietary compatibility and active state,
claims the target before effects run, calls `food.Consume(this)`, then `food.Die()`, and
destroys any remaining original GameObject. `IsConsumptionPending` prevents competing
eaters from repeating effects while Unity defers destruction. Food may still be eaten
when its own simulation is paused or dead; eater simulation must be active.

Birds use this handoff on collider contact only while freely flying with positive Hunt
Desire. Their prefab diet accepts Plant. Bird death overrides create one Bird Bones
object, whose food tag is Matter. Eating bones removes them without further remains.

No nutrition amounts or Age gains are assigned yet. Future subclasses implement those
effects in `Consume`, leaving death transformations in `Die`.

## Adding a category

Edit the nested `FoodTag` **enum** in `Creature.cs`, not an `IEnumerator`.
Add the next unused power of two, for example `Fungus = 8`, before `Everything`.
Keep the existing numeric values unchanged so saved prefab and scene settings retain
their meaning. New categories use `16`, `32`, and so on; do not use overlapping values.

After Unity recompiles, select the new tag on relevant prefabs' **Food Tags** and
**Diet Tags**. `Everything = -1` automatically includes the new category.

## Validation

Run **Tools > Creatures > Validate Creature Diet** in the Unity Editor. It checks
matching, exclusions, defaults, future categories, and inherited serialization in
an isolated preview scene without changing authored scenes or prefabs.
