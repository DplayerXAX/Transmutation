# Leaf Garden captures and checks

- `LeafGarden_Showcase.gif`: approximately thirty seconds of actual game time, from gathering through the huge tornado to settling. The camera frames were captured at roughly four frames per second; this is not the game's rendering frame rate.
- `LeafGarden_Scattering.gif`: close-up movement through the carpet, captured at roughly ten frames per second.
- `LeafGarden_Overview.png`: huge-tornado overview.
- `LeafGarden_Carpet.png` and `LeafGarden_Settling.png`: other frames from the full-cycle capture.
- `Resting_80m_1_000.png`: close view of the carpet and moving demonstration creatures.
- `LeafGarden_PlayerView.png`: player-height view of a creature opening a wake through the carpet.
- `runtime-checks.txt`: Play Mode check results.
- The latest launch update measured the actual player at a 91.26-meter first apex against the 80-meter tornado; see `runtime-checks.txt`. The existing animations were recorded before the higher launch tuning.
- `benchmark.csv` and `benchmark-hardware.txt`: fixed-overview Editor measurements. The 400k stress population is simulated; approximately 172k were rendered from this camera after culling/LOD. These figures do not establish a universal leaf limit.

The standalone build could not complete because Windows Application Control blocked Unity's installed linker. All included benchmark results are from the Editor. Capture frames were excluded from the timing sweep.

Open `Assets/Scenes/Scene_LeafGarden.unity` and enter Play Mode for the interactive version. Controls and implementation notes are in `Docs/LeafGardenPrototype.md`.
