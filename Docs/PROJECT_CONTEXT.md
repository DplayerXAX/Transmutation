# Project context

Repository orientation reviewed **2026-11-23**. Consult this map when needed; individual tasks usually need only the relevant local README and source/assets.

## Project and source of truth

**Transmutation** is a Unity first-person game project with carrying/throwing, climbing, creature-assisted traversal, ecological relationships, combat experiments, procedural terrain, and a Mind Palace/reflection journal. Multiple scenes explore different systems; a working prototype is not necessarily integrated into the main game or included in a build.

Local READMEs describe current feature behavior; inspect code, prefab/scene overrides and runtime setup for actual values. An implementation discrepancy is not automatically intended design. The older Google design document is not well updated and is historical background. The [design guide](DESIGN_GUIDE.md) is a draft for the owner to edit; this overview does not establish game pillars or restrictions.

Some internal identifiers still use **Capstone**, including Unity's product setting and `Tools > Capstone` menus.

## Technical orientation

- **Engine:** Unity **6000.3.6f1** at review time. [ProjectVersion.txt](../ProjectSettings/ProjectVersion.txt) is authoritative; [Packages/manifest.json](../Packages/manifest.json) records package versions, including URP, Input System and Pipeline.
- **Source/content:** `Assets/Scripts/` owns game code and editor tools; `Assets/Prefabs/` owns reusable content; `Assets/Resources/` contains runtime catalogs/tables. See the [prefab index](../Assets/Prefabs/README.md). Some visuals and habitats are built at runtime or baked by tools; inspect the owner before editing a generated result.
- **Scenes/tools:** `Assets/Scenes/` holds levels and prototypes; `Assets/Scripts/Editor/` holds authoring/validation tools, some with persistent write effects. `Assets/Scripts/Dev/` and `Assets/Tests/` include drivers/fixtures. `Docs/` holds feature plans, scene guides and dated reports.
- **Local outputs:** `Library/`, `Temp/`, `Logs/`, `UserSettings/` and generated solution/project files are not game source. `Assets/Generated/` can contain committed assets; do not treat it as a disposable cache.

## Find the relevant system

| Working on | Start here | Important boundary |
| --- | --- | --- |
| Player, climbing, carry, equipment | [Movement README](../Assets/Scripts/Movement/README.md) | Controller, traversal, hands, carrier/interactor and thrower coordinate movement ownership |
| Creature behavior/ecology | [Creature README](../Assets/Scripts/Creatures/README.md), then its local guide | `Creature` owns shared lifecycle/interaction; species own their behavior. Passive items can also derive from `Creature` |
| Simulation or moving islands | [Shared simulation report](CreatureSimulationImplementation.md), [island prototype](LevelDesign/SeaAngelIslandPrototype.md) | Semantic simulation permission differs from optimization dormancy; transport must also move cached goals/poses |
| Terrain and erosion | [Marching Cubes README](../Assets/Scripts/MarchingCubes/README.md), [Fluid README](../Assets/Scripts/Fluid/README.md) | `ProceduralWorld` terrain and small `MarchingCubesVolume` fields have different roles; preserve terrain-ready/changed contracts |
| Habitats/world appearance | [World README](../Assets/Scripts/World/README.md), relevant scene guide | Authored, generated and baked content coexist; some habitat code is under `Assets/Scripts/Realm/` |
| Reflection/journal | [Meditation README](../Assets/Scripts/Meditation/README.md) | Reflection persistence is under `Application.persistentDataPath/Reflection/`; temporary erosion is separate |
| Audio | [Audio README](../Assets/Scripts/Audio/README.md) | Named events through `AudioManager`; Wwise authoring project is `CapstoneTeams_WwiseProject/`, with a Unity backend fallback |

## Scene entry points

[Build settings](../ProjectSettings/EditorBuildSettings.asset) record enabled scenes; at review time these are MainMenu, Daniel, Basin, Exhibition and Lam. Other prototypes are opened directly. Useful feature guides include [Asura battlefield](AsuraBattlefield.README.md) for `Scene Basilt`, [Scene Goo](LevelDesign/SceneGoo.md), [Leaf Garden](LeafGardenPrototype.md), and the island guide above for `Scene-Fishing`.

Use the task's relevant scene rather than assuming one canonical development scene. Check setup/rebuild commands before invoking them; some overwrite hand edits.

## Verification and known gaps

Read the affected feature's validation instructions and prerequisites. Custom checks commonly live under `Assets/Scripts/Editor/`; find candidates with `rg -n 'MenuItem|public static.*Run' Assets/Scripts/Editor`. Compilation and a focused runtime check serve different purposes. Historical reports do not establish the current Editor/Console state or target-hardware performance.

Movement and Meditation READMEs disagree on some resting controls; inspect the implementation before relying on those details. Some creature and support folders lack dedicated guides: add one when working on that feature, rather than requiring a repository-wide documentation sweep. Update this overview only when its high-level facts or navigation change; keep tuning and task history local.
