# Asura push-and-pull options

The populated Basilt encounter starts with a southwest Light half and northeast Dark half. The intended front runs northwest to southeast. Surface connectivity bends this boundary around cliffs and vines. All surface samples reachable from the main battlefield are covered; isolated or inaccessible surfaces remain outside the colony network.

Starting population target: 140 per home, cap 180. Shared base replenishment: 2.5 per second. Initial spawn placement mixes broad territory occupancy with the existing engagement-band preference. Damage from Asura melee and fired projectiles rolls uniformly from 75% to 125% of base damage once per hit/launch. The random multiplier averages 1.0. Existing range, cooldown and blast falloff still apply.

Homes expose Living and Territory Area in the Inspector. Area is the union of connected surface patches, in approximate square metres. Overlap inside one faction is counted once. For balance use A = own area / sum of both faction areas, P = living / population cap, and a 15-second smoothed recent territory-gain rate. Cross-faction overlapping patches count for both sides in this share, so A measures influence share rather than exclusive ownership.

## Plan 1 — momentum followed by supply strain (recommended)

Use a short territory-gain bonus to amplify an early lucky win: up to +20% replenishment for 10–15 seconds after gaining ground. Scale it by recent area gained, not raw node count. Once a faction exceeds 60% influence share, gradually reduce its base rate, reaching 65% of base at 85% share. Give the compressed faction up to +15% recovery below 35% share. Smooth all multipliers over 10 seconds and clamp the final rate to 0.65–1.25 times base. Keep the same living cap. This gives a winning side a visible push, then makes its larger territory harder to hold.

## Plan 2 — accumulated recruitment reserve

Each home gains reserve at the same fixed rate. A successful capture awards a small capped reserve bonus, allowing an early burst. Every living fighter increases upkeep; a large army consumes reserve faster. Spawning spends reserve, with a small protected minimum income so a losing colony cannot become permanently empty. Cap stored reserve at about 10 fighters and allow at most two releases per tick. The winner can exploit a short advantage but must pay for its expanding army. More state and tuning than Plan 1.

## Plan 3 — alternating reinforcement surges

Keep equal long-term budgets but offset each home's 30–45 second reinforcement cycle. A surge raises its rate by roughly 30% for 8 seconds, followed by a lower-rate recovery period preserving the same total budget. Randomize the first cycle phase each run. Territory over 65% share lengthens recovery slightly, while a compressed home receives a shorter recovery. This creates clear waves and reversals with less reliance on casualty luck, though the rhythm may feel more authored.

These three feedback models are proposals, not enabled behavior. The current system already has equal bounded reinforcement budgets, territorial pressure/collapse, replacement after deaths and randomized damage. Short runs have shown formation and collapse, but have not established reliable long-term reversals. A later balance pass should record area share, living population, deaths and spawn rate every five seconds and compare reversal frequency across multiple runs.

Saved result: 82 linkages plus two homes, covering all 5,429 reachable samples. A short live check observed 145 Light and 164 Dark living fighters, 60 current combat targets, no failed spawn attempts, and territory losses/disconnections already occurring. This confirms active combat at the larger population, not sustained balance or performance on other hardware. Rolling-Asura blast damage also receives the same 75–125% multiplier. Compilation passed.
