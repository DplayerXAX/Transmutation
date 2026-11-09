# Asura prototype

## Contact feel and showcase

Open `Assets/Scenes/Scene_AsuraShowcase.unity` and press Play for the revised contact combat.
Keys **1–6** select duel, hurt/size samples, melee crowd, mixed clash, Blast Core, and rolling bomber.
**R** recreates the current finite encounter; **Tab** cycles close/overview/first-person views.
In station 2, **H** applies a sample health hit and impulse. Rooted/kinematic bodies retain their attachments.
The launcher is in station 5; use the existing pickup and held-fire controls in first person.

Melee approaches to a 0.18 m solid-surface gap, uses a 0.1 s preparation and 0.9 s interval,
and applies one physical knockback on a successful hit. A grounded victim receives a small hop
adjusted for world gravity; the motor yields for 0.18 s, then pursues again. Visuals add alternating
steps, a forward jab, red hurt tint and recoil. Health damage creates the shared `CreatureHurtResponse`
on demand, including ordinary creatures. Procedural fire/recycle shaders have an explicit hurt adapter.
The shared response preserves existing material properties and never moves the physics root.

Prefab scales are 0.8 / 1.1 / 1.5 for melee/ranged/General, with knockback multipliers
1 / 0.6 / 0.25. `VelocityChange` is intentionally multiplied explicitly rather than relying on mass.
The General retains its original explosive-shooter behavior and damage-death core drop.

`RollingAsura` rolls toward the nearest visible opposing creature, arms close by, waits 0.6 simulated
seconds, then resolves one 2.8 m blast and dies. Capture/carry permanently disarms it. Damage death
is inert. There is no runtime replenishment, squad behavior or strategy layer.

Blast damage and push resolve once at full radius. The short fading sphere has no collider.
Player blasts use additive velocity and a short launch allowance with normal gravity/air steering;
they do not enter legacy `Fling`. Entering a finished blast has no effect. A muzzle obstructed by
nearby terrain cannot spawn a projectile beyond or inside the obstruction.

**Tools > Creatures > Asura > Upgrade Contact Feel Assets** explicitly updates the three existing
prefabs and creates the bomber; ordinary **Create Assets** keeps its original create-only behavior.
**Create Contact Showcase** creates the dedicated scene without replacing an existing one.
**Validate Contact Feel (Play Mode)** supplements the original gameplay/lifecycle validation.
`AsuraShowcaseRecorder` is an explicitly added development helper for normal-speed duel captures;
it is not attached to the saved scene or gameplay prefabs.

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
default to Random; projectiles resolve to None. Ordinary appearances are retained outside
the shared short health-damage reaction.

Editor commands under **Tools > Creatures > Asura** create missing assets, place
the scene setup, run gameplay checks, and profile disposable groups. Placement is
idempotent and never adds runtime population logic. Existing prefab tuning is
preserved when running asset creation again.

| Setting | Melee | Ranged | General |
| --- | ---: | ---: | ---: |
| Health | 100 | 80 | 300 |
| Speed (m/s) | 3 | 2.5 | 1.5 |
| Reach/range (m) | 0.18 surface gap | 12 | 16 |
| Damage | 20 | 12 | 60 at blast center |
| Interval (seconds between releases) | 0.9 | 1.2 | 3.5 |
| Windup (seconds) | 0.1 | 0.25 | 0.6 |
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
push remains enabled. Player push adds to current velocity through `AddBlastImpulse`,
preserving its velocity through a 0.18-second ground allowance and its ensuing flight.

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
