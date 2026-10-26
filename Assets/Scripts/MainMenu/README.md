# Main Menu

A small 3D menu scene (`Scene_MainMenu`) in the same black / white / gray line world, with saturated
accent colours on the three buttons.

## Setup

**Tools > Capstone > Main Menu > Create Main Menu Scene** (`Editor/MainMenuSetup.cs`) builds and saves
`Assets/Scenes/Scene_MainMenu.unity` and puts it first in Build Settings, then `Scene_Daniel`,
`Scene_Basin` and `Scene_Museum` (if it exists). Scenes already in Build Settings are kept after these.

**Tools > Capstone > Main Menu > Add Menu And Game Scenes To Build Settings** only does the Build Settings part
(run it again after adding a museum scene).

Materials it makes are in `Assets/Materials/MainMenu/` (three URP Unlit accent materials and a bloom /
vignette volume profile). The stone and ground reuse `MAT_Landmark_SpireStone` and `MAT_World_Surface`,
the sky is `MAT_Sky_QuietLines`.

## Buttons

| Button | Scene | Shape | Accent |
| --- | --- | --- | --- |
| MAP | `Scene_Daniel` | Stone globe with an orbiting ring of stones | Orange red |
| TEST LEVEL | `Scene_Basin` | Two towers, a bridge and a seed | Cyan |
| MUSEUM | `Scene_Museum` (placeholder) | Empty frame holding a crystal | Violet |

If a button's scene is not in Build Settings, clicking it shows its `unavailableMessage` instead
(the museum says it is not open yet). To point a button somewhere else, change `sceneName` on its
`MainMenuButton` and add that scene to Build Settings.

## Scripts

- `MainMenuController` — picks buttons by mouse raycast, arrow keys / A D, or gamepad d-pad; Enter, Space,
  click or gamepad south loads; Esc quits. Unlocks the cursor, drifts the camera and leans it toward the
  mouse, fades to black before loading.
- `MainMenuButton` — target scene, label, accent colour, the spinning `shape` and the `glow` renderers.
  Hover makes it rise, spin faster, grow and glow (HDR `_BaseColor` through a property block, picked up by bloom).
- `MainMenuFloat` — slow bob and turn for the shards and motes.
