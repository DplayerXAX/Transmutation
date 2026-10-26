# Ore Colossus and Geode Larva

The **Ore Colossus** is a huge, slow, peaceful creature of rock and ore, inspired by Elden Ring's Walking
Mausoleum. It walks on four long legs and carries a geode shrine on its back. A crystal heart hangs on a
chain under its belly. Only **erosive water** wears the heart away. When the heart breaks, the colossus kneels
for good and the geode opens to release one **Geode Larva**, a new creature that gives other creatures life.

## Adding it to a scene

**Tools > Creatures > Ore Colossus > Add Ore Colossus To Open Scene** creates any missing materials and the
Geode Larva prefab, then adds a "Creature - Ore Colossus". Save the scene to keep it.

- **With a `ProceduralWorld`** (for example `Scene_Marcus`): in Play Mode it waits for the terrain, then places
  itself on flat open ground about 70 m from the spawn. It avoids sinkholes and the map edge, then wanders
  between random waypoints across the map.
- **Without a world**: it builds where it stands and wanders within 30 m on Ground (layer 7). Nothing can erode
  its heart without an `ErosiveFluidSystem`, and the menu warns about this.

Other commands under **Tools > Creatures > Ore Colossus**:

- **Create Assets**: materials in `Assets/Materials/Creatures/OreColossus/` and `GeodeLarva.prefab` in
  `Assets/Prefabs/Creatures/OreColossus/`. Existing assets are kept.
- **Shatter Heart (Play Mode)**: breaks every colossus's heart at once, for testing kneeling.
- **Validate (Play Mode)**: see Validation.

The colossus builds its body from procedural faceted meshes (`OreShapes`) the moment it is placed. Before
that it is parked far below the world.

## Behaviour

| Phase | What happens |
| --- | --- |
| Walking | Walks at 1.5 m/s toward a waypoint. Its legs move in diagonal pairs, one pair at a time, and the feet plant on the terrain. It turns away from sinkholes, steep ground and the map edge. |
| Resting | At a waypoint it sometimes stops for 6–10 s and sinks 2.6 m, so the heart lies on the ground where flowing water can reach it. |
| Staggering | Each time erosion takes the heart past 75%, 50% and 25% of its integrity, the body drops and shudders for 3.5 s and shoves away anything nearby. |
| Settling | When the heart shatters: swinging feet finish their step, the body lowers and levels, the tail lies down as a ramp, and the geode halves swing open. |
| Settled | Permanent. Its colliders move to a static "Ore Colossus Remains" object. Body, back, tail and shrine are on Ground (7) and can be walked on. Legs are on Wall (8) and can be climbed with Goo. The creature stays alive. |

- **Stomps.** A landing foot flings the player outward and upward. It also pushes loose bodies and damages
  small creatures (2.2 m or less) beside it. Water droplets are pushed but never damaged. Just before a foot
  lands, the player is cleared from under it. The space under the belly is safe from feet.
- **Immunity.** It ignores blasts, projectiles, stomps and water ageing (`Creature.Invulnerable`). It is never
  an AI target (polarity None), and it cannot be carried or eaten.

## The heart (`OreHeart`)

The heart hangs on a 5.6 m chain under the belly, with its centre about 2.4 m above the ground while walking.
It swings as a damped pendulum and is kicked sideways by each footstep. Its sphere collider is an
`ErosionTarget` (see `Scripts/Fluid/README.md`), so water erodes it in three ways:

- **Held.** Carry a droplet (right mouse) and press it into the heart. Moving it across the surface erodes much
  faster than holding it still.
- **Thrown.** A droplet thrown with left click bursts on the heart.
- **Flowing or rain.** Water that touches it erodes it. Erosive rain falls through the colossus's body
  onto the heart.

The crystal dims as it wears, flashes and sheds chips while it is eroded, and shatters into shards at zero.

## Geode Larva

The larva rests in the opened geode and is released only once per colossus. It is a carryable crystal grub.

- **Infuse.** While carried, left click gives 15 seconds of remaining Age to the creature under the crosshair
  (up to 5 m), from a store of 60. A creature is never raised above 600. Dead creatures, water droplets and
  invulnerable creatures are refused. This is the reverse of the TimeSpider, which drains Age. It also
  counters water ageing.
- **Recharge.** Its glow follows its store. Empty, it dims and its prompt says it is drained. It regains
  1.5 per second while resting (not carried, nearly still) within 14 m of any Ore Colossus.
- **Immunity.** It is invulnerable, cannot be eaten, and is not an AI target. Webs and bubbles can still
  hold it.

## Settings

| Setting | Default |
| --- | --- |
| Body | 6.5 × 3.6 × 9 m; centre 9.8 m above the feet (top of the geode about 14 m) |
| Legs | upper 6.6 m, lower 6.2 m; feet rest ±5.6 m sideways, ±4.6 m fore and aft |
| Gait | 1.5 m/s, 12°/s turn (twice that standing), 0.9 s steps 1.6 m high, step when a foot lags 1.6 m, feet land on slopes up to 60° |
| Rest | 50% chance at a waypoint, 6–10 s, sinks 2.6 m |
| Stomp | radius 5.5 m, player fling 10 m/s, push 6 m/s, damage 20 |
| Heart | chain 5.6 m, radius 0.7 m, integrity 10, sensitivity 4, at most 1 per second, 0.9 × 4 per burst |
| Kneeling | belly 0.8 m above the ground, 4 tail plates of 3 m, ramp at most 28°, 4 s to lower, 1 s to open |
| Placement | 70 m from spawn; flat within 3 m over 16 m; 18 m from sinkholes; inside 60% of the map |
| Larva | store 60, 15 per click, 0.75 s cooldown, 5 m reach, cap 600, recharge 1.5/s within 14 m |

Erosion time for a single droplet is about 30 s held still, or about 10 s held and moved (the per-second
cap), or about 3 thrown bursts.

## Scripts

| Script | Job |
| --- | --- |
| `OreColossus` | The creature: placement, building, gait and two-bone legs, stomps, phases, kneeling, remains, releasing the larva. |
| `OreHeart` | The weak point: pendulum, erosion, glow, thresholds and shattering. A MonoBehaviour, not a Creature. |
| `GeodeLarva` | The new creature: life store, infusion, recharge and glow. |
| `OreShapes` | Faceted meshes: limbs, boulders, slabs, crystals, geode halves, the larva. |
| `Fluid/ErosionTarget` | Generic non-terrain surface that erosive water wears away. |
| `Editor/OreColossusSetup`, `Editor/OreColossusValidation` | Menus and Play Mode checks. |

## Validation

**Tools > Creatures > Ore Colossus > Validate (Play Mode)** runs in disposable local physics scenes. It checks:

- Immunity and AI exclusion, and that the player cannot carry it.
- 20 s of walking: leg lengths, planted feet, one swinging pair at a time.
- Stomps.
- The heart's erosion cap and burst throttle, and staggering.
- Kneeling: remains layers, a level back, a walkable ramp.
- Exactly one larva, its infusions, recharge, and casting for the meditation journal.
- With a tiny generated world: real water falling on, held against and thrown at an erosion target, and that
  water never ages the colossus.

Not included: Wwise sounds, saving the kneeling state between sessions, and the Creature Wiki entry.
