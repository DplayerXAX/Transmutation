# Space Fishing Prototype

Open `Assets/Scenes/Scene-Fishing.unity` and enter Play Mode. Pick up the pole with RMB, then hold LMB to charge a cast toward the floating sea angels and release to launch. A tap casts about 3 metres; holding for 1.2 seconds reaches the 8-metre maximum. The pole vein fills amber while charging. LMB recalls a cast before a bite. Once bitten, click LMB repeatedly while holding WASD opposite the fish's pull. Diagonals require both keys. Walking, jump and climbing input yield during the struggle; mouse look remains available.

The pole's raised glowing vein fills as the catch progresses. Its small arrow organ points toward the required WASD direction in screen space. Amber indicates a direction change, white flashes acknowledge successful clicks, and red pulses indicate the two-second failure grace at zero. There is no fishing HUD.

RMB cancels the cast and keeps normal tap-to-drop / hold-and-release-to-throw behavior. Sea angels turn and swim head-first in full 3D. Attracted fish first notice and face the bait, then approach at a controlled speed. During a struggle, the fish swims curved paths while successful pulls shorten its tether. Filling the vein triggers a fast final reel-in (0.24 seconds). Caught fish materialize ahead of the player and can be picked up normally. Before capture they cannot collide, be carried, captured, consumed, targeted or damaged. Size determines required progress (12–28).

`Tools > Creatures > Fishing > Create Standalone Scene` creates the scene and assets if absent; existing scenes are opened without replacing authored changes. `Create Assets` fills in missing prefabs and refreshes procedural membrane meshes. Timing and difficulty live on the pole prefab; size and appearance seed live on each fish.

Validation: `FishingValidation.ValidateRules()` or the Validate Rules menu runs numerical checks. In the fishing scene's Play Mode, add `FishingPlayModeProbe` to a temporary GameObject to exercise complete small/large catches, dimensional eligibility, materialization, normal pickup/throw, real held-click input, pause/failure/disable cleanup, blocked placement and RMB cleanup. The probe changes runtime objects only and reports `FishingPlayModeProbe.Result`; do not save it into the scene.

