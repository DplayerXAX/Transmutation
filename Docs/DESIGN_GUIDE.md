# Design guide: intent, pillars, and constraints

Created **2026-11-23**. **Status: draft framework for owner review.** This file separates creative direction from current implementation. 

## Why this is separate from project context

- [Project context](PROJECT_CONTEXT.md) answers: what exists, where is it, and what should I read?
- This guide answers: what experience are we making, why, and what constraints should future work satisfy?
- The technical orientation in [project context](PROJECT_CONTEXT.md#technical-orientation) answers: how is it built, and which implementation contracts matter?
- [AGENTS.md](../AGENTS.md) answers: how must an AI work and maintain the project?

Keep these linked. Avoid copying the full creature wiki, tuning tables, or technical contracts into this file.

## Starting design statement — 

Transmutation is a first-person exploration game about learning an unfamiliar world, creatures as units of mechanics, combinations that produce different kinds of play, and allowing player to make thier own goals with variety of mechanics. 
The owner has confirmed that document is not well updated, so they need confirmation before becoming binding project-wide rules.

## Proposed pillars

Each pillar should guide tradeoffs. A pillar needs an observable design test, not just an adjective.

| ID / proposed pillar | Why it matters | Example design test |  |
| --- | --- | --- | --- |
| P-01: Learn through observation and experiment | Discovery depends on players forming and revising an understanding | Can a tester discover a creature's basic use through its behavior and feedback, then predict another use? |                   |
| P-02: Creatures connect mechanics | Creature abilities become more interesting through relationships | Can a proposed creature affect or be affected by an existing system in an understandable way? |  |
| P-03: Unfamiliar appearance, learnable behavior | Visual experimentation can invite investigation while feedback supports learning | After trying an interaction, can the tester tell what happened and recognize that state again? |  |
| P-04: Players find uses and purposes | Recombinable abilities can support different ways to play | Does a space allow a meaningful alternate use or approach? |  |


## Constraints: record scope and authority

Use three distinct categories:

1. **Hard design constraint:** an explicit owner-approved requirement. State its scope, rationale, exceptions and pass/fail condition. AI must not invent these from patterns in existing code.
2. **Design preference:** a default that may be traded against other needs; record the reason for exceptions.
3. **Prototype target:** an experiment with a scene, measurement and review point. It is not automatically a shipping requirement.

Engineering safeguards, including the scene-edit policy, belong in [AGENTS.md](../AGENTS.md). Feature-specific contracts belong in their local READMEs.

### Existing local conventions to reconcile before generalizing

| Observation | Local evidence | Safe interpretation |
| --- | --- | --- |
| Creature actions generally use the two mouse buttons; prompts can be icon-based | [Movement README](../Assets/Scripts/Movement/README.md) | Preserve the affected feature's current contract; this is not a blanket ban on other keys or text |
| Resting creatures and the Mind Palace support discovery and journaling | [Meditation README](../Assets/Scripts/Meditation/README.md) | Use that system's current instructions; some rest controls conflict with Movement documentation |
| Ecology distinguishes age, health, nutrition, heat and species behavior | [Creature README](../Assets/Scripts/Creatures/README.md) | Do not assume every creature needs hunger, reproduction, or every shared parameter |
| Procedural/shader experimentation is prominent | [systems/art reference](../Assets/Scripts/README.md), [World README](../Assets/Scripts/World/README.md) | This is evidence of existing art practice, not approval of one universal palette or shader |
| Combat and weapons exist alongside exploration/ecology | [Asura battlefield](AsuraBattlefield.README.md), [held weapon proposal](HeldWeaponDesignPlan.md) | Do not infer a global prohibition on combat or assume proposed weapon targets are approved tuning |

### Constraint entry template

```text
ID / title: C-### / one precise rule
Category: hard constraint | preference | prototype target
Status: proposed | approved | superseded
Scope: whole game | named system | named scene
Rule: the required player-facing result
Rationale: experience or problem this protects
Pass/fail example: observable result, including an edge case
Exceptions: explicit cases; otherwise "none recorded"
Priority/conflicts: which decision resolves a competing requirement
Owner/decision source: person or explicit instruction; link if recorded
Approved/reviewed: date, or "not approved"
Implementation/verification: local README and relevant test or playtest
```

Do not treat an empty approval field as approval. For current implementation, check the README and owning assets even when a design rule is approved.

## What the owner should decide next

1. Confirm or rewrite the one-paragraph game identity and select three to five pillars. Rank them when they conflict.
2. Define the essential player verbs, onboarding approach, and desired amount of text/help. Resolve the resting/meditation control discrepancy.
3. Specify world/system rules: which resources and transformations are universal, which are species-specific, and what exceptions are intentional.
4. Describe the role of combat, danger, failure, recovery, progression and player-chosen goals. Include examples of changes that would undermine the intended experience.
5. Set art/audio references with examples and counterexamples, plus accessibility requirements. Distinguish evocative references from exact visual restrictions.
6. Choose the current playable slice, target platforms/hardware and measurable performance budgets. Keep measured results and technical implementation in technical/local guides.
7. Record explicit non-goals and deferred features so experiments do not silently expand the scope.

The highest-value first review is identity, pillar priorities, and five concrete constraints. Grow the guide when a real decision is made rather than filling it with speculative restrictions.
