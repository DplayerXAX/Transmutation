# Three held weapons for Transmutation

All distances, heights, timings, and damage costs below are proposed starting values unless identified as current prefab values. H means the standing player's world-space capsule height. Measure rise from takeoff feet to apex feet and dash distance from the capsule center at release to the center at the end of powered travel. Use the same player rig for every comparison.

| Weapon | What the player does | What moves | First playable target |
|---|---|---|---|
| Blast Core | Fire at a nearby surface and ride the explosion | The player moves away from the impact | A shot at the feet lifts a stationary player 3–4 H |
| Spear | Tap for a stab, or aim and release a held charge | The player and held spear travel together | Full charge carries the player 4 H along the sightline and nudges creatures forward |
| Greatsword | Commit to one slow diagonal smash | Creatures in the blade's path rise vertically | A grounded ordinary creature rises roughly 1 H, with 2 H as the upper tuning limit |

## What the existing blaster already does

The weapon is the **Blast Core**. The player holds the pickup control (default RMB), then clicks LMB to fire a blast seed. The core already has an authored pickup, a muzzle, a cooldown, a straight gravity-free projectile, impact detonation, and muzzle obstruction checks. A floor shot already supports a jump. The current core fires at 12 m/s, at most once per second, with a 4 m blast radius, 12 m/s peak radial velocity change, and 60 peak damage. At 12 m the seed spends about a second in flight. Preserve visible travel and the ability to lead a creature. Shorten the delay between choosing a floor mark and feeling the jump.

The resolver measures distance to the nearest visible collider point and reduces both push and damage linearly to zero at the edge. A body halfway out gets only 6 m/s before direction is considered. A side impact spends much of that velocity horizontally. These settings make general splash sensible, but leave jump height dependent on small changes in where the seed lands. The player can also take ordinary splash on their temporary Asura blood coat because the emitter excluded from damage is the core. A bare player's current receiver spends no health.

The physics support is already good. `ProjectileCreature.FiringPlayer` records who fired. `CreatureImpulse.Apply` sends the player through `SmoothFirstPersonController.AddBlastImpulse`, which adds velocity once, clears short-hop state and engages blast movement. Other eligible bodies receive `ForceMode.VelocityChange`, with motor-specific knockback for Asuras. Keep this division. A second AddForce on the player would apply the launch twice. An ordinary mass-dependent `ForceMode.Impulse` requires impulse = body mass × desired velocity change.

## 1. Blast Core — place the explosion

**The signature move.** Run toward a ledge, look down and slightly behind, click, then look back at the landing while still traveling forward. A straight-down shot should be an easy elevator. Placing the impact behind or beside the feet trades height for horizontal travel. Jumping just before firing should improve the result, but a standing shot must work on its own.

- **Shot.** Start at 22 m/s projectile speed and a 0.8 s minimum firing interval. Keep click-to-fire, a discrete muzzle kick, and the current radius and creature response for the first pass. At 12 m the seed now takes about 0.55 s to arrive. Test that speed with a moving target before changing splash. A blocked muzzle gives a short dry feedback cue and consumes no successful shot.
- **Close floor shot.** Inside approximately 0.75 H of the feet, with the impact below the center of mass, use a strong inner band. Give a directly downward shot enough upward speed to reach 3–4 H above takeoff. Start with a target of 3.5 H and estimate the required speed as `sqrt(2 × rising gravity magnitude × target rise)`. Measure it in play. Tune the upward component and smoothly blend toward the radial direction as the impact moves sideways. Do not normalize a near-zero vector without an upward fallback.
- **Wall shot.** Near a vertical wall, push primarily away from the wall with modest lift. Use the impact normal and the impact-to-player vector to distinguish this from floor contact. Pass actual contact information from the projectile when available. For a midair detonation with no surface normal, use radial push. Fade smoothly from the close response to zero at the radius. Cover occlusion still applies.
- **Momentum.** Apply the boost on detonation, never on trigger pull. Preserve incoming planar velocity. Allow existing jump velocity to contribute to height and allow a second well-placed blast to redirect the player. Check the resulting apex at a low ceiling before tuning repeat boosts. Air steering should permit a landing correction without erasing launch speed.
- **Self cost.** Route the firing player separately for both damage and push, once per blast even with compound colliders. Propose 1% of maximum base health at point-blank range, fading to zero at the edge. For the interim coat-only build, cap the coat cost at 1 point per self-blast and leave a bare player free to jump. When base health lands, charge the small cost once through the chosen health layer. An enemy General's rocket continues to use its existing damage route. Dropping the core before impact must preserve shooter attribution.
- **Feedback.** Give the seed a visible travel streak, a compact muzzle flash, and two distinct sounds for launch and detonation. Center the explosion at impact so the player can read a misplaced shot. Keep camera kick short enough to see the landing. Prefer a quick upward rush on a successful self-launch to a large red damage flash.

## 2. Spear — aim the whole body

**The signature move.** Hold a picked-up spear, draw it back, look toward a ledge and release. The body follows the point, including straight up. The Riptide reference is the sensation of becoming the projectile. The proposed version works on dry ground and in air. The spear stays in hand throughout.

| Time holding LMB | Release result | Starting target |
|---|---|---|
| Less than 0.20 s | Quick forward stab | Reach 0.9 H from the front of the player, no powered body movement, 0.25 s recovery |
| 0.20–0.90 s | Increasingly committed thrust | Grow powered travel smoothly from zero to 4 H, with the same tip reach at the start |
| At least 0.90 s | Full dash, held until released | 4 H of powered travel in about 0.30 s, then 0.35 s recovery |

- **Input and aim.** Begin charging only on a fresh press while carried. Allow walking and turning during charge. Sample camera forward on release and lock the powered segment to that direction. A full charge can be held without auto-firing or gaining extra distance. Use the release point to aim, so a player can charge while looking at a creature and then flick up to escape.
- **Distance and travel.** For the middle band use normalized charge `q = clamp01((heldSeconds − 0.20) / 0.70)` and a starting distance of `4 H × q`. Smooth the animation through the threshold. Cap travel by actual swept path length. Give the movement controller temporary ownership of directed velocity during the powered segment. Return control on contact, distance exhausted, or cancellation. Keep a bounded fraction of exit momentum so an upward dash coasts naturally into an apex. Record powered distance and coast separately.
- **Contact.** Sweep the player capsule against solid terrain each physics step and sweep a smaller volume along the spear tip's traveled path for creature hits. Resolve the earliest blocking surface before considering hits beyond it. A thin wall stops the player short by a small skin distance. A low ceiling ends ascent and clears the blocked velocity component. Do not move the transform past a wall and then rewind it.
- **Creatures.** One damage event per creature per release, including creatures with several colliders. Nudge a movable target along the thrust, aiming for about 0.25 H displacement on level ground. A target struck by an upward thrust gets a small directional nudge. Stop on a physically blocking creature, while allowing the tip to graze another visible creature along an unobstructed path. No repeated contact damage during recovery.
- **Readability.** A tap gets a short point jab and immediate retraction. While charging, pull the tip back alongside the view, tighten a small reticle and raise a quiet charge tone. A single hand-settle and pitch change mark maximum charge. On release extend the spear down the aim line and play a short rush proportional to charge. Suggest rotation with the spear or trail while keeping camera roll steady.
- **Repeat use.** Start with one powered dash per airborne sequence, refreshed on a stable landing. Allow tap stabs in the air. Show a dim charge cue while the dash is spent so the player knows when a second ascent is available. This is a traversal tuning choice to test against the level's intended ledges.

## 3. Greatsword — lift the target

**The signature move.** Let a creature come close, commit early, then drive a broad diagonal cut through it. The creature pops almost straight up and drops near where it stood. The player can step underneath or reposition during recovery. That interaction gives the sword a movement payoff while keeping the spear's dash distinctive.

| Phase after LMB press | Timing | What the player sees or controls |
|---|---|---|
| Windup | 0–0.38 s | Shoulder the heavy blade at upper right. Walk at roughly half speed and adjust facing. |
| Active | 0.38–0.54 s | Sweep toward lower left through a roughly 90° front arc, reaching about 1.2 H from the player's front. Lock attack heading at commitment. |
| Recovery | 0.54–1.10 s | Let the blade follow through low, draw it back and restore full movement. A miss pays the same recovery. |

- **One deliberate swing per press.** No charge gesture. Keep the first version's diagonal consistent so players learn which side reaches a target first. A second press during the final 0.12 s of recovery may queue one follow-up. Holding the button cannot queue repeated swings.
- **Where it hits.** Sweep sampled blade segments between successive fixed poses from hilt to tip. Use an attack-instance hit set and resolve each target once at first contact. This allows a real cleave across adjacent creatures. Stop the affected sweep at solid cover. A creature behind the player or on the far side of a pillar stays safe even if it lies inside the nominal reach.
- **What gets lifted.** Give an ordinary grounded, movable creature enough upward speed to rise about 1 H. Start at `sqrt(2 × effective gravity magnitude × H)`. Apply only the upward velocity needed to reach that speed, preserving horizontal momentum and adding zero sideways shove. Briefly let the creature motor yield so it cannot immediately pin the body to the floor. Heavy creatures can rise less using existing resistance. Captured, carried or kinematic bodies keep existing impulse eligibility rules.
- **Airborne and ceiling behavior.** For an airborne target, allow damage but suppress a fresh lift until it lands. This prevents a slow follow-up from ratcheting the same creature higher. A low ceiling clips the natural rise through ordinary collision. The ceiling contact does not produce another sword hit.
- **Weight cues.** Let the windup silhouette and low scrape announce commitment. Time the loud impact and a brief blade-animation pause to confirmed contact, once per swing. Keep physics and player input running during that local pause. A whiff gets the air cut and heavy recovery. Start with no forced player lunge. If testers repeatedly fall just short despite reading the arc correctly, try a planted step of at most 0.15 H during commitment before extending the blade's reach.

## Build order and integration

1. **Record the baseline.** Use the existing Asura prototype launcher station, the standard player rig, an ordinary mobile Asura, and a heavy target. Mark flat floor offsets at 0, 0.5 H and 1 H, a wall, and ledges at 1 H and 3 H. Record ten identical point-blank jumps with takeoff speed, apex, landing position, coat loss and creature displacement. Repeat the post-change run from the same marks.
2. **Retune only the carried core.** Update the Blast Core prefab and its setup defaults together. Pass shooter and impact metadata through the existing queued explosion request. Apply the shooter response inside the same visibility and falloff resolution so nested detonations and compound bodies retain their one-hit behavior. Leave General prefab values intact. Verify floor shots before spending time on projectile art.
3. **Extend held input at its existing owner.** Use the current carrier and interactor for pickup, prompts, aim, and action priority. Give the spear press/release/cancel and the sword a press. Route each gesture to the item that owned its initial press. Cancel pending charge or queued attacks on drop, swap, disable, focus loss or entry into blocked input. A delayed pickup click must never release a dash. Retain contextual blood use, throw routing, honey and squirrel interactions. Present short carried prompts for each action.
4. **Build the spear slice.** Author a carryable spear with grip and tip anchors. Prototype tap, full forward dash and full vertical dash first. Then add interpolation, wall/ceiling sweeps, the per-release hit set, landing refresh, feedback and interruption paths. Exercise the movement handoff with skating, climbing and other states that take ownership of the body. A kinematic or otherwise unavailable body must decline a dash cleanly.
5. **Build the sword slice.** Author a carryable greatsword with grip, hilt and tip anchors. Make blade pose and sweep agree before adjusting damage. Validate first contact, multi-target cleave and motor yield on an ordinary creature. Tune vertical launch on a heavy target separately. Restore the player's movement modifier on every exit, including loss of the carried item.
6. **Connect player health when ready.** Decide where base health lives and whether coat absorbs damage before base health. Keep polarity and AI target eligibility tied to the coat's existing semantics. Use a distinct self-blast cause so its small cost can be tuned independently of enemy splash. Test bare, coated, coat-breaking and low-health cases. Decide explicitly whether the last point can be spent on a jump. Suggested prototype rule is nonlethal self-cost. Apply that floor only to base health once implemented.
7. **Tune by feel in one room.** Place all three pickups beside the same obstacle course. Use durable targets so damage cannot hide the motion. Tune distance, timing and control first, feedback second, damage last. Record actual height and travel against the proposed targets before committing final prefab values.

## Acceptance playtest

| Trial | Pass condition |
|---|---|
| Stand still and fire at the floor ten times | At least nine clean shots rise 3–4 H without a jump input. Compare median apex with the recorded old core. If the improvement is small, raise the target and retest level reach. |
| Run past a floor mark and fire behind, then fire at a side wall | Forward shots preserve the run into the landing. Wall shots push away. Neither fires through an obstructed muzzle. |
| Fire while coated, drop the core before detonation, then repeat against an enemy rocket | Own shot costs at most one coat point in the interim build. Enemy splash follows its usual damage. A nearby creature gets its baseline response. |
| Release the spear at 0.1, 0.20, 0.55, 0.90 and 2 s | Tap stabs in place. Midcharge travels about 2 H. Both long holds travel about 4 H before coast. Looking straight up carries the player up. |
| Dash beside a creature, into a thin wall, and under a low ceiling at low and high frame rates | One hit per contacted creature, slight directional shove, no tunneling and no target hit through cover. Player control returns immediately after obstruction. |
| Swing into two nearby creatures, then miss, then strike beneath a ceiling | Both visible creatures can rise once. Typical unobstructed apex is about 1 H above takeoff and at most 2 H. No added sideways launch. Whiff and impact have distinguishable feedback and a readable recovery. |
| Drop each item during startup, lose focus while charging, and use contextual coat input | No stale dash, swing, shot, or movement restriction on return. Re-picking the item starts idle. |
| Give a tester one ledge, one gap and one approaching creature | After trying each pickup, they can explain where to aim the core, when to release the spear and when to begin the sword swing. Record failed attempts and what the tester expected to happen. |

### Project references

- Current authored tuning and authoring path — `Assets/Prefabs/Creatures/Asura/BlastCore.prefab`, `GeneralAsura.prefab` in the same folder, and `Assets/Scripts/Editor/AsuraPrototypeSetup.cs`.
- Fire, flight and resolution — `BlastCore.cs`, `ProjectileAttack.cs`, `ProjectileCreature.cs`, `ExplosiveProjectile.cs`, `ExplosionResolver.cs` and `CreatureImpulse.cs` under `Assets/Scripts/Creatures/`.
- Input, movement and coat — `PlayerCreatureInteractor.cs`, `PlayerCreatureCarrier.cs`, `SmoothFirstPersonController.cs` and `PlayerCreatureStats.cs` under `Assets/Scripts/Movement/`.
- Existing test entry points — `Assets/Scripts/Editor/AsuraCombatValidation.cs`, `AsuraBloodValidation.cs` and `AsuraFeelValidation.cs` in the same folder.
