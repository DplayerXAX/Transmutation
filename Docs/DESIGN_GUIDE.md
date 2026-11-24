# Design guide: intent, pillars, and constraints

Updated **2026-11-23**. **Status: owner-confirmed intent and interaction rules, with supporting proposals marked as draft.** This file separates creative direction from current implementation and remains editable by the owner.

## Why this is separate from project context

- [Project context](PROJECT_CONTEXT.md) answers: what exists, where is it, and what should I read?
- This guide answers: what experience are we making, why, and what constraints should future work satisfy?
- The technical orientation in [project context](PROJECT_CONTEXT.md#technical-orientation) answers: how is it built, and which implementation contracts matter?
- [AGENTS.md](../AGENTS.md) answers: how must an AI work and maintain the project?

Keep these linked. Avoid copying the full creature wiki, tuning tables, or technical contracts into this file.

## Design intent — owner confirmed

Transmutation is a first-person exploration game about learning an unfamiliar world through creatures and their mechanics, combining them into different kinds of play, and allowing players to make their own goals.

Learning and discovery are the central experience: the joy, curiosity, and scariness of the unknown. Each mechanic should offer both the challenge of understanding how it works and the reward of being useful once the player understands it. Discovering a rule gives the player new possibilities for acting in the world.

When designing or changing a mechanic, ask: **What does the player have to discover, and what useful possibility does that understanding unlock?**

## Interaction rules — owner confirmed

These rules were supplied directly by the owner on **2026-11-23**. Their category matters: preferences guide tradeoffs; hard constraints govern implementation; defaults apply unless specifically instructed otherwise.

| ID | Category | Rule |
| --- | --- | --- |
| UI-01 | Preference | Use as little text and UI as possible. Prefer communicating through the world, creature behavior, and interaction feedback. This is a preference, not an absolute ban on text or UI. |
| INPUT-01 | Hard constraint | All creature-specific use/action controls use **LMB and RMB only**. **WASD and Space** may also support movement, including creature-assisted movement, and remain primarily movement controls. Do not introduce extra creature-action keys unless the owner specifically instructs it. |
| INPUT-02 | Default | A creature's **LMB use while held** and **LMB use while nearby in the world** should have roughly the same function or meaning wherever possible. Adapt to the held/world context while keeping the action recognizable. Do not invent extra actions or a separate held-use ability unless specifically instructed. |

## Supporting pillars — draft

The learning/discovery intent above is confirmed. The following wording and design tests remain proposals for the owner to refine.

Each pillar should guide tradeoffs. A pillar needs an observable design test, not just an adjective.

| ID / proposed pillar | Why it matters | Example design test |  |
| --- | --- | --- | --- |
| P-01: Learn through observation and experiment | Discovery depends on players forming and revising an understanding | Can a tester discover a creature's basic use through its behavior and feedback, then predict another use? |                   |
| P-02: Creatures connect mechanics | Creature abilities become more interesting through relationships | Can a proposed creature affect or be affected by an existing system in an understandable way? |  |
| P-03: Unfamiliar appearance, learnable behavior | Visual experimentation can invite investigation while feedback supports learning | After trying an interaction, can the tester tell what happened and recognize that state again? |  |
| P-04: Players find uses and purposes | Recombinable abilities can support different ways to play | Does a space allow a meaningful alternate use or approach? |  |


## Constraints: record scope and authority

Keep these categories distinct:

1. **Hard design constraint:** an explicit owner-approved requirement. State its scope, rationale, exceptions and pass/fail condition. AI must not invent these from patterns in existing code.
2. **Design preference:** a default that may be traded against other needs; record the reason for exceptions.
3. **Prototype target:** an experiment with a scene, measurement and review point. It is not automatically a shipping requirement.
4. **Interaction default:** the expected interaction pattern; extra actions or bindings need a specific owner instruction.

Engineering safeguards, including the scene-edit policy, belong in [AGENTS.md](../AGENTS.md). Feature-specific contracts belong in their local READMEs.

### Existing local conventions to reconcile before generalizing

| Observation | Local evidence | Safe interpretation |
| --- | --- | --- |
| Creature input and prompts | [Movement README](../Assets/Scripts/Movement/README.md) | Apply the confirmed interaction rules above; the README describes implementation, which may need reconciliation. Minimal text/UI remains a preference |
| Resting creatures and the Mind Palace support discovery and journaling | [Meditation README](../Assets/Scripts/Meditation/README.md) | Use that system's current instructions; some rest controls conflict with Movement documentation |
| Ecology distinguishes age, health, nutrition, heat and species behavior | [Creature README](../Assets/Scripts/Creatures/README.md) | Do not assume every creature needs hunger, reproduction, or every shared parameter |
| Procedural/shader experimentation is prominent | [systems/art reference](../Assets/Scripts/README.md), [World README](../Assets/Scripts/World/README.md) | This is evidence of existing art practice, not approval of one universal palette or shader |
| Combat and weapons exist alongside exploration/ecology | [Asura battlefield](AsuraBattlefield.README.md), [held weapon proposal](HeldWeaponDesignPlan.md) | Do not infer a global prohibition on combat or assume proposed weapon targets are approved tuning |

### Constraint entry template

```text
ID / title: C-### / one precise rule
Category: hard constraint | preference | prototype target | interaction default
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

1. Refine the supporting pillars and rank them when they conflict. Preserve the confirmed learning/discovery intent above.
2. Refine onboarding and any specific exceptions to the confirmed input rules or minimal text/UI preference. Resolve the resting/meditation control discrepancy when working on that system.
3. Specify world/system rules: which resources and transformations are universal, which are species-specific, and what exceptions are intentional.
4. Describe the role of combat, danger, failure, recovery, progression and player-chosen goals. Include examples of changes that would undermine the intended experience.
5. Set art/audio references with examples and counterexamples, plus accessibility requirements. Distinguish evocative references from exact visual restrictions.
6. Choose the current playable slice, target platforms/hardware and measurable performance budgets. Keep measured results and technical implementation in technical/local guides.
7. Record explicit non-goals and deferred features so experiments do not silently expand the scope.

Grow the guide when a real decision is made rather than filling it with speculative restrictions.
