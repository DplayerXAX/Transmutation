# Main Menu

A simple UI main menu (`Scene_MainMenu`): the title and three buttons on a plain gray background, in the
game's black / white / gray look with a saturated accent colour on hover.

## Setup

**Tools > Capstone > Main Menu > Create Main Menu Scene** (`Editor/MainMenuSetup.cs`) builds and saves
`Assets/Scenes/Scene_MainMenu.unity` and puts it first in Build Settings, then `Scene_Daniel`,
`Scene_Basin` and `Scene_Museum` (if it exists). Scenes already in Build Settings are kept after these.

**Tools > Capstone > Main Menu > Add Menu And Game Scenes To Build Settings** only does the Build Settings part
(run it again after adding a museum scene).

## Buttons

| Button | Scene | Hover colour |
| --- | --- | --- |
| MAP | `Scene_Daniel` | Orange red |
| TEST LEVEL | `Scene_Basin` | Cyan |
| MUSEUM | `Scene_Museum` (placeholder) | Violet |

If a button's scene is not in Build Settings, clicking it shows its `unavailableMessage` instead
(the museum says it is not open yet). To point a button somewhere else, change its `sceneName` in the
`entries` list on `MainMenuController` (on Menu Canvas) and add that scene to Build Settings.

## MainMenuController

On the Menu Canvas. Hooks each entry's UI Button to load its scene, selects the first button so arrow keys
and gamepad work (navigation wraps round), unlocks the cursor, fades to black before loading, and quits on Esc.
Uses the Input System UI module on the EventSystem.
