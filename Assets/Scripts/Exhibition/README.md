# Exhibition

`Scene_Exhibition`: a flat stone hall with one stall per creature or feature, in a ring round the spawn.
Each stall has a sign (name and one or two lines on what it does) facing the middle. The main menu's
EXHIBITION button loads it.

## Setup

**Tools > Capstone > Exhibition > Create Exhibition Scene (Copy Of Scene_Daniel)** (`Editor/ExhibitionSetup.cs`)
copies `Scene_Daniel` to `Scene_Exhibition`, opens it and:

- keeps the player, UI, meditation, audio and the directional light; removes the generated world,
  landmarks, realm sites and everything else,
- adds a 90 m flat floor on the Ground layer (walkable and climbable),
- builds the stalls and the welcome sign, puts the player in the middle,
- adds the scene to Build Settings.

`Scene_Daniel` is not changed. Run it again to rebuild the hall from the current Scene_Daniel and prefabs.

## Stalls

| Stall | Shows | Extra |
| --- | --- | --- |
| GOO | `GooCreature` | Smooth tower (`SlipperySurface`, 14 m) behind it: climb it with goo hands |
| CLIMBING | Bare-hand climbing | Rough 5 m wall |
| TENTACLE | The tentacle creature from Scene_Daniel | |
| FUZZ | A fuzz from Scene_Daniel | |
| BUBBLE FLOWER | `BubbleFlower` | |
| FIRE | `FireSource` + `Fire Flower` | |
| SKY SPIDER | `SkySpider` | |
| COMPOST | `CompostCreature` | |
| SQUIRREL | `Squirrel` | `Nest` behind it |
| BOUNCE SHROOM | `BounceShroom` | |
| CORRUPT SEED | `CorruptSeed` | |
| BIRDS | `BirdCreature` | A 10 bird flock over the hall |
| ASURA | `Asura/GeneralAsura` | |

A missing prefab only logs a warning; that stall keeps its sign. To add a stall, add an entry in
`ExhibitionSetup.Exhibits`.

## ExhibitionStall

One per creature on a stall. Holds an inactive template child, spawns a live copy at start (held still by
`SettleWhenGroundReady` until the floor is found) and grows a new one `respawnDelay` (10 s) after the copy
is lost: dead, disabled, or more than `lostDistance` (40 m) from the stall.
