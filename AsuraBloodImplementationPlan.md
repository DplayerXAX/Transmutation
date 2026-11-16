# Implement Asura Blood in Transmutation

Implement and playtest this feature in `Assets/Scenes/Scene_AsuraShowcase.unity`. The player chooses to join an Asura fight by coating themselves in a fallen Asura’s blood. The coat gives the player a side and temporary health. At zero, the player returns to neutral and keeps playing.

## 1. Implement the rules in one place

Extend the existing `PlayerCreatureStats` component. Keep its current script identity, luminosity fields, and light-perception behavior. Add current coat health, resolved coat polarity, and one event that reports the final health and polarity together. Both the HUD and body visuals read this state. Existing player instances start at 0 and None.

| Input | Result | Consume drop? |
|---|---|---|
| Bare player uses Light or Dark blood | 50 health, drop’s polarity | Yes |
| Player uses matching blood below 100 | Add 50, clamp to 100 | Yes |
| Player uses matching blood at 100 | Stay at 100 | No |
| Player uses opposite blood at any positive health | Replace the coat with 50 health and the new polarity | Yes |
| Coated player takes damage | Subtract the applied amount, clamp at zero | — |
| Damage empties the coat | Set health to 0 and polarity to None in the same operation | — |
| Bare player is hit | Coat stays empty. Existing movement and physical reactions still work. | — |

Use the existing damage amounts. Coat depletion never calls creature death, interrupts player input, drops the held weapon, or triggers a respawn. Blood color has no effect on the player’s numerical luminosity.

## 2. Let combat recognize both kinds of target

1. Introduce a small shared combat-target contract implemented by `Creature` and `PlayerCreatureStats`. Cover identity/root, aim point, polarity, AI target eligibility, and receiving damage. Delegate the creature implementation to its existing behavior. The player implementation accepts damage while coated and exposes the existing player body for collision and aiming. Keep `PlayerCreatureStats` outside the food, corpse, and capture lifecycle.
2. Extend the combat discovery path in `CreatureSpatialQuery` to include active players from the same scene. Preserve the existing creature-only query for callers that need creatures. Reuse existing spatial snapshots and lifecycle registration. Resolve hits on child colliders to one owning combat target. Preserve Unity destroyed-object checks when storing interface references.
3. Migrate `CreatureCombat`, `CreatureAttack`, `MeleeAttack`, `ProjectileAttack`, and their callers to the shared target wherever they currently require a creature victim. Update `RollingAsura` separately because it searches for and validates targets itself. Preserve existing selection priorities, range, visibility, windup, and cooldown. A coated player competes with other eligible targets. The player has no special aggro priority.
4. Check live eligibility while tracking and immediately before releasing a targeted attack. A Light Asura can pursue a Dark-coated player. If the player switches to Light or becomes neutral during windup, the attacker cancels that attack. Apply the rule symmetrically.
5. Route collision damage through the shared receiver in `BasicProjectile` and `ExplosionResolver`. Trace `ExplosiveProjectile`, Blast Core, and Rolling Asura detonation through that resolver. Polarity governs deliberate targeting. A projectile already in flight, an allied blast, or a non-Asura damage source using the shared receiver can still hurt the coat. Preserve existing source exclusions, blast falloff, and impulse behavior. Deduplicate damage per target and impulse per rigidbody. A bare player can still rocket-jump.

## 3. Create the blood creature and wire the drops

1. Create an `AsuraBlood` creature script and prefab with a visible pool or globule, a collider reachable by the interaction ray, and an interaction prompt such as “Coat with Light blood.” Store the donor’s resolved Light/Dark value separately from the drop’s own AI eligibility. Give the drop a neutral combat identity so Asura do not attack their own pickups. Use the existing Asura presentation colors for the two variants.
2. Have Melee, Ranged, General, and Rolling Asura spawn one blood creature at their position on combat death. A rolling self-detonation counts as a combat death. It calls `Die()` directly, so a damage-cause filter alone would miss it. Guard against double drops when a blast and self-detonation overlap. Skip consumption, age death, and scene teardown. Preserve the General’s existing Blast Core drop and place both pickups where each can be selected.
3. Apply blood only after a deliberate interaction within normal reach. Route the request through the blood creature and the player’s coat operation. Remove the drop once, only after successful application. Walking across blood does nothing.
4. Support use while holding the Blast Core. Inspect `PlayerCreatureInteractor` and the existing input bindings. Use the normal interaction when empty-handed. If held-item use owns that input, provide a separate contextual action on an available binding and show its real binding in the existing prompt. One input must perform exactly one action. A player aiming at blood can choose to coat themselves or fire the weapon.

## 4. Show the amount in a small blood globe

1. Add a circular globe to the existing screen-space HUD. Follow `CrosshairHUD` canvas scaling and non-interactive settings. Use the lower-left corner as the initial layout, with an editable diameter of about 64 px and a 24 px inset at the existing 1920 × 1080 reference size. Keep it clear of other prompts and the crosshair.
2. Draw a subtle circular rim and a dim interior. Clip a polarity-colored liquid fill to the circle. Keep the liquid surface horizontal and move it vertically by `coatHealth / 100`. At 50 the surface crosses the center. At 100 the circle is full. Dark blood needs a readable edge against dark scenery. Light blood needs a readable edge against pale scenery.
3. Show the globe only while health is positive. On damage, update the level immediately or ease over at most 0.15 seconds. On a side switch, recolor and refill to 50 together. At zero hide the entire globe, including its rim. Use no numbers, labels, permanent empty slot, or screen tint.

## 5. Show the coat on the first-person body

Use `FirstPersonBody` to follow the existing animated limb geometry. Render a thin, semi-translucent layer in the blood’s polarity color, offset slightly outward so a small gap is visible along the silhouette. Start with hands, forearms, and the legs visible when looking down. Keep the original limb detail visible beneath it. Use the same palette for the drop, shell, and globe.

The shell is visual only. Keep it away from the camera and off the screen plane. Do not apply a full-screen overlay, enclose the camera in a tinted capsule, or reuse the thick goo surface. Check compatibility with goo, honey luminosity, and held items. Remove or hide the shell at zero. The globe communicates the precise amount even when the hands are occupied.

## 6. Verify through the normal scene entry point

| Play Mode check | Expected observation |
|---|---|
| Enter the scene bare and walk between fighting Asura | They continue selecting eligible creatures. No globe or coat appears. Light perception still works. |
| Use one Light drop, then a second | Globe goes half-full, then full. Dark Asura can acquire the player. Light Asura do not deliberately target them. |
| Use matching blood at 80 and at 100 | First pickup caps health at 100. The next matching pickup remains usable on the ground. |
| Switch from 100 Light to Dark while a Dark attacker winds up | Globe becomes half-full and Dark. That attacker abandons the player before release. A Light attacker can acquire them. |
| Stand in the path of an already-fired friendly projectile, then take a nearby blast | Both can drain the coat. Multiple player colliders never multiply one blast’s damage or push. |
| Let each Asura variant attack the coated player | Melee connects, ranged shots hit, the General’s blast applies falloff, and a Rolling Asura can approach and detonate. |
| Lose the last point while moving with a weapon | Globe and coat disappear, aggro clears, movement and weapon use continue. The next pickup restores 50. |
| Defeat each variant and trigger a rolling self-detonation | Exactly one matching blood drop per combat death. The General also drops the Blast Core. |
| Coat while holding the Blast Core, then fire near the ground | The coat action does not fire. Intentional fire and rocket-jump still work. |
| Look ahead, down, and toward a wall with both colors | Limbs show a floating coat. The camera view stays clear. The globe remains readable on bright and dark backgrounds. |

Extend the existing Asura combat validation with coat transitions, target eligibility, and single-drop checks. Run the project’s existing Asura combat/feel and luminosity validation, then perform the visual and input checks in Play Mode. Report the actual contextual binding, tests run, and any checks that still require human playtesting.

