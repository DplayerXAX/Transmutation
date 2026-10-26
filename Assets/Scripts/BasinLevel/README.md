# Basin Level

A small test level: a round basin ringed by mountains, two towers, and creatures that give several
ways onto the tall tower's top (the seed `basin_seed`).

## Setup

**Tools > Capstone > Basin Level > Create Basin Test Scene (Copy Of Scene_Daniel)** (`Editor/BasinLevelSetup.cs`)
copies `Scene_Daniel` to `Scene_Basin`, opens it and:

- removes everything except the world, player, UI, meditation, audio and the directional light
  (landmarks, realm sites, test walls and leftovers go),
- moves the scene's tentacle creature and one fuzz into an inactive **Basin Templates** object, plus a goo
  (food tag Matter) and a bubble flower (fires every 10-16 s),
- sets the `ProceduralWorld` to a 176 m basin (`basin` on, `rimHeight` 42, low hills, no caves or sinkholes),
- puts the player in the middle of the basin,
- adds **Basin Level** (`BasinLevel` + `RealmPopulation`) and **Basin Birds** (flock manager + spawn area, 16 birds).

`Scene_Daniel` is not changed. **Set Up Open Scene As Basin Level** does the same to the open scene without copying.

## Layout

Positions are metres from the spawn (x east, y north), set on `BasinLevel`.

| Place | Default | What is there |
| --- | --- | --- |
| Tower A | (-15, 30) | 20 m, climbable with goo on the hands (`slipperyTowers` off), flat top |
| Tower B | (15, 36) | 28 m, floating (nothing below 10 m), flat top with the seed |
| Sky spider | (8, 26), on the ground | Spins its first web low; webs are 1.8x size and walkable |
| Bubble flowers | (-28, 22) + 4 round the towers | Fire every 10-16 s (template in Basin Templates) |
| Fire garden | (24, 26), just south-east of B | Fire source + fire flower; bone children loop round it (8 m) |
| Goo pool | (-4, 27), between the towers, 6.5 m ring | 7 goo blobs, tagged Matter so birds leave them |
| Fuzz | (0, 16) | 4 fuzz creatures |
| Compost creature | (2, 37), between the towers | Absorbs corpses (spider's leftovers, bird bones) and drops new goo |
| Tentacles | (-11, 22), (6, 24), (-24, 38), (18, 22), (-2, 44) | 5 tentacle creatures |

## Routes to tower B's top

1. **Goo + tentacle:** apply goo (left click, as in Scene_Yoyo) and climb A while it lasts (30 s at most;
   the compost creature makes more from corpses). On A, shoot two arms at B, set the creature down to hang as a cord and walk
   over (or slingshot / reel across).
2. **Fire + bone child (+ tentacle):** the fire source warms the fire flower, which blooms fire bubbles.
   Bone children go after fire bubbles and hug them (up to 3); warm, they fly. Two ways to steer:
   left click next to a child climbs on (right click, or left click with nothing in hand that uses it, gets off);
   tentacle arms as reins (keys), or ride without reins carrying the fire source and shoot fire bubbles
   ahead with left click: a warm child flies after free fire bubbles (and refuels when it catches one).
   Flight heat cost is 1.35/s + 0.54 per metre climbed (+20%).
3. **Goo + spider:** web strands are solid (`SkyWeb` walkable). Climb A carrying the spider and set it
   down out over the gap: a spider dropped far from its webs starts a new one there. Walk or climb its web to B.

Without goo the hands cannot climb, but walking or jumping forward into an edge whose top is within reach
(`HandClimber.lipReach`, 1.5 m above the body's centre) still pulls the player over it.

## Scene_Daniel: creatures round the spire

**Tools > Capstone > Basin Level > Add Basin Creatures Round The Spire (Open Scene)** adds a `SpireCreatures`
object and inactive templates (goo tagged Matter, bubble flowers firing every 10-16 s, a spider with 1.8x webs).
In Play mode, once the spire is built: a goo pool (6 blobs) between the two stage towers with the compost
creature beside it, two bubble flowers out from each stage tower, the spider on the ground beyond the second
tower, and 16 birds overhead. Goo, flowers and compost grow back.

## Goo, fuzz and splats

- Fuzz turns red with heat (cools slowly) and green while it holds goo (up to 2 coats). Apply goo with the
  crosshair on a fuzz, or throw goo at it, to coat it; left click a green fuzz any time (carried too) to take a coat.
- Thrown goo that hits a wall or the ground leaves a splat (`GooPaint`): hands climb on it without goo of their
  own and use none; it also gives a grip on surfaces that are otherwise not climbable. Oldest splats dry up past 24.
- Goo-coated hands do not slide on smooth surfaces (`SlipperySurface`, e.g. the spire towers in Scene_Daniel).
  Fast climbing (Shift) uses goo twice as fast (`HandClimber.fastGooDrain`).
- Throw power shows as a bar under the crosshair while right click is held.

## Regrowth

Birds eat untagged creatures (fuzz, goo, the tentacle creature). Goo, fuzz, tentacle creatures and
bubble flowers grow back at their home spots some seconds after they are gone or dead, so the level never
locks. Creatures are placed once the world has finished generating.

## Scripts

| Script | Job |
| --- | --- |
| `BasinLevel` | Builds the towers (`LandmarkShapes.Spire` with drums, `SpireStructure`, a floor over the shaft), vent and rings; spawns and regrows creatures; The spider and its web are the normal `SkySpider` / `SkyWeb`. |
| `RegrowingSlots` | Keeps one copy of a template alive at each home spot; regrows lost ones after a delay. |
| `SpireCreatures` | Scene_Daniel: goo pool + compost, bubble flowers, a spider and birds round the Hanging Spire's two stage towers. |
| `Editor/BasinLevelSetup` | The menu commands (basin scene, set up open scene, add creatures round the spire). |

## Links to other systems

- **World:** `ProceduralWorld` basin settings (`basin`, `basinFloor`, `basinSlope`, `rimHeight`).
- **Landmarks:** tower meshes from `LandmarkShapes` / `SpireStructure`; seed via `RealmSites.Seed` (`LandmarkProgress`).
- **Creatures:** Goo, Fuzz, Tentacle, Bubble Flower, Sky Spider, Birds, Fire cycle, `BoneChild`.
