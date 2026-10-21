# Asura prototype

Open `Assets/Scenes/Scene_Yoyo.unity` and press Play. The player starts on the
separate arena under **Asura Prototype**, centered at (75, 8, -15). Six authored
Asuras, two ecology witnesses, and a spare Blast Core are placed in the scene.
Nothing respawns. Restart Play Mode to reset the authored test layout.

Use the existing pickup control (hold right mouse in the default player setup)
to carry the nearby Blast Core. Left click fires while held; the click does not
also throw it. Aim a seed at the floor beside or below the player to blast jump.
The spare core and damage-death drops last 90 simulated seconds, including time
spent being carried. Capture pauses the shared simulation and Age normally.

## Assets and authoring

Prefabs: `Assets/Prefabs/Creatures/Asura/`. Materials:
`Assets/Materials/Creatures/Asura/`. Light and Dark share prefabs and statistics;
the scene instances override only their initial polarity. New ordinary creatures
default to Random; projectiles resolve to None. Existing ordinary appearances
are unchanged.

Editor commands under **Tools > Creatures > Asura** create missing assets, place
the scene setup, run gameplay checks, and profile disposable groups. Placement is
idempotent and never adds runtime population logic. Existing prefab tuning is
preserved when running asset creation again.

| Setting | Melee | Ranged | General |
| --- | ---: | ---: | ---: |
| Health | 100 | 80 | 300 |
| Speed (m/s) | 3 | 2.5 | 1.5 |
| Reach/range (m) | 1.5 | 12 | 16 |
| Damage | 20 | 12 | 60 at blast center |
| Interval (seconds between releases) | 1 | 1.2 | 3.5 |
| Windup (seconds) | 0.2 | 0.25 | 0.6 |
| Projectile speed (m/s) | — | 18 | 12 |
| Blast radius (m) | — | — | 4 |

Discovery snapshots refresh at 10 Hz. Each Asura searches every 0.25 seconds,
staggered, detects within 20 m, retains within 25 m, and abandons a continuously
obstructed target after one second. Aim uses the Rigidbody center of mass unless
an aim point is authored. Terrain and physical bodies obstruct attacks.

## Lifecycle contract

`Creature.ApplyDamage(amount, source)` rejects non-finite/nonpositive damage,
dead creatures, and pending meals. `Damaged` reports actual damage. `DeathCause`
is committed before virtual death hooks and the existing `Died(Creature)` event.
Damage, Age, Consumed, and Direct converge on normal species cleanup. Only Age
invokes `OnAgeDeath`. Ordinary combat corpses remain available to existing food
and compost systems.

`CanBeAITargeted` excludes only None; it intentionally permits remains. Combat
adds its own living/body/scene checks. Diet compatibility, physical capture,
contact meals, and squirrel food objects do not depend on polarity.

New individuals start at full health with their own resolved polarity. The spire
template-copy path explicitly resets individual state. Bubble-to-flower and
bird-to-bones replacements preserve polarity; offspring use normal initialization.

Projectiles use solid colliders, continuous collision detection, and initial
velocity with gravity disabled. Source collision exclusions end after separation.
Basic impacts damage once and remove the projectile, including terrain impacts.
Carrying or capture spends either projectile permanently. Flying explosives detonate on impact
or damage death. Age, consumption, direct removal, and damage to spent explosives
do not detonate them. Projectile death removes its body after shared notifications.

Explosions queue chain reactions, use the closest visible collider surface,
deduplicate damage by creature and push by Rigidbody, and damage static creature
colliders too. Damage and push fall linearly to zero at the radius. Initial push
is a 12 m/s velocity change at the center. Emitter damage is excluded; player
push remains enabled. Player push adds to current velocity through `Fling`,
preserving the existing 0.8-second movement grace.

## Verification

The Inspector exposes read-only health, resolved polarity, death cause, combat
state, target, and release count in Play Mode. Select an Asura for its target line.
Feedback observes events and does not drive damage timing.

The gameplay validation uses a disposable local physics scene and checks shared
identity/lifecycle, physical impacts, compound colliders, source protection,
explosive chain reactions and suppression, core drops, held input, blast jumping,
and carry/throw/web/bubble ownership handoffs, including passive corpse throwing.
Existing bird, feeding, infection, TimeSpider,
compost, squirrel, and bubble validations remain available in their original menus.

Initial isolated Editor measurements on this machine (Unity 6000.3.6f1):

| Units | Search ms/unit | Physics ms/step | Warm search allocated bytes | Projectile bodies | Retained memory delta MiB |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 6 | 0.0076 | 0.049 | 0 | 2 | 0.82 |
| 24 | 0.0143 | 0.090 | 0 | 6 | 1.24 |
| 60 | 0.0320 | 0.142 | 0 | 15 | 2.19 |

Search samples force reacquisition against a warmed snapshot. Physics samples
include the harness's body-force submissions and isolated physics simulation.
Memory is the difference in Unity allocated memory before and after each group;
the Editor and deferred destruction affect it. These are diagnostic samples,
not a supported population limit or a full-game frame-time claim.

Respawn, replenishment, population management, PolarMirror, fusion, blood orbs,
new age rewards, projectile pooling, homing, prediction, and arcing are deferred.
