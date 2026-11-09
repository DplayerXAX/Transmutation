# Ore Colossus and Geode Larva

The **Ore Colossus** is a huge, slow, peaceful creature of rock and ore, inspired by Elden Ring's Walking
Mausoleum. It walks on four long legs and carries a geode shrine on its back. A giant ore mass, its heart,
grows on its back. When it rests it kneels and lowers its tail into a ramp, so the player can walk up
onto its back with water in hand. Only **erosive water** wears the heart away, carving it wherever it touches. When the
heart shatters it drops one **Geode Larva**, a new creature that gives other creatures life, and the colossus
kneels for good.

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
| Resting | At a waypoint it sometimes stops and kneels: over 4 s the body lowers to 0.8 m above the ground and the tail lies down as a ramp (at most 28°) up to the back. It stays knelt for 18 s plus 6–10 s, then stands up over 4 s and walks on. Whoever stands on the back is carried along. |
| Staggering | Each time erosion takes the heart past 75%, 50% and 25% of its integrity, the body drops and shudders for 3.5 s and shoves away anything nearby. |
| Settling | When the heart shatters it drops the larva, tossed off the back to the player's side. Then swinging feet finish their step, the body lowers and levels, the tail lies down as a ramp, and the geode halves swing open. |
| Settled | Permanent. Its colliders move to a static "Ore Colossus Remains" object. Body, back, tail and shrine are on Ground (7) and can be walked on. Legs are on Wall (8) and can be climbed with Goo. The creature stays alive. |

- **Stomps.** A landing foot flings the player outward and upward. It also pushes loose bodies and damages
  small creatures (2.2 m or less) beside it. Water droplets are pushed but never damaged. Just before a foot
  lands, the player is cleared from under it. The space under the belly is safe from feet.
- **Getting on.** Walk up the tail ramp while it rests. While it walks, its legs are also climbable Wall (8)
  and its body, tail, shrine and ore are Ground (7). Climbing grips hold on to the moving legs and body (`MovingGripSurface`), and standing on the
  back or the ore carries the player along (`PoseRideSurface`, an `IRideSurface` like the compost back).
  Stomps only fling things within 2.5 m of the ground, so climbers are safe from them.
- **Immunity.** It ignores blasts, projectiles, stomps and water ageing (`Creature.Invulnerable`). It is never
  an AI target (polarity None), and it cannot be carried or eaten.

## The heart (`OreHeart`)

The heart is an ore mass (3.6 × 3.2 × 3.4 m) grown on the rear of the back, behind the geode shrine: a rough
core bristling with big crystals pointing up and out, at the top of the tail ramp when it kneels and about
11 m above the ground while it walks. Like the terrain it
is a density volume remeshed with marching cubes: every contact carves a hollow where the water touches, and
its concave mesh collider follows (rebuilt at most every 0.2 s), so water runs into the hollows. The collider is
an `ErosionTarget` (see `Scripts/Fluid/README.md`), so water erodes it in three ways:

- **Held.** Carry a droplet (right mouse) and press it into the heart. Moving it across the surface erodes much
  faster than holding it still.
- **Thrown.** A droplet thrown with left click bursts on the heart.
- **Flowing or rain.** Water that touches it erodes it. Erosive rain falls through the colossus's body
  but lands on the ore.

Integrity still counts the total erosion taken, not the volume carved. Fresh cuts fizz with an acid glow
(`CorrodingCrystal` shader), the crystal dims as it wears, flashes and sheds chips while it is eroded, and
shatters into shards at zero.

## Geode Larva

The larva drops out of the shattering heart, only once per colossus. It is a carryable crystal grub.

- **Infuse.** While carried, left click gives 15 seconds of remaining Age to the creature under the crosshair
  (up to 5 m), from a store of 60. A creature is never raised above 600. Dead creatures, water droplets and
  invulnerable creatures are refused. This is the reverse of the TimeSpider, which drains Age. It also
  counters water ageing.
- **Recharge.** Its glow follows its store. Empty, it dims and its prompt says it is drained. It regains
  1.5 per second while resting (not carried, nearly still) within 14 m of any Ore Colossus.
- **Stillness aura.** Whenever it is not carried, a 6 m aura pulses around it. Creatures inside do not age,
  neither naturally nor from water (`StillnessField`). Giving life still works inside it.
- **Immunity.** It is invulnerable, cannot be eaten, and is not an AI target. Webs and bubbles can still
  hold it.

## Settings

| Setting | Default |
| --- | --- |
| Body | 6.5 × 3.6 × 9 m; centre 9.8 m above the feet (top of the geode about 14 m) |
| Legs | upper 6.6 m, lower 6.2 m; feet rest ±5.6 m sideways, ±4.6 m fore and aft |
| Gait | 1.5 m/s, 12°/s turn (twice that standing), 0.9 s steps 1.6 m high, step when a foot lags 1.6 m, feet land on slopes up to 60° |
| Rest | 50% chance at a waypoint; kneels 4 s, holds 18 + 6–10 s, rises 4 s |
| Stomp | radius 5.5 m, player fling 10 m/s, push 6 m/s, damage 20 |
| Heart | 3.6 × 3.2 × 3.4 m ore 1.6 m behind the body centre, 0.25 m grid, carves 0.8 m radius × 0.5 m per unit, integrity 10, sensitivity 4, at most 1 per second, 0.9 × 4 per burst |
| Kneeling | belly 0.8 m above the ground, 4 tail plates of 3 m, ramp at most 28°, 4 s to lower, 1 s to open |
| Placement | 70 m from spawn; flat within 3 m over 16 m; 18 m from sinkholes; inside 60% of the map |
| Larva | store 60, 15 per click, 0.75 s cooldown, 5 m reach, cap 600, recharge 1.5/s within 14 m, stillness aura 6 m |

Erosion time for a single droplet is about 30 s held still, or about 10 s held and moved (the per-second
cap), or about 3 thrown bursts.

## Scripts

| Script | Job |
| --- | --- |
| `OreColossus` | The creature: placement, building, gait and two-bone legs, stomps, phases, kneeling, remains, releasing the larva. |
| `OreHeart` | The weak point: density volume, carving and marching-cubes remesh, glow, thresholds and shattering. A MonoBehaviour, not a Creature. |
| `GeodeLarva` | The new creature: life store, infusion, recharge, glow and the stillness aura. |
| `StillnessField` | Registry of places where creatures do not age; checked by natural and water ageing. |
| `OreShapes` | Faceted meshes: limbs, boulders, slabs, crystals, geode halves, the larva. |
| `Fluid/ErosionTarget` | Generic non-terrain surface that erosive water wears away. |
| `Editor/OreColossusSetup`, `Editor/OreColossusValidation` | Menus and Play Mode checks. |

## Validation

**Tools > Creatures > Ore Colossus > Validate (Play Mode)** runs in disposable local physics scenes. It checks:

- Immunity and AI exclusion, and that the player cannot carry it.
- Climbable layers, moving grips, the ore on top of the back, and that the back carries a rider.
- Kneeling to rest: a walkable tail ramp to the ground, a low back, and standing up again.
- 20 s of walking: leg lengths, planted feet, one swinging pair at a time.
- Stomps.
- The heart's erosion cap and burst throttle, carving where water touches, and staggering.
- Kneeling: remains layers, a level back, a walkable ramp.
- Exactly one larva, dropped when the heart shatters; its infusions, recharge, stillness aura, and casting for
  the meditation journal.
- With a tiny generated world: real water falling on, held against and thrown at an erosion target, and that
  water never ages the colossus.

Not included: Wwise sounds, saving the kneeling state between sessions, and the Creature Wiki entry.
