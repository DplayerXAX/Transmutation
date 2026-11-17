# Boomerang

The boomerang is a long-range retrieval weapon. Aim at a loose Bloodstone across a gap, release, and watch the spinning blade bow out toward it. The first thing it touches decides what comes back. If you move behind cover, the blade finds your hand and the stone drops beside you.

## Controls

| While holding the boomerang | Result |
|---|---|
| Tap LMB | Short throw |
| Hold and release LMB | Charged throw |
| Tap RMB | Ordinary put-down |
| Hold and release RMB past the normal throw threshold | Same charged throw as LMB |

Use the carrier's existing throw threshold and full-charge time (defaults 0.2 s and 1.2 s). Both buttons use the same charge curve, sampled aim, flight, retrieval rules, and catch. The first button pressed owns the gesture until release. Cancel on loss of focus, blocked input, or losing the item. A tap put-down never initiates a return.

## Outbound arc

- Fix the origin and camera aim on release. The endpoint is on that original sightline. Turning, running, or jumping afterward only changes where the boomerang returns. A short visual drawback and increasing spin while charging communicate the range.
- Set maximum outward reach to **twice a normal full-charge item throw**. For a reproducible baseline, measure an ordinary item thrown from hand height with a level camera onto flat ground, from release to first ground contact. This is the one-way distance, excluding the trip back. Use the actual thrower's speed, upward aim bias, and gravity. Keep the range multiplier exposed on the boomerang prefab.
- Interpolate from 4 m at minimum charge to maximum reach at full charge. Use a quadratic Bézier from the hand to the endpoint. Offset the midpoint control point toward camera-right by 24% of reach and camera-up by 12%, giving the actual path a sideways bow of 12% and a lift of 6% at its midpoint. A rightward bow lets the player learn which side of an obstacle the blade will pass. The same construction works when aiming straight up or down.
- Start with 0.30 s of outbound flight for a tap and 0.85 s at full charge. Sweep a sphere matching the spinning blade's outer radius along each physics-step segment. Check launch clearance from the player to the hand, ignore the thrower and the boomerang itself, and resolve the nearest blocking contact first. A near wall must register even when the hand animation puts the model against it.

## First contact and return

| First outbound contact | Behavior |
|---|---|
| Ground, wall, mountain, or creature the player cannot pick up | Turn back immediately, empty |
| An item or creature the player could normally carry | Attach that one object beneath the blade and turn back immediately |
| No contact before the endpoint | Turn back empty at the endpoint |

- Use the player's actual pickup eligibility, including size limits and exceptions, capture, existing ownership, and special pickup interactions. Extract the decision from the carrier so hand pickup and remote retrieval agree. A loose Bloodstone qualifies. An Asura blood pool, which must be used in place, does not. Judge eligibility at contact. Prototype contact performs retrieval only; combat damage is a separate tuning decision.
- On return, steer directly toward the **throwing player's current hand position**, updated every physics step. Start at 1.5 times the outbound average speed, with a 20 m/s minimum. Clamp the final step to the hand to prevent overshoot. Disable environment contact and further pickup checks for both returning objects. A wall between the player and the blade cannot strand the return.
- Keep the cargo visually below the blade and mark it as in transit so AI, normal pickup, and physics cannot move or claim it concurrently. Preserve its original component and collision settings. Clear this ownership on every exit. If the cargo disappears, let the blade finish its return alone.
- At the hand, **auto-catch the boomerang and drop the cargo**. If the player picked up something else during flight or has a pickup pending, keep their choice and set the boomerang down nearby as well. Give the cargo a gentle release velocity so it settles within reach.
- Geometry handling belongs at release. Test the cargo's actual collider shapes and proposed pose beside the player, trying either side, forward, and behind. Check clearance from the player to the proposed position so a thin wall cannot put cargo in the next room. Apply a small penetration correction for shallow overlaps, validate again, then restore collisions. If a large retrieved creature cannot fit inside a tight passage, keep it visibly attached and retry as the player moves into space. The boomerang can already be held, with a new throw available after cargo release. If the owner disappears, release at the last validated clear pose in the live scene. Scene unload cleans up the flight with its objects.

## Implementation

1. Author a neutral, carryable, throwable, invulnerable **Boomerang** creature prefab with its own component, a curved two-arm silhouette, a centered spin pivot, and a cargo anchor. Place one next to the spear and greatsword in the Scene_Yoyo weapon station and include it in the station setup tool.
2. Route both launch gestures to one boomerang launch method. Reuse the carrier's put-down and catch operations. Give the boomerang sole ownership of its flight so the generic creature thrower cannot start a second arc. Keep its visual spin independent of collider and cargo rotation.
3. Implement outbound sweep, first-contact classification, straight homing return, and safe cargo release. Use the project's scene-aware physics queries. Share pickup rules and restore previous settings rather than assuming all cargo started dynamic or enabled.
4. Give the turnaround a visible change in blade tilt, a distinct pickup sound on successful retrieval, and a short catch sound at the hand. These cues should distinguish a successful pickup from a wall bounce at maximum range.

## Play Mode acceptance

- From the same mark, compare tap, half-charge, and full-charge throws on both buttons. Measure full outward reach against the level-camera ordinary-throw baseline (target 2×, within 10%). RMB tap still puts the blade down.
- Retrieve a Bloodstone, an eligible moving creature, and a large creature with a carry-size exception. Hit an oversized ineligible creature, a wall with a pickup behind it, and empty air. Each throw retrieves at most one object.
- Run sideways, jump from a ledge, and get behind a wall while the blade is out. It must reach the current hand. Cargo must drop on the player's side with no overlap, launch spike, or damage from crossing geometry.
- Return with hands occupied, beside a wall, in a low passage, and while backing into a corner. Step out of a passage too small for the cargo. Verify ordinary pickup, AI, and physics resume once, and repeat the throw with the same boomerang.
