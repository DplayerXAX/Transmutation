# Working in Transmutation

## Read what the task needs

- Use context already available. Read [project context](Docs/PROJECT_CONTEXT.md) when you need orientation, then the affected creature/system README and owning code/assets. Use the [systems index](Assets/Scripts/README.md) to locate them if needed.
- Read additional documents only when the task depends on them. Do not read every Markdown file, follow every link, or reread unchanged guidance before each command. Recheck when scope changes, files change, or relevant context is missing.
- Consult the [design guide](Docs/DESIGN_GUIDE.md) when making design choices. Follow owner-confirmed rules; supporting draft proposals are not approved restrictions. Local READMEs and code/assets describe current behavior; the older Google design document may be stale. Flag meaningful disagreements rather than guessing intended design.

## Keep local READMEs useful

- When adding or changing a creature/system, update its README for changes to behavior, controls, setup, or shared contracts. If that feature has no README, create a concise one as part of the work. A family can share a guide; helper scripts do not each need one.
- Cover what is relevant: purpose, current behavior/controls, required prefabs/setup, verification steps, and known limits. No mandatory long template or documentation changes for internal edits that leave an existing README accurate.
- Link new creature guides from `Assets/Scripts/Creatures/README.md` and new system guides from `Assets/Scripts/README.md`. Reuse existing document names; in a dedicated feature folder, prefer `README.md`.

## Project-specific safeguards

- Do not edit `*.unity` scenes unless explicitly requested; this preserves the existing [scene-edit rule](.cursor/rules/no-scene-edits.mdc). Check editor/setup tools before running them: some rebuild scenes or overwrite assets.
- Preserve Unity `.meta` files, GUIDs, serialized references, and unrelated work. Implement game behavior in owned source/assets, not `Library/`, `Temp/`, generated project files, or vendor integrations.
- For creature changes, preserve carry/capture/throw ownership, lifecycle cleanup, and semantic pauses versus optimization dormancy. Put species-specific rules in their owning feature.
- Run relevant checks in proportion to the change, using the feature's existing validation when suitable. Report what actually ran and what remains unverified. Documentation-only changes need no Unity run.

## Driving the Unity Editor

- The project uses Unity 6000.3.6f1 and has the Pipeline package (`com.unity.pipeline`), so a running Editor can be driven live with the `unity` CLI. Prefer the CLI (`unity command ...`) over the Unity MCP server when you have a shell.
- Run `unity status --project-path "C:\Users\yoyom\Desktop\GitProjects\Transmutation"` first and look for state `ready`. Pass that same `--project-path` on every Editor command, since other Unity projects may be open at the same time.
- If the Editor won't connect, check `unity status` for Safe Mode (C# compile errors) before anything else. Report timeouts or failed connections instead of falling back to hand-editing scene, prefab, or asset YAML.
- The scene-edit rule above still applies to live Editor commands: don't modify or save `*.unity` scenes unless explicitly requested. Entering Play mode, reading the console, and inspecting the hierarchy are fine.
