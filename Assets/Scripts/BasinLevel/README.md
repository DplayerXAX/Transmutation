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
| Spider web | half way between the towers, 3 m above A's top | Sky spider starts there and spins its own web |
| Vent | on the ground under the web | Updraft column up to the web |
| Bubble flowers | (30, 26) + 4 round the towers | Fire every 10-16 s (template in Basin Templates) |
| Fire garden | (22, 18) | Fire source + fire flower; bone children loop round it |
| Goo pool | (-6, 18) | 4 goo blobs, tagged Matter so birds leave them |
| Fuzz | (0, 16) | 4 fuzz creatures |
| Compost creature | (-24, 18) | Absorbs corpses (spider's leftovers, bird bones) and drops new goo |
| Tentacles | (-11, 22), (6, 24), (24, 28) | 3 tentacle creatures |

## Routes to tower B's top

1. **Goo + tentacle:** apply goo (left click, as in Scene_Yoyo) and climb A while it lasts (30 s at most;
   the compost creature makes more from corpses). On A, shoot two arms at B, set the creature down to hang as a cord and walk
   over (or slingshot / reel across).
2. **Fire + bone child + tentacle:** the fire source warms the fire flower, which blooms fire bubbles.
   Bone children go after fire bubbles and hug them; warm, they fly. Carry a tentacle creature and press C
   to mount (its arms are the reins), then fly to B.
3. **Bubble flower + a small creature + updraft + spider web + tentacle:** the vent lifts light bubbles
   (never one holding the player) up into the spider's web, where the bubble bursts and a fuzz, bird or goo
   inside drops into the silk and sticks (normal `SkyWeb` capture). That catch is a fixed point in mid air:
   from the ground, shoot a tentacle arm at it and hold R to reel up, then shoot on to B. The spider eats
   the catch (drains its Age), so there is only so long before it falls.

## Regrowth

Birds eat untagged creatures (fuzz, goo, the tentacle creature). Goo, fuzz, tentacle creatures and
bubble flowers grow back at their home spots some seconds after they are gone or dead, so the level never
locks. Creatures are placed once the world has finished generating.

## Scripts

| Script | Job |
| --- | --- |
| `BasinLevel` | Builds the towers (`LandmarkShapes.Spire` with drums, `SpireStructure`, a floor over the shaft), vent and rings; spawns and regrows creatures; runs the updraft. The spider and its web are the unchanged `SkySpider` / `SkyWeb`. |
| `Editor/BasinLevelSetup` | The two menu commands. |

## Links to other systems

- **World:** `ProceduralWorld` basin settings (`basin`, `basinFloor`, `basinSlope`, `rimHeight`).
- **Landmarks:** tower meshes from `LandmarkShapes` / `SpireStructure`; seed via `RealmSites.Seed` (`LandmarkProgress`).
- **Creatures:** Goo, Fuzz, Tentacle, Bubble Flower, Sky Spider, Birds, Fire cycle, `BoneChild`.
