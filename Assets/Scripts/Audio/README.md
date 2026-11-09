# Audio: Adaptive Music and Footsteps

Music is **vertical layering**: one base loop always plays, and extra layers fade in when the listener is
near something that owns that layer (a creature type, a place, or the underground). Game code only uses
event names, so it doesn't care whether Wwise or Unity audio is playing.

## Setup

**Tools > Capstone > Audio > Set Up Adaptive Music In Open Scene** (`Editor/AudioSetup.cs`):

- creates or loads the event bank `Assets/Audio/AudioEvents.asset`,
- adds an `AudioManager` object and links the bank,
- adds a `BGM_landmark` `MusicChannel` to the Hanging Spire, a `CaveMusicChannel` (`BGM_cave`) to the
  `ProceduralWorld`, and `Footsteps` to the player body.

Other menu items:

- **Add Music Zone At Scene View** — an empty object with a box collider and a `MusicChannel`.
- **Relink Clips By File Name** — fills Unity clips in the bank from files in `Assets/Audio`.

## Scripts

| Script | Job |
| --- | --- |
| `AudioManager` | Singleton. Posts events, keeps layers in sync, computes each layer's weight every frame. |
| `AudioEventBank` | ScriptableObject: every event by name, plus creature type → layer mapping. |
| `MusicChannel` | Makes a layer audible near this object (or inside its `zone` collider). |
| `CaveMusicChannel` | `MusicChannel` that fades in by depth: on below the surface, fully on under the inner ceiling. |
| `Footsteps` | Posts a left / right footstep each time a leg in `FirstPersonBody` plants. |
| `PlaceholderSynth` | Generates simple in-sync loops and blips so the system is audible without real assets. |

## Event bank

Each `AudioEventDef` has a `name`, a `kind`, clips, volume, pitch range, 3D settings, fade in / out times
and a placeholder synth voice.

- `MusicBase` — the main loop. Starting it starts every layer silently and in sync.
- `MusicLayer` — a loop in sync with the base; its volume follows nearby channels.
- `Sound` — a one-shot or looping effect, 2D or on an emitter.

`creatureChannels` map a Creature class name (e.g. `FuzzCreature`) to a layer with inner / outer radius.
The manager rescans for creatures every `creatureScanInterval`, so creatures need no audio code.

Current layers: `BGM_fuzzy` (FuzzCreature), `BGM_fluid` (FireEater), `BGM_cave` (underground),
`BGM_landmark` (Hanging Spire).

## Code API (`AudioManager`, all static)

```csharp
AudioManager.PostEvent("Play_Something", gameObject);         // play, 3D if an emitter is given
AudioManager.StopEvent("Play_Something", gameObject, 0.3f);    // stop with fade
AudioManager.PostEventOnGrid("Play_Something", gameObject, 2); // snap to the music grid (1 beat, 2 eighths, 4 sixteenths)
AudioManager.SetLayerWeight("BGM_cave", 1f);                   // override a layer; negative = back to automatic
AudioManager.GetLayerWeight("BGM_cave");
```

`PostEventOnGrid` drops extra calls in a slot that is already taken, and plays at once if no music runs.
Use it for musical sounds so they land on the beat.

## Backends

Set on the `AudioManager` (`backend`). Wwise is the default; if Wwise is not initialized (no `AkInitializer`
in the scene) it falls back to Unity with a warning.

**Wwise**

- Project: `CapstoneTeams_WwiseProject/`. SoundBank `Transmutation` is loaded on Start (`wwiseBanks`).
- `startEvent` `PlayBGM` is posted on Start and plays the base music.
- Each layer is a **Game Parameter (0–100) with the layer's name**. Inside the base music each layer has a
  music track whose volume follows that parameter. The manager sets the parameter to `weight × 100`.
- Beat callbacks from the music give the grid for `PostEventOnGrid`.
- Footsteps: `Play_Footstep_Left` / `Play_Footstep_Right`, random containers on the SFX bus.
- After changing the Wwise project, regenerate the `Transmutation` SoundBank.

**Unity**

The base and layers are synced looping AudioSources (`masterVolume`, `musicVolume`, `soundVolume`, optional
mixer groups in the bank). Events without clips use `PlaceholderSynth` voices (clock: `bpm`,
`beatsPerBar`, `bars` in the bank).

## Sound effects (`Assets/Audio/SFX`)

The clips are imported into Wwise (Containers > SFX, SFX Audio Bus) with events of the same names, so with the Wwise
backend they play through Wwise. **Tools > Capstone > Audio > Add Sound Effects (Assets/Audio/SFX)** also puts the
clips on the bank entries; those are only used by the Unity backend.

| Event | Clip(s) | Played by |
| --- | --- | --- |
| `Play_ShootTentacle` | `shootTenacles_D2`, `shootTenacles_F2` (one at random) | `TentacleTether.FireAt` when an arm sticks |
| `Play_TentacleNear` | `tenacles` (loop, 3D) | `creatureSounds`: on each `TentacleCreature` while the listener is within 14 m |
| `Play_LMB` | `LMB` | `PlayerCreatureInteractor`, a left click that interacts / uses |
| `Play_PickUp` | `pickUp_RMB` | `PlayerCreatureCarrier.BeginCarry` |
| `Play_Drop` | `drop_RMB` | `PlayerCreatureCarrier.Drop` and throws |
| `Play_BubblePop` | `bubble_pop` (3D) | `TrappingBubble.Pop` |

`creatureSounds` in the bank maps a Creature class name to a looping sound that plays on the creature while the
listener is near (fades in, fades out past 1.15 × radius); like music channels, creatures need no audio code.

### Tutorial music (`TutorialMusic`)

Wwise only. `Play_Tutorial` plays the `Tutorial` playlist: segment `Tutorial_Stems` (tracks intro_1..5, 12.5 s,
looping) and then segment `Tutorial_Outro` (intro_loop trimmed to its last 3.75 s, played once).

Put a `TutorialMusic` on any object (or turn on `playOnStart`). `Begin()` stops `PlayBGM` and starts the stems, all
silent; `Unlock(n)` / `UnlockNext()` fades stems 1..n in through Game Parameters `Tutorial_1`..`Tutorial_5` (they
stack); `Finish()` sends a Break, so the current loop ends, the outro plays, and `PlayBGM` starts again when it is done;
`End()` stops at once. Code: `TutorialMusic.Instance.UnlockNext();`

## Adding sound

- New layer: add a `MusicLayer` to the bank and a Game Parameter + track of the same name in Wwise, then
  either map a creature type in `creatureChannels` or put a `MusicChannel` in the scene.
- New sound effect: create a Wwise event, add a `Sound` entry of the same name, call `PostEvent` /
  `PostEventOnGrid`.
- The music is in E minor; keep musical effects in key and on the grid.
