# Asura Blood implementation and verification

Verified November 15, 2026 in Unity 6000.3.6f1, using `Assets/Scenes/Scene_AsuraShowcase.unity` through its normal Play Mode entry point. The scene is left saved and out of Play Mode.

Blood adds a temporary, voluntary combat side to the player. `PlayerCreatureStats` owns the health/polarity operation and one committed-pair event. It retains its existing script GUID, luminosity fields and light-perception contract. Blood never changes numerical luminosity or invokes creature death, respawn, input disabling or weapon release.

## Controls and scene setup

- **LMB** on aimed blood within the existing five-metre interaction reach coats an empty-handed player.
- While the Blast Core owns LMB, **F** coats with aimed blood; **LMB** fires the core. The existing prompt displays the configured keyboard control's actual display name. Simultaneous F and LMB in one gameplay update coats without firing.
- Existing showcase controls remain: **1–6** select encounters, **R** resets, and **Tab** cycles side view, overview and walking mode. Blood comes from fallen Asura; walking over it never applies it.
- `Tools > Creatures > Asura > Install Blood In Showcase` reruns the idempotent asset/scene wiring. The blood prefab is `Assets/Prefabs/Creatures/Asura/Resources/AsuraBlood.prefab`.
- The globe starts at 64 reference pixels with a 24-pixel lower-left inset, using the existing 1920 × 1080 scaling. Both are editable on `BloodCoatHUD`. The shell material and 3.5 mm gap are editable on `FirstPersonBody`.

## Automated validation

| Validation entry point | Result |
|---|---|
| `AsuraCombatValidation.Run()` | PASS — 151 combat, blood and lifecycle checks |
| `AsuraFeelValidation.Run()` | PASS — 15 contact, hurt, impulse, launcher and bomber checks |
| `LuminosityValidation.Run()` | PASS — 32 luminosity checks |
| `HoneyLuminosityValidation.Begin()` / `Finish()` | PASS — 14 checks, including real callbacks from both cameras |
| Unity script compilation | PASS — zero compilation errors |
| Final Play Mode console | Zero errors; two existing warnings described below |
| `git diff --check` | PASS |

`AsuraBloodValidation.Run()` is included by the existing combat validation and is also available under `Tools > Creatures > Asura > Validate Blood (Play Mode)`. Fixtures use disposable physics scenes, real colliders, real damage receivers, interaction rays and real prefab death hooks.

Coverage includes all coat transitions, invalid input, the committed event pair, matching blood at 80 and 100, opposite-side replacement, neutralization, unchanged luminosity, both windup-cancellation directions, nearest-target priority, creature-only queries, scene isolation, destroyed interface references, player child colliders, allied projectiles, blast falloff, one damage event per compound target, one impulse per rigidbody, bare-player rocket jumping, retained held weapons, reach/occlusion, and reentrant pickup rejection. Every Asura variant is checked for exactly one matching combat-death blood object and no blood on direct lifecycle death, age expiry or consumption. Rolling self-detonation is tested separately. The General retains its separately positioned Blast Core.

Two old luminosity assertions were updated to match existing behavior: honey already fades to half strength halfway through its duration, and the shared player light continues to show non-honey luminosity after honey expires. No luminosity, honey decay, perception or light-intensity implementation was changed. The honey status panel only shifts horizontally while the globe occupies its usual corner.

## Live scene observations

Play Mode advancement was confirmed by increasing `Time.frameCount`. The existing showcase was opened normally; temporary actors, pickup fixtures and backdrop walls were then created only in the running scene and discarded on exit.

| Check | Observed result |
|---|---|
| Bare scene entry | Coat health 0, polarity None, globe hidden; existing creatures continue their fight |
| Normal blood interaction | A queued LMB event through the normal gameplay update applied 50 Light health and removed the aimed drop |
| Held Blast Core | Prompt displayed `[F] Coat with Dark blood` alongside `[LMB] Fire blast seed` |
| Contextual use and simultaneous input | Queued F, and later F + LMB together, produced 50 Dark health with `ShotsFired == 0`; core stayed held |
| Intentional fire | A separate LMB event fired one shot; its nearby blast reduced coat health from 50 to approximately 11.64 |
| Live Melee Asura | Pursued the player and depleted the coat; controller stayed enabled at zero |
| Live Ranged Asura | Acquired the player and landed repeated 12-damage shots (100 → 64 → 52 sampled) |
| Live General Asura | Its actual projectile blast reduced health to approximately 42.40, then zero; controller remained enabled |
| Live Rolling Asura | Approached and detonated, reducing health to approximately 62.23; exactly one blood pickup remained |
| First-person body | Both colors followed hands, continuous arm skin, feet and continuous leg skin while looking down; original anatomy remained visible |
| Camera clearance | Ahead and wall-facing captures remained clear of a full-screen tint or camera-enclosing shell |
| Globe | Horizontal half-full surface at 50; full circle at 100; readable rim on pale and nearly black walls; entire globe absent at zero |
| Goo and honey | Both remained usable with blood; switching Light → Dark left the sampled honey luminosity at 50; honey status remained beside the globe |

The live melee check exposed a motor integration issue: the old terrain-only ownership check made Asura circle a player collider. The motor now recognizes the shared combat owner, and that case has a regression check.

## Captures

- [Normal blood prompt](../ArtCaptures/AsuraBlood/bare-pickup-prompt.png)
- [Light coat and half-full globe](../ArtCaptures/AsuraBlood/light-50-down.png)
- [Dark coat and half-full globe](../ArtCaptures/AsuraBlood/dark-50-down.png)
- [Held core contextual binding](../ArtCaptures/AsuraBlood/contextual-f-final.png)
- [Blood with goo and honey; separated HUD](../ArtCaptures/AsuraBlood/compatibility-final.png)
- [Full Light globe against pale scenery](../ArtCaptures/AsuraBlood/light-100-final.png)
- [Dark globe against dark scenery](../ArtCaptures/AsuraBlood/dark-on-dark-final.png)
- [Zero health hides the globe](../ArtCaptures/AsuraBlood/zero-final.png)

## Human follow-up and limits

Input checks used Input System events queued for the next normal Dynamic update, rather than a physical keyboard/mouse session. Human playtesting should judge coat visibility and comfort during fast traversal, wall climbing, and continuous movement while the last coat point is lost with a weapon held. Other display aspect ratios and hardware input devices were not tested. The automated checks cover the underlying state, weapon retention, controller continuity and blast movement behavior.

Final console warnings came from the existing showcase reset assigning velocity to a kinematic player and Unity AI's account API timing out. Neither was a blood exception. CLI tooling also reported its existing Pipeline command-name compatibility warning. No standalone build or platform deployment was run.
