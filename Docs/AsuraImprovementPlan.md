# Ashura improvement plan: simple creatures, forceful encounters

Revised 2026-11-07. This supersedes the earlier squad/territory proposal. Status: implemented first pass with Play Mode verification and a dedicated showcase. Minecraft is the experiential reference for contact combat, readable hurt feedback, and immediate pursuit. The design numbers below are Transmutation tuning candidates, not claims about Minecraft's implementation or edition-specific mechanics.

### Implementation follow-through

The first implementation is now present: surface-contact melee, successful-hit knockback and motor yield,
gravity-adjusted grounded hops, shared health hurt tint, Ashura recoil/step/jab animation, procedural
shader tint adapters, explicit size resistance, instantaneous blast presentation, additive player blast
motion, blocked-muzzle checks, the rolling bomber, and the six-station `Scene_AsuraShowcase`.
The original plan text below records design intent; use `Assets/Scripts/Creatures/AsuraPrototype.md`
for current controls and values. The General remains a simple explosive shooter.

Unity Play Mode verification passed the original 55 integration checks plus 15 dedicated contact-feel
regressions (70 total). A normal-speed 8-second duel recording showed nine successful damage events, visible
separation/re-engagement, hurt flashes, and grounded hops. Capture: `ArtCaptures/AsuraDuel.gif`.
The six procedural shader adapters compile without shader errors. Existing missing Wwise sound-bank
errors are separate from this work. Broader world population/performance budgets and human feel
playtests remain tuning work rather than claims established by the isolated checks.

A live 20-creature mixed-showcase snapshot reported 3.94 ms CPU frame time, 1.20 ms main-thread
frame time and 0.31 ms GPU frame time in this Editor session. This is a diagnostic snapshot,
not a sustained benchmark or a performance claim for the procedural world. The bomber also
detonated during the actual showcase, and the first-person launcher station had a dynamic
player body, the expected camera, and one available Blast Core. The Editor is left out of Play
Mode with the showcase open.

## Direction

Transmutation is an ecological sandbox. Keep simple autonomous rules: find an opposing creature, chase, attack, repeat. Make the encounter compelling through physical contact, readable reactions, population and scale. Do not add squads, morale, orders, supply systems, strategic objectives, or commander AI. The General remains a large explosive shooter.

## Findings from the implementation

- CreatureCombat stops movement whenever the target is within attack range, including recovery. MeleeAttack only applies damage to its selected target. There is no melee impulse. Two melee creatures therefore stop near each other and trade invisible health loss.
- AsuraMotor actively steers horizontal velocity toward its intent, including zero. A newly added push would be rapidly opposed unless the motor briefly yields; AcceptPush already provides that mechanism for explosions.
- Creature.ApplyDamage already emits Damaged with actual health damage. This is the shared entry point for a consistent hurt reaction. Current AsuraPresentation adds a short white flash but no shared creature-wide recoil.
- ExplosionResolver already performs one overlap query and one push per distinct rigidbody per explosion. Ordinary bodies receive AddForce with VelocityChange. The player receives current velocity plus the blast velocity through Fling. There is no lasting explosion damage volume in this path, and the visual sphere collider is disabled immediately.
- The visual sphere grows to its blast radius over 0.12 seconds and disappears after 0.22 seconds, although the full-radius damage/push is immediate. Its default primitive scale also exists before its first Update. Presentation does not faithfully show the instant affected region.
- Fling lasts 0.8 seconds by default. During that window the player bypasses normal SteerVelocity and instead enters the older force-based movement branches; speed limits and extra fall gravity are also bypassed. On slopes, the alternate branch may add downward force and disable gravity. This is a concrete source of inconsistent post-blast movement, but is not proof of the reported late-push symptom.
- BlastCore fires from its moving muzzle along the interaction direction. This path does not validate a clear launch position against nearby terrain. Inspect close-wall, floor, and rotated-held-core shots during reproduction before choosing a correction.

## 1. Repair the explosion/player contract first

Keep one instantaneous resolution: find affected bodies, evaluate cover and falloff, damage each creature once, add one velocity change to each eligible body. The effect is visual/audio only. Entering the former blast area later must do nothing. Separate explosions in a chain can each contribute one impulse; distinguish this from duplicate applications by one explosion.

The player needs a small additive impulse entry point that cooperates with normal movement. Do not change to the legacy movement mode for 0.8 seconds. Preserve current momentum, allow normal steering and gravity, and prevent speed limiting or ground braking from deleting the added impulse immediately. Use a short explicit external-motion allowance or a decaying external-velocity component, choosing the smallest solution that fits the controller. Merely replacing Fling with AddForce leaves the controller free to cancel the result next step.

Do not globally change Fling without checking its tentacle and other callers. Integrate blast motion separately first, then consolidate only if behavior stays correct.

Show the full blast footprint at detonation, followed by a short fade/dust aftermath with no collider. Preserve deliberate blast jumping. Validate launch clearance from the held core; do not teleport shots through walls to make them escape an overlap.

Reproduce standing, walking across a finished blast, jumping, slopes, point-blank floor and wall shots, successive shots, chain reactions, compound colliders, and shots while carrying/rotating the core. Record explosion identity and applied impulses during diagnosis. No claim of a fixed bug until these scenarios are run.

## 2. Make melee a pursuit-impact-pursuit loop

Keep the existing target selection and cooldown. Move toward physical contact using collider-aware reach rather than allowing large center-distance gaps. Briefly lean/contract before a hit, deliver damage and a single directional impulse, let the victim recoil, then resume pursuit as it separates. Maintain a short cooldown so contact cannot apply damage every frame. Stop/brace at contact rather than continuously forcing colliders into each other.

Starting tuning hypotheses: roughly 0.12-0.2 seconds of anticipation, 0.15-0.25 seconds of movement yield on a hit, and about 0.8-1.1 seconds between attacks. Tune impulses to produce visible separation without turning every hit into a launch. Add only a small vertical lift where the body and ground contact permit it. Keep facing and a visual forward jab readable.

Apply melee knockback only on a successful damage result. Use the same impulse receiver as explosions where suitable, but do not also apply a generic damage impulse: that would double explosion knockback. Existing AcceptPush can support the initial Ashura motor yield. Carry/capture ownership remains authoritative.

## 3. One shared hurt reaction

Observe Creature.Damaged through a reusable response component. All damageable creatures share the same visual language: brief damage tint, quick squash/recoil, and smooth return. Directional lean uses a hit direction when available; a neutral squash works when the source is absent. Health damage triggers it, not ordinary Age countdown or a rejected damage call.

Apply the pose to a designated presentation pivot or additive animation layer, not the physics root or colliders. Existing custom/procedural creatures need a small visual adapter so the response does not fight their animation or overwrite materials permanently. Share timing and semantics, while scaling displacement to body size. Static plants can flinch without being uprooted.

A mobile creature briefly yields locomotion after a physical hit so its own movement does not erase recoil. Visual hurt should remain distinct from attack cancellation; avoid permanent stun-lock in crowds. Do not freeze the whole game or make every damage source forcibly move every creature. Death and capture must cleanly end or hand off the reaction.

## 4. Variety through scale and density

Retain the three roles: many small melee creatures, fewer medium ranged creatures, and a rare large explosive creature. Try an authored group with roughly 70% small, 25% medium, 5% large, rounded for the encounter. These are composition targets, not a new population system.

Small creatures have quick steps, short reach, and pronounced recoil. Medium creatures have a slower bounce, visible firing recoil, and moderate resistance. Large creatures move ponderously, brace before firing, and barely flinch under small hits. A simple serialized knockback resistance is sufficient. Current VelocityChange explosions ignore mass, so setting Rigidbody mass alone will not create this distinction. Use explicit resistance or mass-scaled impulses, not both accidentally.

Scale collider, visual, reach and mass coherently; avoid randomized health/speed/scale combinations that make the same-looking creature unpredictable. Preserve density without allowing crowds to become interpenetrating piles.

## 5. Optional fourth creature: a rolling bomber

A compact creature rapidly rolls toward the nearest opposite-polarity target. Inside a short trigger distance it visibly swells/flashes for a brief fuse, then detonates once and dies. The readable fuse gives observation and displacement a role without sophisticated AI. No homing leap, prediction, squad coordination or new strategic system.

Proposed simple rules: arming commits the fuse; capture/carry disarms it consistently with spent projectiles; ordinary damage death is inert in the first version to avoid uncontrolled crowd chain reactions. Fuse completion invokes the shared explosion resolver exactly once and normal death cleanup. These rules are proposals requiring playtesting, not current behavior. Keep bombers uncommon enough that they punctuate a melee instead of erasing it immediately.

## 6. Spectacle without more AI

Give small melee creatures an exaggerated stomping/bobbing chase and forward head/shoulder jab. Ranged creatures inflate a firing organ and recoil on each shot. Make the large creature's body visibly house its Blast Core and rock backward when firing. Use one strong silhouette per role, shared faction colors, localized hit particles, clear impacts and spatial audio.

The brutality comes from bodies repeatedly surging forward, visibly recoiling, falling, and being thrown apart by occasional blasts. Uneven sizes, many little bodies, scattered remains and short bursts of fighting sell a chaotic conflict. Preserve the readable ecosystem and quiet between encounters. More visual noise is not a substitute for visible contact.

## Delivery order and acceptance

1. Reproduce and repair launcher/explosion motion and presentation. One blast applies one impulse per body; entering its old location cannot push; controls remain consistent afterward.
2. Add successful-hit melee knockback and brief motor yield. Two melee units visibly separate and re-engage instead of remaining parked together.
3. Introduce shared hurt presentation, first on Ashura and then through adapters on other creature visuals. Health damage is recognizable everywhere without breaking carry, capture, death, or procedural animation.
4. Tune three size bands and a dense mixed encounter. Small creatures visibly yield more than large ones; chase and impacts remain legible at normal viewing distance.
5. Add the rolling bomber only after the basic encounter already feels good.

Retain existing projectile spending, cover, chain-reaction deduplication, damage-only Blast Core drops, corpse ecology and blast-jump checks. Profile in the real scene with the intended density. This plan changes physical expression and shared feedback, not the basic intelligence of the creatures.

## 7. Minecraft reference translated into an exact melee specification

The reference feeling is a creature walking straight into striking distance, a sudden unmistakable contact hit, a red hurt flash, a short backwards hop, and renewed pursuit. The visible consequence on the victim matters more than an elaborate attacker animation. Do not require combos, parries, root-motion lunges, cinematic hit-stop, or long attack commitments. Preserve the game's existing art style; block textures and copied mob models are unnecessary.

### Movement and contact

- Drive locomotion with the current Rigidbody motor. Animate steps from actual horizontal distance traveled so legs stop cycling when blocked. Use opposing foot/limb swings and subtle body bob; floaty squash is secondary.
- Use the target's solid colliders for melee separation. Treat melee range as a small allowed gap between physical surfaces with a facing check, rather than interpreting the current 1.5 m center distance as contact. Ranged distances retain their current meaning. Approach and release must use the same reach calculation.
- Start with a surface gap of 0.1-0.2 m in the current world scale. Keep a little approach/stop hysteresis to avoid rapid start-stop changes at the boundary. Do not add a global neighbor scan or formation system.
- On slopes or against different-sized targets, use the nearest relevant collider surfaces and retain line-of-sight/solid obstruction checks. An overhead or out-of-reach target must not be hit because its horizontal distance is small.
- While cooling down, continue closing any gap. At actual contact, stop forward intent without disabling turning or physics. Knockback opens the gap and therefore restarts pursuit naturally.

### One contact hit

1. In range and off cooldown, use approximately 0.1 seconds of a small shoulder/head/forelimb motion. It can read almost as an immediate hit. The body does not retreat for a long windup.
2. At release, recheck the same contact reach and visibility. Apply one hit to the selected creature, not a crowd sweep or collision-stay stream.
3. On success, apply health damage, emit a contact cue and sound, and deliver one outward impulse. The receiver begins its shared hurt reaction in the same frame. Failed attacks produce no hit flash, impact sound, or impulse.
4. Use about 0.12-0.18 seconds of attacker visual return. This is not a movement lock; the attacker can continue pursuing.
5. Keep the next attack time tied to a bounded attack attempt/release schedule. A miss should not cause rapid attack-animation spam. No separate physics-collision damage path.

The desired side-view sequence is: walk together, contact, red flash, brief backwards hop, close the gap, contact again. When both land nearly together, both may recoil. Damage events should not automatically cancel every already-started attack; otherwise script ordering can make one creature win by permanently denying the other's release.

### Initial tuning sheet

| Parameter | Initial candidate | What to tune against |
|---|---|---|
| Small melee chase speed | Existing 3 m/s | Closes a knockback gap promptly without skating |
| Attack interval | 0.9 s | Repeated distinct contacts, not continuous shivering |
| Contact preparation | 0.10 s | Quick attack visible from side and first person |
| Attacker visual jab | 5-10 degrees or 5-10% body-depth offset | Obvious motion without extending collider reach |
| Horizontal melee velocity change | 2.5-3.5 m/s on a small target | Roughly half to one small body-width of separation in the integrated scene |
| Vertical melee velocity change | 0.8-1.2 m/s when grounded | A small hop, not flight |
| Motor yield after impulse | 0.15-0.20 s | Recoil survives steering but pursuit resumes quickly |
| Hurt tint | Red-tinted overlay, about 0.15 s | Visible against both faction colors |
| Hurt visual recovery | About 0.20 s | Fast snap followed by a short return |
| Resistance multiplier, small/medium/large | 1.0 / 0.6 / 0.25 | Same hit visibly moves smaller bodies farther |

These parameters interact with damping, collider dimensions, slope handling, and current motor acceleration. Tune the measured result rather than treating the table as final physics. For airborne victims, do not keep stacking an upward hop on each melee contact. Bound combined knockback/yield extension so crowds cannot suspend a creature indefinitely; keep real damage separate from any presentation coalescing. Do not introduce global damage immunity as part of visual feedback.

## 8. Shared hurt implementation details

Use a reusable damage-response component subscribed to Damaged. It owns the short hurt timer and notifies a visual adapter. A successful damage event restarts or strengthens a bounded reaction; it does not launch accumulating coroutines or multiply scales repeatedly. A source-less hit still flashes and recoils neutrally. Lethal damage may begin feedback, but death takes priority over recovery and never restores a living pose or locomotion.

Recommended Ashura transform structure: physics root -> HurtPivot -> LocomotionPivot -> model/attack limbs. HurtPivot owns additive recoil; locomotion owns bob/steps; attack owns the strike limb. For other creatures, use equivalent explicit bindings or a shader-only response when moving the visual would violate a rooted or attached body. Do not insert a hierarchy blindly into prefabs with existing animation paths.

Color must have one compositor. Current AsuraPresentation writes colors every Update, so an unrelated component writing the same material property would be overwritten. Preserve the faction/body material as the baseline, then blend in hurt tint through its existing presentation path or a shared adapter. Procedural fur, membrane, and custom shader creatures must be audited for supported tint properties. Never mutate shared material assets to flash one individual.

Remove the current whole-body white attack flash so firing and being hurt have different meanings. Put attacker feedback at its moving limb or firing organ. Reserve the common red tint for actual health damage; accompany it with recoil and sound so color alone is not required.

Apply physical impulses through a small receiver/helper with explicit resistance and motor handoff. Only impulse delivery requests movement yield. Visual-only health damage does not automatically disable an animal's behavior. Captured/carried/kinematic bodies retain their existing owner; do not forcibly make them dynamic or break attachments. Rooted creatures stay rooted.

## 9. Presentation of the other three roles

**Ranged:** retain basic chase-until-in-range and straight projectile shots. Add a short visible mouth/organ charge, an immediate firing twitch, a readable projectile, and the same hurt response on impact. A modest successful-projectile impulse can reuse melee's helper at lower strength. No strafing, predictive aiming, or tactical retreat is required for this version.

**Large explosive creature:** retain the current slow approach and explosive shot. Enlarge the silhouette coherently and emphasize planted feet, a visible core, a brief firing preparation, and recoil. Reuse the General's current roughly 0.6-second preparation as a starting point. Preserve the damage-death Blast Core drop. The model should make the dropped object recognizable as its firing organ.

**Rolling bomber:** add after the three existing roles feel complete. Roll the visual by traveled distance/body radius; do not rotate the physics root uncontrollably. Stop and arm when close, use a starting fuse near 0.6 seconds, visibly swell/flash, and explode once even if the opponent escapes after arming. Capture/carry changes it to an inert state; dropping it does not silently re-arm it. Paused simulation pauses its fuse. Damage death does not explode in the initial version. A spent bomber remains a normal interactive creature/remain according to existing lifecycle rules. Keep the initial blast smaller than the General's and tune in the showcase.

No gore system is required. Sharp hit sounds, a brief body flash, one or two fragments/dust puffs, small bodies thrown backward, and retained corpses provide the desired brutality. Limit simultaneous sounds and cosmetic particles so a large group remains readable.

## 10. Showcase scene: prove the feeling before expanding

Author a dedicated Ashura showcase scene; preserve the existing arena as a regression reference and do not overwrite unrelated scene work. Provide the ordinary first-person player plus fixed side and elevated observation viewpoints. Flat neutral ground and simple scale markers should expose sliding, separation, and hovering. Use actual gameplay prefabs, not cinematic substitutes or scripted outcomes. A reset restores authored initial positions; resetting a demonstration is not a runtime respawn system.

### Station A: contact duel — the approval reference

Two opposing small melee creatures start about 5-6 m apart on an open pad. View them at eye level and from the side. Run at normal speed first. Optional developer slow motion can diagnose synchronization but must not be needed to appreciate it.

Pass when each successful hit clearly identifies attacker and victim, produces a short backwards hop, and leads to renewed pursuit. There should be several readable exchanges before death with the current health/damage scale. The pair must not sit at a distance trading flashes, skate while recoiling, or leap repeatedly upward. Repeat against a wall and on a small incline.

### Station B: shared hurt and size comparison

Apply the same controlled health hit and separately the same controlled impulse to small, medium, and large creatures. Show one ordinary non-Ashura mobile creature and one rooted creature too. All share the hurt language; size changes displacement, and roots stay attached. Also demonstrate damage while carrying/captured, rejected damage, and death. Diagnostic controls remain editor/test tools, not a new player weapon.

### Station C: small melee crowd

Use six small creatures on each side. No General or bomber. The crowd should repeatedly collide, separate and close again. Check per-target hit limits, bounded impulses, movement yield, blocked approach, and audio/particle limits. This station must work before spectacle can mask its problems.

### Station D: mixed-scale clash

Use seven small melee, two medium ranged, and one large explosive creature on each side as an initial 20-body demonstration, subject to profiling. Place a few simple obstacles without strategic control points. Start bodies far enough apart to let the player see the approach. The intended picture is a dense small-creature clash, shots crossing behind it, and an occasional blast scattering bodies. Corpses remain available to the existing ecosystem. The encounter ends naturally.

### Station E: launcher and explosion laboratory

Provide a Blast Core, flat ground, a slope, a wall, a ledge, and a marked blast-radius reference. Demonstrate floor blast jumping, oblique pushes, close-wall shooting, airborne shots and entering the area after detonation. Developer counters show explosion ID and impulse count per body only while diagnosing. One old blast must never reactivate; intentional separate chain blasts remain distinct.

### Station F: bomber introduction

One bomber approaches a few opponents in a separate pad. Demonstrate visible rolling, committed fuse, escape from the fuse, capture disarming, ordinary damage death, and one detonation. Then add at most one per side to the mixed clash and compare whether it improves the encounter rather than erasing the melee instantly.

Record short normal-speed clips of A, C, D and E after implementation. Use a before/after duel from the same viewpoint as the primary showcase. Screenshots alone cannot validate this request because the important quality is motion and timing.

## 11. Work packages and completion gates

| Package | Main implementation areas | Completion gate |
|---|---|---|
| Baseline and isolation | New showcase authoring utility/scene; existing Asura validation | Reproducible unmodified duel and launcher cases captured; existing checks recorded |
| Instantaneous blasts | ExplosionResolver, BlastCore, ProjectileCreature, SmoothFirstPersonController, AsuraBlastFlash | One impulse per body per blast, no residual field, clear launch behavior, normal player control preserved |
| Contact and recoil | CreatureCombat, MeleeAttack, AsuraMotor, shared impulse helper | Station A passes on flat ground, wall and incline; no duplicate hit or forced collider overlap |
| Common hurt | Shared damage response, AsuraPresentation, creature visual adapters | Station B passes and all damageable creature families have an audited response |
| Simple model motion | Asura prefab visuals and presentation | Steps follow movement, hit/attack colors are distinct, each role reads without elaborate effects |
| Scale and density | Prefab tuning and showcase placement | Stations C and D remain readable and performant with surrounding systems active |
| Rolling bomber | New Creature-derived bomber and prefab using existing resolver | Station F lifecycle and detonation checks pass |
| Final validation | Existing combat/ecology validation plus targeted new regressions | Repeatable normal-speed before/after clips and documented final tuning |

AsuraPrototypeSetup currently creates missing prefabs and preserves existing ones. Add an explicit scoped migration/update path for the changed visuals and component fields; repeatedly invoking Create Assets alone will not upgrade the current prefabs. Preserve scene polarity overrides and user-tuned unrelated fields.

Automate the contracts most likely to regress: one damage and impulse per melee release; no impulse on rejected damage; one impulse per body with compound colliders; no push to a body entering after a blast; repeated hits do not accumulate invalid visual transforms; dead/captured/carried ownership remains correct; bomber detonates once. Use playtests for animation quality and crowd readability rather than tests that merely assert tuning constants.

Profile the full showcase before declaring a population budget. Inspect physics, projectile/particle allocations, shadow and transparency cost, sound voices, and growing corpse counts. Do not automatically destroy or pool a corpse being carried, captured, eaten, or recycled. Keep combat hits deterministic within the existing simulation contract and avoid visual feedback affecting damage timing.

The complete outcome is a simple ecosystem encounter that feels forceful at contact: recognizable chase, sudden hit, unmistakable hurt, physical separation, and continued pursuit, with size differences and occasional explosions supplying spectacle.
